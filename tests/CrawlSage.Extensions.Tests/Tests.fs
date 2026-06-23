module CrawlSage.Extensions.Tests

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open CrawlSage
open CrawlSage.Extensions

[<Fact>]
let ``AddCrawlSage registers a resolvable CrawlSageClient`` () =
    let services = ServiceCollection()
    services.AddLogging() |> ignore
    services.AddCrawlSage() |> ignore
    use provider = services.BuildServiceProvider()

    // GetRequiredService throws if IHttpClientFactory / ILogger weren't wired — so reaching
    // the assert proves the DI graph resolves.
    let client = provider.GetRequiredService<CrawlSageClient>()
    Assert.NotNull(box client)
    Assert.NotNull(box client.Renderer)
    Assert.NotNull(box client.OnEvent)

[<Fact>]
let ``Logging.toLogger maps every CrawlEvent without throwing`` () =
    let onEvent = Logging.toLogger NullLogger.Instance

    // Exercises the LogInformation / LogDebug / LogWarning calls (and their boxed args).
    onEvent (Fetched(Request.create "https://example.com/", 200))
    onEvent (Skipped(Request.create "https://example.com/private"))
    onEvent (Failed(Request.create "https://example.com/x", System.Exception "boom"))

    Assert.True(true)
