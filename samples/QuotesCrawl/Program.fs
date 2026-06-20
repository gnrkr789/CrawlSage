module CrawlSage.Samples.QuotesCrawl.Program

// Recipe: *follow pagination* + *polite crawl* + *stream to JSON Lines*.
// Seed page 1, emit each quote as an item, follow the "Next →" link, and let the engine
// walk every page. `Spider.crawl` is polite by default: robots.txt-respecting + per-host
// pacing. Items stream to data/quotes.jsonl as they're scraped.

open System
open System.IO
open CrawlSage

type Quote =
    { Text: string
      Author: string
      Tags: string list
      Page: string }

/// Resolve a possibly-relative href (e.g. "/page/2/") against the page it was found on.
let private absolute (baseUrl: string) (href: string) : string =
    match Uri.TryCreate(Uri baseUrl, href) with
    | true, uri -> uri.AbsoluteUri
    | _ -> href

/// Emit every quote on the page as an Item, then Follow the pagination "Next" link.
let parse (response: Response) : ParseResult<Quote> list =
    let doc = Html.parse response.Body

    let quotes =
        doc
        |> Html.selectAll ".quote"
        |> List.map (fun quote ->
            Item
                { Text = quote |> Html.select ".text" |> Option.map Html.text |> Option.defaultValue ""
                  Author = quote |> Html.select ".author" |> Option.map Html.text |> Option.defaultValue ""
                  Tags = quote |> Html.selectAll ".tags .tag" |> List.map Html.text
                  Page = response.Request.Url })

    let next =
        doc
        |> Html.select "li.next > a"
        |> Option.bind (Html.attr "href")
        |> Option.map (absolute response.Request.Url >> Request.create >> Follow)
        |> Option.toList

    quotes @ next

[<EntryPoint>]
let main _ =
    Directory.CreateDirectory "data" |> ignore
    let path = Path.Combine("data", "quotes.jsonl")

    if File.Exists path then
        File.Delete path // start each run fresh

    let mutable count = 0

    let pipeline (quote: Quote) =
        Export.appendJsonLine path quote
        count <- count + 1

    let spider =
        { Seeds = [ Request.create "https://quotes.toscrape.com/" ]
          Parse = parse
          Pipeline = pipeline
          Options = SpiderOptions.Default }

    printfn "Crawling quotes.toscrape.com politely (robots + per-host pacing)…"
    Spider.crawl spider |> Async.RunSynchronously

    printfn "Done: %d quotes -> %s" count path
    0
