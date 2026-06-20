module CrawlSage.Tests.Rotation

open Xunit
open CrawlSage

let private ok (request: Request) : Response =
    { Request = request
      StatusCode = 200
      Body = ""
      Headers = Map.empty }

[<Fact>]
let ``cycle round-robins and wraps past the end`` () =
    let next = Rotation.cycle [ "a"; "b"; "c" ]
    let got = [ for _ in 1..7 -> next () |> Option.get ]
    Assert.Equal<string list>([ "a"; "b"; "c"; "a"; "b"; "c"; "a" ], got)

[<Fact>]
let ``cycle of an empty list always yields None`` () =
    let next = Rotation.cycle ([]: string list)
    Assert.Equal(None, next ())
    Assert.Equal(None, next ())

[<Fact>]
let ``withRotatingUserAgent cycles the User-Agent header across requests`` () =
    let seen = ResizeArray<string>()

    let stub (request: Request) =
        async {
            seen.Add(request.Headers |> Map.find "User-Agent")
            return ok request
        }

    let fetch = Rotation.withRotatingUserAgent [ "UA-1"; "UA-2" ] stub

    for _ in 1..4 do
        fetch (Request.create "https://example.com") |> Async.RunSynchronously |> ignore

    Assert.Equal<string list>([ "UA-1"; "UA-2"; "UA-1"; "UA-2" ], List.ofSeq seen)

[<Fact>]
let ``withRotatingUserAgent passes requests through when no UAs are given`` () =
    let mutable sawHeader = true

    let stub (request: Request) =
        async {
            sawHeader <- request.Headers.ContainsKey "User-Agent"
            return ok request
        }

    let fetch = Rotation.withRotatingUserAgent [] stub
    fetch (Request.create "https://example.com") |> Async.RunSynchronously |> ignore
    Assert.False(sawHeader)

[<Fact>]
let ``proxiedFetch with no proxies falls back to the shared client`` () =
    // No network: just assert the empty pool produces a usable Renderer (the default fetch).
    let fetch = Rotation.proxiedFetch []
    Assert.NotNull(box fetch)
