namespace CrawlSage

open System
open System.Threading
open System.Threading.Tasks
open Polly

/// Composable resilience wrappers around a fetch function. Each has type
/// <c>(Request -&gt; Async&lt;Response&gt;) -&gt; (Request -&gt; Async&lt;Response&gt;)</c>,
/// so they stack with <c>|&gt;</c> over <c>Http.fetch</c> — policy is composition,
/// never a flag baked into the downloader.
module Resilience =

    /// Tunable retry schedule.
    type RetryOptions =
        { /// Maximum number of retries after the initial attempt.
          MaxRetries: int
          /// Base back-off delay; doubled each attempt, plus jitter. <c>Zero</c> = no wait.
          BaseDelay: TimeSpan }

        /// 4 retries with 200 ms base back-off.
        static member Default = { MaxRetries = 4; BaseDelay = TimeSpan.FromMilliseconds 200.0 }

    /// HTTP status codes worth retrying.
    let private isTransientStatus code =
        code = 408 || code = 429 || code = 500 || code = 502 || code = 503 || code = 504

    /// Exceptions worth retrying — transient network faults, not bugs or cancellation.
    let private isTransientException (ex: exn) =
        match ex with
        | :? System.Net.Http.HttpRequestException -> true
        | :? TimeoutException -> true
        | _ -> false

    /// The server's Retry-After header (delta-seconds form), capped to 60 s.
    let private retryAfter (response: Response) : TimeSpan option =
        response.Headers
        |> Map.tryFind "retry-after"
        |> Option.bind List.tryHead
        |> Option.bind (fun value ->
            match Int32.TryParse value with
            | true, seconds when seconds >= 0 -> Some(TimeSpan.FromSeconds(float (min seconds 60)))
            | _ -> None)

    /// Retry transient failures (network faults and 408/429/5xx) with exponential
    /// back-off + jitter, honouring Retry-After when the server sends it.
    let withRetryOptions (options: RetryOptions) (fetch: Request -> Async<Response>) : Request -> Async<Response> =
        let sleepProvider =
            Func<int, DelegateResult<Response>, Context, TimeSpan>(fun attempt outcome _ ->
                let fromHeader =
                    if isNull outcome.Exception then retryAfter outcome.Result else None

                match fromHeader with
                | Some delay -> delay
                | None ->
                    let baseMs = options.BaseDelay.TotalMilliseconds
                    let backoff = baseMs * (2.0 ** float (attempt - 1))
                    let jitter = Random.Shared.NextDouble() * baseMs
                    TimeSpan.FromMilliseconds(backoff + jitter))

        let onRetry =
            Func<DelegateResult<Response>, TimeSpan, int, Context, Task>(fun _ _ _ _ -> Task.CompletedTask)

        let policy =
            Policy
                .Handle<exn>(fun ex -> isTransientException ex)
                .OrResult<Response>(fun r -> isTransientStatus r.StatusCode)
                .WaitAndRetryAsync(options.MaxRetries, sleepProvider, onRetry)

        fun request ->
            async {
                let! token = Async.CancellationToken

                return!
                    policy.ExecuteAsync(
                        Func<CancellationToken, Task<Response>>(fun ct ->
                            Async.StartAsTask(fetch request, cancellationToken = ct)),
                        token)
                    |> Async.AwaitTask
            }

    /// Retry transient failures with the default schedule.
    let withRetry (fetch: Request -> Async<Response>) : Request -> Async<Response> =
        withRetryOptions RetryOptions.Default fetch

    /// Fail a request that exceeds <paramref name="timeout"/> with a
    /// <see cref="T:System.TimeoutException"/>, cancelling the in-flight fetch via a
    /// token linked to the ambient one.
    let withTimeout (timeout: TimeSpan) (fetch: Request -> Async<Response>) : Request -> Async<Response> =
        fun request ->
            async {
                let! parent = Async.CancellationToken
                use cts = CancellationTokenSource.CreateLinkedTokenSource(parent)
                cts.CancelAfter(timeout)

                try
                    return! Async.StartAsTask(fetch request, cancellationToken = cts.Token) |> Async.AwaitTask
                with :? OperationCanceledException when not parent.IsCancellationRequested ->
                    return raise (TimeoutException($"Request to {request.Url} timed out after {timeout.TotalSeconds}s"))
            }

    /// Allow at most <paramref name="maxConcurrent"/> requests through this fetch at
    /// once. The gate is shared by every call to the returned function.
    let throttle (maxConcurrent: int) (fetch: Request -> Async<Response>) : Request -> Async<Response> =
        let gate = new SemaphoreSlim(maxConcurrent, maxConcurrent)

        fun request ->
            async {
                let! token = Async.CancellationToken
                do! gate.WaitAsync(token) |> Async.AwaitTask

                try
                    return! fetch request
                finally
                    gate.Release() |> ignore
            }

    /// A sensible default downloader: throttled, retried, and timed-out fetching over
    /// <c>Http.fetch</c>. Throttle is outermost, so a request holds its slot across its
    /// own retries — fewer concurrent hits on a struggling host.
    let politeFetch: Request -> Async<Response> =
        Http.fetch
        |> withTimeout (TimeSpan.FromSeconds 30.0)
        |> withRetry
        |> throttle 4
