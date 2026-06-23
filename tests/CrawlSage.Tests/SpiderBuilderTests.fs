module CrawlSage.Tests.SpiderBuilder

open Xunit
open CrawlSage

type private Item = { Title: string }

let private parser (_: Response) : ParseResult<Item> list = [ Item { Title = "x" } ]

[<Fact>]
let ``spider CE builds a Spider with seeds, parser and options`` () =
    let collected = ResizeArray<Item>()

    let crawler =
        spider {
            seed "https://example.com/"
            seed "https://example.com/page/2"
            parse parser
            pipeline collected.Add
            maxDepth 3
            maxConcurrency 4
        }

    Assert.Equal(2, crawler.Seeds.Length)
    Assert.Equal("https://example.com/", crawler.Seeds.[0].Url)
    Assert.Equal(3, crawler.Options.MaxDepth)
    Assert.Equal(4, crawler.Options.MaxConcurrency)

    crawler.Pipeline { Title = "y" } // pipeline is wired through
    Assert.Equal(1, collected.Count)

[<Fact>]
let ``spider CE defaults options when omitted`` () =
    let crawler =
        spider {
            seed "https://example.com/"
            parse parser
        }

    Assert.Equal(SpiderOptions.Default.MaxDepth, crawler.Options.MaxDepth)
    Assert.Equal(1, crawler.Seeds.Length)

[<Fact>]
let ``spider CE result crawls through the engine`` () =
    let stub (_: Request) =
        async {
            return
                { Request = Request.create "https://example.com/"
                  StatusCode = 200
                  Body = "<h1>hi</h1>"
                  Headers = Map.empty }
        }

    let got = ResizeArray<Item>()

    let crawler =
        spider {
            seed "https://example.com/"
            parse (fun _ -> [ Item { Title = "hi" } ])
            pipeline got.Add
            maxDepth 0
        }

    Spider.crawlWith stub crawler |> Async.RunSynchronously
    Assert.Equal(1, got.Count)
