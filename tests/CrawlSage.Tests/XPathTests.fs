module CrawlSage.Tests.XPath

open Xunit
open CrawlSage

let private sample =
    """
    <html><body>
      <ul id="list">
        <li class="item"><a href="/a">First</a></li>
        <li class="item"><a href="/b">Second</a></li>
        <li class="other"><a href="/c">Third</a></li>
      </ul>
    </body></html>
    """

[<Fact>]
let ``selectAllXPath returns matching elements in order`` () =
    let texts =
        Html.parse sample
        |> Html.selectAllXPath "//li[@class='item']/a"
        |> List.map Html.text

    Assert.Equal<string list>([ "First"; "Second" ], texts)

[<Fact>]
let ``selectXPath returns the first match, composing with attr`` () =
    let href =
        Html.parse sample
        |> Html.selectXPath "//ul[@id='list']/li/a"
        |> Option.bind (Html.attr "href")

    Assert.Equal(Some "/a", href)

[<Fact>]
let ``selectXPath returns None when nothing matches`` () =
    let hit = Html.parse sample |> Html.selectXPath "//table"
    Assert.True(hit.IsNone)

[<Fact>]
let ``selectAllXPath expresses a predicate CSS can't`` () =
    // "the <li> whose <a>'s href contains 'b'" — awkward/impossible in CSS, trivial in XPath.
    let texts =
        Html.parse sample
        |> Html.selectAllXPath "//li[a[contains(@href,'b')]]"
        |> List.map Html.text

    Assert.Equal<string list>([ "Second" ], texts)
