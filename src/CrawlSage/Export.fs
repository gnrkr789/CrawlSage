namespace CrawlSage

open System.Globalization
open System.IO
open System.Text.Json
open CsvHelper
open Deedle

/// Output sinks — get scraped data *out*. Stream items to a file as you crawl (partially
/// apply a path to get a <see cref="T:CrawlSage.Sink`1"/> for a <c>Spider.Pipeline</c>),
/// or write a finished batch. <c>toFrame</c> is the on-ramp to Deedle for pandas-style
/// post-processing — the data-wrangling story F# is often said to lack.
module Export =

    let private jsonOptions = JsonSerializerOptions(WriteIndented = true)

    /// Write an entire sequence of items to a pretty JSON array file.
    let toJson (path: string) (items: 'T seq) : unit =
        use stream = File.Create path
        JsonSerializer.Serialize(stream, Seq.toArray items, jsonOptions)

    /// Append one item as a line of JSON (JSON Lines) — stream-friendly for big crawls.
    /// Partially apply the path to get a <c>Sink&lt;'T&gt;</c> for a spider pipeline.
    let appendJsonLine (path: string) (item: 'T) : unit =
        use writer = new StreamWriter(path, append = true)
        writer.WriteLine(JsonSerializer.Serialize(item))

    /// Write an entire sequence of records to a CSV file (one column per field).
    let toCsv (path: string) (items: 'T seq) : unit =
        use writer = new StreamWriter(path)
        use csv = new CsvWriter(writer, CultureInfo.InvariantCulture)
        csv.WriteRecords(items)

    /// Fan one item out to several sinks — use as a <c>Spider.Pipeline</c> so a single
    /// crawl writes to many destinations at once.
    let fanout (sinks: Sink<'T> list) : Sink<'T> =
        fun item -> for sink in sinks do sink item

    /// A sink that prints each item with <c>%A</c> — handy while developing a crawler.
    let console (item: 'T) : unit = printfn "%A" item

    /// Build a Deedle data frame from records — the entry point to pandas-style
    /// post-processing (group, pivot, aggregate, join) before saving.
    let toFrame (items: 'T seq) : Frame<int, string> =
        Frame.ofRecords items
