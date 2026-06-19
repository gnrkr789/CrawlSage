---
name: data-export
description: Export scraped data from CrawlSage to JSON, JSON Lines, CSV or a database, and into Deedle for pandas-style analysis. Use when the user wants to save/persist/write scraped results, produce a CSV/JSON file, wire a Spider pipeline to disk, or post-process data. Covers the Export module and the Sink seam.
---

# data-export

Scraping is half the job — the data has to land somewhere. CrawlSage models output as
**sinks**: `Sink<'T> = 'T -> unit`, the exact shape of a `Spider.Pipeline`. Stream items to
a file as you crawl, or write a finished batch; `toFrame` hands off to Deedle for
pandas-style work — the data-wrangling story F# is often said to lack.

## The `Export` module (shipped — Phase 5)

`src/CrawlSage/Export.fs`:

| Function | Shape | Use |
| --- | --- | --- |
| `toJson path items` | batch | pretty JSON array file |
| `toCsv path items` | batch | CSV (CsvHelper), one column per record field |
| `appendJsonLine path` | `Sink<'T>` | append one JSON object per line (JSONL) — streaming |
| `console` | `Sink<'T>` | print each item with `%A` while developing |
| `fanout [s1; s2]` | `Sink<'T>` | send each item to several sinks at once |
| `toFrame items` | → `Frame` | Deedle frame for group / pivot / aggregate |

## Wire a sink into a crawl

`Spider.Pipeline` is a `Sink<'Item>`, so an `Export` sink drops straight in:

```fsharp
open CrawlSage

let spider =
    { Seeds = [ Request.create "https://example.com" ]
      Parse = parse
      Pipeline = Export.appendJsonLine "data/items.jsonl"   // streams as it crawls
      Options = SpiderOptions.Default }

Spider.crawl spider |> Async.RunSynchronously
```

Fan out to several destinations at once:

```fsharp
Pipeline = Export.fanout [ Export.appendJsonLine "data/items.jsonl"; Export.console ]
```

## Batch write + analyse

When you already hold all the items:

```fsharp
Export.toJson "data/items.json" items
Export.toCsv  "data/items.csv"  items          // item type must be a public record

let frame = Export.toFrame items               // pandas-style from here
// frame |> Frame.groupRowsBy "category" |> Frame.…
```

> CsvHelper maps **public** properties — keep scraped item records public (the default).

## Database

For a DB sink, use **Dapper** (thin SQL) or **EF Core** (migrations); wrap an insert as a
`Sink<'T>` and batch the writes — never one row per round-trip.

## Guidance

- **Stream big crawls** with `appendJsonLine` instead of buffering everything in memory and
  writing once.
- **Deduplicate before writing** — a `HashSet` of seen keys (URL / id).
- **Pick the format for the consumer:** JSONL for further code, CSV for spreadsheets,
  Deedle / DB for analysis.
- **Write under `data/`** (git-ignored) so scraped data never gets committed.
