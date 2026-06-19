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
| **Extract a list** | CSS selectors, mapping nodes to records | 2 |
| **Follow pagination** | yielding follow-up requests from a parser | 3 |
| **Login + keep session** | form POST, cookie jar, authenticated pages | 4 |
| **Infinite scroll** | Playwright, scroll-to-load, waiting for content | 4 |
| **Dynamic SPA** | render JS, wait for selectors, extract | 4 |
| **Export to CSV/JSON** | piping scraped items to a file sink | 5 |
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

## Extract a list (target API, Phase 2)

```fsharp
open CrawlSage

let titles =
    Http.getString "https://news.ycombinator.com"
    |> Async.RunSynchronously
    |> Html.parse
    |> Html.selectAll ".titleline > a"
    |> List.map Html.text
```

> Recipes graduate from "target API" to "available today" as each phase ships.
> Track progress in
> [PROMPTS.md](https://github.com/gnrkr789/CrawlSage/blob/main/PROMPTS.md).
