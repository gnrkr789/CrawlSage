module CrawlSage.Tests.Export

open System.IO
open System.Text.Json
open Xunit
open CrawlSage

// Public so CsvHelper's reflection can map the columns (item types are public in practice).
type Row = { Name: string; Score: int }

let private rows = [ { Name = "A"; Score = 1 }; { Name = "B"; Score = 2 } ]

let private tempPath (ext: string) =
    Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ext)

[<Fact>]
let ``toJson writes a readable JSON array`` () =
    let path = tempPath ".json"

    try
        Export.toJson path rows
        use parsed = JsonDocument.Parse(File.ReadAllText path)
        Assert.Equal(JsonValueKind.Array, parsed.RootElement.ValueKind)
        Assert.Equal(2, parsed.RootElement.GetArrayLength())
    finally
        File.Delete path

[<Fact>]
let ``appendJsonLine writes one JSON object per line`` () =
    let path = tempPath ".jsonl"

    try
        for row in rows do
            Export.appendJsonLine path row

        let lines = File.ReadAllLines path
        Assert.Equal(2, lines.Length)
        lines |> Array.iter (fun line -> (JsonDocument.Parse line).Dispose())
    finally
        File.Delete path

[<Fact>]
let ``toCsv writes a header and one row per record`` () =
    let path = tempPath ".csv"

    try
        Export.toCsv path rows
        let lines = File.ReadAllLines path |> Array.filter (fun l -> l.Trim() <> "")
        Assert.Equal(3, lines.Length) // header + 2 rows
        Assert.Contains("Name", lines.[0])
        Assert.Contains("Score", lines.[0])
    finally
        File.Delete path

[<Fact>]
let ``fanout sends each item to every sink`` () =
    let a = ResizeArray<Row>()
    let b = ResizeArray<Row>()
    let sink = Export.fanout [ a.Add; b.Add ]
    rows |> List.iter sink
    Assert.Equal(2, a.Count)
    Assert.Equal(2, b.Count)

[<Fact>]
let ``toFrame builds a frame with one row per record`` () =
    let frame = Export.toFrame rows
    Assert.Equal(2, frame.RowCount)
