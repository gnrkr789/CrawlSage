---
layout: default
title: Getting started
---

# Getting started

[← Home](index.html) &nbsp;·&nbsp; [Guide](guide.html)

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

## Crawling with the engine

`Spider` turns seeds + a parser into a full breadth-first crawl with dedup, depth limits
and bounded concurrency. A parser returns `Item`s (→ the pipeline) and `Follow`s (→ back
to the scheduler):

```fsharp
open CrawlSage

type Story = { Title: string }

let parse (response: Response) : ParseResult<Story> list =
    let doc = Html.parse response.Body

    let stories =
        doc
        |> Html.selectAll ".titleline > a"
        |> List.map (fun a -> Item { Title = Html.text a })

    let next =
        doc
        |> Html.selectAll "a.morelink"
        |> List.choose (Html.attr "href")
        |> List.map (Request.create >> Follow)

    stories @ next

let spider =
    { Seeds = [ Request.create "https://news.ycombinator.com" ]
      Parse = parse
      Pipeline = (fun s -> printfn "%s" s.Title)
      Options = { SpiderOptions.Default with MaxDepth = 1 } }

Spider.crawl spider |> Async.RunSynchronously
```

`Spider.crawl` fetches through `Resilience.politeFetch`; inject a stub with
`Spider.crawlWith` for hermetic tests.

## Saving results

`Spider.Pipeline` is a `Sink<'Item>`, so an `Export` sink streams a crawl straight to disk:

```fsharp
let spider =
    { Seeds = [ Request.create "https://news.ycombinator.com" ]
      Parse = parse
      Pipeline = Export.fanout [ Export.appendJsonLine "data/stories.jsonl"; Export.console ]
      Options = { SpiderOptions.Default with MaxDepth = 1 } }

Spider.crawl spider |> Async.RunSynchronously
```

Already hold everything in memory? `Export.toJson` / `toCsv` write a batch, and
`Export.toFrame` opens it as a Deedle data frame for analysis.

## What's next

Read the [architecture](architecture.html) for how the layers fit together, or browse the
[cookbook](cookbook.html) for runnable recipes.
