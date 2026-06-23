namespace CrawlSage.Extensions

open Microsoft.Extensions.Logging
open CrawlSage

/// Bridge CrawlSage's <see cref="T:CrawlSage.CrawlEvent"/>s into the standard
/// Microsoft.Extensions.Logging pipeline, so a crawl's progress flows to the host app's
/// configured sinks (console, Serilog, Seq, Application Insights…) instead of a bespoke
/// <c>OnEvent</c> handler.
module Logging =

    /// A crawl-event handler that logs each event through <paramref name="logger"/>. Wire it
    /// to <c>SpiderOptions.OnEvent</c> (or use the DI-registered <c>CrawlSageClient.OnEvent</c>).
    let toLogger (logger: ILogger) : CrawlEvent -> unit =
        fun event ->
            match event with
            | Fetched(request, status) ->
                logger.LogInformation("crawlsage fetched {Status} {Url}", [| box status; box request.Url |])
            | Skipped request -> logger.LogDebug("crawlsage skipped (robots) {Url}", [| box request.Url |])
            | Failed(request, error) -> logger.LogWarning(error, "crawlsage failed {Url}", [| box request.Url |])
