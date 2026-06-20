namespace CrawlSage

open System
open System.Globalization
open System.Collections.Concurrent
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

/// robots.txt parsing and per-host politeness — *not* evasion. The format is line-based
/// (the de-facto standard / RFC 9309): rules are grouped by <c>User-agent</c>, and we
/// honour <c>Allow</c>, <c>Disallow</c> (longest matching pattern wins; an equal-length
/// <c>Allow</c> beats a <c>Disallow</c>) and <c>Crawl-delay</c>. A missing or unreadable
/// robots.txt is treated as "crawl allowed"; <c>Disallow: /</c> means "stay out".
module Robots =

    /// The rules one <c>User-agent</c> block declares.
    type Group =
        { /// User-agent tokens this group targets, lower-cased (<c>"*"</c> = the default group).
          Agents: string list
          /// <c>Allow:</c> path patterns.
          Allow: string list
          /// <c>Disallow:</c> path patterns (an empty <c>Disallow:</c> is dropped — it means allow-all).
          Disallow: string list
          /// <c>Crawl-delay:</c> the host asked for, if any.
          CrawlDelay: TimeSpan option }

    /// A parsed robots.txt.
    type Rules =
        { /// Rule groups, in file order.
          Groups: Group list }

        /// Everything allowed — used for a missing or unreadable robots.txt.
        static member Empty = { Groups = [] }

    // ---- parsing ------------------------------------------------------------

    /// Drop a trailing <c>#</c> comment.
    let private stripComment (line: string) =
        match line.IndexOf '#' with
        | -1 -> line
        | i -> line.Substring(0, i)

    /// Split "Field: value" into a lower-cased field name and its trimmed value.
    let private directive (line: string) =
        match line.IndexOf ':' with
        | -1 -> None
        | i ->
            let field = line.Substring(0, i).Trim().ToLowerInvariant()
            let value = line.Substring(i + 1).Trim()
            if field = "" then None else Some(field, value)

    /// Parse robots.txt text into <see cref="T:CrawlSage.Robots.Rules"/>.
    let parse (text: string) : Rules =
        let groups = ResizeArray<Group>()
        let mutable current: Group option = None
        // True while consecutive `User-agent:` lines are still extending the current group;
        // the first rule line (Allow/Disallow/Crawl-delay) closes it, so the next
        // `User-agent:` starts a fresh group.
        let mutable collectingAgents = false

        let flush () =
            current |> Option.iter groups.Add
            current <- None

        let edit f =
            let g = current |> Option.defaultValue { Agents = []; Allow = []; Disallow = []; CrawlDelay = None }
            current <- Some(f g)

        let lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')

        for raw in lines do
            match raw |> stripComment |> directive with
            | None -> () // blank or comment-only line
            | Some(field, value) ->
                match field with
                | "user-agent" ->
                    if not collectingAgents then
                        flush ()
                        collectingAgents <- true

                    if value <> "" then
                        edit (fun g -> { g with Agents = g.Agents @ [ value.ToLowerInvariant() ] })
                | "disallow" ->
                    collectingAgents <- false
                    if value <> "" then edit (fun g -> { g with Disallow = g.Disallow @ [ value ] })
                | "allow" ->
                    collectingAgents <- false
                    if value <> "" then edit (fun g -> { g with Allow = g.Allow @ [ value ] })
                | "crawl-delay" ->
                    collectingAgents <- false

                    match Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture) with
                    | true, secs when secs >= 0.0 ->
                        edit (fun g -> { g with CrawlDelay = Some(TimeSpan.FromSeconds secs) })
                    | _ -> ()
                | _ -> () // Sitemap, Host, … — ignored

        flush ()
        { Groups = List.ofSeq groups }

    // ---- matching -----------------------------------------------------------

    /// If <paramref name="pattern"/> matches <paramref name="path"/> (prefix semantics,
    /// with <c>*</c> wildcards and an optional trailing <c>$</c> end-anchor), its
    /// significant length — longer = more specific; otherwise <c>-1</c>.
    let private matchLength (pattern: string) (path: string) : int =
        if pattern = "" then
            -1
        elif not (pattern.Contains "*" || pattern.EndsWith "$") then
            if path.StartsWith(pattern, StringComparison.Ordinal) then pattern.Length else -1
        else
            let anchored = pattern.EndsWith "$"
            let core = if anchored then pattern.Substring(0, pattern.Length - 1) else pattern
            let body = core.Split('*') |> Array.map Regex.Escape |> String.concat ".*"
            let rx = "^" + body + (if anchored then "$" else "")
            if Regex.IsMatch(path, rx) then core.Length else -1

    /// The group whose rules apply to <paramref name="userAgent"/>: the most specific
    /// matching agent token, else the <c>*</c> group, else none (= unrestricted).
    let private groupFor (userAgent: string) (rules: Rules) : Group option =
        let ua = userAgent.ToLowerInvariant()

        let specificity (g: Group) =
            g.Agents
            |> List.filter (fun a -> a <> "*" && a <> "" && ua.Contains a)
            |> List.fold (fun best a -> max best a.Length) 0

        let bestSpecific =
            rules.Groups
            |> List.map (fun g -> specificity g, g)
            |> List.filter (fun (s, _) -> s > 0)
            |> List.sortByDescending fst
            |> List.map snd
            |> List.tryHead

        match bestSpecific with
        | Some g -> Some g
        | None -> rules.Groups |> List.tryFind (fun g -> List.contains "*" g.Agents)

    /// Whether <paramref name="path"/> is crawlable for <paramref name="userAgent"/> under
    /// <paramref name="rules"/>. Longest matching pattern wins; an equal-length <c>Allow</c>
    /// beats a <c>Disallow</c> (the conventional tie-break).
    let isAllowed (userAgent: string) (path: string) (rules: Rules) : bool =
        match groupFor userAgent rules with
        | None -> true
        | Some g ->
            let best patterns =
                patterns |> List.fold (fun best p -> max best (matchLength p path)) -1

            best g.Allow >= best g.Disallow

    /// The host's <c>Crawl-delay</c> for <paramref name="userAgent"/>, if it set one.
    let crawlDelay (userAgent: string) (rules: Rules) : TimeSpan option =
        groupFor userAgent rules |> Option.bind (fun g -> g.CrawlDelay)

    // ---- per-host fetching --------------------------------------------------

    /// The robots.txt URL for the host of <paramref name="pageUrl"/>.
    let robotsUrl (pageUrl: string) : string option =
        match Uri.TryCreate(pageUrl, UriKind.Absolute) with
        | true, uri -> Some(uri.GetLeftPart(UriPartial.Authority) + "/robots.txt")
        | _ -> None

    let private pathOf (pageUrl: string) =
        match Uri.TryCreate(pageUrl, UriKind.Absolute) with
        | true, uri -> uri.PathAndQuery
        | _ -> pageUrl

    let private hostOf (pageUrl: string) =
        match Uri.TryCreate(pageUrl, UriKind.Absolute) with
        | true, uri -> uri.Authority.ToLowerInvariant()
        | _ -> pageUrl

    /// A per-host robots.txt cache: fetches each host's robots.txt once (via the injected
    /// <see cref="T:CrawlSage.Renderer"/>) and answers from the cached rules. Thread-safe —
    /// concurrent first-touches of one host share a single fetch. A missing, error, or
    /// unreadable file is cached as "allow all".
    type Cache(fetch: Renderer, userAgent: string) =
        let byHost = ConcurrentDictionary<string, Lazy<Task<Rules>>>()

        let rulesFor (pageUrl: string) : Async<Rules> =
            match robotsUrl pageUrl with
            | None -> async { return Rules.Empty }
            | Some url ->
                let entry =
                    byHost.GetOrAdd(
                        url,
                        fun u ->
                            lazy
                                (async {
                                    try
                                        let! resp = fetch (Request.create u)
                                        return (if resp.IsSuccess then parse resp.Body else Rules.Empty)
                                    with _ ->
                                        return Rules.Empty
                                 }
                                 |> Async.StartAsTask))

                async { return! entry.Value |> Async.AwaitTask }

        /// Is <paramref name="pageUrl"/> crawlable for this cache's User-Agent?
        member _.IsAllowed(pageUrl: string) : Async<bool> =
            async {
                let! rules = rulesFor pageUrl
                return isAllowed userAgent (pathOf pageUrl) rules
            }

        /// The host's <c>Crawl-delay</c> for this cache's User-Agent, if any.
        member _.CrawlDelay(pageUrl: string) : Async<TimeSpan option> =
            async {
                let! rules = rulesFor pageUrl
                return crawlDelay userAgent rules
            }

    /// Pace requests to the *same host*: hold a per-host gate (one fetch in flight per host)
    /// and ensure at least <c>delayFor request</c> has elapsed since that host's previous
    /// fetch started. Politeness as composition — stack it under a fetch like the
    /// <c>Resilience</c> wrappers. Different hosts proceed in parallel.
    let pacePerHost (delayFor: Request -> Async<TimeSpan>) (fetch: Renderer) : Renderer =
        let gates = ConcurrentDictionary<string, SemaphoreSlim>()
        let last = ConcurrentDictionary<string, DateTime>()

        fun request ->
            async {
                let host = hostOf request.Url
                let gate = gates.GetOrAdd(host, fun _ -> new SemaphoreSlim(1, 1))
                let! token = Async.CancellationToken
                do! gate.WaitAsync token |> Async.AwaitTask

                try
                    let! delay = delayFor request

                    match last.TryGetValue host with
                    | true, t ->
                        let wait = delay - (DateTime.UtcNow - t)
                        if wait > TimeSpan.Zero then do! Async.Sleep wait
                    | _ -> ()

                    let! response = fetch request
                    last.[host] <- DateTime.UtcNow
                    return response
                finally
                    gate.Release() |> ignore
            }

    /// A fixed minimum gap between same-host requests — the simple form of
    /// <see cref="M:CrawlSage.Robots.pacePerHost"/>.
    let perHostDelay (delay: TimeSpan) : Renderer -> Renderer =
        pacePerHost (fun _ -> async { return delay })
