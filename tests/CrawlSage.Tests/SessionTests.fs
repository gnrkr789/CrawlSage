module CrawlSage.Tests.Session

open System.IO
open Xunit
open CrawlSage

[<Fact>]
let ``a fresh session has an empty jar`` () =
    let session = Session.create ()
    Assert.Empty(Session.cookies session "https://site.com/")

[<Fact>]
let ``addCookie then cookies round-trips`` () =
    let session = Session.create ()
    Session.addCookie session "https://site.com/" "sid" "abc123"
    Assert.Equal(Some "abc123", Session.cookies session "https://site.com/" |> Map.tryFind "sid")

[<Fact>]
let ``save and load preserve the cookie jar (with escaped values)`` () =
    let path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())

    try
        let session = Session.create ()
        Session.addCookie session "https://site.com/" "sid" "a b=c%d" // chars needing escaping
        Session.save session path

        let restored = Session.load path
        Assert.Equal(Some "a b=c%d", Session.cookies restored "https://site.com/" |> Map.tryFind "sid")
    finally
        File.Delete path
