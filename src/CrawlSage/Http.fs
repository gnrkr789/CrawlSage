namespace CrawlSage

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.RegularExpressions

/// Minimal, F#-idiomatic HTTP fetching built on <see cref="T:System.Net.Http.HttpClient"/>.
///
/// This is the seed of CrawlSage's downloader layer; retry/back-off, throttling and
/// rotation are composed around it (see <c>Resilience</c> and <c>Rotation</c>).
module Http =

    // Make legacy code pages (EUC-KR, Shift_JIS, GBK, …) available to Encoding.GetEncoding,
    // which on .NET otherwise only knows UTF-8/UTF-16/ASCII.
    do Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)

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

    /// Look up an encoding by charset label, returning None for a blank or unknown one.
    let private encodingOrNone (name: string) : Encoding option =
        if String.IsNullOrWhiteSpace name then
            None
        else
            try
                Some(Encoding.GetEncoding(name.Trim().Trim('"', '\'')))
            with _ ->
                None

    /// Matches <c>&lt;meta charset="x"&gt;</c> and <c>&lt;meta http-equiv content="…; charset=x"&gt;</c>.
    let private metaCharsetRegex =
        Regex(
            "<meta[^>]+?charset\\s*=\\s*[\"']?\\s*([a-zA-Z0-9_\\-:.]+)",
            RegexOptions.IgnoreCase ||| RegexOptions.Compiled
        )

    /// Sniff a charset declared in an HTML <c>&lt;meta&gt;</c> within the first 2 KB of bytes,
    /// read as Latin-1 so every byte maps to a char without throwing.
    let private metaCharset (bytes: byte[]) : string option =
        let head = Encoding.Latin1.GetString(bytes, 0, min bytes.Length 2048)
        let m = metaCharsetRegex.Match head
        if m.Success then Some m.Groups.[1].Value else None

    /// Decode response bytes to text the way a browser does: a byte-order mark wins; else the
    /// HTTP <c>Content-Type; charset</c> (<paramref name="httpCharset"/>); else a charset declared
    /// in an HTML <c>&lt;meta&gt;</c>; else UTF-8. This keeps non-UTF-8 pages (EUC-KR, Shift_JIS,
    /// GBK, …) from turning into mojibake.
    let decode (httpCharset: string option) (bytes: byte[]) : string =
        let fallback =
            httpCharset
            |> Option.bind encodingOrNone
            |> Option.orElseWith (fun () -> metaCharset bytes |> Option.bind encodingOrNone)
            |> Option.defaultValue Encoding.UTF8

        use stream = new MemoryStream(bytes)
        use reader = new StreamReader(stream, fallback, detectEncodingFromByteOrderMarks = true)
        reader.ReadToEnd()

    /// Fetches <paramref name="request"/> over a specific <see cref="T:System.Net.Http.HttpClient"/>
    /// and decodes it into a <see cref="T:CrawlSage.Response"/>. Use this to fetch over a
    /// chosen egress — e.g. a proxied client from <c>Rotation</c> — without duplicating the
    /// decode logic; <c>fetch</c> is just this over the shared client.
    let fetchWith (client: HttpClient) (request: Request) : Async<Response> =
        async {
            use message = buildMessage request
            let! token = Async.CancellationToken
            use! response = client.SendAsync(message, token) |> Async.AwaitTask
            let! bytes = response.Content.ReadAsByteArrayAsync(token) |> Async.AwaitTask

            // The page's declared charset (HTTP header) — passed to decode as the primary hint.
            let httpCharset =
                response.Content.Headers.ContentType
                |> Option.ofObj
                |> Option.bind (fun ct -> Option.ofObj ct.CharSet)

            let headers =
                response.Headers
                |> Seq.map (fun h -> h.Key.ToLowerInvariant(), List.ofSeq h.Value)
                |> Map.ofSeq

            return
                { Request = request
                  StatusCode = int response.StatusCode
                  Body = decode httpCharset bytes
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
