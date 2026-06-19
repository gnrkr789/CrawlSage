---
name: new-spider
description: Scaffold a new CrawlSage crawler. Use when the user wants to start a new spider/crawler/scraper, add a runnable example under samples/, or says "create a crawler for <site>". Wires an F# console project to CrawlSage's fetch → parse → output flow.
---

# new-spider

Scaffold a new, runnable crawler under `samples/` and wire it into the solution.

## Steps

1. **Pick a name** in PascalCase from the target (e.g. `HackerNews`, `BlogPosts`).
   The sample lives at `samples/<Name>/`.

2. **Create the project and add it to the solution:**

   ```bash
   dotnet new console -lang "F#" -o samples/<Name> -n CrawlSage.Samples.<Name>
   dotnet sln CrawlSage.slnx add samples/<Name>/CrawlSage.Samples.<Name>.fsproj
   dotnet add samples/<Name>/CrawlSage.Samples.<Name>.fsproj reference src/CrawlSage/CrawlSage.fsproj
   ```

3. **Replace `Program.fs`** with the template below, then fill in the URL and parsing.

4. **Run it:** `dotnet run --project samples/<Name>`

## Template

```fsharp
module CrawlSage.Samples.<Name>.Program

open CrawlSage

/// One scraped record. Shape it to the site.
type Item = { Title: string; Url: string }

let private startUrl = "https://example.com"

/// Turn a fetched page into items. Until the parsing DSL (Phase 2) lands, work the
/// body directly or use the `parse-html` skill once `Html.fs` exists.
let parse (response: Response) : Item list =
    // TODO: extract items from response.Body
    [ { Title = "example"; Url = response.Request.Url } ]

[<EntryPoint>]
let main _ =
    async {
        let! response = Http.fetch (Request.create startUrl)
        if response.IsSuccess then
            for item in parse response do
                printfn "%s — %s" item.Title item.Url
        else
            eprintfn "Fetch failed: %d" response.StatusCode
        return 0
    }
    |> Async.RunSynchronously
```

## Then

- To extract structured data, use the **`parse-html`** skill.
- If the page needs JavaScript, use the **`dynamic-page`** skill.
- For logged-in pages, use **`session-auth`**.
- To save results, use **`data-export`**.
- Keep crawls polite — see **`crawl-ops`**.

## Conventions

- Sample namespace: `CrawlSage.Samples.<Name>`.
- One self-contained crawler per folder; no shared sample state.
- Hard-code the start URL as a `let` at the top so it's easy to find and change.
