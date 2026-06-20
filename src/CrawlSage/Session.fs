namespace CrawlSage

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http

/// A browser-like session: one <see cref="T:System.Net.Http.HttpClient"/> with its own
/// cookie jar, so cookies a login sets persist across later requests. Use
/// <c>Session.fetch</c> as the <see cref="T:CrawlSage.Renderer"/> for an authenticated crawl.
[<NoComparison; NoEquality>]
type Session =
    { /// The session's client (carries the cookie jar and gzip/deflate/brotli decompression).
      Client: HttpClient
      /// The cookie jar — inspect, seed, save or load it.
      Cookies: CookieContainer }

/// Construct and drive an authenticated <see cref="T:CrawlSage.Session"/>.
module Session =

    /// A fresh session with an empty cookie jar.
    let create () : Session =
        let cookies = CookieContainer()

        let handler =
            new HttpClientHandler(
                CookieContainer = cookies,
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.All
            )

        { Client = new HttpClient(handler)
          Cookies = cookies }

    /// Fetch through the session — cookies are sent and any <c>Set-Cookie</c> is captured.
    /// The result is a <see cref="T:CrawlSage.Renderer"/>, so it drops into <c>Spider.crawlWith</c>.
    let fetch (session: Session) : Renderer =
        fun request -> Http.fetchWith session.Client request

    /// Seed a cookie on the jar for <paramref name="url"/>'s domain (e.g. a token you already hold).
    let addCookie (session: Session) (url: string) (name: string) (value: string) : unit =
        session.Cookies.Add(Uri url, Cookie(name, value))

    /// The cookies the session would send to <paramref name="url"/>, as name → value.
    let cookies (session: Session) (url: string) : Map<string, string> =
        session.Cookies.GetCookies(Uri url)
        |> Seq.cast<Cookie>
        |> Seq.map (fun cookie -> cookie.Name, cookie.Value)
        |> Map.ofSeq

    /// Log in by POSTing <paramref name="fields"/> as an HTML form to <paramref name="loginUrl"/>.
    /// The response's <c>Set-Cookie</c>s land in the jar, so later <c>Session.fetch</c> calls are
    /// authenticated. (CSRF flows: GET the form first, read the token, then include it here.)
    let login (session: Session) (loginUrl: string) (fields: (string * string) list) : Async<Response> =
        async {
            use content = new FormUrlEncodedContent(fields |> List.map (fun (k, v) -> KeyValuePair(k, v)))
            use message = new HttpRequestMessage(HttpMethod.Post, loginUrl, Content = content)
            let! token = Async.CancellationToken
            use! response = session.Client.SendAsync(message, token) |> Async.AwaitTask
            let! body = response.Content.ReadAsStringAsync(token) |> Async.AwaitTask

            let headers =
                response.Headers
                |> Seq.map (fun h -> h.Key.ToLowerInvariant(), List.ofSeq h.Value)
                |> Map.ofSeq

            return
                { Request = Request.create loginUrl
                  StatusCode = int response.StatusCode
                  Body = body
                  Headers = headers }
        }

    /// Persist the jar to <paramref name="path"/> (tab-separated, values percent-escaped) so a
    /// logged-in session survives a restart.
    let save (session: Session) (path: string) : unit =
        let lines =
            session.Cookies.GetAllCookies()
            |> Seq.cast<Cookie>
            |> Seq.map (fun c -> $"{c.Domain}\t{c.Path}\t{c.Name}\t{Uri.EscapeDataString c.Value}")

        File.WriteAllLines(path, lines)

    /// Restore a jar saved with <see cref="M:CrawlSage.Session.save"/> into a fresh session.
    let load (path: string) : Session =
        let session = create ()

        if File.Exists path then
            for line in File.ReadAllLines path do
                match line.Split('\t') with
                | [| domain; cookiePath; name; value |] ->
                    session.Cookies.Add(Cookie(name, Uri.UnescapeDataString value, cookiePath, domain))
                | _ -> ()

        session
