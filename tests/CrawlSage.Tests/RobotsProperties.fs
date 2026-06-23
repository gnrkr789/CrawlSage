module CrawlSage.Tests.RobotsProperties

open FsCheck
open FsCheck.Xunit
open CrawlSage

// Inputs are non-null by F# convention (the nullable feature is off; a robots body and a
// configured User-Agent are never null), so we generate non-null strings — `null` is not a
// value these functions are meant to accept.

// --- totality on arbitrary (non-null) input ---

[<Property>]
let ``parse never throws on arbitrary text`` (NonNull text) =
    Robots.parse text |> ignore
    true

[<Property>]
let ``isAllowed never throws on arbitrary inputs`` (NonNull ua) (NonNull path) (NonNull text) =
    Robots.isAllowed ua path (Robots.parse text) |> ignore
    true

[<Property>]
let ``crawlDelay never throws on arbitrary inputs`` (NonNull ua) (NonNull text) =
    Robots.crawlDelay ua (Robots.parse text) |> ignore
    true

// --- behavioural invariants ---

[<Property>]
let ``empty rules allow every path`` (NonNull ua) (NonNull path) = Robots.isAllowed ua path Robots.Rules.Empty

[<Property>]
let ``Disallow-all blocks every absolute path`` (rest: string) =
    let rules = Robots.parse "User-agent: *\nDisallow: /"
    not (Robots.isAllowed "anybot" ("/" + rest) rules)

[<Property>]
let ``a longer Allow beats a Disallow`` (rest: string) =
    // /admin is disallowed, but /admin/public (a longer match) is explicitly allowed.
    let rules = Robots.parse "User-agent: *\nDisallow: /admin\nAllow: /admin/public"

    Robots.isAllowed "bot" ("/admin/public/" + rest) rules
    && not (Robots.isAllowed "bot" "/admin/secret" rules)
