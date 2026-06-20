module CrawlSage.Tests.Url

open Xunit
open CrawlSage

[<Fact>]
let ``resolve makes a relative href absolute against the page`` () =
    Assert.Equal("https://site.com/page/2/", Url.resolve "https://site.com/list" "/page/2/")
    Assert.Equal("https://site.com/a/c", Url.resolve "https://site.com/a/b" "c")
    Assert.Equal("https://site.com/x", Url.resolve "https://site.com/a/b" "../x")

[<Fact>]
let ``resolve leaves an absolute href unchanged`` () =
    Assert.Equal("https://other.com/x", Url.resolve "https://site.com/a" "https://other.com/x")

[<Fact>]
let ``host is lower-cased and empty for non-absolute input`` () =
    Assert.Equal("site.com", Url.host "https://Site.COM/path")
    Assert.Equal("", Url.host "/relative/only")

[<Fact>]
let ``isSameHost compares hosts case-insensitively`` () =
    Assert.True(Url.isSameHost "https://site.com/a" "https://SITE.com/b")
    Assert.False(Url.isSameHost "https://site.com/a" "https://other.com/b")
    Assert.False(Url.isSameHost "/relative" "/also-relative")

[<Fact>]
let ``normalize collapses fragment, default port and host casing`` () =
    let canonical = "https://site.com/path?q=1"
    Assert.Equal(canonical, Url.normalize "https://Site.com:443/path?q=1#frag")
    Assert.Equal(canonical, Url.normalize "https://site.com/path?q=1")

[<Fact>]
let ``normalize gives an empty path a trailing slash`` () =
    Assert.Equal("https://site.com/", Url.normalize "https://site.com")
