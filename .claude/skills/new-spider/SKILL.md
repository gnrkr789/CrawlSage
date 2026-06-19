---
name: new-spider
description: Scaffold a new CrawlSage crawler. Use when the user wants to start a new spider/crawler/scraper, add a runnable example under samples/, or says "create a crawler for <site>". Wires an F# console project to CrawlSage's Spider engine (fetch → parse → pipeline).
---

# new-spider

Scaffold a new, runnable crawler under `samples/`, built on CrawlSage's `Spider` engine.

## Steps

1. **Pick a name** in PascalCase from the target (e.g. `HackerNews`, `BlogPosts`).
   The sample lives at `samples/<Name>/`.

2. **Create the project and add it to the solution:**

   ```bash
   dotnet new console -lang "F#" -o samples/<Name> -n CrawlSage.Samples.<Name>
   dotnet sln CrawlSage.slnx add samples/<Name>/CrawlSage.Samples.<Name>.fsproj
   dotnet add samples/<Name>/CrawlSage.Samples.<Name>.fsproj reference src/CrawlSage/CrawlSage.fsproj
   ```

3. **Replace `Program.fs`** with the template below; set the seed URL, the item shape,
   and the selectors.

4. **Run it:** `dotnet run --project samples/<Name>`

## Template

```fsharp
module CrawlSage.Samples.<Name>.Program

open CrawlSage

/// One scraped record — shape it to the site.
type Item = { Title: string; Url: string }

/// Turn a fetched page into items + follow-up requests.
let parse (response: Response) : ParseResult<Item> list =
    let doc = Html.parse response.Body

    let items =
        doc
        |> Html.selectAll "article h2 > a"
        |> List.map (fun a -> Item { Title = Html.text a; Url = Html.attrOr "" "href" a })

    let next =
        doc
        |> Html.selectAll "a.next"
        |> List.choose (Html.attr "href")
        |> List.map (Request.create >> Follow)

    items @ next

[<EntryPoint>]
let main _ =
    let spider =
        { Seeds = [ Request.create "https://example.com" ]
          Parse = parse
          Pipeline = (fun item -> printfn "%s — %s" item.Title item.Url)
          Options = { SpiderOptions.Default with MaxDepth = 2 } }

    Spider.crawl spider |> Async.RunSynchronously
    0
```

`Spider.crawl` uses the production downloader (`Resilience.politeFetch`: throttled,
retried, timed-out). For a dry run or a hermetic test, `Spider.crawlWith myFetch spider`
injects your own fetch.

## Then

- Extract structured data with the **`parse-html`** skill (`select` / `selectAll` /
  `text` / `attr`).
- If the page needs JavaScript, render it with the **`dynamic-page`** skill, then parse.
- For logged-in pages, use **`session-auth`**.
- To persist items, swap the `Pipeline` for a sink from **`data-export`**.
- Tune politeness (rate, robots.txt, proxies) with **`crawl-ops`**.

## Conventions

- Sample namespace: `CrawlSage.Samples.<Name>`.
- One self-contained crawler per folder; no shared sample state.
- Keep the seed URL and `Options` near the top so they're easy to find and change.
- Be polite by default, and write any output under `data/` (git-ignored).
