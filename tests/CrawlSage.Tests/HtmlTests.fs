module CrawlSage.Tests.Html

open Xunit
open CrawlSage

let private sample =
    """
    <html>
      <body>
        <h1 class="title">Hello</h1>
        <ul id="list">
          <li class="item"><a href="/a">First</a></li>
          <li class="item"><a href="/b">Second</a></li>
          <li class="item"><a href="/c">Third</a></li>
        </ul>
      </body>
    </html>
    """

[<Fact>]
let ``select returns the first match`` () =
    let title = Html.parse sample |> Html.select "h1.title" |> Option.map Html.text
    Assert.Equal(Some "Hello", title)

[<Fact>]
let ``select returns None when nothing matches`` () =
    let hit = Html.parse sample |> Html.select ".missing"
    Assert.True(hit.IsNone)

[<Fact>]
let ``selectAll returns every match in order`` () =
    let items =
        Html.parse sample
        |> Html.selectAll "li.item"
        |> List.map Html.text

    Assert.Equal<string list>([ "First"; "Second"; "Third" ], items)

[<Fact>]
let ``attr reads a present attribute and None for an absent one`` () =
    let firstLink = Html.parse sample |> Html.select "li.item a"
    Assert.Equal(Some "/a", firstLink |> Option.bind (Html.attr "href"))
    Assert.True((firstLink |> Option.bind (Html.attr "rel")).IsNone)

[<Fact>]
let ``selectAll then map extracts hrefs`` () =
    let hrefs =
        Html.parse sample
        |> Html.selectAll "li.item > a"
        |> List.map (Html.attrOr "" "href")

    Assert.Equal<string list>([ "/a"; "/b"; "/c" ], hrefs)

[<Fact>]
let ``parse recovers from malformed HTML like a browser`` () =
    let paragraphs =
        Html.parse "<div><p>unclosed<p>tags<span>here"
        |> Html.selectAll "p"
        |> List.length

    Assert.True(paragraphs >= 2)

[<Fact>]
let ``links resolves, filters and de-duplicates anchors`` () =
    let html =
        """
        <a href="/page/2/">next</a>
        <a href="https://other.com/x">external</a>
        <a href="/page/2/">dup</a>
        <a href="#section">anchor</a>
        <a href="javascript:void(0)">js</a>
        <a href="mailto:a@b.com">mail</a>
        """

    let links = Html.parse html |> Html.links "https://site.com/list"

    Assert.Equal<string list>(
        [ "https://site.com/page/2/"; "https://other.com/x" ],
        links
    )
