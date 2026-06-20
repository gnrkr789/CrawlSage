module CrawlSage.Samples.QuotesToCsv.Program

// Recipe: *extract a list* + *export to CSV*.
// Fetch one page politely, map each `.quote` block to a record, and write a CSV.

open System.IO
open CrawlSage

/// One scraped quote — a flat record so it maps cleanly to CSV columns.
type Quote =
    { Text: string
      Author: string
      Tags: string }

/// Parse every `.quote` block on the page into a Quote.
let parseQuotes (response: Response) : Quote list =
    Html.parse response.Body
    |> Html.selectAll ".quote"
    |> List.map (fun quote ->
        { Text = quote |> Html.select ".text" |> Option.map Html.text |> Option.defaultValue ""
          Author = quote |> Html.select ".author" |> Option.map Html.text |> Option.defaultValue ""
          Tags = quote |> Html.selectAll ".tags .tag" |> List.map Html.text |> String.concat "; " })

[<EntryPoint>]
let main _ =
    // One polite fetch (throttled, retried, timed-out), then parse + export.
    let response =
        Request.create "https://quotes.toscrape.com/"
        |> Resilience.politeFetch
        |> Async.RunSynchronously

    let quotes = parseQuotes response

    Directory.CreateDirectory "data" |> ignore
    let path = Path.Combine("data", "quotes.csv")
    Export.toCsv path quotes

    printfn "Scraped %d quotes -> %s" quotes.Length path

    for quote in quotes |> List.truncate 3 do
        printfn "  %s — %s" quote.Text quote.Author

    0
