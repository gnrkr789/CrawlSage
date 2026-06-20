namespace CrawlSage

open System
open System.Collections.Generic
open System.IO
open System.Text.Json

/// The crawl frontier — the pending requests plus the dedup filter. The engine pulls work
/// from it and pushes follow-ups back, so swapping the implementation buys persistence,
/// bounding or a different ordering with zero engine changes.
[<NoComparison; NoEquality>]
type Frontier =
    { /// Enqueue <c>(request, depth)</c> unless its fingerprint was already seen (or the
      /// frontier is full); returns <c>true</c> if it was newly added.
      Add: Request -> int -> bool
      /// Remove and return up to <c>max</c> pending <c>(request, depth)</c> pairs in FIFO
      /// (breadth-first) order; <c>[]</c> when the frontier is drained.
      Take: int -> (Request * int) list }

/// Frontier constructors: <c>inMemory</c> (default), <c>bounded</c> (capped) and
/// <c>persistent</c> (disk-backed, resumable).
module Frontier =

    /// Dedup key: HTTP method + canonicalised URL (see <see cref="M:CrawlSage.Url.normalize"/>).
    let fingerprint (request: Request) : string =
        $"{request.Method}|{Url.normalize request.Url}"

    /// Drain up to <paramref name="n"/> items from <paramref name="queue"/> (caller holds the lock).
    let private drain (queue: Queue<Request * int>) (n: int) : (Request * int) list =
        [ let mutable taken = 0

          while taken < n && queue.Count > 0 do
              yield queue.Dequeue()
              taken <- taken + 1 ]

    /// Shared in-memory core: a FIFO queue behind a hash-set dedup filter, capped at
    /// <paramref name="capacity"/> pending items (additions past the cap are dropped).
    let private make (capacity: int) : Frontier =
        let seen = HashSet<string>()
        let queue = Queue<Request * int>()
        let gate = obj ()

        { Add =
            fun request depth ->
                lock gate (fun () ->
                    if queue.Count >= capacity then false
                    elif seen.Add(fingerprint request) then
                        queue.Enqueue(request, depth)
                        true
                    else
                        false)
          Take = fun n -> lock gate (fun () -> drain queue n) }

    /// The default in-process frontier: a FIFO queue behind a hash-set dedup filter.
    let inMemory () : Frontier = make Int32.MaxValue

    /// An in-memory frontier capped at <paramref name="maxPending"/> queued requests; once
    /// full it drops further additions, bounding memory on pathological (e.g. link-farm)
    /// crawls. Dedup still applies.
    let bounded (maxPending: int) : Frontier = make (max 1 maxPending)

    // ---- persistent (disk-backed, resumable) --------------------------------

    /// Serialise a pending request to one JSON line. Written by hand (Utf8JsonWriter) so it
    /// needs no F# option/DU/Map converters and no reflectable DTO.
    let private serialize (request: Request) (depth: int) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)

        let writeMap (name: string) (m: Map<string, string>) =
            writer.WriteStartObject name
            m |> Map.iter (fun k v -> writer.WriteString(k, v))
            writer.WriteEndObject()

        writer.WriteStartObject()
        writer.WriteString("u", request.Url)

        writer.WriteString(
            "m",
            match request.Method with
            | Get -> "Get"
            | Post -> "Post"
        )

        match request.Body with
        | Some b -> writer.WriteString("b", b)
        | None -> ()

        writeMap "h" request.Headers
        writeMap "meta" request.Meta
        writer.WriteNumber("d", depth)
        writer.WriteEndObject()
        writer.Flush()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    /// Parse a pending line back into a request and its depth.
    let private deserialize (line: string) : Request * int =
        use doc = JsonDocument.Parse line
        let root = doc.RootElement

        let str (name: string) =
            match root.TryGetProperty name with
            | true, p -> p.GetString()
            | _ -> null

        let readMap (name: string) =
            match root.TryGetProperty name with
            | true, p when p.ValueKind = JsonValueKind.Object ->
                [ for prop in p.EnumerateObject() -> prop.Name, prop.Value.GetString() ] |> Map.ofList
            | _ -> Map.empty

        { Url = root.GetProperty("u").GetString()
          Method = (if str "m" = "Post" then Post else Get)
          Headers = readMap "h"
          Body = (let b = str "b" in if isNull b then None else Some b)
          Meta = readMap "meta" },
        (match root.TryGetProperty "d" with
         | true, p -> p.GetInt32()
         | _ -> 0)

    /// A disk-backed frontier for resumable / large crawls: the dedup set and the pending
    /// queue are journaled under <paramref name="dir"/>, so a crawl can stop (or crash) and
    /// resume — already-seen URLs are skipped and the remaining queue is replayed. At most
    /// the last in-flight batch (taken but not yet finished) is lost on a hard crash.
    let persistent (dir: string) : Frontier =
        Directory.CreateDirectory dir |> ignore
        let seenPath = Path.Combine(dir, "seen.log")
        let pendingPath = Path.Combine(dir, "pending.jsonl")
        let cursorPath = Path.Combine(dir, "cursor.txt")
        let gate = obj ()

        let seen = HashSet<string>()
        let queue = Queue<Request * int>()
        let mutable cursor = 0

        if File.Exists seenPath then
            for line in File.ReadLines seenPath do
                if line <> "" then seen.Add line |> ignore

        if File.Exists cursorPath then
            match Int32.TryParse(File.ReadAllText(cursorPath).Trim()) with
            | true, c -> cursor <- c
            | _ -> ()

        if File.Exists pendingPath then
            let mutable index = 0

            for line in File.ReadLines pendingPath do
                if line <> "" then
                    if index >= cursor then
                        queue.Enqueue(deserialize line)

                    index <- index + 1

        // Append per add (no held file handle), so a fresh instance can reopen the journal
        // on resume — even from the same process, as the tests do.
        let appendLine path (line: string) = File.AppendAllText(path, line + "\n")

        { Add =
            fun request depth ->
                lock gate (fun () ->
                    let fp = fingerprint request

                    if seen.Add fp then
                        appendLine seenPath fp
                        appendLine pendingPath (serialize request depth)
                        queue.Enqueue(request, depth)
                        true
                    else
                        false)
          Take =
            fun n ->
                lock gate (fun () ->
                    let taken = drain queue n

                    if not (List.isEmpty taken) then
                        cursor <- cursor + taken.Length
                        File.WriteAllText(cursorPath, string cursor)

                    taken) }
