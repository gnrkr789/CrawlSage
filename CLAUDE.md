# CLAUDE.md

Guidance for [Claude Code](https://claude.com/claude-code) working in this repository.

## What this is

CrawlSage is an **F#-first web crawling & scraping framework** for .NET — the F#
counterpart to C#'s [DotnetSpider](https://github.com/dotnetcore/DotnetSpider) and
Python's Scrapy. The mission is to close the gaps that make F# feel second-class for
crawling: no dedicated framework, few real-world examples, verbose HTML parsing,
sparse dynamic-page samples, weaker data post-processing.

It is built **incrementally** — the phased plan in `PROMPTS.md` (kept locally, not
committed) is the source of truth for what to build next. When asked to "continue the
framework," read that file and pick up the next unchecked phase.

## Commands

```bash
dotnet build CrawlSage.slnx          # build everything
dotnet test  CrawlSage.slnx          # run all tests
dotnet test  CrawlSage.slnx --filter "FullyQualifiedName~Http"   # one area
dotnet fantomas src tests            # format F# (after: dotnet tool restore)
```

- Target framework: **net10.0**. SDK: .NET 10 (`global.json` is intentionally absent
  so CI can float to the latest 10.x).
- Tests use **xUnit**. Keep them **hermetic** — never hit the live network in a unit
  test; stub `Http.fetch` or feed canned HTML to parsers.

## Architecture & layering

Dependencies flow one direction. Lower layers never reference higher ones.

```
Types.fs   →  Http.fs   →  (Phase 2) Html.fs  →  (Phase 3) Spider engine  →  (Phase 5) Pipelines
domain        downloader    parsing DSL          queue/dedup/middleware       export/storage
```

- **`Types.fs`** — pure domain: `Request`, `Response`, `HttpVerb`. No I/O. Records +
  DUs only.
- **`Http.fs`** — the downloader. One shared `HttpClient`. Returns `Async<Response>`.
- **`Resilience.fs`** — retry/back-off/timeout/throttle, composed as wrappers *around*
  `Http.fetch` (via Polly) — never baked into `fetch` itself. `politeFetch` is the stack.
- **`Extract.fs`** — embedded-state / JSON extraction (`__NEXT_DATA__`, JSON-LD, assigned
  globals) for "dynamic" pages, no browser. `Renderer = Request -> Async<Response>` is the
  pluggable seam the engine speaks.
- Compile order matters in F#: files are listed top-to-bottom in `CrawlSage.fsproj`,
  and a file can only use what is declared **above** it. Add new files in dependency
  order.

## F# conventions

- **Records + DUs over classes.** Model data with records, choices with DUs. Reach for
  classes only at .NET interop boundaries.
- **Immutability + pipelines.** Transform with `|>`; return new values rather than
  mutating. `with`-copies for updates (see `Request.withHeader`).
- **`Async` for I/O**, threaded through `let!` / `use!`. Honour the ambient cancellation
  token (`let! token = Async.CancellationToken`) on every network call.
- **Smart-constructor modules.** Each type gets a same-named module of constructors and
  combinators (`Request.create`, `Request.withBody`, …). Keep the curried argument order
  pipe-friendly: the thing being transformed comes **last**.
- **XML doc comments (`///`)** on every public type and function — `GenerateDocumentation
  File` is on, and the docs site is generated from these.
- Match the style of the surrounding code; format with **Fantomas** before committing.

## Adding a dependency

Prefer the .NET ecosystem's best-in-class libraries and wrap them behind an F#-friendly
surface so callers never touch the raw API:

| Concern | Library | Wrapped in |
| --- | --- | --- |
| HTML parsing | AngleSharp | `Html.fs` (Phase 2) |
| Retry / back-off | Polly | `Resilience.fs` (Phase 1) |
| Embedded data / JSON | System.Text.Json (in-box) | `Extract.fs` (Phase 4a) |
| Data frames / export | Deedle, CsvHelper | `Export.fs` (Phase 5) |

Add packages with `dotnet add <proj> package <name>`; pin versions in the `.fsproj`.

**No browser in core.** Dynamic pages are handled by extracting embedded state / JSON
(`Extract.fs`), not by driving a browser. If a real browser is ever truly needed, it lives
in a separate **opt-in adapter** implementing `Renderer` — never a core dependency. This is
deliberate: wrapping Playwright would make CrawlSage "just another wrapper."

## Skills

`.claude/skills/` holds task-specific skills. Prefer them over improvising:

- `new-spider`, `parse-html`, `dynamic-page`, `session-auth`, `data-export`, `crawl-ops`.

When you add a capability to the framework, update the matching skill so it teaches the
*current* API.

## Guardrails

- **Crawl ethically.** Default to respecting `robots.txt`, sane rate limits, and a
  site's ToS. Do not write code whose purpose is to defeat anti-abuse systems, solve
  CAPTCHAs for evasion, or scrape behind authentication you don't own. Throttling,
  proxy *rotation for resilience*, and back-off are fine; stealth-for-abuse is not.
- **No secrets in the repo.** Credentials, cookies and proxy lists go in `.env` /
  `*.local.json` (git-ignored), never committed.
- **Keep CI green.** Run `dotnet test CrawlSage.slnx` before declaring a change done.
- **Don't push without being asked.** The remote and release flow are the maintainer's
  call.
