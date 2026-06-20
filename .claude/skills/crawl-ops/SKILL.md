---
name: crawl-ops
description: Make a CrawlSage crawl robust and polite — retries, back-off, throttling/rate limits, timeouts, robots.txt, proxy and User-Agent rotation. Use when a crawl is getting blocked/rate-limited, failing intermittently, hammering a site too fast, or needs to respect robots.txt. Covers Polly-based resilience composed around Http.fetch.
---

# crawl-ops

Production crawling is mostly *operations*: don't hammer sites, recover from transient
failures, and respect the rules. In CrawlSage these are **wrappers composed around
`Http.fetch`**, never flags baked into it.

> Throttling, back-off and proxy *rotation for resilience* are good citizenship.
> Defeating anti-abuse systems, CAPTCHA-solving for evasion, or ignoring a site's ToS
> is out of scope — keep crawls polite and lawful.

## Retry with exponential back-off (Polly)

```bash
dotnet add src/CrawlSage/CrawlSage.fsproj package Polly
```

```fsharp
namespace CrawlSage

open System
open Polly

module Resilience =

    /// Retry transient failures with exponential back-off + jitter.
    let private policy =
        Policy
            .Handle<exn>()
            .WaitAndRetryAsync(
                retryCount = 4,
                sleepDurationProvider =
                    fun attempt -> TimeSpan.FromSeconds(Math.Pow(2.0, float attempt)))

    /// Wrap any fetch with retry/back-off.
    let withRetry (fetch: Request -> Async<Response>) (request: Request) : Async<Response> =
        async {
            return!
                policy.ExecuteAsync(fun () ->
                    fetch request |> Async.StartAsTask)
                |> Async.AwaitTask
        }
```

## Throttling (rate limit)

Cap concurrency and space out requests so you stay polite:

```fsharp
open System.Threading

/// Allow at most `n` requests in flight at once.
let throttle (n: int) (fetch: Request -> Async<Response>) =
    let gate = new SemaphoreSlim(n)
    fun request ->
        async {
            do! gate.WaitAsync() |> Async.AwaitTask
            try return! fetch request
            finally gate.Release() |> ignore
        }
```

Add a fixed delay between requests to one host, or use a token-bucket limiter for
requests-per-second.

## robots.txt

`Robots.fs` ships this. The format is line-based, so there's no HTML parsing — it groups
rules by `User-agent` and honours `Allow` / `Disallow` (longest match wins, with `*`/`$`
wildcards) and `Crawl-delay`. A missing or unreachable `robots.txt` is treated as
"allowed"; `Disallow: /` means "stay out".

```fsharp
// Pure parse + check (hermetic — feed it canned text):
let rules = Robots.parse "User-agent: *\nDisallow: /private"
Robots.isAllowed "CrawlSage" "/private/x" rules   // false
Robots.crawlDelay "CrawlSage" rules               // TimeSpan option

// Per-host cache — fetches each host's robots.txt once, over any Renderer:
let cache = Robots.Cache(Resilience.politeFetch, "CrawlSage")
let ok = cache.IsAllowed "https://site/page" |> Async.RunSynchronously
```

You rarely call these directly — `Spider.crawlPolitely` (below) wires the cache into the
engine so disallowed URLs are skipped before they're ever fetched.

## User-Agent & proxy rotation

`Rotation.fs` ships honest, round-robin rotation — for *resilience and geo-distribution*,
not for evading bans.

```fsharp
// Stamp a rotating, honest User-Agent on each request before it's fetched:
let fetch =
    Http.fetch
    |> Rotation.withRotatingUserAgent [ "CrawlSage/0.1 (+contact)"; "CrawlSage/0.1 (alt)" ]

// One HttpClient per proxy, round-robined (egress resilience / geo-distribution):
let viaProxies = Rotation.proxiedFetch [ "http://proxy-a:8080"; "http://proxy-b:8080" ]

// The primitive both use — a thread-safe round-robin (also handy for proxy selection):
let next = Rotation.cycle [ "a"; "b" ]   // unit -> 'a option
```

## Composing the stack

Stack the wrappers so every fetch is throttled, retried and timed out:

```fsharp
let politeFetch =
    Http.fetch
    |> Resilience.withRetry
    |> throttle 4
```

## Engine-level politeness

`Spider.crawlPolitely` (and the default `Spider.crawl`) layer robots.txt + per-host pacing
on top of any fetch — disallowed URLs are dropped before they're fetched, and one host is
never hit faster than `PerHostDelay` (a host's `Crawl-delay` overrides it upward).

```fsharp
// Polite by default: robots-respecting, ≤ 1 request/host/second.
spider |> Spider.crawl |> Async.RunSynchronously

// Tune it — slower per host, or robots off for a site you own:
let politeness = { Politeness.Default with PerHostDelay = TimeSpan.FromSeconds 3.0 }
Spider.crawlPolitely politeness Resilience.politeFetch spider |> Async.RunSynchronously
```

Need pacing without the engine? `Robots.perHostDelay (TimeSpan.FromSeconds 1.0)` is a
fetch wrapper you can drop into the stack above.

## Observability

The engine emits `CrawlEvent`s (`Fetched` / `Skipped` / `Failed`) to `SpiderOptions.OnEvent`.
Wire `Stats.console` to log, or `Stats.collector` to tally:

```fsharp
let stats, handle = Stats.collector ()
let spider = { spider with Options = { spider.Options with OnEvent = handle } }
spider |> Spider.crawl |> Async.RunSynchronously
printfn "fetched %d, skipped %d, failed %d" stats.Fetched stats.Skipped stats.Failed
```

A per-page fetch failure is reported as `Failed` and the crawl continues — one bad page
never aborts the run (cancellation still propagates).

## Timeouts

Set `HttpClient.Timeout` (or a Polly timeout policy) so a single hung request can't
stall the whole crawl. Default to something tight (10–30s) and honour the ambient
cancellation token end-to-end.

## Guidance

- **Start slow.** Low concurrency + a per-host delay; raise only if the site tolerates
  it. Getting blocked is usually self-inflicted.
- **Back off on 429/503**, not just exceptions — read `Retry-After` when present.
- **Make failures observable** — log status, host and attempt so a stuck crawl is
  diagnosable.
- **Keep policy composable** — each concern is one wrapper; never entangle them inside
  `fetch`.
