namespace CrawlSage

open System
open AngleSharp.Dom
open AngleSharp.Html.Parser

/// Forgiving HTML parsing + CSS-selector queries over AngleSharp.
///
/// Functions are curried with the node/element **last** so queries pipe with <c>|&gt;</c>,
/// and every lookup returns an <c>option</c> instead of null. Feed canned HTML strings to
/// <c>parse</c> in tests to keep them hermetic.
module Html =

    let private parser = HtmlParser()

    /// Parse a raw HTML string into a queryable document. Malformed markup is recovered
    /// the way a browser would.
    let parse (html: string) : IDocument =
        parser.ParseDocument(html) :> IDocument

    /// First element matching a CSS selector, if any.
    let select (selector: string) (node: IParentNode) : IElement option =
        node.QuerySelector(selector) |> Option.ofObj

    /// Every element matching a CSS selector, in document order.
    let selectAll (selector: string) (node: IParentNode) : IElement list =
        node.QuerySelectorAll(selector) |> List.ofSeq

    /// Trimmed text content of an element (including its descendants).
    let text (element: IElement) : string =
        element.TextContent.Trim()

    /// Value of an attribute (e.g. "href"), if present.
    let attr (name: string) (element: IElement) : string option =
        element.GetAttribute(name) |> Option.ofObj

    /// Value of an attribute, or <paramref name="fallback"/> when it is absent.
    let attrOr (fallback: string) (name: string) (element: IElement) : string =
        attr name element |> Option.defaultValue fallback

    /// Every link on the page as an absolute URL: the <c>href</c> of each <c>&lt;a&gt;</c>,
    /// resolved against <paramref name="baseUrl"/> (usually the page's own URL) and
    /// de-duplicated in document order. Fragment-only, <c>javascript:</c> and <c>mailto:</c>
    /// hrefs are dropped — feed the result straight to <c>Request.create &gt;&gt; Follow</c>.
    let links (baseUrl: string) (node: IParentNode) : string list =
        node
        |> selectAll "a[href]"
        |> List.choose (attr "href")
        |> List.map (fun href -> href.Trim())
        |> List.filter (fun href ->
            href <> ""
            && not (href.StartsWith "#")
            && not (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            && not (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)))
        |> List.map (Url.resolve baseUrl)
        |> List.distinct
