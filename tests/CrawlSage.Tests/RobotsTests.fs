module CrawlSage.Tests.Robots

open System
open System.Collections.Generic
open System.Diagnostics
open Xunit
open CrawlSage

let private resp (request: Request) status body : Response =
    { Request = request
      StatusCode = status
      Body = body
      Headers = Map.empty }

// ---- parsing & policy ------------------------------------------------------

[<Fact>]
let ``Disallow blocks matching paths and allows others`` () =
    let rules = Robots.parse "User-agent: *\nDisallow: /private"
    Assert.False(Robots.isAllowed "CrawlSage" "/private/report" rules)
    Assert.True(Robots.isAllowed "CrawlSage" "/public" rules)

[<Fact>]
let ``an empty robots.txt allows everything`` () =
    let rules = Robots.parse ""
    Assert.True(Robots.isAllowed "CrawlSage" "/anything" rules)

[<Fact>]
let ``Disallow root blocks the whole site`` () =
    let rules = Robots.parse "User-agent: *\nDisallow: /"
    Assert.False(Robots.isAllowed "CrawlSage" "/" rules)
    Assert.False(Robots.isAllowed "CrawlSage" "/page" rules)

[<Fact>]
let ``an empty Disallow means allow-all`` () =
    let rules = Robots.parse "User-agent: *\nDisallow:"
    Assert.True(Robots.isAllowed "CrawlSage" "/anything" rules)

[<Fact>]
let ``Allow overrides a more general Disallow (longest match wins)`` () =
    let rules = Robots.parse "User-agent: *\nDisallow: /a\nAllow: /a/b"
    Assert.False(Robots.isAllowed "CrawlSage" "/a/x" rules)
    Assert.True(Robots.isAllowed "CrawlSage" "/a/b/c" rules)

[<Fact>]
let ``an agent-specific group beats the wildcard group`` () =
    let rules = Robots.parse "User-agent: BadBot\nDisallow: /\n\nUser-agent: *\nDisallow:"
    Assert.False(Robots.isAllowed "BadBot/1.0" "/x" rules)
    Assert.True(Robots.isAllowed "CrawlSage" "/x" rules)

[<Fact>]
let ``two user-agents share the following rule block`` () =
    let rules = Robots.parse "User-agent: A\nUser-agent: B\nDisallow: /no"
    Assert.False(Robots.isAllowed "A" "/no/x" rules)
    Assert.False(Robots.isAllowed "B" "/no/x" rules)

[<Fact>]
let ``wildcard and end-anchor patterns match`` () =
    let rules = Robots.parse "User-agent: *\nDisallow: /*.pdf$"
    Assert.False(Robots.isAllowed "CrawlSage" "/docs/file.pdf" rules) // matches
    Assert.True(Robots.isAllowed "CrawlSage" "/docs/file.pdf?v=1" rules) // $ anchors to end → query excluded
    Assert.True(Robots.isAllowed "CrawlSage" "/docs/page.html" rules)

[<Fact>]
let ``Crawl-delay is parsed for the matching agent`` () =
    let rules = Robots.parse "User-agent: *\nCrawl-delay: 2\nDisallow: /x"
    Assert.Equal(Some(TimeSpan.FromSeconds 2.0), Robots.crawlDelay "CrawlSage" rules)
    Assert.Equal(None, Robots.crawlDelay "CrawlSage" (Robots.parse "User-agent: *\nDisallow:"))

[<Fact>]
let ``comments and blank lines are ignored`` () =
    let rules = Robots.parse "# a comment\n\nUser-agent: *   # inline\nDisallow: /secret # tail"
    Assert.False(Robots.isAllowed "CrawlSage" "/secret/x" rules)
    Assert.True(Robots.isAllowed "CrawlSage" "/open" rules)

// ---- per-host cache --------------------------------------------------------

[<Fact>]
let ``Cache fetches robots.txt once per host and blocks disallowed paths`` () =
    let mutable robotsFetches = 0

    let stub (request: Request) =
        async {
            if request.Url.EndsWith "/robots.txt" then
                robotsFetches <- robotsFetches + 1
                return resp request 200 "User-agent: *\nDisallow: /private"
            else
                return resp request 200 "<html></html>"
        }

    let cache = Robots.Cache(stub, "CrawlSage")
    let allowedPublic = cache.IsAllowed "https://host/public" |> Async.RunSynchronously
    let allowedPrivate = cache.IsAllowed "https://host/private/x" |> Async.RunSynchronously
    let allowedOther = cache.IsAllowed "https://host/other" |> Async.RunSynchronously

    Assert.True(allowedPublic)
    Assert.False(allowedPrivate)
    Assert.True(allowedOther)
    Assert.Equal(1, robotsFetches) // cached per host, not refetched

[<Fact>]
let ``Cache treats a missing robots.txt (404) as allow-all`` () =
    let stub (request: Request) = async { return resp request 404 "Not Found" }
    let cache = Robots.Cache(stub, "CrawlSage")
    Assert.True(cache.IsAllowed "https://host/anything" |> Async.RunSynchronously)

// ---- per-host pacing -------------------------------------------------------

[<Fact>]
let ``perHostDelay spaces out consecutive same-host requests`` () =
    let stub (request: Request) = async { return resp request 200 "" }
    let fetch = Robots.perHostDelay (TimeSpan.FromMilliseconds 120.0) stub

    let sw = Stopwatch.StartNew()

    [ for _ in 1..3 -> fetch (Request.create "https://one-host/x") ]
    |> Async.Parallel
    |> Async.RunSynchronously
    |> ignore

    sw.Stop()
    // 3 same-host requests → at least 2 inter-request gaps of ~120ms.
    Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds 200.0, $"elapsed {sw.ElapsedMilliseconds}ms, expected >= 200ms")

[<Fact>]
let ``perHostDelay lets different hosts run concurrently`` () =
    let stub (request: Request) =
        async {
            do! Async.Sleep 100
            return resp request 200 ""
        }

    let fetch = Robots.perHostDelay (TimeSpan.FromMilliseconds 500.0) stub
    let sw = Stopwatch.StartNew()

    [ fetch (Request.create "https://a/x"); fetch (Request.create "https://b/x") ]
    |> Async.Parallel
    |> Async.RunSynchronously
    |> ignore

    sw.Stop()
    // Distinct hosts aren't serialised: ~100ms (one sleep), not the 500ms per-host gap.
    Assert.True(
        sw.Elapsed < TimeSpan.FromMilliseconds 450.0,
        $"elapsed {sw.ElapsedMilliseconds}ms; hosts were serialised")

// ---- engine integration ----------------------------------------------------

[<Fact>]
let ``crawlPolitely skips robots-disallowed URLs`` () =
    let site =
        Map.ofList
            [ "https://site/robots.txt", "User-agent: *\nDisallow: /private"
              "https://site/public", "<h1>ok</h1>"
              "https://site/private", "<h1>secret</h1>" ]

    let counter = Dictionary<string, int>()

    let stub (request: Request) =
        async {
            let n =
                match counter.TryGetValue request.Url with
                | true, c -> c
                | _ -> 0

            counter.[request.Url] <- n + 1
            return resp request 200 (site |> Map.tryFind request.Url |> Option.defaultValue "<html></html>")
        }

    let visited = ResizeArray<string>()

    let spider =
        { Seeds = [ Request.create "https://site/public"; Request.create "https://site/private" ]
          Parse = fun response -> [ Item response.Request.Url ]
          Pipeline = visited.Add
          Options = SpiderOptions.Default }

    // PerHostDelay = Zero keeps the test fast; robots respect is what we're asserting.
    Spider.crawlPolitely { Politeness.Default with PerHostDelay = TimeSpan.Zero } stub spider
    |> Async.RunSynchronously

    Assert.True(counter.ContainsKey "https://site/public", "public should be fetched")
    Assert.False(counter.ContainsKey "https://site/private", "private is robots-disallowed")
    Assert.DoesNotContain("https://site/private", visited)
    Assert.Contains("https://site/public", visited)
