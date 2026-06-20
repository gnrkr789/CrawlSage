namespace CrawlSage

open System

/// What a parser yields for each page: either a scraped item (→ the pipeline) or a
/// follow-up request (→ back to the scheduler). The crawler's callback model, as a DU.
type ParseResult<'Item> =
    | Item of 'Item
    | Follow of Request

/// Tunable engine limits.
type SpiderOptions =
    { /// Maximum concurrent fetches within one depth level.
      MaxConcurrency: int
      /// How many links deep to follow from the seeds (0 = seeds only).
      MaxDepth: int }

    /// 8-wide, 16 levels deep.
    static member Default = { MaxConcurrency = 8; MaxDepth = 16 }

/// How a crawl paces itself and respects robots.txt — politeness, not evasion. Consulted
/// by <c>Spider.crawlPolitely</c>, and so by the production <c>Spider.crawl</c>.
type Politeness =
    { /// Consult each host's robots.txt and skip disallowed URLs.
      RespectRobots: bool
      /// The crawler identity robots rules are matched against.
      UserAgent: string
      /// Minimum gap between requests to one host. A host's robots <c>Crawl-delay</c>
      /// overrides this upward when the host asks for more.
      PerHostDelay: TimeSpan }

    /// Robots-respecting, at most one request per host per second.
    static member Default =
        { RespectRobots = true
          UserAgent = "CrawlSage"
          PerHostDelay = TimeSpan.FromSeconds 1.0 }

/// A complete crawl: where to start, how to parse, where items go, and the limits.
/// (No structural equality/comparison — it holds function-typed fields.)
[<NoComparison; NoEquality>]
type Spider<'Item> =
    { /// URLs to start from.
      Seeds: Request list
      /// Turns a fetched page into items and follow-up requests.
      Parse: Response -> ParseResult<'Item> list
      /// Sink for scraped items — wire an `Export` sink (CSV / JSON / JSONL / DB) here.
      Pipeline: Sink<'Item>
      /// Engine limits.
      Options: SpiderOptions }

/// The crawl engine — a frontier-driven scheduler with dedup and depth bounding. Work is
/// pulled from a <see cref="T:CrawlSage.Frontier"/> in <c>MaxConcurrency</c>-sized batches,
/// fetched concurrently, then parsed sequentially (so the pipeline and frontier are never
/// touched concurrently). Items go to the pipeline, follow-ups back to the frontier. The
/// crawl ends when the frontier drains.
module Spider =

    /// Build a spider from seeds, a parser and a pipeline, with default options.
    let create
        (seeds: Request list)
        (parse: Response -> ParseResult<'Item> list)
        (pipeline: 'Item -> unit)
        : Spider<'Item> =
        { Seeds = seeds
          Parse = parse
          Pipeline = pipeline
          Options = SpiderOptions.Default }

    /// The core loop shared by every entry point. Pulls batches from <paramref name="frontier"/>,
    /// applies the <paramref name="allow"/> gate (robots; disallowed URLs are dropped before
    /// fetch *and* parse), fetches concurrently, routes items to the pipeline and follow-ups
    /// back to the frontier (depth-bounded), until the frontier drains.
    let private runWith
        (frontier: Frontier)
        (allow: Request -> Async<bool>)
        (fetch: Renderer)
        (spider: Spider<'Item>)
        : Async<unit> =
        async {
            let maxDepth = spider.Options.MaxDepth
            let maxConcurrency = max 1 spider.Options.MaxConcurrency

            for seed in spider.Seeds do
                frontier.Add seed 0 |> ignore

            let rec loop () : Async<unit> =
                async {
                    match frontier.Take maxConcurrency with
                    | [] -> return ()
                    | batch ->
                        // Robots gate: drop disallowed URLs before fetching (checked in parallel).
                        let! flags = batch |> List.map (fun (request, _) -> allow request) |> Async.Parallel

                        let toFetch =
                            List.zip batch (List.ofArray flags)
                            |> List.choose (fun (item, ok) -> if ok then Some item else None)

                        let! responses =
                            Async.Parallel(toFetch |> List.map (fun (request, _) -> fetch request), maxConcurrency)

                        for (_, depth), response in List.zip toFetch (List.ofArray responses) do
                            for result in spider.Parse response do
                                match result with
                                | Item item -> spider.Pipeline item
                                | Follow next ->
                                    if depth < maxDepth then
                                        frontier.Add next (depth + 1) |> ignore

                        return! loop ()
                }

            do! loop ()
        }

    /// Always-allow gate — no robots check.
    let private allowAll: Request -> Async<bool> = fun _ -> async { return true }

    /// Shared politeness wiring: builds the robots cache, the per-host pacer and the robots
    /// gate over <paramref name="fetch"/>, then runs the crawl on <paramref name="frontier"/>.
    let private runPolitely
        (frontier: Frontier)
        (politeness: Politeness)
        (fetch: Renderer)
        (spider: Spider<'Item>)
        : Async<unit> =
        let cache = Robots.Cache(fetch, politeness.UserAgent)

        let delayFor (request: Request) =
            if politeness.RespectRobots then
                async {
                    let! crawlDelay = cache.CrawlDelay request.Url

                    return
                        match crawlDelay with
                        | Some delay when delay > politeness.PerHostDelay -> delay
                        | _ -> politeness.PerHostDelay
                }
            else
                async { return politeness.PerHostDelay }

        let paced = fetch |> Robots.pacePerHost delayFor

        let allow (request: Request) =
            if politeness.RespectRobots then
                cache.IsAllowed request.Url
            else
                async { return true }

        runWith frontier allow paced spider

    /// Run <paramref name="spider"/> with an explicit fetch function and no politeness gate
    /// — inject a stub in tests, or compose your own middleware. Uses an in-memory frontier.
    let crawlWith (fetch: Renderer) (spider: Spider<'Item>) : Async<unit> =
        runWith (Frontier.inMemory ()) allowAll fetch spider

    /// Run <paramref name="spider"/> politely over <paramref name="fetch"/> on an explicit
    /// <paramref name="frontier"/> — pass <c>Frontier.persistent</c> for a resumable crawl.
    /// Consults robots.txt (skipping disallowed URLs) and spaces out per-host requests.
    let crawlOn
        (frontier: Frontier)
        (politeness: Politeness)
        (fetch: Renderer)
        (spider: Spider<'Item>)
        : Async<unit> =
        runPolitely frontier politeness fetch spider

    /// Run <paramref name="spider"/> politely over <paramref name="fetch"/> on an in-memory
    /// frontier (<see cref="M:CrawlSage.Spider.crawlOn"/> with the default frontier).
    let crawlPolitely (politeness: Politeness) (fetch: Renderer) (spider: Spider<'Item>) : Async<unit> =
        runPolitely (Frontier.inMemory ()) politeness fetch spider

    /// Run <paramref name="spider"/> with the production downloader
    /// (<c>Resilience.politeFetch</c>: throttled, retried, timed-out), politely:
    /// robots.txt-respecting with a per-host delay (<c>Politeness.Default</c>).
    let crawl (spider: Spider<'Item>) : Async<unit> =
        crawlPolitely Politeness.Default Resilience.politeFetch spider
