module CrawlSage.Tests.ExtractProperties

open System.Collections.Generic
open System.Text.Json
open FsCheck
open FsCheck.Xunit
open CrawlSage

/// Safe alphanumeric strings, so embedding generated keys in a <script> can't break the
/// surrounding HTML or JSON — the property then isolates the balanced-bracket slicer.
type SafeGen =
    static member Safe() : Arbitrary<string> =
        gen {
            let! chars = Gen.nonEmptyListOf (Gen.elements ([ 'a' .. 'z' ] @ [ 'A' .. 'Z' ] @ [ '0' .. '9' ]))
            return System.String(List.toArray chars)
        }
        |> Arb.fromGen

[<Property>]
let ``json never throws on arbitrary input`` (s: string) =
    Extract.json s |> ignore
    true

[<Property(Arbitrary = [| typeof<SafeGen> |])>]
let ``assignedJson round-trips an embedded object past trailing script`` (pairs: (string * int) list) =
    let dict = Dictionary<string, int>()

    for key, value in pairs do
        dict.[key] <- value

    let jsonText = JsonSerializer.Serialize dict
    let html = sprintf "<html><body><script>window.__DATA__ = %s; boot();</script></body></html>" jsonText

    match Html.parse html |> Extract.assignedJson "__DATA__" with
    | Some node -> node.ToJsonString() = jsonText
    | None -> false
