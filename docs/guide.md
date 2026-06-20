---
layout: default
title: Guide
---

# Guide

[← Home](index.html) &nbsp;·&nbsp; [Getting started](getting-started.html) &nbsp;·&nbsp; [Architecture](architecture.html) &nbsp;·&nbsp; [Cookbook](cookbook.html)

The full API, module by module, with runnable snippets. Every example assumes `open CrawlSage`.
Functions are curried with the thing being transformed **last**, so they pipe with `|>`, and
lookups return `option` instead of null.

## Contents

- [Requests & responses](#requests--responses)
- [Fetching (`Http`)](#fetching-http)
- [URLs (`Url`)](#urls-url)
- [Resilience](#resilience)
- [Rotation](#rotation)
- [Sessions & login (`Session`)](#sessions--login-session)
- [Parsing HTML (`Html`)](#parsing-html-html)
- [Extracting embedded data (`Extract`)](#extracting-embedded-data-extract)
- [robots.txt (`Robots`)](#robotstxt-robots)
- [Sitemaps (`Sitemap`)](#sitemaps-sitemap)
- [The frontier (`Frontier`)](#the-frontier-frontier)
- [The crawl engine (`Spider`)](#the-crawl-engine-spider)
- [Observability (`CrawlEvent` / `Stats`)](#observability-crawlevent--stats)
- [Output (`Export`)](#output-export)
- [JavaScript rendering (`CrawlSage.Browser`)](#javascript-rendering-crawlsagebrowser)

---

## Requests & responses

A `Request` is a URL plus everything needed to fetch it; a `Response` is what comes back.
Both are immutable records — build requests with the `Request` combinators.

```fsharp
let request =
    Request.create "https://example.com"          // a GET request
    |> Request.withHeader "Accept-Language" "en"
    |> Request.withMeta "category" "news"          // user data threaded to your parser
    |> Request.withBody """{"q":"f#"}"""           // sets the method to POST

// Response: Request · StatusCode · Body · Headers (Map<string, string list>)
let response = Http.fetch request |> Async.RunSynchronously
if response.IsSuccess then printfn "%d bytes" response.Body.Length
```

Two seams the whole framework speaks:

```fsharp
type Renderer = Request -> Async<Response>   // any download/render strategy
type Sink<'T> = 'T -> unit                   // any output destination
```

## Fetching (`Http`)

```fsharp
Http.fetch request                 // Async<Response>
Http.getString "https://x"         // Async<string> — convenience GET
Http.fetchBytes request            // Async<byte[]> — images, PDFs, binary
Http.download "out.pdf" request    // Async<unit>  — stream to a file, no buffering
Http.fetchWith myClient request    // fetch over a specific HttpClient (e.g. a proxied one)
```

The shared client negotiates gzip/deflate/brotli and decompresses transparently.

## URLs (`Url`)

```fsharp
Url.resolve "https://s.com/list" "/page/2/"   // "https://s.com/page/2/"
Url.normalize "https://S.com:443/a#frag"      // "https://s.com/a"  (dedup form)
Url.host "https://s.com/a"                     // "s.com"
Url.isSameHost "https://s.com/a" "https://s.com/b"  // true
```

## Resilience

Composable wrappers of type `Renderer -> Renderer` — stack them over any fetch.

```fsharp
let fetch =
    Http.fetch
    |> Resilience.withTimeout (TimeSpan.FromSeconds 30.0)
    |> Resilience.withRetry           // back-off + jitter on 408/429/5xx, honours Retry-After
    |> Resilience.throttle 4          // ≤ 4 in flight at once

// Or the ready-made stack (throttle ∘ retry ∘ timeout ∘ Http.fetch):
Resilience.politeFetch

// Tune the retry schedule:
let fetch2 = Http.fetch |> Resilience.withRetryOptions { Resilience.RetryOptions.Default with MaxRetries = 6 }
```

## Rotation

Honest User-Agent / proxy rotation for load-spreading (not evasion).

```fsharp
let fetch =
    Resilience.politeFetch
    |> Rotation.withRotatingUserAgent [ "MyBot/1.0 (+contact)"; "MyBot/1.0 (alt)" ]

let viaProxies = Rotation.proxiedFetch [ "http://proxy-a:8080"; "http://proxy-b:8080" ]
let next = Rotation.cycle [ "a"; "b" ]    // unit -> 'a option (the round-robin primitive)
```

## Sessions & login (`Session`)

A session is one client with its own cookie jar, so a login persists across requests.

```fsharp
let session = Session.create ()

Session.login session "https://site/login"
    [ "username", user; "password", pass ]
|> Async.RunSynchronously
|> ignore

// Session.fetch session is a Renderer — drive the engine with it:
Spider.crawlWith (Session.fetch session) spider |> Async.RunSynchronously

Session.cookies session "https://site/"     // Map<string,string> it would send
Session.addCookie session "https://site/" "token" "abc"
Session.save session "session.local.json"    // persist; Session.load to resume authenticated
```

## Parsing HTML (`Html`)

Forgiving CSS selectors over AngleSharp; everything is `option`, curried node-last.

```fsharp
let doc = Html.parse response.Body

doc |> Html.select ".price"          // IElement option (first match)
doc |> Html.selectAll ".quote"       // IElement list (all matches, document order)
element |> Html.text                  // trimmed text content
element |> Html.attr "href"           // string option
element |> Html.attrOr "" "href"      // string with a fallback
doc |> Html.links response.Request.Url  // absolute, de-duped links, ready to Follow
```

Scrape structured rows by nesting `select` inside `selectAll`:

```fsharp
let quotes =
    doc
    |> Html.selectAll ".quote"
    |> List.map (fun q ->
        {| Text = q |> Html.select ".text" |> Option.map Html.text |> Option.defaultValue ""
           Author = q |> Html.select ".author" |> Option.map Html.text |> Option.defaultValue "" |})
```

## Extracting embedded data (`Extract`)

Most "dynamic" pages ship their data as JSON inside the HTML. Pull it instead of rendering.

```fsharp
let doc = Html.parse response.Body

doc |> Extract.nextData                       // <script id="__NEXT_DATA__"> (Next.js)
doc |> Extract.jsonLd                          // every application/ld+json block (list)
doc |> Extract.assignedJson "__NUXT__"         // window.x = {…} or var data = […]
doc |> Extract.scriptJson "script#state"       // JSON in any <script> you select
Extract.json """{"a":1}"""                     // parse a raw string

// Navigate the JSON option-style, like Html:
doc
|> Extract.nextData
|> Option.bind (Extract.path [ "props"; "pageProps"; "title" ])
|> Option.bind Extract.asString                // string option

// Arrays: asList enumerates, prop reads a field
doc
|> Extract.assignedJson "data"
|> Option.map Extract.asList
|> Option.defaultValue []
|> List.choose (Extract.prop "text" >> Option.bind Extract.asString)
```

## robots.txt (`Robots`)

The engine consults robots for you (see [Spider](#the-crawl-engine-spider)); these are the
pieces if you need them directly.

```fsharp
let rules = Robots.parse robotsTxtBody
Robots.isAllowed "MyBot" "/private/x" rules    // bool (longest-match, Allow beats Disallow)
Robots.crawlDelay "MyBot" rules                 // TimeSpan option

// Per-host cache (fetches each host's robots.txt once, over any Renderer):
let cache = Robots.Cache(Resilience.politeFetch, "MyBot")
cache.IsAllowed "https://site/page" |> Async.RunSynchronously

// Per-host pacing as a composable wrapper:
let paced = Http.fetch |> Robots.perHostDelay (TimeSpan.FromSeconds 1.0)
```

## Sitemaps (`Sitemap`)

Seed a crawl from a site's own URL list.

```fsharp
Sitemap.parse xmlBody                          // <loc> URLs (urlset or sitemapindex)
Sitemap.fromRobotsTxt robotsBody               // the Sitemap: directives
Sitemap.fetchUrls Resilience.politeFetch "https://site/sitemap.xml"
|> Async.RunSynchronously                       // expands a sitemapindex into all page URLs

let seeds =
    Sitemap.fetchUrls Resilience.politeFetch "https://site/sitemap.xml"
    |> Async.RunSynchronously
    |> List.map Request.create
```

## The frontier (`Frontier`)

The pending queue + dedup filter the engine pulls from. Swap it for persistence or bounding.

```fsharp
Frontier.inMemory ()              // default: FIFO + dedup
Frontier.bounded 100_000          // memory-capped (drops past the cap)
Frontier.persistent "data/state"  // disk-backed: resume after a stop or crash
```

Pass one to `Spider.crawlOn` (below) for a resumable or bounded crawl.

## The crawl engine (`Spider`)

A parser turns a page into items and follow-ups; the engine schedules, dedups, bounds depth
and runs your pipeline.

```fsharp
type Quote = { Text: string; Author: string }

let parse (response: Response) : ParseResult<Quote> list =
    let doc = Html.parse response.Body
    let items =
        doc |> Html.selectAll ".quote"
        |> List.map (fun q ->
            Item { Text = q |> Html.select ".text" |> Option.map Html.text |> Option.defaultValue ""
                   Author = q |> Html.select ".author" |> Option.map Html.text |> Option.defaultValue "" })
    let follows =
        doc |> Html.links response.Request.Url |> List.map (Request.create >> Follow)
    items @ follows

let spider =
    { Seeds = [ Request.create "https://quotes.toscrape.com/" ]
      Parse = parse
      Pipeline = Export.appendJsonLine "data/quotes.jsonl"   // a Sink<Quote>
      Options = { SpiderOptions.Default with MaxDepth = 5 } }
```

Run it — pick the entry point you need:

```fsharp
Spider.crawl spider                                   // production: polite (robots + per-host pacing)
Spider.crawlWith myFetch spider                        // explicit fetch, no gate (tests / custom middleware)
Spider.crawlPolitely Politeness.Default myFetch spider // polite over a fetch you choose
Spider.crawlOn (Frontier.persistent "state") Politeness.Default Resilience.politeFetch spider  // resumable
```

`Politeness` controls the robots gate and pacing:

```fsharp
let politeness = { Politeness.Default with PerHostDelay = TimeSpan.FromSeconds 2.0; RespectRobots = true }
```

`SpiderOptions`: `MaxConcurrency`, `MaxDepth`, and `OnEvent` (below). A failed page is
reported and skipped — one bad URL never aborts the crawl; cancellation still propagates.

## Observability (`CrawlEvent` / `Stats`)

The engine emits `CrawlEvent`s (`Fetched` / `Skipped` / `Failed`) to `SpiderOptions.OnEvent`.

```fsharp
let stats, handle = Stats.collector ()
let spider = { spider with Options = { spider.Options with OnEvent = handle } }

Spider.crawl spider |> Async.RunSynchronously
printfn "fetched %d, skipped %d, failed %d" stats.Fetched stats.Skipped stats.Failed

// Or just log:  { spider.Options with OnEvent = Stats.console }
```

## Output (`Export`)

Sinks get scraped items *out*. Partially apply a path to get a `Sink<'T>` for the pipeline.

```fsharp
Export.toJson "out.json" items          // whole batch → pretty JSON array
Export.appendJsonLine "out.jsonl" item   // one item per line (stream-friendly)  ← a Sink
Export.toCsv "out.csv" items             // CSV (one column per record field)
Export.saveBytes "logo.png" bytes        // binary files
Export.console item                       // print while developing  ← a Sink
Export.fanout [ Export.appendJsonLine "a.jsonl"; Export.console ]  // one item → many sinks
Export.toFrame items                      // Deedle data frame for group/pivot/aggregate
```

## JavaScript rendering (`CrawlSage.Browser`)

For pages that truly build their DOM client-side. **Opt-in** — a separate project, so the
core stays browser-free. Reference `src/CrawlSage.Browser` and install the browser once
(`pwsh bin/Debug/net10.0/playwright.ps1 install chromium`).

```fsharp
open CrawlSage.Browser

// Browser.render is a Renderer — drop it into the engine like any other:
Spider.crawlWith Browser.render spider |> Async.RunSynchronously
```

Climb the [rendering ladder](architecture.html) cheapest-first: static fetch → `Extract` →
API replay → this. Reach for a browser only when the rest can't get the data.
