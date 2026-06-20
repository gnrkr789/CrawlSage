---
name: dynamic-page
description: Handle JavaScript-heavy / "dynamic" pages in CrawlSage WITHOUT a browser — by extracting the data SSR/hydration frameworks embed in the page (Next.js __NEXT_DATA__, Nuxt __NUXT__, JSON-LD, inline state) or replaying the JSON API behind it. Use when a page looks empty in Http.fetch, renders client-side, loads via XHR, or is a SPA. Covers the Extract module and the rendering ladder.
---

# dynamic-page

CrawlSage's stance: **don't render — extract.** A real browser is heavy, fragile and a
foreign dependency that would make CrawlSage "just a browser wrapper." Most "dynamic"
pages aren't: SSR / hydration frameworks ship the data *inside* the HTML as JSON, or the
page fetches it from a discoverable JSON API. Get the data behind the page and you skip the
browser entirely.

## The rendering ladder — cheapest first

1. **Static** — `Http.fetch`. Try it first; if the data is in the HTML, you're done.
2. **Embedded state / JSON** — `Extract` (this skill). `__NEXT_DATA__`, `__NUXT__`,
   `__INITIAL_STATE__`, JSON-LD, inline `<script>` JSON. **No JS engine.** Covers a large
   fraction of "dynamic" sites.
3. **Replay the API** — find the XHR/JSON endpoint the page calls (DevTools → Network) and
   hit it directly with `Http.fetch`; it usually returns clean JSON.
4. **In-process JS** *(future, opt-in)* — a managed JS engine (Jint) on AngleSharp's DOM,
   for pages that truly compute the DOM client-side. Best-effort, no browser binary.
5. **External browser** *(opt-in adapter, not core)* — last resort for heavy SPAs / anti-bot.
   A separate package implementing `Renderer`; the core stays browser-free.

A renderer is just `Renderer = Request -> Async<Response>`, so every rung plugs into
`Spider.crawlWith` with zero engine changes.

## Extract embedded data (shipped — Phase 4a)

`src/CrawlSage/Extract.fs`:

| Function | Gets |
| --- | --- |
| `nextData doc` | `<script id="__NEXT_DATA__">` JSON (Next.js) |
| `assignedJson "data" doc` | a global assigned an **object or array** — `window.__NUXT__ = {…}`, `__INITIAL_STATE__ = {…}`, `var data = […]` |
| `jsonLd doc` | all `application/ld+json` blocks |
| `scriptJson selector doc` | JSON inside any `<script>` you select |
| `json raw` | parse a raw string → `JsonNode option` |

Navigate the JSON option-style, just like `Html`: `prop`, `path`, `asString`, `asList`.

```fsharp
open CrawlSage

let doc =
    Http.getString "https://a-next-site.example" |> Async.RunSynchronously |> Html.parse

let title =
    doc
    |> Extract.nextData
    |> Option.bind (Extract.path [ "props"; "pageProps"; "title" ])
    |> Option.bind Extract.asString
```

Use it inside a `Spider` parser exactly like the HTML path — `Extract` and `Html` compose.

When a page builds its list from an array assigned to a global — `var data = [ {…}, {…} ]`,
common on client-rendered sites — `assignedJson` lifts the array; enumerate it with `asList`:

```fsharp
let quotes =
    doc
    |> Extract.assignedJson "data"            // var data = [ … ]
    |> Option.map Extract.asList
    |> Option.defaultValue []
    |> List.choose (Extract.prop "text" >> Option.bind Extract.asString)
```

The runnable [`samples/QuotesJs`](../../../samples/QuotesJs) recipe does exactly this against
`quotes.toscrape.com/js`, whose static HTML ships no `.quote` markup at all.

## Replay the JSON API

If step 2 finds nothing, open the site, watch DevTools → Network → Fetch/XHR, copy the
request that returns the data, and fetch it directly:

```fsharp
let payload =
    Request.create "https://api.example.com/v1/items?page=1"
    |> Request.withHeader "Accept" "application/json"
    |> Http.fetch
    |> Async.RunSynchronously
    |> fun r -> Extract.json r.Body
```

Faster and far more stable than scraping rendered HTML.

## When you genuinely need a browser

If the data is neither embedded nor in a reachable API (heavy client-only SPA, anti-bot),
that's rungs 4–5 — **opt-in and out of the core.** Implement a `Renderer` in a separate
adapter so CrawlSage's core never takes a browser dependency. Don't reach for one until
rungs 1–3 are exhausted.

## Guidance

- **Exhaust 1–3 before 4–5.** Extraction / API replay solves most "dynamic" pages at a
  fraction of the cost and breaks far less often.
- **Stay option-style.** `Extract` mirrors `Html`: everything returns `option`; navigate
  with `path` / `prop`.
- **Test against canned HTML / JSON** — feed string literals to `Html.parse` /
  `Extract.json` so tests stay hermetic.
- **Be polite on every rung** — see the `crawl-ops` skill.
