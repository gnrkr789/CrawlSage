namespace CrawlSage

/// The state a `spider { … }` block accumulates before it is turned into a
/// <see cref="T:CrawlSage.Spider`1"/> by the builder's <c>Run</c>.
[<NoComparison; NoEquality>]
type SpiderSpec<'Item> =
    { Seeds: Request list
      Parse: (Response -> ParseResult<'Item> list) option
      Pipeline: Sink<'Item>
      Options: SpiderOptions }

/// A `spider { … }` computation expression — a declarative, IntelliSense-friendly way to
/// describe a crawl instead of assembling the <see cref="T:CrawlSage.Spider`1"/> record by
/// hand (and its nested <c>Options</c>). Omitted settings fall back to
/// <c>SpiderOptions.Default</c>; a <c>parse</c> step is required.
///
/// <code>
/// let crawler =
///     spider {
///         seed "https://quotes.toscrape.com/"
///         parse parseQuotes
///         pipeline (Export.appendJsonLine "data/quotes.jsonl")
///         maxDepth 3
///         onEvent Stats.console
///     }
///
/// Spider.crawl crawler |> Async.RunSynchronously
/// </code>
type SpiderBuilder() =

    member _.Yield(_) : SpiderSpec<'Item> =
        { Seeds = []
          Parse = None
          Pipeline = ignore
          Options = SpiderOptions.Default }

    /// Add one seed URL (repeatable).
    [<CustomOperation "seed">]
    member _.Seed(spec: SpiderSpec<'Item>, url: string) =
        { spec with Seeds = spec.Seeds @ [ Request.create url ] }

    /// Add one seed <see cref="T:CrawlSage.Request"/> — when you need custom headers or a POST.
    [<CustomOperation "seedRequest">]
    member _.SeedRequest(spec: SpiderSpec<'Item>, request: Request) =
        { spec with Seeds = spec.Seeds @ [ request ] }

    /// Add several seed URLs at once.
    [<CustomOperation "seeds">]
    member _.SeedMany(spec: SpiderSpec<'Item>, urls: string seq) =
        { spec with Seeds = spec.Seeds @ (urls |> Seq.map Request.create |> List.ofSeq) }

    /// The parser: a fetched page → items and follow-up requests. Required.
    [<CustomOperation "parse">]
    member _.Parse(spec: SpiderSpec<'Item>, parser: Response -> ParseResult<'Item> list) =
        { spec with Parse = Some parser }

    /// Where scraped items go (an <c>Export</c> sink). Defaults to discarding them.
    [<CustomOperation "pipeline">]
    member _.Pipeline(spec: SpiderSpec<'Item>, sink: Sink<'Item>) =
        { spec with Pipeline = sink }

    /// Maximum link depth to follow from the seeds (0 = seeds only).
    [<CustomOperation "maxDepth">]
    member _.MaxDepth(spec: SpiderSpec<'Item>, depth: int) =
        { spec with Options = { spec.Options with MaxDepth = depth } }

    /// Maximum concurrent fetches in flight.
    [<CustomOperation "maxConcurrency">]
    member _.MaxConcurrency(spec: SpiderSpec<'Item>, n: int) =
        { spec with Options = { spec.Options with MaxConcurrency = n } }

    /// Observe crawl progress — logging / metrics (e.g. <c>Stats.console</c>).
    [<CustomOperation "onEvent">]
    member _.OnEvent(spec: SpiderSpec<'Item>, handler: CrawlEvent -> unit) =
        { spec with Options = { spec.Options with OnEvent = handler } }

    member _.Run(spec: SpiderSpec<'Item>) : Spider<'Item> =
        match spec.Parse with
        | Some parser ->
            { Seeds = spec.Seeds
              Parse = parser
              Pipeline = spec.Pipeline
              Options = spec.Options }
        | None -> invalidOp "spider { … } requires a `parse` step."

/// Brings the `spider { … }` builder into scope with `open CrawlSage`.
[<AutoOpen>]
module SpiderBuilderInstance =

    /// The `spider { … }` computation-expression builder.
    let spider = SpiderBuilder()
