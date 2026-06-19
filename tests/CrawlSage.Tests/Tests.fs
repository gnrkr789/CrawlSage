module CrawlSage.Tests

open Xunit
open CrawlSage

[<Fact>]
let ``Request.create produces a GET with no headers or body`` () =
    let request = Request.create "https://example.com"
    Assert.Equal("https://example.com", request.Url)
    Assert.True(request.Method = Get)
    Assert.True(request.Headers.IsEmpty)
    Assert.True(request.Body = None)

[<Fact>]
let ``Request.withBody switches the verb to POST`` () =
    let request = Request.create "https://example.com" |> Request.withBody "payload"
    Assert.True(request.Method = Post)
    Assert.True(request.Body = Some "payload")

[<Fact>]
let ``Request.withHeader and withMeta accumulate entries`` () =
    let request =
        Request.create "https://example.com"
        |> Request.withHeader "User-Agent" "CrawlSage"
        |> Request.withMeta "category" "news"

    Assert.Equal("CrawlSage", request.Headers.["User-Agent"])
    Assert.Equal("news", request.Meta.["category"])

[<Fact>]
let ``Response.IsSuccess reflects 2xx status codes`` () =
    let make code =
        { Request = Request.create "https://example.com"
          StatusCode = code
          Body = ""
          Headers = Map.empty }

    Assert.True((make 200).IsSuccess)
    Assert.True((make 299).IsSuccess)
    Assert.False((make 404).IsSuccess)
    Assert.False((make 500).IsSuccess)
