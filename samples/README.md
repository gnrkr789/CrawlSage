# Samples — the CrawlSage cookbook

Runnable, self-contained crawlers — the real-world F# examples that are scarce
elsewhere. Each lands here as the matching phase is built (see
[`PROMPTS.md`](../PROMPTS.md) Phase 7 and [`docs/cookbook.md`](../docs/cookbook.md)).

Each sample is its own console project:

```bash
dotnet run --project samples/<Name>
```

To add one, use the **`new-spider`** skill (or copy an existing folder). Every sample:

- references `src/CrawlSage` and is added to `CrawlSage.slnx`,
- is **polite by default** (rate-limited, `robots.txt`-aware),
- writes any output under `data/` (git-ignored),
- ships a short `README.md` saying what it demonstrates.

## Recipes

All three crawl [quotes.toscrape.com](https://quotes.toscrape.com), a site built for
scraping practice.

| Sample | Recipe | Demonstrates |
| --- | --- | --- |
| [`QuotesToCsv`](QuotesToCsv) | extract a list → CSV | `Html` selectors + `Export.toCsv` |
| [`QuotesCrawl`](QuotesCrawl) | follow pagination → polite crawl → JSONL | `Spider.crawl` (robots + per-host pacing) + `Export.appendJsonLine` |
| [`QuotesJs`](QuotesJs) | dynamic data, no browser | `Extract.assignedJson` lifts an embedded `var data = […]` array |
| [`PoliteRotation`](PoliteRotation) | UA rotation → crawl-ops | `Rotation.withRotatingUserAgent` + `Spider.crawlPolitely` |
