module CrawlSage.Samples.PoliteRotation.Program

// Recipe: *proxy & UA rotation* + *crawl-ops*.
// Rotate an honest User-Agent over the polite downloader, and drive the engine with an
// explicitly tuned `Politeness` (robots-respecting, 2s between hits to one host). The
// parser records which UA fetched each page, so the rotation is visible in the output.

open System
open CrawlSage

type Sighting =
    { Author: string
      Page: string
      UserAgent: string }

/// Honest, identifiable User-Agents to spread load across — declare who you are; this is
/// load-spreading, not disguise.
let userAgents =
    [ "CrawlSage-sample/0.1 (+https://github.com/gnrkr789/CrawlSage)"
      "CrawlSage-sample/0.1 (rotation demo)" ]

let private absolute (baseUrl: string) (href: string) : string =
    match Uri.TryCreate(Uri baseUrl, href) with
    | true, uri -> uri.AbsoluteUri
    | _ -> href

let parse (response: Response) : ParseResult<Sighting> list =
    let doc = Html.parse response.Body
    let ua = response.Request.Headers |> Map.tryFind "User-Agent" |> Option.defaultValue "(default)"

    let sightings =
        doc
        |> Html.selectAll ".quote .author"
        |> List.map (fun author ->
            Item
                { Author = Html.text author
                  Page = response.Request.Url
                  UserAgent = ua })

    let next =
        doc
        |> Html.select "li.next > a"
        |> Option.bind (Html.attr "href")
        |> Option.map (absolute response.Request.Url >> Request.create >> Follow)
        |> Option.toList

    sightings @ next

[<EntryPoint>]
let main _ =
    // Compose: rotate an honest UA over the polite (throttled/retried/timed-out) downloader.
    let fetch = Resilience.politeFetch |> Rotation.withRotatingUserAgent userAgents

    let collected = ResizeArray<Sighting>()

    let spider =
        { Seeds = [ Request.create "https://quotes.toscrape.com/" ]
          Parse = parse
          Pipeline = collected.Add
          Options = { SpiderOptions.Default with MaxDepth = 2 } } // seed + 2 follows = first 3 pages

    // Tuned politeness: robots-respecting, at most one request every 2s to a host.
    let politeness =
        { Politeness.Default with
            PerHostDelay = TimeSpan.FromSeconds 2.0 }

    printfn "Crawling the first 3 pages — rotating UA, %.0fs per host…" politeness.PerHostDelay.TotalSeconds
    Spider.crawlPolitely politeness fetch spider |> Async.RunSynchronously

    // One line per page, showing which rotated UA fetched it.
    collected
    |> Seq.distinctBy (fun s -> s.Page)
    |> Seq.iter (fun s -> printfn "  %-40s ua=%s" s.Page s.UserAgent)

    printfn "Collected %d author sightings across the pages." collected.Count
    0
