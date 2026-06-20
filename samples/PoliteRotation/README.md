# PoliteRotation

**Recipe:** User-Agent rotation → crawl-ops.

Composes `Rotation.withRotatingUserAgent` over the polite downloader and drives the engine
with an explicitly tuned `Politeness` (robots-respecting, 2s between hits to one host). The
parser records which User-Agent fetched each page, so you can watch the rotation cycle.

```bash
dotnet run --project samples/PoliteRotation
```

Crawls the first 3 pages and prints the UA used per page. The User-Agents are honest and
identifiable — this spreads load, it does not disguise the crawler. Demonstrates the
`Rotation` module and `Spider.crawlPolitely`.
