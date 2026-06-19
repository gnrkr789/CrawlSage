namespace CrawlSage

/// HTTP method used by a crawl <see cref="T:CrawlSage.Request"/>.
type HttpVerb =
    | Get
    | Post

/// A unit of work for the crawler: a URL plus everything needed to fetch it,
/// and arbitrary metadata carried through to the parser.
type Request =
    { /// Absolute URL to fetch.
      Url: string
      /// HTTP verb. Defaults to <c>Get</c>.
      Method: HttpVerb
      /// Extra request headers (e.g. User-Agent, Cookie).
      Headers: Map<string, string>
      /// Optional request body, used for <c>Post</c>.
      Body: string option
      /// User metadata propagated to the parser (category, depth, source page, ...).
      Meta: Map<string, string> }

/// Smart constructors and combinators for <see cref="T:CrawlSage.Request"/>.
module Request =

    /// A plain GET request for <paramref name="url"/> with no extra headers.
    let create (url: string) : Request =
        { Url = url
          Method = Get
          Headers = Map.empty
          Body = None
          Meta = Map.empty }

    /// Returns a copy of <paramref name="request"/> with header <paramref name="name"/> set.
    let withHeader (name: string) (value: string) (request: Request) : Request =
        { request with Headers = request.Headers |> Map.add name value }

    /// Returns a POST copy of <paramref name="request"/> carrying <paramref name="body"/>.
    let withBody (body: string) (request: Request) : Request =
        { request with
            Method = Post
            Body = Some body }

    /// Attaches a metadata key/value carried through to the parser.
    let withMeta (key: string) (value: string) (request: Request) : Request =
        { request with Meta = request.Meta |> Map.add key value }

/// The result of fetching a <see cref="T:CrawlSage.Request"/>, handed to parsers.
type Response =
    { /// The request that produced this response.
      Request: Request
      /// HTTP status code (e.g. 200, 404).
      StatusCode: int
      /// Decoded response body.
      Body: string
      /// Response headers, lower-cased keys mapping to their values.
      Headers: Map<string, string list> }

    /// <c>true</c> when the status code is in the 2xx range.
    member this.IsSuccess =
        this.StatusCode >= 200 && this.StatusCode < 300
