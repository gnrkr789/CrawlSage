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

/// The crawl engine — a breadth-first scheduler with dedup and depth bounding.
///
/// Each depth level is fetched concurrently (bounded by <c>MaxConcurrency</c>); the
/// results are parsed sequentially, so the dedup set, the pipeline and the frontier are
/// never touched concurrently. The crawl ends when a level produces no new requests.
module Spider =

    /// Dedup key: method + canonicalised URL (see <see cref="M:CrawlSage.Url.normalize"/>).
    let private fingerprint (request: Request) : string =
        $"{request.Method}|{Url.normalize request.Url}"

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

    /// The core breadth-first loop shared by every entry point. <paramref name="allow"/>
    /// decides whether a request is fetched at all (the robots gate; disallowed URLs are
    /// dropped before fetch *and* parse), and <paramref name="fetch"/> performs the download.
    let private run (allow: Request -> Async<bool>) (fetch: Renderer) (spider: Spider<'Item>) : Async<unit> =
        async {
            let seen = System.Collections.Generic.HashSet<string>()
            let maxDepth = spider.Options.MaxDepth
            let maxConcurrency = max 1 spider.Options.MaxConcurrency

            // Seeds are depth 0, after dedup.
            let seedLevel = spider.Seeds |> List.filter (fun r -> seen.Add(fingerprint r))

            let rec loop (level: Request list) (depth: int) : Async<unit> =
                async {
                    if List.isEmpty level then
                        return ()
                    else
                        // Robots gate: drop disallowed URLs before fetching (checked in parallel).
                        let! flags = level |> List.map allow |> Async.Parallel

                        let toFetch =
                            List.zip level (List.ofArray flags)
                            |> List.choose (fun (request, ok) -> if ok then Some request else None)

                        let! responses = Async.Parallel(toFetch |> List.map fetch, maxConcurrency)
                        let nextLevel = ResizeArray<Request>()

                        for response in responses do
                            for result in spider.Parse response do
                                match result with
                                | Item item -> spider.Pipeline item
                                | Follow request ->
                                    if depth < maxDepth && seen.Add(fingerprint request) then
                                        nextLevel.Add request

                        return! loop (List.ofSeq nextLevel) (depth + 1)
                }

            do! loop seedLevel 0
        }

    /// Always-allow gate — no robots check.
    let private allowAll: Request -> Async<bool> = fun _ -> async { return true }

    /// Run <paramref name="spider"/> with an explicit fetch function and no politeness gate
    /// — inject a stub in tests, or compose your own middleware. Use <c>crawl</c> for the
    /// polite production downloader.
    let crawlWith (fetch: Renderer) (spider: Spider<'Item>) : Async<unit> =
        run allowAll fetch spider

    /// Run <paramref name="spider"/> politely over <paramref name="fetch"/>: consult each
    /// host's robots.txt (skipping disallowed URLs) and space out per-host requests,
    /// honouring a host's robots <c>Crawl-delay</c> when it exceeds <c>PerHostDelay</c>.
    /// robots.txt itself is fetched over <paramref name="fetch"/> but never paced or gated.
    let crawlPolitely (politeness: Politeness) (fetch: Renderer) (spider: Spider<'Item>) : Async<unit> =
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

        run allow paced spider

    /// Run <paramref name="spider"/> with the production downloader
    /// (<c>Resilience.politeFetch</c>: throttled, retried, timed-out), politely:
    /// robots.txt-respecting with a per-host delay (<c>Politeness.Default</c>).
    let crawl (spider: Spider<'Item>) : Async<unit> =
        crawlPolitely Politeness.Default Resilience.politeFetch spider
