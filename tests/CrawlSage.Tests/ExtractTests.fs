module CrawlSage.Tests.Extract

open Xunit
open CrawlSage

let private sample =
    """
    <html><head>
      <script id="__NEXT_DATA__" type="application/json">
        {"props":{"pageProps":{"title":"Hello"}}}
      </script>
      <script type="application/ld+json">{"@type":"Article","headline":"News"}</script>
      <script>window.__NUXT__ = {"data":[{"name":"A"},{"name":"B"}]};</script>
    </head><body></body></html>
    """

[<Fact>]
let ``nextData extracts the __NEXT_DATA__ payload`` () =
    let title =
        Html.parse sample
        |> Extract.nextData
        |> Option.bind (Extract.path [ "props"; "pageProps"; "title" ])
        |> Option.bind Extract.asString

    Assert.Equal(Some "Hello", title)

[<Fact>]
let ``jsonLd returns each ld+json block`` () =
    let blocks = Html.parse sample |> Extract.jsonLd
    Assert.Equal(1, blocks.Length)

    let headline =
        blocks |> List.head |> Extract.prop "headline" |> Option.bind Extract.asString

    Assert.Equal(Some "News", headline)

[<Fact>]
let ``assignedJson lifts window.__NUXT__ and navigates the array`` () =
    let names =
        Html.parse sample
        |> Extract.assignedJson "__NUXT__"
        |> Option.bind (Extract.path [ "data" ])
        |> Option.map Extract.asList
        |> Option.defaultValue []
        |> List.choose (Extract.prop "name" >> Option.bind Extract.asString)

    Assert.Equal<string list>([ "A"; "B" ], names)

[<Fact>]
let ``assignedJson lifts an array-assigned global and stops at its close`` () =
    // Mirrors quotes.toscrape.com/js: `var data = [ {...}, {...} ];` then more script.
    let html =
        """
        <html><body>
          <script>
            var data = [
              {"author": "Albert Einstein", "text": "A"},
              {"author": "J.K. Rowling", "text": "B"}
            ];
            for (var i in data) { document.write(data[i].text); }
          </script>
        </body></html>
        """

    let authors =
        Html.parse html
        |> Extract.assignedJson "data"
        |> Option.map Extract.asList
        |> Option.defaultValue []
        |> List.choose (Extract.prop "author" >> Option.bind Extract.asString)

    Assert.Equal<string list>([ "Albert Einstein"; "J.K. Rowling" ], authors)

[<Fact>]
let ``json returns None on malformed input`` () =
    Assert.True((Extract.json "{ not json").IsNone)

[<Fact>]
let ``asList enumerates a JSON array`` () =
    let count =
        Extract.json "[1,2,3]"
        |> Option.map (Extract.asList >> List.length)
        |> Option.defaultValue 0

    Assert.Equal(3, count)
