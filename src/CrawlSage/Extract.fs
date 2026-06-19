namespace CrawlSage

open System.Text.Json.Nodes
open AngleSharp.Dom

/// Pull embedded structured data out of HTML — the data that SSR / hydration frameworks
/// ship *inside* the page (Next.js <c>__NEXT_DATA__</c>, Nuxt <c>__NUXT__</c>, JSON-LD,
/// inline state). Most "dynamic" pages aren't dynamic: extract the data behind the page
/// instead of rendering pixels — faster, lighter and harder to break than any browser,
/// and with no JavaScript engine.
///
/// JSON is navigated with the same option-returning, pipe-friendly style as <c>Html</c>.
module Extract =

    /// Parse a string as JSON, returning None on malformed input.
    let json (raw: string) : JsonNode option =
        try
            Option.ofObj (JsonNode.Parse raw)
        with _ ->
            None

    /// The JSON inside the first <c>&lt;script&gt;</c> matching a CSS selector
    /// (e.g. <c>"script#__NEXT_DATA__"</c>).
    let scriptJson (selector: string) (doc: IDocument) : JsonNode option =
        doc |> Html.select selector |> Option.map Html.text |> Option.bind json

    /// Next.js page data: the JSON in <c>&lt;script id="__NEXT_DATA__"&gt;</c>.
    let nextData (doc: IDocument) : JsonNode option =
        scriptJson "script#__NEXT_DATA__" doc

    /// Every JSON-LD block on the page (<c>&lt;script type="application/ld+json"&gt;</c>).
    let jsonLd (doc: IDocument) : JsonNode list =
        doc
        |> Html.selectAll "script[type='application/ld+json']"
        |> List.choose (Html.text >> json)

    /// Slice a balanced <c>{…}</c> beginning at <paramref name="start"/>, skipping string
    /// literals, so an object literal can be lifted out of a <c>&lt;script&gt;</c> body.
    let private sliceObject (s: string) (start: int) : string option =
        let mutable depth = 0
        let mutable i = start
        let mutable inString = false
        let mutable quote = ' '
        let mutable escaped = false
        let mutable result = None

        while i < s.Length && result.IsNone do
            let c = s.[i]

            if inString then
                if escaped then escaped <- false
                elif c = '\\' then escaped <- true
                elif c = quote then inString <- false
            else
                match c with
                | '"'
                | '\'' ->
                    inString <- true
                    quote <- c
                | '{' -> depth <- depth + 1
                | '}' ->
                    depth <- depth - 1

                    if depth = 0 then
                        result <- Some(s.Substring(start, i - start + 1))
                | _ -> ()

            i <- i + 1

        result

    /// Lift the JSON object assigned to a global, e.g. <c>window.__NUXT__ = {…}</c> or
    /// <c>__INITIAL_STATE__ = {…}</c>, from any <c>&lt;script&gt;</c> on the page.
    /// Best-effort: it takes the first balanced <c>{…}</c> after the name and parses it.
    let assignedJson (name: string) (doc: IDocument) : JsonNode option =
        doc
        |> Html.selectAll "script"
        |> List.map Html.text
        |> List.tryPick (fun body ->
            let nameAt = body.IndexOf name

            if nameAt < 0 then
                None
            else
                let braceAt = body.IndexOf('{', nameAt)
                if braceAt < 0 then None else sliceObject body braceAt |> Option.bind json)

    /// Navigate to an object property, if this node is an object that has it.
    let prop (name: string) (node: JsonNode) : JsonNode option =
        match node with
        | :? JsonObject as jObject -> Option.ofObj jObject.[name]
        | _ -> None

    /// Follow a path of object keys, e.g. <c>path [ "props"; "pageProps"; "title" ]</c>.
    let path (keys: string list) (node: JsonNode) : JsonNode option =
        (Some node, keys) ||> List.fold (fun acc key -> acc |> Option.bind (prop key))

    /// This node as a string, if it is a JSON string.
    let asString (node: JsonNode) : string option =
        match node with
        | :? JsonValue as jValue ->
            match jValue.TryGetValue<string>() with
            | true, s -> Option.ofObj s
            | _ -> None
        | _ -> None

    /// This node's elements, if it is a JSON array.
    let asList (node: JsonNode) : JsonNode list =
        match node with
        | :? JsonArray as jArray -> jArray |> Seq.choose Option.ofObj |> List.ofSeq
        | _ -> []
