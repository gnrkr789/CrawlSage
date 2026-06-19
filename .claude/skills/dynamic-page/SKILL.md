---
name: dynamic-page
description: Render JavaScript-heavy pages in CrawlSage with Playwright for .NET. Use when a site needs a real browser — SPA/JS rendering, infinite scroll, content that loads after the initial HTML, "click to load more", or waiting for a selector. Covers setup, the F# Browser wrapper, and scroll/wait patterns.
---

# dynamic-page

When a page's content is rendered by JavaScript, `Http.fetch` only sees the empty
shell. Use **Microsoft.Playwright** to drive a real headless browser, then hand the
rendered HTML to the `parse-html` skill.

## Setup (Phase 4)

```bash
dotnet add src/CrawlSage/CrawlSage.fsproj package Microsoft.Playwright
# After building once, install the browser binaries:
dotnet build CrawlSage.slnx
pwsh src/CrawlSage/bin/Debug/net10.0/playwright.ps1 install chromium
```

Add `Browser.fs` to `CrawlSage.fsproj` after `Http.fs`.

```fsharp
namespace CrawlSage

open Microsoft.Playwright

/// A Playwright-backed renderer for JavaScript-heavy pages.
module Browser =

    /// Render a URL with a headless browser and return the fully-loaded HTML.
    let render (url: string) : Async<string> =
        async {
            use! pw = Playwright.CreateAsync() |> Async.AwaitTask
            let! browser = pw.Chromium.LaunchAsync() |> Async.AwaitTask
            let! page = browser.NewPageAsync() |> Async.AwaitTask
            let! _ = page.GotoAsync(url) |> Async.AwaitTask
            let! html = page.ContentAsync() |> Async.AwaitTask
            do! browser.CloseAsync() |> Async.AwaitTask
            return html
        }
```

## Wait for content

Don't scrape before the data arrives. Wait for a selector:

```fsharp
let! _ = page.WaitForSelectorAsync(".product-card") |> Async.AwaitTask
```

## Infinite scroll

Scroll until the page stops growing (cap the iterations to stay polite):

```fsharp
let scrollToEnd (page: IPage) =
    async {
        let mutable previous = 0
        let mutable stable = false
        let mutable guard = 0
        while not stable && guard < 50 do
            let! height =
                page.EvaluateAsync<int>("document.body.scrollHeight") |> Async.AwaitTask
            do! page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)")
                |> Async.AwaitTask |> Async.Ignore
            do! page.WaitForTimeoutAsync(800f) |> Async.AwaitTask
            stable <- height = previous
            previous <- height
            guard <- guard + 1
    }
```

## Then

- Pass the rendered HTML to **`parse-html`** for extraction.
- For login-gated dynamic pages, drive the form with Playwright (`FillAsync` /
  `ClickAsync`) — see **`session-auth`**.

## Guidance

- **Reuse the browser, not one per request.** Launching Chromium is expensive; render a
  batch of URLs on one `IBrowser`, opening a fresh `IPage` per URL.
- **Prefer `Http.fetch` when you can.** A real browser is 10–100× heavier. Only reach
  for Playwright when the content genuinely requires JS.
- **Always wait for a concrete selector**, not a fixed sleep, before reading content.
- Run headless in CI; set a realistic viewport and locale if the site is sensitive to
  them.
