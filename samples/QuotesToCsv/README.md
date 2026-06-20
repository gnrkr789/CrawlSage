# QuotesToCsv

**Recipe:** extract a list → export to CSV.

Fetches one page of [quotes.toscrape.com](https://quotes.toscrape.com) with the polite
downloader, maps each `.quote` block to a record using the `parse-html` selectors, and
writes a CSV with the `data-export` sink.

```bash
dotnet run --project samples/QuotesToCsv
```

Writes `data/quotes.csv` (10 rows) and prints the first few. Demonstrates `Html.parse` /
`Html.select` / `Html.selectAll` / `Html.text` and `Export.toCsv`.
