module CrawlSage.Browser.Tests

open System
open Xunit
open CrawlSage
open CrawlSage.Browser

// Trait-gated: needs the Playwright browsers (`playwright install chromium`), so it is
// excluded from the default CI run (--filter "Category!=Browser") and run on demand with
// --filter "Category=Browser".
[<Trait("Category", "Browser")>]
[<Fact>]
let ``render executes the page's JavaScript`` () =
    // A data: URL whose script rewrites the DOM — static HTML says EMPTY, the rendered DOM
    // must say RENDERED_BY_JS. No network: the only external need is the Chromium binary.
    let html =
        "<div id='q'>EMPTY</div><script>document.getElementById('q').innerText='RENDERED_BY_JS'</script>"

    let url = "data:text/html," + Uri.EscapeDataString html
    let response = Browser.render (Request.create url) |> Async.RunSynchronously

    Assert.Contains("RENDERED_BY_JS", response.Body)
    Assert.DoesNotContain("EMPTY", response.Body)
