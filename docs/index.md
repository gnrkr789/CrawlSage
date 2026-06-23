---
layout: default
title: CrawlSage
---

# CrawlSage

**An F#-first web crawling & scraping framework for .NET.**

[Guide](guide.html){: .btn } &nbsp;
[Architecture](architecture.html){: .btn } &nbsp;
[GitHub](https://github.com/gnrkr789/CrawlSage){: .btn }

![CrawlSage — scrape a list and write a CSV in one command](assets/demo.svg)

---

## A complete crawling stack for F#

CrawlSage gives F# a full crawling stack: a crawl engine — request queue, dedup,
scheduler, item pipelines — over a resilient downloader, a concise HTML selector DSL,
browser-free extraction of embedded data, and polite-by-default crawl ops. The API is
F#-idiomatic: records, discriminated unions and pipelines instead of attributes and
inheritance.

## What you get

- **Resilient downloader** — retry/back-off, timeouts, throttling, transparent gzip/brotli, and binary file downloads.
- **Spider engine** — a frontier scheduler with dedup, depth bounds, per-page fault tolerance and a stats/logging hook. Swap in a resumable (disk-backed) or bounded frontier.
- **Parse & extract** — forgiving CSS selectors, link extraction, and embedded-JSON extraction (`__NEXT_DATA__`, JSON-LD, assigned globals) — no browser.
- **Polite by default** — `robots.txt`, per-host pacing, honest User-Agent / proxy rotation, sitemap discovery.
- **Sessions** — cookie-jar login, saved and restored across runs.
- **Output** — JSON, JSON Lines, CSV, and Deedle data frames.
- **JavaScript when you must** — an opt-in headless-Chromium renderer, kept out of the core.

## Install

CrawlSage targets **.NET 10**.

```bash
dotnet add package CrawlSage
```

## A taste

Scrape a list with the selector DSL:

```fsharp
open CrawlSage

let authors =
    Http.getString "https://quotes.toscrape.com/"
    |> Async.RunSynchronously
    |> Html.parse
    |> Html.selectAll ".quote .author"
    |> List.map Html.text
```

Or run a full crawl — follow links, stream JSON Lines, polite by default:

```fsharp
open CrawlSage

type Quote = { Text: string; Author: string }

let parse (response: Response) : ParseResult<Quote> list =
    let doc = Html.parse response.Body
    let items =
        doc
        |> Html.selectAll ".quote"
        |> List.map (fun q ->
            Item
                { Text = q |> Html.select ".text" |> Option.map Html.text |> Option.defaultValue ""
                  Author = q |> Html.select ".author" |> Option.map Html.text |> Option.defaultValue "" })
    items @ (doc |> Html.links response.Request.Url |> List.map (Request.create >> Follow))

{ Seeds = [ Request.create "https://quotes.toscrape.com/" ]
  Parse = parse
  Pipeline = Export.appendJsonLine "data/quotes.jsonl"
  Options = SpiderOptions.Default }
|> Spider.crawl
|> Async.RunSynchronously
```

Every module, with runnable snippets, is **[documented in full →](guide.html)**.

---

<sub>Built for .NET 10 · MIT licensed · [github.com/gnrkr789/CrawlSage](https://github.com/gnrkr789/CrawlSage)</sub>
