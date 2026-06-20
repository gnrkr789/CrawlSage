namespace CrawlSage

open System

/// URL resolution and canonicalisation — the plumbing behind link following and dedup.
/// Pure (<see cref="T:System.Uri"/> only); every function is total and returns the input
/// unchanged when it cannot parse a URL, so callers never have to guard against nulls.
module Url =

    /// Resolve a possibly-relative <paramref name="href"/> (e.g. <c>"/page/2/"</c>,
    /// <c>"../x"</c>) against the page's <paramref name="baseUrl"/>. Absolute hrefs pass
    /// through; anything unparseable is returned unchanged.
    let resolve (baseUrl: string) (href: string) : string =
        match Uri.TryCreate(href, UriKind.Absolute) with
        | true, abs -> abs.AbsoluteUri
        | _ ->
            match Uri.TryCreate(baseUrl, UriKind.Absolute) with
            | true, b ->
                match Uri.TryCreate(b, href) with
                | true, resolved -> resolved.AbsoluteUri
                | _ -> href
            | _ -> href

    /// The lower-cased host of <paramref name="url"/>, or <c>""</c> if it is not absolute.
    let host (url: string) : string =
        match Uri.TryCreate(url, UriKind.Absolute) with
        | true, uri -> uri.Host.ToLowerInvariant()
        | _ -> ""

    /// Whether <paramref name="a"/> and <paramref name="b"/> share a (non-empty) host.
    let isSameHost (a: string) (b: string) : bool =
        let ha = host a
        ha <> "" && ha = host b

    /// Canonical form for dedup: lower-cased scheme + host, the default port dropped, an
    /// empty path normalised to <c>"/"</c>, the fragment removed and the query preserved.
    /// Two URLs that differ only by fragment, default port or host casing collapse to one.
    let normalize (url: string) : string =
        match Uri.TryCreate(url, UriKind.Absolute) with
        | true, uri ->
            let scheme = uri.Scheme.ToLowerInvariant()
            let host = uri.Host.ToLowerInvariant()
            let port = if uri.IsDefaultPort then "" else $":{uri.Port}"
            let path = if String.IsNullOrEmpty uri.AbsolutePath then "/" else uri.AbsolutePath
            $"{scheme}://{host}{port}{path}{uri.Query}"
        | _ -> url
