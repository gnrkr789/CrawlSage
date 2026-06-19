---
name: data-export
description: Export scraped data from CrawlSage to CSV, JSON, Parquet or a database. Use when the user wants to save/persist/write scraped results, produce a CSV/JSON/Excel file, load into pandas/Deedle, or pipe items to a sink. Covers the Export module and a Scrapy-style item pipeline.
---

# data-export

Scraping is only half the job — the data has to land somewhere. CrawlSage models output
as **sinks**: a scraped item flows into one or more writers (CSV, JSON, DB).

## JSON (zero dependencies)

`System.Text.Json` ships with .NET. Good default for nested data.

```fsharp
namespace CrawlSage

open System.IO
open System.Text.Json

module Export =

    let private options = JsonSerializerOptions(WriteIndented = true)

    /// Write a sequence of items to a JSON array file.
    let toJson (path: string) (items: 'T seq) =
        use stream = File.Create(path)
        JsonSerializer.Serialize(stream, Seq.toArray items, options)

    /// Append one item per line (JSON Lines) — stream-friendly for big crawls.
    let appendJsonLine (path: string) (item: 'T) =
        use writer = new StreamWriter(path, append = true)
        writer.WriteLine(JsonSerializer.Serialize(item))
```

## CSV (CsvHelper)

```bash
dotnet add src/CrawlSage/CrawlSage.fsproj package CsvHelper
```

```fsharp
open System.Globalization
open CsvHelper

let toCsv (path: string) (items: 'T seq) =
    use writer = new StreamWriter(path)
    use csv = new CsvWriter(writer, CultureInfo.InvariantCulture)
    csv.WriteRecords(items)
```

> F# records work directly with CsvHelper — each field becomes a column. Use
> `[<CLIMutable>]` on the record if CsvHelper needs to construct it on *read*.

## Data frames & Excel (Deedle)

For pandas-style post-processing — the gap this closes vs Python:

```bash
dotnet add src/CrawlSage/CrawlSage.fsproj package Deedle
```

```fsharp
open Deedle

let frame = Frame.ofRecords items
frame |> Frame.saveCsv "out.csv"
// group, pivot, aggregate, then save — the Deedle API mirrors pandas closely.
```

## Database

Use **Dapper** for a thin SQL layer, or **EF Core** if you want migrations. Batch
inserts; don't write one row per round-trip.

## Item pipeline (Scrapy-style, Phase 5)

Model export as a list of sinks each item passes through, so a crawl can fan out to
several destinations:

```fsharp
type Sink<'T> = 'T -> unit

let pipeline (sinks: Sink<'T> list) (item: 'T) =
    for sink in sinks do sink item

// usage
let writeRow = Export.appendJsonLine "out.jsonl"
let log item = printfn "scraped %A" item
let emit = pipeline [ writeRow; log ]
```

## Guidance

- **Stream large crawls** (JSON Lines / append) instead of buffering everything in
  memory, then writing once.
- **Deduplicate before writing** — keep a `HashSet` of seen keys (URL / id).
- **Pick the format for the consumer:** JSON/JSONL for further code, CSV for
  spreadsheets, Parquet/DB for analytics at scale.
- Write output under `data/` (git-ignored) so scraped data never gets committed.
