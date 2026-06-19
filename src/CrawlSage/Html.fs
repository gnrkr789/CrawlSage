namespace CrawlSage

open AngleSharp.Dom
open AngleSharp.Html.Parser

/// Forgiving HTML parsing + CSS-selector queries — BeautifulSoup-style, over AngleSharp.
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
