namespace CrawlSage.Extensions

open System.Net.Http
open Microsoft.Extensions.Logging
open CrawlSage

/// A DI-resolved entry point for hosting CrawlSage inside a .NET app: a renderer backed by
/// <c>IHttpClientFactory</c> (correct socket/DNS lifetime for long-running hosts) plus an
/// <c>ILogger</c>-bridged crawl-event handler. Resolve it from the container after
/// <c>services.AddCrawlSage()</c>.
type CrawlSageClient(httpFactory: IHttpClientFactory, logger: ILogger<CrawlSageClient>) =

    /// A <see cref="T:CrawlSage.Renderer"/> that fetches through the pooled, DI-managed
    /// "CrawlSage" <c>HttpClient</c> — drop it into <c>Spider.crawlWith</c>.
    member _.Renderer: Renderer =
        fun request -> Http.fetchWith (httpFactory.CreateClient "CrawlSage") request

    /// A crawl-event handler that logs through the host's logging pipeline — wire it to
    /// <c>SpiderOptions.OnEvent</c>.
    member _.OnEvent: CrawlEvent -> unit = Logging.toLogger logger


namespace Microsoft.Extensions.DependencyInjection

open System.Net
open System.Net.Http
open System.Runtime.CompilerServices
open Microsoft.Extensions.DependencyInjection.Extensions
open CrawlSage.Extensions

/// <c>IServiceCollection</c> integration for CrawlSage — call <c>services.AddCrawlSage()</c>.
[<Extension>]
type CrawlSageServiceCollectionExtensions =

    /// Register CrawlSage for a .NET host: a pooled, decompressing "CrawlSage" <c>HttpClient</c>
    /// (via <c>IHttpClientFactory</c>) and a singleton
    /// <see cref="T:CrawlSage.Extensions.CrawlSageClient"/> exposing an IHttpClientFactory-backed
    /// <c>Renderer</c> and an <c>ILogger</c>-bridged crawl-event handler.
    [<Extension>]
    static member AddCrawlSage(services: IServiceCollection) : IServiceCollection =
        services
            .AddHttpClient("CrawlSage", fun (client: HttpClient) -> client.DefaultRequestHeaders.UserAgent.ParseAdd "CrawlSage")
            .ConfigurePrimaryHttpMessageHandler(fun () ->
                new HttpClientHandler(AutomaticDecompression = DecompressionMethods.All) :> HttpMessageHandler)
        |> ignore

        services.TryAddSingleton<CrawlSageClient>()
        services
