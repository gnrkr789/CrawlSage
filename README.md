# CrawlSage

> An F#-first web crawling & scraping framework for .NET.

[![CI](https://github.com/gnrkr789/CrawlSage/actions/workflows/ci.yml/badge.svg)](https://github.com/gnrkr789/CrawlSage/actions/workflows/ci.yml)
[![Docs](https://github.com/gnrkr789/CrawlSage/actions/workflows/docs.yml/badge.svg)](https://gnrkr789.github.io/CrawlSage/)
[![NuGet](https://img.shields.io/nuget/v/CrawlSage.svg)](https://www.nuget.org/packages/CrawlSage/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Status](https://img.shields.io/badge/status-early%20development-orange)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)

![CrawlSage — scrape a list and write a CSV in one command](docs/assets/demo.gif)

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

- **Resilient downloader** — retry with back-off + jitter, per-request timeout, concurrency
  throttling, and gzip/brotli decompression, composed as wrappers around one `HttpClient`.
- **Parsing & links** — forgiving, `option`-returning CSS selectors, plus a link extractor and
  URL canonicalisation for dedup and same-host filtering.
- **Spider engine** — a frontier-driven scheduler with dedup, depth bounding, an item pipeline,
  per-page fault tolerance, and a `CrawlEvent` hook for stats / logging.
- **Resumable & bounded crawls** — swap the frontier for a disk-backed one (resume after a stop
  or crash) or a memory-capped one, with zero engine changes.
- **Dynamic data, no browser** — pull `__NEXT_DATA__`, JSON-LD and object/array globals out of
  the page, or replay the JSON API; an opt-in `CrawlSage.Browser` (Playwright) adapter renders
  the rest.
- **Politeness & rotation** — `robots.txt` (per-host cache + `Crawl-delay`), per-host pacing,
  honest User-Agent / proxy rotation, and `sitemap.xml` discovery.
- **Sessions** — a cookie-jar session for form login, saved and restored across runs.
- **Output** — JSON / JSON Lines / CSV / data-frame sinks for items, plus binary file downloads.

---

## Status

🚧 **Early development.** The framework (phases 0–8) is in place — resilient downloader,
parsing DSL, spider engine, extraction, export, crawl ops, a sample cookbook and a tag-driven
NuGet release — plus a hardening pass: link extraction, binary downloads, resumable/bounded
frontiers, crawl stats, sessions, sitemaps and an opt-in browser renderer.

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

**Detailed usage:** the [**Guide**](https://gnrkr789.github.io/CrawlSage/guide.html) documents
the full API, module by module — `Http`, `Html`, `Extract`, `Spider`, `Session`, `Frontier`,
and the rest.

---

## Project layout

```
CrawlSage/
├── src/CrawlSage/            # the framework library (browser-free core)
│   ├── Types.fs              #   Request / Response / Renderer / Sink
│   ├── Url.fs                #   resolve · canonicalise · same-host
│   ├── Http.fs               #   the downloader (shared HttpClient, gzip, bytes/download)
│   ├── Resilience.fs         #   retry · back-off · timeout · throttle
│   ├── Rotation.fs           #   honest UA & proxy rotation
│   ├── Session.fs            #   cookie-jar session (login, save/load)
│   ├── Html.fs               #   CSS selector DSL + link extraction
│   ├── Extract.fs            #   embedded-state / JSON extraction (no browser)
│   ├── Robots.fs             #   robots.txt parse · per-host cache · per-host pacing
│   ├── Sitemap.fs            #   sitemap.xml / sitemapindex discovery
│   ├── Frontier.fs           #   in-memory · bounded · persistent (resumable) frontier
│   ├── Spider.fs             #   crawl engine (frontier · dedup · depth · pipeline · stats)
│   └── Export.fs             #   sinks: JSON / JSONL / CSV / data frames + saveBytes
├── src/CrawlSage.Browser/    # opt-in JS renderer (Playwright) — not a core dependency
├── tests/                    # xUnit test projects (core + browser)
├── samples/                  # runnable, self-contained crawlers
└── docs/                     # documentation site
```

---

## License

[MIT](LICENSE).

**Please crawl responsibly.** Respect `robots.txt`, rate limits, a site's Terms of Service,
and applicable law. CrawlSage is a tool; how you use it is your responsibility.
