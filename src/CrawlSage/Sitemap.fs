namespace CrawlSage

open System.Xml.Linq

/// sitemap.xml discovery — seed a crawl from a site's own list of URLs instead of guessing.
/// Handles a plain <c>&lt;urlset&gt;</c> and a <c>&lt;sitemapindex&gt;</c> (a sitemap of
/// sitemaps), and pulls <c>Sitemap:</c> directives out of robots.txt. XML namespaces are
/// ignored, so it copes with the slightly different flavours sites ship.
module Sitemap =

    /// Parse sitemap XML into <c>(isIndex, locs)</c>: every <c>&lt;loc&gt;</c> URL, plus
    /// whether the root is a <c>&lt;sitemapindex&gt;</c>. Malformed XML → <c>(false, [])</c>.
    let private locsAndKind (xml: string) : bool * string list =
        try
            let doc = XDocument.Parse xml
            let isIndex = not (isNull doc.Root) && doc.Root.Name.LocalName = "sitemapindex"

            let locs =
                doc.Descendants()
                |> Seq.filter (fun e -> e.Name.LocalName = "loc")
                |> Seq.map (fun e -> e.Value.Trim())
                |> Seq.filter (fun loc -> loc <> "")
                |> List.ofSeq

            isIndex, locs
        with _ ->
            false, []

    /// The <c>&lt;loc&gt;</c> URLs in a sitemap document (page URLs for a <c>urlset</c>, child
    /// sitemap URLs for a <c>sitemapindex</c>). Malformed XML → <c>[]</c>.
    let parse (xml: string) : string list = snd (locsAndKind xml)

    /// The <c>Sitemap:</c> URLs declared in a robots.txt body.
    let fromRobotsTxt (robotsText: string) : string list =
        robotsText.Replace("\r\n", "\n").Split('\n')
        |> Array.choose (fun line ->
            let trimmed = line.Trim()

            match trimmed.IndexOf ':' with
            | i when i > 0 && trimmed.Substring(0, i).Trim().ToLowerInvariant() = "sitemap" ->
                match trimmed.Substring(i + 1).Trim() with
                | "" -> None
                | url -> Some url
            | _ -> None)
        |> List.ofArray

    /// Fetch a sitemap URL and return its page URLs. A <c>sitemapindex</c> is expanded — each
    /// child sitemap is fetched (sequentially, politely) and its URLs aggregated. Anything
    /// unreadable yields <c>[]</c>, so a missing sitemap never breaks the caller.
    let rec fetchUrls (fetch: Renderer) (url: string) : Async<string list> =
        async {
            try
                let! response = fetch (Request.create url)

                if not response.IsSuccess then
                    return []
                else
                    match locsAndKind response.Body with
                    | true, children ->
                        let! nested = children |> List.map (fetchUrls fetch) |> Async.Sequential
                        return List.concat (Array.toList nested)
                    | false, locs -> return locs
            with _ ->
                return []
        }
