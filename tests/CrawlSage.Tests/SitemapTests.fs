module CrawlSage.Tests.Sitemap

open Xunit
open CrawlSage

[<Fact>]
let ``parse reads loc urls from a urlset`` () =
    let xml =
        """<?xml version="1.0"?>
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
          <url><loc>https://s/a</loc></url>
          <url><loc>https://s/b</loc></url>
        </urlset>"""

    Assert.Equal<string list>([ "https://s/a"; "https://s/b" ], Sitemap.parse xml)

[<Fact>]
let ``parse returns empty on malformed xml`` () =
    Assert.Empty(Sitemap.parse "<not closed")

[<Fact>]
let ``fromRobotsTxt extracts Sitemap directives`` () =
    let robots = "User-agent: *\nDisallow: /x\nSitemap: https://s/sitemap.xml\nSitemap: https://s/news.xml"
    Assert.Equal<string list>([ "https://s/sitemap.xml"; "https://s/news.xml" ], Sitemap.fromRobotsTxt robots)

[<Fact>]
let ``fetchUrls expands a sitemap index and aggregates child urls`` () =
    let index =
        """<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
          <sitemap><loc>https://s/sm1.xml</loc></sitemap>
        </sitemapindex>"""

    let child =
        """<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
          <url><loc>https://s/p1</loc></url>
          <url><loc>https://s/p2</loc></url>
        </urlset>"""

    let site = Map.ofList [ "https://s/index.xml", index; "https://s/sm1.xml", child ]

    let stub (request: Request) =
        async {
            return
                { Request = request
                  StatusCode = 200
                  Body = site.[request.Url]
                  Headers = Map.empty }
        }

    let urls = Sitemap.fetchUrls stub "https://s/index.xml" |> Async.RunSynchronously
    Assert.Equal<string list>([ "https://s/p1"; "https://s/p2" ], urls)
