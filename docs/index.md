---
layout: default
title: CrawlSage
---

# CrawlSage

**An F#-first web crawling & scraping framework for .NET.**
Scrapy-grade ergonomics, BeautifulSoup-grade convenience — with the type safety of F#.

[Getting started](getting-started.html){: .btn } &nbsp;
[Architecture](architecture.html){: .btn } &nbsp;
[Cookbook](cookbook.html){: .btn } &nbsp;
[GitHub](https://github.com/gnrkr789/CrawlSage){: .btn }

---

## Why CrawlSage?

F# can crawl the web today — `HttpClient`, AngleSharp, HtmlAgilityPack and Playwright
for .NET are all excellent. But next to Python (BeautifulSoup, Scrapy, Selenium,
Playwright) the F# story has gaps: no dedicated framework, few real-world examples,
verbose HTML parsing, sparse dynamic-page samples, weaker data post-processing.

CrawlSage closes those gaps with an F#-idiomatic API — records, discriminated unions,
pipelines and computation expressions instead of attributes and inheritance.

> CrawlSage is to F# what [DotnetSpider](https://github.com/dotnetcore/DotnetSpider)
> is to C#.

## Status

🚧 **Early development.** A buildable core ships today (`Request`, `Response`,
`Http.fetch`); the engine, parsing DSL, dynamic renderer and cookbook are built out
phase by phase. See the
[roadmap & build prompts](https://github.com/gnrkr789/CrawlSage/blob/main/PROMPTS.md).

## A taste

```fsharp
open CrawlSage

let body =
    Request.create "https://example.com"
    |> Request.withHeader "Accept-Language" "en"
    |> Http.fetch
    |> Async.RunSynchronously

printfn "%d — %d bytes" body.StatusCode body.Body.Length
```
