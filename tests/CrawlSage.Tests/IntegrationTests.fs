module CrawlSage.Tests.Integration

open System
open System.IO
open System.IO.Compression
open System.Text
open System.Collections.Concurrent
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Primitives
open Xunit
open CrawlSage

/// An in-process Kestrel server on an OS-assigned free port. <c>handle</c> writes each
/// response; dispose to stop. Real HTTP end-to-end — what stub tests can't cover.
type private TestServer(handle: HttpContext -> Task) =
    let app =
        let builder = WebApplication.CreateBuilder()
        builder.Logging.ClearProviders() |> ignore
        builder.WebHost.UseUrls "http://127.0.0.1:0" |> ignore
        let a = builder.Build()
        a.Run(RequestDelegate handle)
        a.StartAsync().GetAwaiter().GetResult()
        a

    /// Base URL with the bound port, e.g. "http://127.0.0.1:51234" (no trailing slash).
    member _.BaseUrl = (Seq.head app.Urls).TrimEnd '/'

    interface IDisposable with
        member _.Dispose() = (app :> IDisposable).Dispose()

/// Serve a fixed path → HTML map and tally hits per path.
let private serve (pages: Map<string, string>) (hits: ConcurrentDictionary<string, int>) : HttpContext -> Task =
    fun ctx ->
        let path = ctx.Request.Path.Value
        hits.AddOrUpdate(path, 1, (fun _ n -> n + 1)) |> ignore

        match Map.tryFind path pages with
        | Some body ->
            ctx.Response.ContentType <- "text/html; charset=utf-8"
            ctx.Response.WriteAsync body
        | None ->
            ctx.Response.StatusCode <- 404
            ctx.Response.WriteAsync "not found"

/// Parser: the page's <h1> as an item, plus a Follow for every <a href> (resolved absolute).
let private linkParse (response: Response) : ParseResult<string> list =
    let doc = Html.parse response.Body
    let title = doc |> Html.select "h1" |> Option.map Html.text |> Option.defaultValue ""

    let follows =
        doc
        |> Html.selectAll "a"
        |> List.choose (Html.attr "href")
        |> List.map (fun href -> Follow(Request.create (Url.resolve response.Request.Url href)))

    Item title :: follows

[<Fact>]
let ``crawl follows links and fetches each page exactly once over real HTTP`` () =
    let hits = ConcurrentDictionary<string, int>()

    let pages =
        Map.ofList
            [ "/", """<h1>Home</h1><a href="/a">a</a><a href="/b">b</a><a href="/">self</a>"""
              "/a", """<h1>A</h1><a href="/">home</a>"""
              "/b", "<h1>B</h1>" ]

    use server = new TestServer(serve pages hits)
    let titles = ResizeArray<string>()

    let spider =
        { Seeds = [ Request.create (server.BaseUrl + "/") ]
          Parse = linkParse
          Pipeline = titles.Add
          Options =
            { SpiderOptions.Default with
                MaxConcurrency = 4
                MaxDepth = 5 } }

    Spider.crawlWith Http.fetch spider |> Async.RunSynchronously

    Assert.Equal(1, hits.["/"])
    Assert.Equal(1, hits.["/a"])
    Assert.Equal(1, hits.["/b"])
    Assert.Equal<string list>([ "A"; "B"; "Home" ], titles |> Seq.sort |> List.ofSeq)

[<Fact>]
let ``crawlPolitely consults robots.txt and skips disallowed URLs`` () =
    let hits = ConcurrentDictionary<string, int>()

    let pages =
        Map.ofList
            [ "/robots.txt", "User-agent: *\nDisallow: /private"
              "/", """<h1>Home</h1><a href="/public">p</a><a href="/private">x</a>"""
              "/public", "<h1>Public</h1>"
              "/private", "<h1>Secret</h1>" ]

    use server = new TestServer(serve pages hits)
    let titles = ResizeArray<string>()
    let stats, onEvent = Stats.collector ()

    let spider =
        { Seeds = [ Request.create (server.BaseUrl + "/") ]
          Parse = linkParse
          Pipeline = titles.Add
          Options =
            { SpiderOptions.Default with
                MaxDepth = 3
                OnEvent = onEvent } }

    let politeness =
        { Politeness.Default with
            PerHostDelay = TimeSpan.Zero }

    Spider.crawlPolitely politeness Http.fetch spider |> Async.RunSynchronously

    Assert.True(hits.ContainsKey "/robots.txt") // robots.txt was consulted
    Assert.True(hits.ContainsKey "/public") // allowed → fetched
    Assert.False(hits.ContainsKey "/private") // disallowed → never fetched
    Assert.True(stats.Skipped >= 1) // recorded as skipped
    Assert.Contains("Public", titles)
    Assert.DoesNotContain("Secret", titles)

[<Fact>]
let ``Http.fetch follows redirects`` () =
    let hits = ConcurrentDictionary<string, int>()

    let handle (ctx: HttpContext) : Task =
        let path = ctx.Request.Path.Value
        hits.AddOrUpdate(path, 1, (fun _ n -> n + 1)) |> ignore

        match path with
        | "/old" ->
            ctx.Response.StatusCode <- 301
            ctx.Response.Headers.Location <- StringValues "/new"
            ctx.Response.WriteAsync ""
        | "/new" -> ctx.Response.WriteAsync "<h1>Arrived</h1>"
        | _ ->
            ctx.Response.StatusCode <- 404
            ctx.Response.WriteAsync ""

    use server = new TestServer(handle)

    let response =
        Http.fetch (Request.create (server.BaseUrl + "/old")) |> Async.RunSynchronously

    Assert.Equal(200, response.StatusCode)
    Assert.Contains("Arrived", response.Body)
    Assert.True(hits.ContainsKey "/new")

/// gzip-compress a string for the decompression test.
let private gzip (s: string) : byte[] =
    use output = new MemoryStream()

    (use gz = new GZipStream(output, CompressionMode.Compress)
     let bytes = Encoding.UTF8.GetBytes s
     gz.Write(bytes, 0, bytes.Length))

    output.ToArray()

[<Fact>]
let ``Http.fetch transparently decodes a gzip response`` () =
    let payload = "<h1>compressed</h1>"
    let body = gzip payload

    let handle (ctx: HttpContext) : Task =
        ctx.Response.Headers.ContentEncoding <- StringValues "gzip"
        ctx.Response.ContentType <- "text/html"
        ctx.Response.Body.WriteAsync(body, 0, body.Length)

    use server = new TestServer(handle)

    let response =
        Http.fetch (Request.create (server.BaseUrl + "/")) |> Async.RunSynchronously

    Assert.Equal(payload, response.Body)
