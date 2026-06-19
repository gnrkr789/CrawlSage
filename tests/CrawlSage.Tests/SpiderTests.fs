module CrawlSage.Tests.Spider

open System.Collections.Generic
open Xunit
open CrawlSage

let private ok url body : Response =
    { Request = Request.create url
      StatusCode = 200
      Body = body
      Headers = Map.empty }

/// A stub that serves a fixed URL → HTML map and counts fetches per URL.
let private stubFrom (site: Map<string, string>) (counter: Dictionary<string, int>) : Request -> Async<Response> =
    fun request ->
        async {
            let n =
                match counter.TryGetValue request.Url with
                | true, c -> c
                | _ -> 0

            counter.[request.Url] <- n + 1
            return ok request.Url (site |> Map.tryFind request.Url |> Option.defaultValue "<html></html>")
        }

type private Page = { Title: string }

/// Emit the page's <h1> as an item, and a Follow for every <a href>.
let private linkParse (response: Response) : ParseResult<Page> list =
    let doc = Html.parse response.Body
    let title = doc |> Html.select "h1" |> Option.map Html.text |> Option.defaultValue ""

    let follows =
        doc
        |> Html.selectAll "a"
        |> List.choose (Html.attr "href")
        |> List.map (Request.create >> Follow)

    Item { Title = title } :: follows

[<Fact>]
let ``crawl visits each page once despite duplicate links`` () =
    let site =
        Map.ofList
            [ "https://site/1", """<h1>One</h1><a href="https://site/2">2</a><a href="https://site/1">self</a>"""
              "https://site/2", """<h1>Two</h1><a href="https://site/1">back</a>""" ]

    let counter = Dictionary<string, int>()
    let items = ResizeArray<Page>()

    let spider =
        { Seeds = [ Request.create "https://site/1" ]
          Parse = linkParse
          Pipeline = items.Add
          Options = { SpiderOptions.Default with MaxDepth = 5 } }

    Spider.crawlWith (stubFrom site counter) spider |> Async.RunSynchronously

    Assert.Equal(1, counter.["https://site/1"])
    Assert.Equal(1, counter.["https://site/2"])

    let titles = items |> Seq.map (fun p -> p.Title) |> Seq.sort |> List.ofSeq
    Assert.Equal<string list>([ "One"; "Two" ], titles)

[<Fact>]
let ``crawl follows pagination across two pages`` () =
    let site =
        Map.ofList
            [ "https://news/page/1", """<h1>P1</h1><a href="https://news/page/2">next</a>"""
              "https://news/page/2", """<h1>P2</h1>""" ]

    let counter = Dictionary<string, int>()
    let items = ResizeArray<Page>()

    let spider =
        { Seeds = [ Request.create "https://news/page/1" ]
          Parse = linkParse
          Pipeline = items.Add
          Options = SpiderOptions.Default }

    Spider.crawlWith (stubFrom site counter) spider |> Async.RunSynchronously

    Assert.Equal(2, items.Count)
    Assert.True(counter.ContainsKey "https://news/page/2")

[<Fact>]
let ``crawl respects MaxDepth`` () =
    let site =
        Map.ofList
            [ "https://chain/a", """<a href="https://chain/b"></a>"""
              "https://chain/b", """<a href="https://chain/c"></a>"""
              "https://chain/c", """<a href="https://chain/d"></a>"""
              "https://chain/d", "<html></html>" ]

    let counter = Dictionary<string, int>()

    let linksOnly (response: Response) : ParseResult<unit> list =
        Html.parse response.Body
        |> Html.selectAll "a"
        |> List.choose (Html.attr "href")
        |> List.map (Request.create >> Follow)

    let spider =
        { Seeds = [ Request.create "https://chain/a" ]
          Parse = linksOnly
          Pipeline = ignore
          Options = { SpiderOptions.Default with MaxDepth = 2 } }

    Spider.crawlWith (stubFrom site counter) spider |> Async.RunSynchronously

    Assert.True(counter.ContainsKey "https://chain/a") // depth 0
    Assert.True(counter.ContainsKey "https://chain/b") // depth 1
    Assert.True(counter.ContainsKey "https://chain/c") // depth 2
    Assert.False(counter.ContainsKey "https://chain/d") // depth 3 > MaxDepth

[<Fact>]
let ``crawl with no seeds fetches nothing`` () =
    let mutable fetched = 0
    let stub (_: Request) = async {
        fetched <- fetched + 1
        return ok "https://x" "" }

    let spider: Spider<int> =
        { Seeds = []
          Parse = (fun _ -> [])
          Pipeline = ignore
          Options = SpiderOptions.Default }

    Spider.crawlWith stub spider |> Async.RunSynchronously
    Assert.Equal(0, fetched)
