# CrawlSage — Step-by-step build prompts

This is the roadmap **and** the construction kit. Each phase below has a single,
ready-to-paste prompt for [Claude Code](https://claude.com/claude-code). Run them in
order: every phase builds on the last and ends with a green `dotnet test`.

**How to use:** open this repo in Claude Code, copy a phase's prompt verbatim into the
prompt box, and let it work. The prompts deliberately point at the skills in
`.claude/skills/` — Claude will load the matching one. After each phase, review the
diff, run the tests, and commit before moving on.

**Conventions every prompt assumes** (also in `CLAUDE.md`):
- F# idioms: records + DUs, immutability, `|>` pipelines, `Async` for I/O.
- Wrap best-in-class .NET libraries behind a thin F# surface.
- Keep `dotnet test CrawlSage.slnx` green; tests stay hermetic (no live network).
- Add new `.fs` files to `CrawlSage.fsproj` in dependency order.

---

## Phase 0 — Scaffold ✅ (done)

Repository, CI, GitHub Pages docs, six skills, and the core types (`Request`,
`Response`, `Http.fetch`). Nothing to do — this is your starting point.

---

## Phase 1 — Resilient downloader ✅ (done)

**Shipped:** `src/CrawlSage/Resilience.fs` — `withRetry` / `withRetryOptions`,
`withTimeout`, `throttle`, and `politeFetch` (throttle ∘ retry ∘ timeout ∘ `Http.fetch`),
covered by 8 hermetic tests. The original build prompt is kept below for reference.

**Goal:** retries, back-off, throttling and timeouts, composed *around* `Http.fetch`.

```text
Add a resilience layer to CrawlSage's downloader. Use the `crawl-ops` skill.

Create src/CrawlSage/Resilience.fs (added to CrawlSage.fsproj after Http.fs) with
composable wrappers of type (Request -> Async<Response>) -> (Request -> Async<Response>):
- withRetry: exponential back-off + jitter on transient failures and on 429/503,
  honouring Retry-After when present (use Polly).
- withTimeout: per-request timeout that cancels via the ambient cancellation token.
- throttle (maxConcurrent): a SemaphoreSlim gate.
Expose a `politeFetch` that stacks all three over Http.fetch.

Add hermetic xUnit tests that drive these wrappers with a stub fetch function (no real
network): assert retry count, that a non-transient success isn't retried, and that the
throttle caps concurrency. Keep dotnet test green.
```

**Done when:** `Resilience.fs` exists, `politeFetch` composes retry+timeout+throttle,
and new tests pass.

---

## Phase 2 — Parsing DSL ✅ (done)

**Shipped:** `src/CrawlSage/Html.fs` — `parse`, `select`, `selectAll`, `text`, `attr`,
`attrOr` over AngleSharp, curried node-last and `option`-returning, with 6 hermetic
tests. The original build prompt is kept below for reference.

**Goal:** BeautifulSoup-grade extraction — a concise selector API over AngleSharp.

```text
Add an HTML parsing module to CrawlSage. Use the `parse-html` skill.

Add the AngleSharp package and create src/CrawlSage/Html.fs (after Http.fs) exposing:
parse, select (CSS -> IElement option), selectAll, text, attr. Keep everything
null-free with Option, and curried node-last so queries pipe.

Add hermetic tests that parse a canned HTML string literal and assert selection, text
extraction and attribute reads. Update the parse-html skill if the API differs. Keep
dotnet test green.
```

**Done when:** `html |> Html.parse |> Html.selectAll "h2" |> List.map Html.text` works
and is tested against canned HTML.

---

## Phase 3 — Spider engine

**Goal:** the Scrapy-style core — queue, dedup, scheduler, parser callbacks, pipelines.

```text
Build CrawlSage's crawl engine. This is the heart of the framework.

Design (records + DUs, no inheritance):
- A parser is a function Response -> ParseResult list, where
  ParseResult = Item of obj | Follow of Request.
- A Spider record bundles: seed requests, the parser, an item pipeline (Item -> unit),
  and options (maxConcurrency, maxDepth).
- A Scheduler with an in-memory frontier queue and a dedup filter (HashSet of a request
  fingerprint: method + normalised URL).
- An engine loop: pull from the scheduler, fetch via Phase 1's politeFetch, run the
  parser, route Items to the pipeline and Follows back to the scheduler (respecting
  dedup + maxDepth), until the frontier drains. Honour cancellation throughout.

Put this in src/CrawlSage/Spider.fs (after Html.fs). Add hermetic tests with a stub
fetch returning canned HTML: assert dedup prevents refetching, depth is bounded, and a
two-page paginated crawl visits both pages. Keep dotnet test green.
```

**Done when:** a tiny spider crawls a 2-page fixture end-to-end (seed → parse → follow →
pipeline) in a test, with dedup and depth limits working.

---

## Phase 4 — Dynamic pages

**Goal:** render JavaScript sites; handle infinite scroll and click-to-load.

```text
Add a Playwright-backed renderer to CrawlSage. Use the `dynamic-page` skill.

Add the Microsoft.Playwright package and create src/CrawlSage/Browser.fs (after Http.fs)
with: render (url -> Async<string>) returning fully-loaded HTML, a waitForSelector
helper, and a scrollToEnd helper for infinite scroll. Reuse one IBrowser across a batch.

Make the engine able to use Browser.render instead of Http.fetch for a spider flagged as
needing JS (add a `renderJs` option to the Spider record). Document the one-time
`playwright install chromium` step in the README. Tests here can be skipped/trait-gated
since they need a browser — mark them clearly. Keep the default dotnet test green.
```

**Done when:** a spider can opt into JS rendering, and the rendered HTML flows through
the same parse → pipeline path.

---

## Phase 5 — Data pipelines

**Goal:** get scraped data out — CSV / JSON / Parquet / DB — pandas-style if wanted.

```text
Add output sinks to CrawlSage. Use the `data-export` skill.

Create src/CrawlSage/Export.fs (after Spider.fs) with: toJson, appendJsonLine (JSON
Lines), toCsv (CsvHelper), and a `pipeline` combinator that fans an item out to a list
of sinks. Add a Deedle-based `toFrame`/`saveCsv` helper for post-processing.

Wire Export sinks as the Spider's item pipeline so a crawl writes results to disk under
data/ (git-ignored). Add hermetic tests writing to a temp path and reading the file
back. Keep dotnet test green.
```

**Done when:** a crawl's items land in a CSV/JSONL file via the spider's pipeline, tested
round-trip.

---

## Phase 6 — Crawl ops & politeness

**Goal:** robots.txt, rotation, and the operational polish that keeps crawls alive.

```text
Add operational middleware to CrawlSage. Use the `crawl-ops` skill.

Create src/CrawlSage/Robots.fs (parse robots.txt, per-host cache, isAllowed for a UA)
and src/CrawlSage/Rotation.fs (rotating User-Agent and proxy selection). Integrate a
robots.txt check + per-host delay into the engine's scheduler so disallowed URLs are
skipped and one host is never hammered. Frame everything as politeness/resilience, not
evasion.

Add hermetic tests: robots.txt Disallow is respected; UA rotation cycles. Keep dotnet
test green.
```

**Done when:** the engine refuses disallowed URLs, spaces out per-host requests, and
rotates UA — all tested.

---

## Phase 7 — Cookbook

**Goal:** the real-world examples F# is missing. Each is a runnable sample.

```text
Create a cookbook recipe as a runnable sample. Use the `new-spider` skill, plus the
skill matching the recipe (parse-html / dynamic-page / session-auth / data-export /
crawl-ops).

Build the "<RECIPE>" recipe from docs/cookbook.md as samples/<Name>/, added to the
solution and referencing src/CrawlSage. It must run with `dotnet run --project
samples/<Name>` against a real, crawl-friendly site (e.g. a docs site or
quotes.toscrape.com), be polite by default, and write its output under data/. Add a
short README in the sample folder explaining what it demonstrates.
```

Repeat per recipe: extract-a-list, follow-pagination, login+session, infinite-scroll,
export-to-csv, polite-crawl.

**Done when:** `samples/` has several self-contained crawlers, each runnable and listed
in `docs/cookbook.md`.

---

## Phase 8 — Packaging & release

**Goal:** ship it — NuGet package + versioned docs.

```text
Prepare CrawlSage for a 0.1 release.

Enable NuGet packaging on src/CrawlSage (PackageId, README, release notes, symbols).
Add a GitHub Actions release workflow that, on a vX.Y.Z tag, builds in Release, runs
tests, packs, and pushes to NuGet using a NUGET_API_KEY secret. Update README badges and
docs to reference the published package. Don't publish from this prompt — just wire the
workflow and explain the tag + secret steps.
```

**Done when:** a tag-triggered workflow packs and (given the secret) can publish, and the
README documents the release process.

---

## After each phase

```bash
dotnet test CrawlSage.slnx        # must be green
git add -A && git commit -m "Phase N: <summary>"
```

Then update the roadmap tables in `README.md` and `docs/` to tick the phase off.
