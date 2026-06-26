module CrawlSage.Tests.Safety

open System.Net
open Xunit
open CrawlSage

let private ip (s: string) = IPAddress.Parse s

[<Theory>]
[<InlineData("127.0.0.1")>] // loopback
[<InlineData("10.1.2.3")>] // RFC 1918
[<InlineData("172.16.5.9")>] // RFC 1918
[<InlineData("172.31.255.255")>] // RFC 1918 (upper bound)
[<InlineData("192.168.0.1")>] // RFC 1918
[<InlineData("169.254.169.254")>] // link-local — cloud metadata
[<InlineData("100.64.0.1")>] // CGNAT
[<InlineData("::1")>] // IPv6 loopback
[<InlineData("fe80::1")>] // IPv6 link-local
let ``isPrivateAddress flags non-public addresses`` (addr: string) =
    Assert.True(Safety.isPrivateAddress (ip addr))

[<Theory>]
[<InlineData("8.8.8.8")>]
[<InlineData("93.184.216.34")>]
[<InlineData("172.32.0.1")>] // just outside 172.16/12
[<InlineData("2606:4700:4700::1111")>] // public IPv6
let ``isPrivateAddress passes public addresses`` (addr: string) =
    Assert.False(Safety.isPrivateAddress (ip addr))

[<Theory>]
[<InlineData("localhost")>]
[<InlineData("127.0.0.1")>]
[<InlineData("169.254.169.254")>]
let ``isPublicHost refuses local / metadata hosts`` (host: string) =
    Assert.False(Safety.isPublicHost host |> Async.RunSynchronously)

[<Fact>]
let ``isPublicHost allows a public IP literal`` () =
    Assert.True(Safety.isPublicHost "93.184.216.34" |> Async.RunSynchronously)

[<Fact>]
let ``publicOnly blocks a loopback URL without fetching`` () =
    let mutable called = false

    let stub (_: Request) =
        async {
            called <- true
            return { Request = Request.create "x"; StatusCode = 200; Body = ""; Headers = Map.empty }
        }

    let guarded = Safety.publicOnly stub

    let blocked =
        try
            guarded (Request.create "http://127.0.0.1:8080/admin")
            |> Async.RunSynchronously
            |> ignore

            false
        with Safety.BlockedHost _ ->
            true

    Assert.True(blocked)
    Assert.False(called) // never reached the inner fetch

[<Fact>]
let ``publicOnly lets a public host through`` () =
    let mutable called = false

    let stub (_: Request) =
        async {
            called <- true
            return { Request = Request.create "x"; StatusCode = 200; Body = "ok"; Headers = Map.empty }
        }

    let guarded = Safety.publicOnly stub
    let response = guarded (Request.create "http://93.184.216.34/") |> Async.RunSynchronously
    Assert.True(called)
    Assert.Equal(200, response.StatusCode)
