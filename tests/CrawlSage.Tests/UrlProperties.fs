module CrawlSage.Tests.UrlProperties

open FsCheck
open FsCheck.Xunit
open CrawlSage

/// Generates valid absolute http(s) URLs assembled from safe parts, so the structural
/// properties below exercise real URLs rather than mostly-unparseable random strings.
type UrlGen =
    static member Url() : Arbitrary<string> =
        gen {
            let! scheme = Gen.elements [ "http"; "https" ]
            let! hostName = Gen.elements [ "example.com"; "Example.COM"; "a.test"; "sub.example.org"; "site.io" ]
            let! segments = Gen.listOf (Gen.elements [ "a"; "b"; "page"; "Items"; "p2"; "x" ])
            return sprintf "%s://%s/%s" scheme hostName (String.concat "/" segments)
        }
        |> Arb.fromGen

// --- totality on arbitrary / hostile input (default string generator) ---

[<Property>]
let ``normalize never throws on arbitrary input`` (s: string) =
    Url.normalize s |> ignore
    true

[<Property>]
let ``resolve never throws on arbitrary input`` (baseUrl: string) (href: string) =
    Url.resolve baseUrl href |> ignore
    true

[<Property>]
let ``host never throws on arbitrary input`` (s: string) =
    Url.host s |> ignore
    true

// --- structural properties over generated valid URLs ---

[<Property(Arbitrary = [| typeof<UrlGen> |])>]
let ``normalize is idempotent`` (url: string) =
    Url.normalize (Url.normalize url) = Url.normalize url

[<Property(Arbitrary = [| typeof<UrlGen> |])>]
let ``normalize drops the fragment`` (url: string) =
    Url.normalize (url + "#section") = Url.normalize url

[<Property(Arbitrary = [| typeof<UrlGen> |])>]
let ``normalize lower-cases the host`` (url: string) =
    let h = Url.host (Url.normalize url)
    h = h.ToLowerInvariant()

[<Property(Arbitrary = [| typeof<UrlGen> |])>]
let ``resolving an absolute-path href off an http(s) base stays http(s), never file://`` (url: string) =
    // Guards the regression where Uri.TryCreate("/x", Absolute) parses as file:// on Unix.
    let resolved = Url.resolve url "/some/path"
    resolved.StartsWith "http://" || resolved.StartsWith "https://"

[<Property(Arbitrary = [| typeof<UrlGen> |])>]
let ``a URL shares a host with itself`` (url: string) = Url.isSameHost url url
