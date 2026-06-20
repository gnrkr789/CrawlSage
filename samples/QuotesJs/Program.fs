module CrawlSage.Samples.QuotesJs.Program

// Recipe: *dynamic data, no browser*.
// quotes.toscrape.com/js renders its quotes client-side from a `var data = [ … ]` array
// embedded in a <script>. The static HTML ships no `.quote` markup at all — so instead of
// driving a browser, we lift the embedded JSON with `Extract` and read it directly.

open System.IO
open CrawlSage

type Quote =
    { Text: string
      Author: string
      Tags: string }

/// Read one element of the embedded `data` array into a flat record.
let private toQuote (node: System.Text.Json.Nodes.JsonNode) : Quote =
    { Text = node |> Extract.prop "text" |> Option.bind Extract.asString |> Option.defaultValue ""
      Author = node |> Extract.path [ "author"; "name" ] |> Option.bind Extract.asString |> Option.defaultValue ""
      Tags =
        node
        |> Extract.prop "tags"
        |> Option.map Extract.asList
        |> Option.defaultValue []
        |> List.choose Extract.asString
        |> String.concat "; " }

[<EntryPoint>]
let main _ =
    let response =
        Request.create "https://quotes.toscrape.com/js/"
        |> Resilience.politeFetch
        |> Async.RunSynchronously

    let doc = Html.parse response.Body

    // There is no .quote markup in the static HTML — proof we need the data, not the DOM.
    let domQuotes = doc |> Html.selectAll ".quote" |> List.length

    // Lift `var data = [ {…}, {…} ]` from the <script> and map each element.
    let quotes =
        doc
        |> Extract.assignedJson "data"
        |> Option.map Extract.asList
        |> Option.defaultValue []
        |> List.map toQuote

    Directory.CreateDirectory "data" |> ignore
    let path = Path.Combine("data", "quotes-js.csv")
    Export.toCsv path quotes

    printfn
        "Static HTML had %d .quote elements; lifted %d quotes from embedded JSON -> %s"
        domQuotes
        quotes.Length
        path

    for quote in quotes |> List.truncate 3 do
        printfn "  %s — %s" quote.Text quote.Author

    0
