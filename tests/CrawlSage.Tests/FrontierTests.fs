module CrawlSage.Tests.Frontier

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
