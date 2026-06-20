namespace CrawlSage

open System.Collections.Generic

/// The crawl frontier — the pending requests plus the dedup filter. The engine pulls work
/// from it and pushes follow-ups back, so swapping the implementation buys persistence,
/// bounding or a different ordering with zero engine changes.
[<NoComparison; NoEquality>]
type Frontier =
    { /// Enqueue <c>(request, depth)</c> unless its fingerprint was already seen; returns
      /// <c>true</c> if it was newly added.
      Add: Request -> int -> bool
      /// Remove and return up to <c>max</c> pending <c>(request, depth)</c> pairs in FIFO
      /// (breadth-first) order; <c>[]</c> when the frontier is drained.
      Take: int -> (Request * int) list }

/// Frontier constructors.
module Frontier =

    /// Dedup key: HTTP method + canonicalised URL (see <see cref="M:CrawlSage.Url.normalize"/>).
    let fingerprint (request: Request) : string =
        $"{request.Method}|{Url.normalize request.Url}"

    /// The default in-process frontier: a FIFO queue behind a hash-set dedup filter. The
    /// lock keeps it safe even though the engine touches it from one thread at a time.
    let inMemory () : Frontier =
        let seen = HashSet<string>()
        let queue = Queue<Request * int>()
        let gate = obj ()

        { Add =
            fun request depth ->
                lock gate (fun () ->
                    if seen.Add(fingerprint request) then
                        queue.Enqueue(request, depth)
                        true
                    else
                        false)
          Take =
            fun n ->
                lock gate (fun () ->
                    [ let mutable taken = 0

                      while taken < n && queue.Count > 0 do
                          yield queue.Dequeue()
                          taken <- taken + 1 ]) }
