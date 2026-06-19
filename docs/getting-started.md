---
layout: default
title: Getting started
---

# Getting started

[← Home](index.html)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Git

## Clone & build

```bash
git clone https://github.com/gnrkr789/CrawlSage.git
cd CrawlSage
dotnet build CrawlSage.slnx
dotnet test  CrawlSage.slnx
```

## Your first fetch

CrawlSage's downloader returns an `Async<Response>`, so it composes with everything
in F#'s async toolbox.

```fsharp
open CrawlSage

[<EntryPoint>]
let main _ =
    async {
        let! response = Http.fetch (Request.create "https://example.com")
        printfn "Status: %d" response.StatusCode
        printfn "Bytes:  %d" response.Body.Length
        return 0
    }
    |> Async.RunSynchronously
```

Need a quick one-liner? `Http.getString` skips straight to the body:

```fsharp
let html = Http.getString "https://example.com" |> Async.RunSynchronously
```

## Building a request

`Request` is an immutable record; the `Request` module gives you pipe-friendly
combinators:

```fsharp
let req =
    Request.create "https://httpbin.org/post"
    |> Request.withHeader "Accept" "application/json"
    |> Request.withMeta "source" "example"
    |> Request.withBody """{"hello":"world"}"""   // switches the verb to POST
```

## Resilient fetching

`Resilience` wraps any fetch with retries, timeouts and throttling — composed, not
configured. `politeFetch` is the batteries-included stack:

```fsharp
open CrawlSage

let response =
    Request.create "https://example.com"
    |> Resilience.politeFetch          // throttle ∘ retry ∘ timeout ∘ Http.fetch
    |> Async.RunSynchronously
```

Or build your own stack:

```fsharp
let myFetch =
    Http.fetch
    |> Resilience.withTimeout (System.TimeSpan.FromSeconds 10.0)
    |> Resilience.withRetry
    |> Resilience.throttle 8
```

## What's next

The parsing DSL, spider engine, dynamic renderer and data pipelines are built
phase by phase. Follow the
[step-by-step prompts](https://github.com/gnrkr789/CrawlSage/blob/main/PROMPTS.md)
to grow the framework — or read the [architecture](architecture.html) first.
