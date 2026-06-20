# QuotesCrawl

**Recipe:** follow pagination → polite crawl → stream to JSON Lines.

Seeds page 1 of [quotes.toscrape.com](https://quotes.toscrape.com), emits each quote as an
item and follows the "Next →" link, letting the `Spider` engine walk all 10 pages.
`Spider.crawl` is polite by default — it respects `robots.txt` and paces per host — and each
quote streams to `data/quotes.jsonl` as it is scraped.

```bash
dotnet run --project samples/QuotesCrawl
```

Writes `data/quotes.jsonl` (~100 lines). Takes ~15s: the per-host delay spaces the ten page
fetches about a second apart. Demonstrates the crawl engine, pagination, Phase 6 politeness,
and `Export.appendJsonLine`.
