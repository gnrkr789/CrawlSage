---
name: session-auth
description: Handle login, cookies and sessions in CrawlSage. Use when a crawl needs authentication — form login, keeping a session across requests, persisting a cookie jar, CSRF tokens, or bearer/API tokens. Covers the CookieContainer-backed client and form-POST login flow.
---

# session-auth

Crawling behind a login means two things: **send credentials once**, then **carry the
session cookie** on every following request. .NET's `HttpClientHandler` +
`CookieContainer` does the cookie-carrying automatically.

> Only automate logins for accounts and sites you are authorised to access. Keep
> credentials out of the repo (use `.env` / `*.local.json`, both git-ignored).

## A session-aware client

A `CookieContainer` shared by the handler persists cookies across requests, just like a
browser session.

```fsharp
namespace CrawlSage

open System
open System.Net
open System.Net.Http

/// An HTTP session that remembers cookies across requests.
module Session =

    type T = { Client: HttpClient; Cookies: CookieContainer }

    /// Create a fresh session with its own cookie jar.
    let create () : T =
        let cookies = CookieContainer()
        let handler = new HttpClientHandler(CookieContainer = cookies, UseCookies = true)
        { Client = new HttpClient(handler); Cookies = cookies }

    /// POST a form (login) and keep whatever cookies come back.
    let postForm (url: string) (fields: (string * string) list) (session: T) =
        async {
            use content = new FormUrlEncodedContent(dict fields)
            let! token = Async.CancellationToken
            let! resp = session.Client.PostAsync(url, content, token) |> Async.AwaitTask
            return resp
        }

    /// GET an authenticated page using the session's cookies.
    let get (url: string) (session: T) =
        async {
            let! token = Async.CancellationToken
            let! resp = session.Client.GetAsync(url, token) |> Async.AwaitTask
            return! resp.Content.ReadAsStringAsync(token) |> Async.AwaitTask
        }
```

## Login flow

```fsharp
open CrawlSage

let session = Session.create ()

// 1. Log in (cookies are captured automatically).
let _ =
    session
    |> Session.postForm "https://example.com/login"
        [ "username", Environment.GetEnvironmentVariable "CS_USER"
          "password", Environment.GetEnvironmentVariable "CS_PASS" ]
    |> Async.RunSynchronously

// 2. Now fetch pages that require the session.
let dashboard = session |> Session.get "https://example.com/dashboard" |> Async.RunSynchronously
```

## CSRF tokens

Many login forms embed a hidden token. Fetch the login page first, extract it with the
**`parse-html`** skill, then include it in `postForm`:

```fsharp
let token =
    session |> Session.get loginUrl |> Async.RunSynchronously
    |> Html.parse |> Html.select "input[name=_csrf]"
    |> Option.bind (Html.attr "value") |> Option.defaultValue ""
```

## Bearer / API tokens

For token APIs, skip cookies and set the header per request:

```fsharp
Request.create "https://api.example.com/me"
|> Request.withHeader "Authorization" $"Bearer {token}"
|> Http.fetch
```

## Dynamic logins

If the login page is a JavaScript SPA, drive it with Playwright (`FillAsync` /
`ClickAsync`) from the **`dynamic-page`** skill, then export `context.cookies()` into a
`CookieContainer` for fast `HttpClient` fetches afterward.

## Gotchas

- Reuse **one** `Session.T` for the whole crawl — a new client means a new (empty) jar.
- Some sites set the session cookie only after a redirect; leave `AllowAutoRedirect`
  on (the default) so it's captured.
- Persist cookies between runs by serialising the `CookieContainer` if you don't want to
  log in every time.
