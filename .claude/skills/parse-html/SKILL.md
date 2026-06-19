---
name: parse-html
description: Parse and extract data from HTML in CrawlSage using AngleSharp with CSS selectors. Use when the user wants to scrape fields/lists/tables from a page, select elements, read text or attributes, or build/extend the Html parsing module. Covers the F#-idiomatic selector DSL.
---

# parse-html

CrawlSage parses HTML with **AngleSharp** (a standards-compliant, forgiving parser —
the closest .NET has to BeautifulSoup) behind a thin F# module, `Html.fs`.

## If `Html.fs` does not exist yet (Phase 2)

Create it. Add the package and a `Html` module wrapping AngleSharp.

```bash
dotnet add src/CrawlSage/CrawlSage.fsproj package AngleSharp
```

Add `Html.fs` to `CrawlSage.fsproj` **after** `Http.fs` in compile order:

```xml
<Compile Include="Html.fs" />
```

```fsharp
namespace CrawlSage

open AngleSharp
open AngleSharp.Dom

/// Forgiving HTML parsing + CSS-selector queries, BeautifulSoup-style.
module Html =

    let private context = BrowsingContext.New(Configuration.Default)

    /// Parse a raw HTML string into a queryable document.
    let parse (html: string) : IDocument =
        context.OpenAsync(fun req -> req.Content(html) |> ignore)
        |> Async.AwaitTask
        |> Async.RunSynchronously

    /// First element matching a CSS selector, if any.
    let select (selector: string) (node: IParentNode) : IElement option =
        node.QuerySelector(selector) |> Option.ofObj

    /// All elements matching a CSS selector.
    let selectAll (selector: string) (node: IParentNode) : IElement list =
        node.QuerySelectorAll(selector) |> List.ofSeq

    /// Trimmed text content of an element.
    let text (el: IElement) : string =
        el.TextContent.Trim()

    /// Value of an attribute (e.g. "href"), if present.
    let attr (name: string) (el: IElement) : string option =
        el.GetAttribute(name) |> Option.ofObj
```

## Usage

```fsharp
open CrawlSage

let doc = Http.getString "https://news.ycombinator.com" |> Async.RunSynchronously |> Html.parse

// A list of records, BeautifulSoup-style.
let stories =
    doc
    |> Html.selectAll ".titleline > a"
    |> List.map (fun a ->
        {| Title = Html.text a
           Url = Html.attr "href" a |> Option.defaultValue "" |})
```

## Guidance

- **Prefer CSS selectors.** AngleSharp's `QuerySelectorAll` covers the vast majority of
  cases and reads cleanly. Reach for XPath (via `HtmlAgilityPack`) only when CSS can't
  express the query.
- **Return `option`, not null.** Wrap every nullable AngleSharp result with
  `Option.ofObj` so callers stay in F#'s null-free world.
- **Compose with `|>`.** Keep helpers curried with the node last so queries pipe.
- **Test against canned HTML**, never the live site — feed a string literal to
  `Html.parse` in unit tests so they stay hermetic.

## Gotchas

- AngleSharp lower-cases tag/attribute names per the HTML spec.
- `TextContent` includes descendant text; use a more specific selector if you only want
  a leaf's text.
- For malformed real-world HTML, AngleSharp still parses — it mirrors browser behaviour.
