namespace CrawlSage

open System
open System.Net
open System.Net.Sockets

/// SSRF / safety guard — refuse to fetch URLs that resolve to non-public addresses: loopback,
/// link-local (including the cloud metadata address <c>169.254.169.254</c>), private (RFC 1918)
/// and unique-local IPv6. A crawler that follows links it *discovered* should not be steerable
/// into the host's own localhost or a cloud provider's internal metadata service. Opt-in:
/// compose <see cref="M:CrawlSage.Safety.publicOnly"/> under your fetch, like the
/// <c>Resilience</c> / <c>Robots</c> wrappers.
module Safety =

    /// Raised by <see cref="M:CrawlSage.Safety.publicOnly"/> when a request targets a non-public host.
    exception BlockedHost of url: string

    /// Is <paramref name="ip"/> a non-public address — loopback, link-local (incl. the cloud
    /// metadata address 169.254.169.254), private (RFC 1918), CGNAT (100.64/10), or unique-local IPv6?
    let rec isPrivateAddress (ip: IPAddress) : bool =
        if IPAddress.IsLoopback ip then
            true
        else
            match ip.AddressFamily with
            | AddressFamily.InterNetwork ->
                let b = ip.GetAddressBytes()

                b.[0] = 10uy
                || (b.[0] = 172uy && b.[1] >= 16uy && b.[1] <= 31uy)
                || (b.[0] = 192uy && b.[1] = 168uy)
                || (b.[0] = 169uy && b.[1] = 254uy)
                || (b.[0] = 100uy && b.[1] >= 64uy && b.[1] <= 127uy)
                || b.[0] = 0uy
                || b.[0] = 127uy
            | AddressFamily.InterNetworkV6 ->
                ip.IsIPv6LinkLocal
                || ip.IsIPv6SiteLocal
                || ip.IsIPv6UniqueLocal
                || (ip.IsIPv4MappedToIPv6 && isPrivateAddress (ip.MapToIPv4()))
            | _ -> true // unknown address family — refuse, to be safe

    /// Decide whether <paramref name="host"/> is safe to fetch. <c>localhost</c> and any host that
    /// resolves to a private/loopback/link-local address are refused; an IP literal is judged
    /// directly (no DNS), a hostname is resolved.
    let isPublicHost (host: string) : Async<bool> =
        async {
            if String.IsNullOrWhiteSpace host || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) then
                return false
            else
                match IPAddress.TryParse host with
                | true, ip -> return not (isPrivateAddress ip)
                | _ ->
                    try
                        let! addresses = Dns.GetHostAddressesAsync host |> Async.AwaitTask
                        // Safe only if it resolves and *no* resolved address is private.
                        return addresses.Length > 0 && not (Array.exists isPrivateAddress addresses)
                    with _ ->
                        return false
        }

    /// Guard a fetch so it refuses any request whose host resolves to a non-public address — SSRF
    /// protection for crawls that follow links from untrusted pages. Compose it under your fetch
    /// like the <c>Resilience</c> / <c>Robots</c> wrappers. A blocked request raises
    /// <see cref="T:CrawlSage.Safety.BlockedHost"/> (recorded as a per-page failure by the engine,
    /// so the crawl carries on).
    let publicOnly (fetch: Renderer) : Renderer =
        fun request ->
            async {
                let host =
                    match Uri.TryCreate(request.Url, UriKind.Absolute) with
                    | true, uri -> uri.Host
                    | _ -> ""

                let! ok = isPublicHost host

                if ok then
                    return! fetch request
                else
                    return raise (BlockedHost request.Url)
            }
