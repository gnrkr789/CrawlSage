namespace CrawlSage

open System

/// What a parser yields for each page: either a scraped item (→ the pipeline) or a
/// follow-up request (→ back to the scheduler). The crawler's callback model, as a DU.
type ParseResult<'Item> =
    | Item of 'Item
    | Follow of Request

/// Something the engine did while crawling — wire <c>SpiderOptions.OnEvent</c> to observe it.
[<NoComparison>]
type CrawlEvent =
    /// A page was fetched, with its HTTP status code.
    | Fetched of Request * status: int
    /// A URL was skipped because robots.txt disallowed it.
    | Skipped of Request
    /// A fetch failed (after retries); the crawl records it and moves on.
    | Failed of Request * exn

/// A mutable tally of crawl progress, filled by the handler from <c>Stats.collector</c>.
[<NoComparison; NoEquality>]
type Stats =
    { /// Pages successfully fetched.
      mutable Fetched: int
      /// URLs skipped by the robots gate.
      mutable Skipped: int
      /// Fetches that failed after retries.
      mutable Failed: int
      /// Count of fetched pages by HTTP status code.
      Status: System.Collections.Generic.Dictionary<int, int> }

/// Observability — turn the engine's <c>CrawlEvent</c>s into a tally or a log line.
module Stats =

    /// A fresh tally plus the event handler that fills it: wire the handler to
    /// <c>SpiderOptions.OnEvent</c> and read the tally once the crawl finishes.
    let collector () : Stats * (CrawlEvent -> unit) =
        let stats =
            { Fetched = 0
              Skipped = 0
              Failed = 0
              Status = System.Collections.Generic.Dictionary<int, int>() }

        let handle event =
            match event with
            | Fetched(_, status) ->
                stats.Fetched <- stats.Fetched + 1

                let previous =
                    match stats.Status.TryGetValue status with
                    | true, count -> count
                    | _ -> 0

                stats.Status.[status] <- previous + 1
            | Skipped _ -> stats.Skipped <- stats.Skipped + 1
            | Failed _ -> stats.Failed <- stats.Failed + 1

        stats, handle

    /// A ready-made console logger for <c>SpiderOptions.OnEvent</c>.
    let console (event: CrawlEvent) : unit =
        match event with
        | Fetched(request, status) -> printfn "%3d  %s" status request.Url
        | Skipped request -> printfn "skip %s (robots)" request.Url
        | Failed(request, error) -> printfn "FAIL %s — %s" request.Url error.Message

/// Tunable engine limits and the observability hook.
[<NoComparison; NoEquality>]
type SpiderOptions =
    { /// Maximum concurrent fetches in flight.
      MaxConcurrency: int
      /// How many links deep to follow from the seeds (0 = seeds only).
      MaxDepth: int
      /// Observe crawl progress (logging / metrics); defaults to a no-op.
      OnEvent: CrawlEvent -> unit }

    /// 8-wide, 16 levels deep, no event handler.
    static member Default =
        { MaxConcurrency = 8
          MaxDepth = 16
          OnEvent = ignore }

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
                        let onEvent = spider.Options.OnEvent

                        // Robots gate (checked in parallel); disallowed URLs are skipped.
                        let! flags = batch |> List.map (fun (request, _) -> allow request) |> Async.Parallel

                        let toFetch =
                            List.zip batch (List.ofArray flags)
                            |> List.choose (fun ((request, depth), ok) ->
                                if ok then
                                    Some(request, depth)
                                else
                                    onEvent (Skipped request)
                                    None)

                        // Fetch concurrently; a per-page failure is recorded, not fatal — so one
                        // bad page never aborts the crawl. Cancellation still propagates.
                        let! results =
                            Async.Parallel(
                                toFetch
                                |> List.map (fun (request, depth) ->
                                    async {
                                        try
                                            let! response = fetch request
                                            return Some(request, depth, response)
                                        with ex when not (ex :? OperationCanceledException) ->
                                            onEvent (Failed(request, ex))
                                            return None
                                    }),
                                maxConcurrency
                            )

                        for request, depth, response in results |> Array.choose id do
                            onEvent (Fetched(request, response.StatusCode))

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
