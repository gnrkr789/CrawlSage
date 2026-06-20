---
layout: default
title: Architecture
---

# Architecture

[← Home](index.html) &nbsp;·&nbsp; [Guide](guide.html)

CrawlSage is built in layers. Dependencies flow one way — lower layers never reference
higher ones — which keeps each piece testable in isolation.

```
Types → Url → Http → Resilience · Rotation · Session → Html · Extract → Robots · Sitemap → Frontier → Spider → Export
```

The opt-in `CrawlSage.Browser` adapter (headless Chromium) plugs in as a `Renderer`, so the
core never takes a browser dependency.

## Layers

| Layer | File | Responsibility | Phase |
| --- | --- | --- | --- |
| **Domain** | `Types.fs` | `Request`, `Response`, `Renderer`, `Sink` — pure data, no I/O | 0 ✅ |
| **URLs** | `Url.fs` | resolve · canonicalise (dedup) · same-host | 9 ✅ |
| **Downloader** | `Http.fs` | `fetch` / `fetchBytes` / `download` over `HttpClient` (gzip) | 0 ✅ |
| **Resilience** | `Resilience.fs` | retry · back-off · timeout · throttle (Polly) | 1 ✅ |
| **Rotation** | `Rotation.fs` | honest User-Agent & proxy rotation (round-robin) | 6 ✅ |
| **Session** | `Session.fs` | cookie-jar session — login, save/load | 9 ✅ |
| **Parsing** | `Html.fs` | AngleSharp selector DSL + link extraction | 2 ✅ |
| **Extraction** | `Extract.fs` | embedded-state / JSON, no browser: `__NEXT_DATA__`, JSON-LD | 4a ✅ |
| **Politeness** | `Robots.fs` | robots.txt parse · per-host cache · per-host pacing | 6 ✅ |
| **Discovery** | `Sitemap.fs` | `sitemap.xml` / `sitemapindex` URLs | 9 ✅ |
| **Frontier** | `Frontier.fs` | in-memory · bounded · persistent (resumable) | 9 ✅ |
| **Engine** | `Spider.fs` | frontier scheduler, dedup, depth, pipeline, robots gate, stats | 3 ✅ |
| **Export** | `Export.fs` | JSON / JSONL / CSV sinks + Deedle frames + `saveBytes` | 5 ✅ |
| **Browser** *(opt-in)* | `CrawlSage.Browser` | headless Chromium renderer (Playwright) | 9 ✅ |

## Design principles

1. **F# idioms first.** Records for data, discriminated unions for choices, modules of
   pipe-friendly functions for behaviour. No attribute-driven magic, no inheritance
   trees — the things being transformed come *last* in the argument list so `|>` reads
   naturally.
2. **Async all the way down.** Every network or browser call is `Async<_>` and honours
   the ambient cancellation token, so a whole crawl can be cancelled as one unit.
3. **Policy as composition.** Retry, throttling and proxy rotation are *wrappers* around
   `Http.fetch`, not flags inside it. You opt in by composing functions.
4. **Wrap, don't expose.** Best-in-class .NET libraries (AngleSharp, Polly, Deedle) sit
   behind a thin F# surface so callers never touch the raw API.
5. **Don't render — extract.** Dynamic data is pulled from the page's embedded state /
   JSON or its API, not a browser. The core stays browser-free; a real browser is an
   opt-in `Renderer` adapter, never a core dependency.
6. **Ethical by default.** `robots.txt`, rate limits and back-off are first-class
   middleware, not afterthoughts.

## The request lifecycle (target design, Phase 3)

```
seed Requests
     │
     ▼
 ┌─────────┐   dedup    ┌───────────┐   fetch   ┌──────────┐   parse   ┌───────────┐
 │ Scheduler│──────────▶│ Downloader│──────────▶│  Parser  │──────────▶│ Pipelines │
 └─────────┘            └───────────┘           └──────────┘           └───────────┘
     ▲                                                │                       │
     │             follow-up Requests                 │                       ▼
     └────────────────────────────────────────────────┘                 export / store
```

The parser yields two things: **items** (scraped data → pipelines) and **follow-up
requests** (→ back to the scheduler), expressed as an F# function returning a
discriminated union.
