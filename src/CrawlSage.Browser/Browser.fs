namespace CrawlSage.Browser

open Microsoft.Playwright
open CrawlSage

/// Opt-in browser renderer — drives headless Chromium (Playwright) to run a page's scripts
/// and return the fully rendered HTML, exposed as a CrawlSage <see cref="T:CrawlSage.Renderer"/>.
///
/// This is deliberately **not** part of CrawlSage core: a real browser is a heavy,
/// last-resort dependency. Climb the rendering ladder first — static fetch, embedded-state
/// extraction (<c>Extract</c>), then API replay — and reach for this only for pages that
/// truly compute their DOM client-side. Requires the Playwright browsers to be installed
/// once (e.g. <c>pwsh bin/Debug/net10.0/playwright.ps1 install chromium</c>).
module Browser =

    /// One headless Chromium, launched on first use and reused (like the shared HttpClient).
    let private browser =
        lazy
            (async {
                let! playwright = Playwright.CreateAsync() |> Async.AwaitTask
                return! playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true)) |> Async.AwaitTask
             }
             |> Async.RunSynchronously)

    /// Render <paramref name="request"/> with a real browser: open a page, navigate, wait for
    /// the network to go idle (so client-side content has loaded), and return the rendered
    /// HTML. Drops into <c>Spider.crawlWith</c> like any other renderer.
    let render: Renderer =
        fun request ->
            async {
                let! page = browser.Value.NewPageAsync() |> Async.AwaitTask

                try
                    let! response =
                        page.GotoAsync(request.Url, PageGotoOptions(WaitUntil = WaitUntilState.NetworkIdle))
                        |> Async.AwaitTask

                    let! content = page.ContentAsync() |> Async.AwaitTask
                    let status = if isNull response then 200 else response.Status

                    return
                        { Request = request
                          StatusCode = status
                          Body = content
                          Headers = Map.empty }
                finally
                    page.CloseAsync() |> Async.AwaitTask |> Async.RunSynchronously
            }
