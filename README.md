# CrawlSage

> An F#-first web crawling & scraping framework for .NET.

[![CI](https://github.com/gnrkr789/CrawlSage/actions/workflows/ci.yml/badge.svg)](https://github.com/gnrkr789/CrawlSage/actions/workflows/ci.yml)
[![Docs](https://github.com/gnrkr789/CrawlSage/actions/workflows/docs.yml/badge.svg)](https://gnrkr789.github.io/CrawlSage/)
[![NuGet](https://img.shields.io/nuget/v/CrawlSage.svg)](https://www.nuget.org/packages/CrawlSage/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Status](https://img.shields.io/badge/status-early%20development-orange)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)

---

## What it is

CrawlSage is a web crawling and scraping framework designed around F# idioms — records and
discriminated unions for data, `option` over null, and `|>` pipelines for behaviour. It pairs
a full crawl engine (request queue, dedup, scheduler, item pipelines) with a resilient
downloader, a concise HTML selector DSL, and first-class politeness.

Two principles shape it:

- **Don't render — extract.** Most "dynamic" pages ship their data as embedded JSON.
  CrawlSage lifts that state directly, so the core needs no browser.
- **Polite by default.** `robots.txt`, per-host pacing, retries and back-off are built into
  the engine, not bolted on.

## Features

- **Resilient downloader** — retry with exponential back-off + jitter, per-request timeout,
  and concurrency throttling, composed as wrappers around one shared `HttpClient`.
- **Parsing DSL** — forgiving, `option`-returning CSS selectors (`parse` / `select` /
  `selectAll` / `text` / `attr`) that pipe naturally.
- **Spider engine** — a breadth-first scheduler with URL-fingerprint dedup, depth bounding,
  and an item pipeline; a parser returns items and follow-up requests as a discriminated union.
- **Dynamic data, no browser** — pull `__NEXT_DATA__`, JSON-LD, and object/array globals out
  of the page, or replay the JSON API behind it.
- **Crawl ops** — `robots.txt` parsing with a per-host cache, per-host rate limiting, and
  honest User-Agent / proxy rotation.
- **Output sinks** — stream items to JSON, JSON Lines, or CSV, or load them into data frames
  for post-processing.

---

## Status

🚧 **Early development.** Phases 0–8 are in place: resilient downloader, parsing DSL, spider
engine, extraction, export, crawl ops, a runnable sample cookbook, and a tag-driven NuGet
release workflow.

---

## Install

```bash
dotnet add package CrawlSage
```

---

## Quick start

```bash
# Requires the .NET 10 SDK
dotnet build CrawlSage.slnx
dotnet test  CrawlSage.slnx
```

A minimal fetch:

```fsharp
open CrawlSage

let body =
    Request.create "https://example.com"
    |> Request.withHeader "Accept-Language" "en"
    |> Http.fetch
    |> Async.RunSynchronously

printfn "%d — %d bytes" body.StatusCode body.Body.Length
```

Pull a list of fields with the selector DSL:

```fsharp
open CrawlSage

let authors =
    Http.getString "https://quotes.toscrape.com/"
    |> Async.RunSynchronously
    |> Html.parse
    |> Html.selectAll ".quote .author"
    |> List.map Html.text
```

Full crawlers — extract a list, follow pagination, lift embedded JSON, rotate User-Agents —
are runnable under [`samples/`](samples), each polite by default.

---

## Project layout

```
CrawlSage/
├── src/CrawlSage/            # the framework library
│   ├── Types.fs              #   Request / Response / HttpVerb
│   ├── Http.fs               #   the downloader (shared HttpClient)
│   ├── Resilience.fs         #   retry · back-off · timeout · throttle
│   ├── Rotation.fs           #   honest UA & proxy rotation
│   ├── Html.fs               #   CSS selector DSL (parse / select / text / attr)
│   ├── Extract.fs            #   embedded-state / JSON extraction (no browser)
│   ├── Robots.fs             #   robots.txt parse · per-host cache · per-host pacing
│   ├── Spider.fs             #   BFS crawl engine (queue · dedup · depth · pipeline · robots)
│   └── Export.fs             #   output sinks: JSON / JSONL / CSV + data frames
├── tests/CrawlSage.Tests/    # xUnit test project
├── samples/                  # runnable, self-contained crawlers
└── docs/                     # documentation site
```

---

## Roadmap

| Phase | Theme | Output |
| --- | --- | --- |
| 0 | **Scaffold** ✅ | repo, CI, docs, core types |
| 1 | **Downloader** ✅ | retry/back-off, throttling, timeouts |
| 2 | **Parsing DSL** ✅ | CSS selector DSL over a forgiving HTML parser |
| 3 | **Spider engine** ✅ | request queue, dedup, scheduler, pipelines |
| 4 | **Dynamic data** ✅ | embedded-state / JSON extraction (no browser) |
| 5 | **Data pipelines** ✅ | JSON / JSONL / CSV sinks + data frames |
| 6 | **Crawl ops** ✅ | robots.txt, per-host rate limit, UA & proxy rotation |
| 7 | **Cookbook** ✅ | runnable recipes in `samples/` |
| 8 | **Packaging** ✅ | tag-driven NuGet release (pack · symbols · publish) |

---

## Releasing

Releases are tag-driven. Add a `NUGET_API_KEY` repository secret
(Settings → Secrets and variables → Actions), then push a version tag:

```bash
git tag v0.1.0
git push origin v0.1.0
```

The release workflow builds, tests, packs (with a symbols package), and publishes to NuGet.

---

## License

[MIT](LICENSE).

**Please crawl responsibly.** Respect `robots.txt`, rate limits, a site's Terms of Service,
and applicable law. CrawlSage is a tool; how you use it is your responsibility.
