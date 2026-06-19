module CrawlSage.Tests.Resilience

open System
open Xunit
open CrawlSage

let private respondWith status headers : Response =
    { Request = Request.create "https://example.com"
      StatusCode = status
      Body = ""
      Headers = headers }

let private respond status = respondWith status Map.empty

/// Zero back-off so timing-free tests stay fast and deterministic.
let private fast =
    { Resilience.RetryOptions.Default with
        MaxRetries = 4
        BaseDelay = TimeSpan.Zero }

[<Fact>]
let ``withRetry retries a transient 503 then succeeds`` () =
    let mutable calls = 0

    let stub (_: Request) =
        async {
            calls <- calls + 1
            return respond (if calls < 3 then 503 else 200)
        }

    let fetch = Resilience.withRetryOptions fast stub
    let result = fetch (Request.create "https://example.com") |> Async.RunSynchronously
    Assert.Equal(200, result.StatusCode)
    Assert.Equal(3, calls) // 1 initial + 2 retries

[<Fact>]
let ``withRetry does not retry a 2xx success`` () =
    let mutable calls = 0
    let stub (_: Request) = async {
        calls <- calls + 1
        return respond 200 }

    let fetch = Resilience.withRetryOptions fast stub
    let result = fetch (Request.create "https://example.com") |> Async.RunSynchronously
    Assert.Equal(200, result.StatusCode)
    Assert.Equal(1, calls)

[<Fact>]
let ``withRetry does not retry a non-transient 404`` () =
    let mutable calls = 0
    let stub (_: Request) = async {
        calls <- calls + 1
        return respond 404 }

    let fetch = Resilience.withRetryOptions fast stub
    let result = fetch (Request.create "https://example.com") |> Async.RunSynchronously
    Assert.Equal(404, result.StatusCode)
    Assert.Equal(1, calls)

[<Fact>]
let ``withRetry gives up after MaxRetries and returns the last response`` () =
    let mutable calls = 0
    let stub (_: Request) = async {
        calls <- calls + 1
        return respond 503 }

    let fetch = Resilience.withRetryOptions { fast with MaxRetries = 3 } stub
    let result = fetch (Request.create "https://example.com") |> Async.RunSynchronously
    Assert.Equal(503, result.StatusCode)
    Assert.Equal(4, calls) // 1 initial + 3 retries

[<Fact>]
let ``withRetry honours Retry-After on 429`` () =
    let mutable calls = 0

    let stub (_: Request) =
        async {
            calls <- calls + 1

            return
                if calls < 2 then
                    respondWith 429 (Map.ofList [ "retry-after", [ "0" ] ])
                else
                    respond 200
        }

    let fetch = Resilience.withRetryOptions fast stub
    let result = fetch (Request.create "https://example.com") |> Async.RunSynchronously
    Assert.Equal(200, result.StatusCode)
    Assert.Equal(2, calls)

[<Fact>]
let ``withTimeout raises TimeoutException for a slow fetch`` () =
    let slow (_: Request) =
        async {
            do! Async.Sleep 5000
            return respond 200
        }

    let fetch = Resilience.withTimeout (TimeSpan.FromMilliseconds 100.0) slow

    Assert.Throws<TimeoutException>(fun () ->
        fetch (Request.create "https://example.com") |> Async.RunSynchronously |> ignore)
    |> ignore

[<Fact>]
let ``withTimeout passes through a fast fetch`` () =
    let quick (_: Request) = async { return respond 200 }
    let fetch = Resilience.withTimeout (TimeSpan.FromSeconds 5.0) quick
    let result = fetch (Request.create "https://example.com") |> Async.RunSynchronously
    Assert.Equal(200, result.StatusCode)

[<Fact>]
let ``throttle caps concurrency at the configured limit`` () =
    let mutable current = 0
    let mutable peak = 0
    let sync = obj ()

    let stub (_: Request) =
        async {
            lock sync (fun () ->
                current <- current + 1
                peak <- max peak current)

            do! Async.Sleep 100
            lock sync (fun () -> current <- current - 1)
            return respond 200
        }

    let fetch = Resilience.throttle 3 stub

    [ for _ in 1..9 -> fetch (Request.create "https://example.com") ]
    |> Async.Parallel
    |> Async.RunSynchronously
    |> ignore

    Assert.True(peak <= 3, $"peak concurrency was {peak}, expected <= 3")
    Assert.True(peak >= 2, $"expected genuine concurrency, peak was {peak}")
