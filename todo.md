# CrawlSage — TODO & cross-machine handoff

A "resume here" note so this can be continued from any PC. CrawlSage is an **F#-first web
crawling & scraping framework** (the F# answer to DotnetSpider / Scrapy). Full vision in
[README.md](README.md); phase-by-phase build prompts in [PROMPTS.md](PROMPTS.md); working
conventions in [CLAUDE.md](CLAUDE.md).

## Start working on a fresh machine

```bash
git clone git@github.com:gnrkr789/CrawlSage.git
cd CrawlSage
dotnet test CrawlSage.slnx        # should be all green
```

- Needs the **.NET 10 SDK** (`dotnet --version` → 10.x). No `global.json`, so it floats to the latest 10.x.
- Open in Claude Code: the `.claude/skills/` are auto-available; type `/` to see them.
- **Pushing:** `gh` is not authenticated here, so push over SSH yourself —
  `git push` (the `origin` remote is already set). To use `gh` instead: `gh auth login` first.

## Status — Phases 0–7 done ✅ (52 tests green)

| Phase | What | File |
| --- | --- | --- |
| 0 | scaffold (CI, Pages docs, skills, roadmap) | — |
| 1 | resilient downloader (retry/back-off/timeout/throttle) | `src/CrawlSage/Resilience.fs` |
| 2 | HTML parsing DSL (AngleSharp) | `src/CrawlSage/Html.fs` |
| 3 | spider engine (BFS, dedup, depth, pipeline) | `src/CrawlSage/Spider.fs` |
| 4a | dynamic data — embedded-state / JSON extraction, **no browser** | `src/CrawlSage/Extract.fs` |
| 5 | output sinks: JSON / JSONL / CSV + Deedle | `src/CrawlSage/Export.fs` |
| 6 | crawl ops — robots.txt + per-host pacing (engine-wired), UA & proxy rotation | `src/CrawlSage/Robots.fs`, `src/CrawlSage/Rotation.fs` |
| 7 | cookbook — 3 runnable samples vs. quotes.toscrape.com | `samples/QuotesToCsv`, `samples/QuotesCrawl`, `samples/PoliteRotation` |

## Next up — pick one (prompts in [PROMPTS.md](PROMPTS.md))

- [ ] **Phase 8 — Packaging**: NuGet pack + tag-triggered release workflow, versioned docs.
- [ ] **Phase 7 (more recipes)**: login+session (`session-auth`), infinite-scroll (`dynamic-page`).
- [ ] **Phase 4b (optional)** — in-process JS renderer via Jint (managed, no browser binary).
      Only if extraction + API replay (Phase 4a) don't cover a target. Best-effort.

## Decisions to keep (don't regress)

- **No browser in the core.** Dynamic pages → extract embedded state / JSON (`Extract.fs`)
  or replay the API. A real browser, if ever needed, is an **opt-in `Renderer` adapter**,
  never a core dependency. (Wrapping Playwright would make CrawlSage "just another wrapper.")
- **Seams:** `Renderer = Request -> Async<Response>` (input) and `Sink<'T> = 'T -> unit`
  (output) — both in `Types.fs`. New strategies plug into `Spider.crawlWith` / `Spider.Pipeline`
  with zero engine changes.
- **F# idioms:** records + DUs, `option` over null, `|>` pipelines, node/item **last** for
  piping. Nullable-reference feature is **off**; `[<NoComparison; NoEquality>]` on records
  with function fields.
- **Every phase ends green:** `dotnet test CrawlSage.slnx`. Tests stay hermetic (no live network).

## House rules

- Commit per phase: `git add -A && git commit -m "Phase N: <summary>"`, then push.
- Add new `.fs` files to `CrawlSage.fsproj` in dependency order (a file sees only what's above it).
- Keep scraped output under `data/` (git-ignored). No secrets in the repo.
