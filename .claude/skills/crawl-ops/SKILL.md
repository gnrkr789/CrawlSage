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

Before crawling a host, fetch `/robots.txt` and honour `Disallow` for your User-Agent.
Cache it per host. Treat a missing/unreachable `robots.txt` as "allowed", a `Disallow:
/` as "do not crawl". The `parse-html` skill is not needed — `robots.txt` is line-based.

## User-Agent & proxy rotation

```fsharp
let userAgents = [ "Mozilla/5.0 ..."; "Mozilla/5.0 ..." ]

/// Stamp a rotating, honest User-Agent on each request.
let withRotatingUa (uas: string list) =
    let mutable i = 0
    fun (request: Request) ->
        let ua = uas.[i % uas.Length]
        i <- i + 1
        request |> Request.withHeader "User-Agent" ua
```

For proxies, build `HttpClient` instances over `HttpClientHandler(Proxy = ...)` and
round-robin them — useful for *resilience and geo-distribution*, not for evading bans.

## Composing the stack

Stack the wrappers so every fetch is throttled, retried and timed out:

```fsharp
let politeFetch =
    Http.fetch
    |> Resilience.withRetry
    |> throttle 4
```

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
