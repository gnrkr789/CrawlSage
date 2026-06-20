namespace CrawlSage

open System.IO
open System.Net
open System.Net.Http

/// Minimal, F#-idiomatic HTTP fetching built on <see cref="T:System.Net.Http.HttpClient"/>.
///
/// This is the seed of CrawlSage's downloader layer; retry/back-off, throttling and
/// rotation are composed around it (see <c>Resilience</c> and <c>Rotation</c>).
module Http =

    /// A single shared client. <c>HttpClient</c> is thread-safe and meant to be reused;
    /// creating one per request exhausts sockets under load.
    let private client =
        // Negotiate gzip/deflate/brotli and decompress transparently — less bandwidth, and
        // correct bodies from hosts that compress by default.
        let handler = new HttpClientHandler(AutomaticDecompression = DecompressionMethods.All)
        let c = new HttpClient(handler)
        c.DefaultRequestHeaders.Add("User-Agent", "CrawlSage/0.1 (+https://github.com/gnrkr789/CrawlSage)")
        c

    let private toHttpMethod verb =
        match verb with
        | Get -> HttpMethod.Get
        | Post -> HttpMethod.Post

    /// Build the outgoing message (method, headers, optional body) shared by every fetch.
    let private buildMessage (request: Request) : HttpRequestMessage =
        let message = new HttpRequestMessage(toHttpMethod request.Method, request.Url)

        for KeyValue(name, value) in request.Headers do
            message.Headers.TryAddWithoutValidation(name, value) |> ignore

        match request.Body with
        | Some body -> message.Content <- new StringContent(body)
        | None -> ()

        message

    /// Fetches <paramref name="request"/> over a specific <see cref="T:System.Net.Http.HttpClient"/>
    /// and decodes it into a <see cref="T:CrawlSage.Response"/>. Use this to fetch over a
    /// chosen egress — e.g. a proxied client from <c>Rotation</c> — without duplicating the
    /// decode logic; <c>fetch</c> is just this over the shared client.
    let fetchWith (client: HttpClient) (request: Request) : Async<Response> =
        async {
            use message = buildMessage request
            let! token = Async.CancellationToken
            use! response = client.SendAsync(message, token) |> Async.AwaitTask
            let! body = response.Content.ReadAsStringAsync(token) |> Async.AwaitTask

            let headers =
                response.Headers
                |> Seq.map (fun h -> h.Key.ToLowerInvariant(), List.ofSeq h.Value)
                |> Map.ofSeq

            return
                { Request = request
                  StatusCode = int response.StatusCode
                  Body = body
                  Headers = headers }
        }

    /// Fetches <paramref name="request"/> over the shared client (see <c>fetchWith</c>).
    let fetch (request: Request) : Async<Response> = fetchWith client request

    /// Fetch the raw bytes of a response — for images, PDFs and other binary assets that the
    /// text-decoding <c>fetch</c> would mangle.
    let fetchBytes (request: Request) : Async<byte[]> =
        async {
            use message = buildMessage request
            let! token = Async.CancellationToken
            use! response = client.SendAsync(message, token) |> Async.AwaitTask
            return! response.Content.ReadAsByteArrayAsync(token) |> Async.AwaitTask
        }

    /// Stream a response straight to <paramref name="path"/> without buffering the whole body
    /// in memory — the way to download large files. Throws on a non-success status.
    let download (path: string) (request: Request) : Async<unit> =
        async {
            use message = buildMessage request
            let! token = Async.CancellationToken

            use! response =
                client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token)
                |> Async.AwaitTask

            response.EnsureSuccessStatusCode() |> ignore
            use! source = response.Content.ReadAsStreamAsync(token) |> Async.AwaitTask

            use destination =
                new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync = true)

            do! source.CopyToAsync(destination, token) |> Async.AwaitTask
        }

    /// Convenience: fetch a URL with a plain GET and return the body text.
    let getString (url: string) : Async<string> =
        async {
            let! response = fetch (Request.create url)
            return response.Body
        }
