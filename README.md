# CrawlSage

> **An F#-first web crawling & scraping framework for .NET.**
> Scrapy-grade ergonomics, BeautifulSoup-grade convenience — with the type safety of F#.

[![CI](https://github.com/gnrkr789/CrawlSage/actions/workflows/ci.yml/badge.svg)](https://github.com/gnrkr789/CrawlSage/actions/workflows/ci.yml)
[![Docs](https://github.com/gnrkr789/CrawlSage/actions/workflows/docs.yml/badge.svg)](https://gnrkr789.github.io/CrawlSage/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Status](https://img.shields.io/badge/status-early%20development-orange)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)

---

## Why CrawlSage? (왜 F#인가)

F# *can* crawl the web today — `HttpClient`, `AngleSharp`, `HtmlAgilityPack` and
`Playwright for .NET` are all excellent. But compared to Python's ecosystem
(BeautifulSoup, Scrapy, Requests, Selenium, Playwright) the F# story has real gaps:

| Gap in F# crawling | What CrawlSage provides |
| --- | --- |
| **No dedicated framework** — you assemble .NET libraries by hand | A Scrapy-style engine: request queue, dedup, pipelines, middleware, retry, throttling |
| **Few real-world examples** — login, cookies, infinite scroll, dynamic rendering | A curated **cookbook** of runnable F# recipes for exactly these cases |
| **Verbose HTML parsing** vs BeautifulSoup's forgiving API | A concise, F#-idiomatic selector DSL over AngleSharp |
| **Dynamic pages** need Playwright/Selenium, sparse F# samples | A first-class Playwright-backed renderer with F# helpers |
| **Weaker data post-processing** than pandas | Export pipelines to CSV / JSON / Parquet / DB, Deedle-friendly |
| **Thin operational know-how** — proxy rotation, rate limits, robots.txt | Built-in middleware for proxies, UA rotation, back-off, `robots.txt` |

**The gap, in one screenful** — Python gets you from request to parsed data in 5 lines:

```python
import requests
from bs4 import BeautifulSoup
html = requests.get("https://example.com").text
soup = BeautifulSoup(html, "html.parser")
titles = [t.text.strip() for t in soup.select("h2")]
```

CrawlSage's goal is to make the F# equivalent just as short — and then give you the
queue, retries, throttling and pipelines that Python's Scrapy adds on top:

```fsharp
open CrawlSage

// Works today: fetch + decode.
let html = Http.getString "https://example.com" |> Async.RunSynchronously

// Roadmap (Phase 2+): the concise parsing DSL.
//   let titles =
//       html |> Html.parse |> Html.select "h2" |> List.map Html.text
```

> CrawlSage is to F# what [DotnetSpider](https://github.com/dotnetcore/DotnetSpider)
> is to C# — but designed around F# idioms (records, discriminated unions, pipelines,
> computation expressions) instead of attributes and inheritance.

---

## Status

🚧 **Early development.** The repository ships a buildable core (`Request`, `Response`,
`Http.fetch`) plus full project tooling. The framework engine, parsing DSL, dynamic
renderer and cookbook are built out phase by phase — see
**[PROMPTS.md](PROMPTS.md)** for the step-by-step roadmap (each phase is a ready-to-run
prompt for Claude Code).

---

## Quick start

```bash
# Requires the .NET 10 SDK
dotnet build CrawlSage.slnx
dotnet test  CrawlSage.slnx
```

Use the library:

```fsharp
open CrawlSage

let body =
    Request.create "https://example.com"
    |> Request.withHeader "Accept-Language" "en"
    |> Http.fetch
    |> Async.RunSynchronously

printfn "%d — %d bytes" body.StatusCode body.Body.Length
```

---

## Project layout

```
CrawlSage/
├── src/CrawlSage/            # the framework library
│   ├── Types.fs              #   Request / Response / HttpVerb
│   ├── Http.fs               #   the downloader (HttpClient)
│   ├── Resilience.fs         #   retry · back-off · timeout · throttle (Polly)
│   ├── Html.fs               #   AngleSharp selector DSL (parse / select / text / attr)
│   └── Spider.fs             #   BFS crawl engine (queue · dedup · depth · pipeline)
├── tests/CrawlSage.Tests/    # xUnit test project
├── samples/                  # cookbook: runnable, self-contained crawlers
├── docs/                     # GitHub Pages site (Jekyll)
├── .claude/skills/           # Claude Code skills for building & using CrawlSage
├── .github/workflows/        # CI + docs deploy
├── PROMPTS.md                # step-by-step build prompts (the roadmap)
└── CLAUDE.md                 # guidance for Claude Code in this repo
```

---

## Roadmap

| Phase | Theme | Output |
| --- | --- | --- |
| 0 | **Scaffold** ✅ | repo, CI, docs, skills, core types |
| 1 | **Downloader** ✅ | retry/back-off, throttling, timeouts (Polly) |
| 2 | **Parsing DSL** ✅ | AngleSharp wrapper, CSS selectors |
| 3 | **Spider engine** ✅ | request queue, dedup, scheduler, pipelines |
| 4 | **Dynamic pages** | Playwright renderer, infinite scroll, login |
| 5 | **Data pipelines** | CSV / JSON / Parquet / DB export |
| 6 | **Crawl ops** | proxy & UA rotation, rate limits, robots.txt |
| 7 | **Cookbook** | real-world recipes in `samples/` |
| 8 | **Packaging** | NuGet release, versioned docs |

Full prompts for each phase live in **[PROMPTS.md](PROMPTS.md)**.

---

## Building with Claude Code

This repo is set up to be built *with* [Claude Code](https://claude.com/claude-code).
The `.claude/skills/` directory contains task-specific skills:

| Skill | Use it when you… |
| --- | --- |
| `new-spider` | scaffold a new crawler in `samples/` |
| `parse-html` | extract data with the selector DSL |
| `dynamic-page` | render JS / handle infinite scroll with Playwright |
| `session-auth` | log in and keep cookies / sessions |
| `data-export` | write scraped data to CSV / JSON / DB |
| `crawl-ops` | add proxies, rate limits, retries, robots.txt |

Open the repo in Claude Code and type `/` to discover them, or follow `PROMPTS.md`.

---

## License

[MIT](LICENSE) © 2026 Jun Tae Kim.

**Please crawl responsibly.** Respect `robots.txt`, rate limits, a site's Terms of
Service and applicable law. CrawlSage is a tool; how you use it is your responsibility.
