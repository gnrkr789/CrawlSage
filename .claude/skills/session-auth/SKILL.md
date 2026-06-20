---
name: session-auth
description: Handle login, cookies and sessions in CrawlSage. Use when a crawl needs authentication — form login, keeping a session across requests, persisting a cookie jar, CSRF tokens, or bearer/API tokens. Covers the Session module (CookieContainer-backed client + form-POST login).
---

# session-auth

Crawling behind a login means two things: **send credentials once**, then **carry the
session cookie** on every following request. CrawlSage's `Session` does both — one
`HttpClient` with its own `CookieContainer` that persists cookies like a browser.

> Only automate logins for accounts and sites you are authorised to access. Keep
> credentials out of the repo (use `.env` / `*.local.json`, both git-ignored).

## The Session module (shipped — `Session.fs`)

| Function | Does |
| --- | --- |
| `Session.create ()` | a fresh session with an empty cookie jar |
| `Session.login session url fields` | POST a login form; captures the `Set-Cookie`s |
| `Session.fetch session` | a `Renderer` that fetches with the session's cookies |
| `Session.cookies session url` | the cookies it would send to a URL (name → value) |
| `Session.addCookie session url name value` | seed a cookie (e.g. a token you already hold) |
| `Session.save` / `Session.load` | persist / restore the jar across runs |

## Login flow

```fsharp
open CrawlSage

let session = Session.create ()

// 1. Log in — cookies are captured automatically.
Session.login session "https://example.com/login"
    [ "username", Environment.GetEnvironmentVariable "CS_USER"
      "password", Environment.GetEnvironmentVariable "CS_PASS" ]
|> Async.RunSynchronously
|> ignore

// 2. Crawl authenticated pages — Session.fetch is the Renderer.
Spider.crawlWith (Session.fetch session) spider |> Async.RunSynchronously
```

`Session.fetch session` is a `Renderer`, so it drops straight into `Spider.crawlWith` (or
compose resilience around it). For a one-off page: `Session.fetch session request`.

## CSRF tokens

Many login forms embed a hidden token. Fetch the login page with the session first,
extract it (the `parse-html` skill), then include it in `login`:

```fsharp
let token =
    Session.fetch session (Request.create loginUrl)
    |> Async.RunSynchronously
    |> fun response -> Html.parse response.Body
    |> Html.select "input[name=_csrf]"
    |> Option.bind (Html.attr "value")
    |> Option.defaultValue ""

Session.login session loginUrl [ "_csrf", token; "username", user; "password", pass ]
|> Async.RunSynchronously
|> ignore
```

## Persisting the session between runs

```fsharp
Session.save session "session.local.json"        // git-ignored
// next run — already authenticated, no re-login:
let session = Session.load "session.local.json"
```

## Bearer / API tokens

For token APIs, skip cookies and set the header per request:

```fsharp
Request.create "https://api.example.com/me"
|> Request.withHeader "Authorization" $"Bearer {token}"
|> Http.fetch
```

## Dynamic logins

If the login page is a JavaScript SPA, render it first (the `dynamic-page` skill / JS
renderer), copy the cookies it sets into the session with `Session.addCookie`, then
`Session.fetch` from there.

## Gotchas

- Reuse **one** `Session` for the whole crawl — a new session means a new (empty) jar.
- Some sites set the session cookie only after a redirect; the client follows redirects by
  default, so it is captured.
- `Session.save` / `load` persist the jar between runs — no need to log in every time.
