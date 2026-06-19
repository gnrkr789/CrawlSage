---
name: parse-html
description: Parse and extract data from HTML in CrawlSage using AngleSharp with CSS selectors. Use when the user wants to scrape fields/lists/tables from a page, select elements, read text or attributes, or build/extend the Html parsing module. Covers the F#-idiomatic selector DSL.
---

# parse-html

CrawlSage parses HTML with **AngleSharp** (a standards-compliant, forgiving parser —
the closest .NET has to BeautifulSoup) behind a thin F# module, `Html.fs`.

## The `Html` module (shipped — Phase 2)

`src/CrawlSage/Html.fs` exposes a small, pipe-friendly, null-free API. Everything is
curried with the node/element **last** so queries compose with `|>`, and lookups return
`option` instead of null.

| Function | Signature | Notes |
| --- | --- | --- |
| `parse` | `string -> IDocument` | forgiving; recovers malformed markup like a browser |
| `select` | `string -> IParentNode -> IElement option` | first CSS match |
| `selectAll` | `string -> IParentNode -> IElement list` | all matches, in document order |
| `text` | `IElement -> string` | trimmed text content (incl. descendants) |
| `attr` | `string -> IElement -> string option` | attribute value if present |
| `attrOr` | `string -> string -> IElement -> string` | attribute value, or a fallback |

## Usage

```fsharp
open CrawlSage

let doc =
    Http.getString "https://news.ycombinator.com"
    |> Async.RunSynchronously
    |> Html.parse

// A list of records, BeautifulSoup-style.
let stories =
    doc
    |> Html.selectAll ".titleline > a"
    |> List.map (fun a -> {| Title = Html.text a; Url = Html.attrOr "" "href" a |})
```

Nest `select` inside a `selectAll` to scrape structured rows:

```fsharp
let rows =
    doc
    |> Html.selectAll "tr.row"
    |> List.choose (fun tr ->
        match tr |> Html.select "td.name", tr |> Html.select "td.price" with
        | Some name, Some price -> Some {| Name = Html.text name; Price = Html.text price |}
        | _ -> None)
```

## Extending it

Need a new helper (XPath, table extraction, `attrMany`)? Add it to `Html.fs`, keep it
**curried node-last** and **`option`-returning**, and add a hermetic test against a
canned HTML string literal. (XPath isn't in AngleSharp's core CSS engine — pull in
`HtmlAgilityPack` only if a query genuinely can't be expressed in CSS.)

To wire AngleSharp from scratch: `dotnet add src/CrawlSage/CrawlSage.fsproj package AngleSharp`.

## Guidance

- **Prefer CSS selectors.** `selectAll`/`select` cover the vast majority of cases and
  read cleanly.
- **Stay in option-land.** Every nullable AngleSharp result is wrapped with
  `Option.ofObj`; keep new helpers null-free too.
- **Compose with `|>`.** Node-last currying is what makes `doc |> Html.selectAll "…"`
  read well — preserve it.
- **Test against canned HTML**, never the live site — feed a string literal to
  `Html.parse` so tests stay hermetic.

## Gotchas

- AngleSharp lower-cases tag/attribute names per the HTML spec.
- `text` returns *all* descendant text; use a more specific selector for a leaf's text.
- For malformed real-world HTML, AngleSharp still parses — it mirrors browser recovery.
