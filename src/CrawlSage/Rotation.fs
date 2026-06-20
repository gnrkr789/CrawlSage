namespace CrawlSage

open System.Net
open System.Net.Http
open System.Threading

/// Rotation middleware — spread a crawl across several honest identities / egresses for
/// *resilience and geo-distribution*, never to evade bans or hide who you are. Each
/// rotation is a wrapper composed around a fetch, like the <c>Resilience</c> stack.
///
/// > Rotating a User-Agent or proxy to balance load and stay reachable is good
/// > citizenship. Rotating them to dodge anti-abuse systems or a site's ToS is not —
/// > keep crawls polite and lawful.
module Rotation =

    /// A thread-safe round-robin over <paramref name="items"/>: each call returns the next
    /// entry, wrapping at the end. Returns <c>None</c> for an empty list. Drives both
    /// User-Agent and proxy rotation.
    let cycle (items: 'a list) : unit -> 'a option =
        match List.toArray items with
        | [||] -> fun () -> None
        | arr ->
            let mutable i = -1

            fun () ->
                let n = Interlocked.Increment(&i)
                // The double-mod keeps the index in range even if the counter overflows.
                Some arr.[((n % arr.Length) + arr.Length) % arr.Length]

    /// Stamp a rotating, honest User-Agent on each request before it is fetched — declare
    /// who you are and spread load across a few UA strings. An empty list passes requests
    /// through unchanged.
    let withRotatingUserAgent (userAgents: string list) (fetch: Renderer) : Renderer =
        let next = cycle userAgents

        fun request ->
            match next () with
            | Some ua -> fetch (request |> Request.withHeader "User-Agent" ua)
            | None -> fetch request

    /// A fetch that round-robins over a pool of proxied clients — one
    /// <see cref="T:System.Net.Http.HttpClient"/> per proxy URL — for egress resilience and
    /// geo-distribution. An empty list falls back to the shared client. The clients are
    /// long-lived (like the shared one), so build the pool once and reuse the returned
    /// <see cref="T:CrawlSage.Renderer"/>.
    let proxiedFetch (proxyUrls: string list) : Renderer =
        let clients =
            proxyUrls
            |> List.map (fun url ->
                new HttpClient(
                    new HttpClientHandler(
                        Proxy = WebProxy(url),
                        UseProxy = true,
                        AutomaticDecompression = DecompressionMethods.All)))

        match clients with
        | [] -> Http.fetch
        | _ ->
            let next = cycle clients

            fun request ->
                match next () with
                | Some client -> Http.fetchWith client request
                | None -> Http.fetch request
