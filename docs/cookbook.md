---
layout: default
title: Cookbook
---

# Cookbook

[← Home](index.html)

Real-world recipes — the examples that are abundant for Python and scarce for F#.
Each one lands as a runnable sample under
[`samples/`](https://github.com/gnrkr789/CrawlSage/tree/main/samples) as the matching
phase is built. This page is the index of what's planned.

| Recipe | What it shows | Phase |
| --- | --- | --- |
| **Hello, fetch** | request → response → body | 0 ✅ |
| **Extract a list** | CSS selectors, mapping nodes to records | 2 ✅ |
| **Follow pagination** | yielding follow-up requests from a parser | 3 ✅ |
| **Login + keep session** | form POST, cookie jar, authenticated pages | 4 |
| **Dynamic data, no browser** | `__NEXT_DATA__` / JSON-LD / API replay via `Extract` | 4a ✅ |
| **Heavy SPA (opt-in)** | in-process JS / external-browser adapter — last resort | 4b+ |
| **Export to CSV/JSON** | piping scraped items to a file sink | 5 ✅ |
| **Polite crawl** | `robots.txt`, rate limit, retry/back-off | 6 |
| **Proxy & UA rotation** | resilient fetching behind rotating egress | 6 |

## Hello, fetch (available today)

```fsharp
open CrawlSage

let titlesLength =
    Http.getString "https://example.com"
    |> Async.RunSynchronously
    |> String.length

printfn "Fetched %d characters" titlesLength
```

## Extract a list (available now — Phase 2)

```fsharp
open CrawlSage

let titles =
    Http.getString "https://news.ycombinator.com"
    |> Async.RunSynchronously
    |> Html.parse
    |> Html.selectAll ".titleline > a"
    |> List.map Html.text
```

## Dynamic data, no browser (available now — Phase 4a)

Most "dynamic" pages ship their data as embedded JSON. Extract it instead of rendering:

```fsharp
open CrawlSage

// A Next.js page embeds its data in <script id="__NEXT_DATA__"> — no browser needed.
let title =
    Http.getString "https://a-next-site.example"
    |> Async.RunSynchronously
    |> Html.parse
    |> Extract.nextData
    |> Option.bind (Extract.path [ "props"; "pageProps"; "title" ])
    |> Option.bind Extract.asString
```

> Recipes graduate from "target API" to "available today" as each phase ships.
> Track progress in
> [PROMPTS.md](https://github.com/gnrkr789/CrawlSage/blob/main/PROMPTS.md).
