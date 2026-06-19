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

/// A complete crawl: where to start, how to parse, where items go, and the limits.
/// (No structural equality/comparison — it holds function-typed fields.)
[<NoComparison; NoEquality>]
type Spider<'Item> =
    { /// URLs to start from.
      Seeds: Request list
      /// Turns a fetched page into items and follow-up requests.
      Parse: Response -> ParseResult<'Item> list
      /// Sink for scraped items (CSV/JSON/DB sinks arrive in Phase 5).
      Pipeline: 'Item -> unit
      /// Engine limits.
      Options: SpiderOptions }

/// The crawl engine — a breadth-first scheduler with dedup and depth bounding.
///
/// Each depth level is fetched concurrently (bounded by <c>MaxConcurrency</c>); the
/// results are parsed sequentially, so the dedup set, the pipeline and the frontier are
/// never touched concurrently. The crawl ends when a level produces no new requests.
module Spider =

    /// Dedup key: method + host-normalised URL (host lower-cased, fragment dropped,
    /// path and query preserved).
    let private fingerprint (request: Request) : string =
        let url =
            match Uri.TryCreate(request.Url, UriKind.Absolute) with
            | true, uri -> uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant() + uri.PathAndQuery
            | _ -> request.Url

        $"{request.Method}|{url}"

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

    /// Run <paramref name="spider"/> with an explicit fetch function — inject a stub in
    /// tests, or use <c>crawl</c> for the production downloader.
    let crawlWith (fetch: Request -> Async<Response>) (spider: Spider<'Item>) : Async<unit> =
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
                        let! responses = Async.Parallel(level |> List.map fetch, maxConcurrency)
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

    /// Run <paramref name="spider"/> with the production downloader
    /// (<c>Resilience.politeFetch</c>: throttled, retried, timed-out).
    let crawl (spider: Spider<'Item>) : Async<unit> =
        crawlWith Resilience.politeFetch spider
