# QuotesJs

**Recipe:** dynamic data, no browser.

[quotes.toscrape.com/js](https://quotes.toscrape.com/js) renders its quotes client-side from
a `var data = [ … ]` array embedded in a `<script>` — the static HTML contains **no**
`.quote` markup. Instead of driving a browser, this sample lifts the embedded array with
`Extract.assignedJson "data"` and reads it directly.

```bash
dotnet run --project samples/QuotesJs
```

Prints how many `.quote` elements the static HTML had (zero) versus how many quotes were
extracted from the embedded JSON, and writes `data/quotes-js.csv`. Demonstrates the
"extract, don't render" rung of the [`dynamic-page`](../../.claude/skills/dynamic-page)
ladder — `Extract.assignedJson` / `asList` / `prop` / `path`.
