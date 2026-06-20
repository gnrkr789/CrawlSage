module CrawlSage.Tests.Frontier

open System.IO
open Xunit
open CrawlSage

[<Fact>]
let ``inMemory dedups by normalized url and yields FIFO`` () =
    let f = Frontier.inMemory ()
    Assert.True(f.Add (Request.create "https://s/a") 0)
    Assert.True(f.Add (Request.create "https://s/b") 1)
    Assert.False(f.Add (Request.create "https://s/a#frag") 0) // same after Url.normalize

    let got = f.Take 10 |> List.map (fun (r, d) -> r.Url, d)
    Assert.Equal<(string * int) list>([ "https://s/a", 0; "https://s/b", 1 ], got)
    Assert.Empty(f.Take 10)

[<Fact>]
let ``Take returns at most n, leaving the rest queued`` () =
    let f = Frontier.inMemory ()

    for i in 1..5 do
        f.Add (Request.create $"https://s/{i}") 0 |> ignore

    Assert.Equal(2, (f.Take 2).Length)
    Assert.Equal(2, (f.Take 2).Length)
    Assert.Equal(1, (f.Take 2).Length)
    Assert.Empty(f.Take 2)

[<Fact>]
let ``bounded drops additions past the cap`` () =
    let f = Frontier.bounded 2
    Assert.True(f.Add (Request.create "https://s/1") 0)
    Assert.True(f.Add (Request.create "https://s/2") 0)
    Assert.False(f.Add (Request.create "https://s/3") 0) // full
    Assert.Equal(2, (f.Take 10).Length)

[<Fact>]
let ``persistent resumes seen and pending across instances`` () =
    let dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())

    try
        let first = Frontier.persistent dir
        first.Add (Request.create "https://s/a") 0 |> ignore
        first.Add (Request.create "https://s/b") 1 |> ignore
        let taken = first.Take 1 |> List.map (fun (r, d) -> r.Url, d) // consumes /a
        Assert.Equal<(string * int) list>([ "https://s/a", 0 ], taken)

        // A fresh instance over the same dir: /a is already seen + consumed, /b is pending.
        let resumed = Frontier.persistent dir
        Assert.False(resumed.Add (Request.create "https://s/a") 0)
        let rest = resumed.Take 10 |> List.map (fun (r, d) -> r.Url, d)
        Assert.Equal<(string * int) list>([ "https://s/b", 1 ], rest)
    finally
        if Directory.Exists dir then
            Directory.Delete(dir, true)
