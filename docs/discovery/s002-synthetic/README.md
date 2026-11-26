# S002: synthetic PDF extraction and lexical retrieval ($16h)

Discovery only, not a product decision. One generated PDF contains 2,000
physical pages: five short Portuguese legal-style passages, one empty page,
and repeated boilerplate on the remaining pages. It contains no real research
material. The experiment extracts with PdfPig and indexes each physical page
as a SQLite FTS5 row with `unicode61 remove_diacritics 2`.

## Observations

One run on Linux x86-64, AMD Ryzen 5 5600X, .NET SDK 10.0.114, Python
3.14.10, SQLite 3.54.0 and PdfPig 0.1.16:

| Measurement | Observed |
| --- | ---: |
| Generated PDF | 859,715 bytes; 2,000 pages |
| Extracted | 1,729,184 characters; 1,611 ms |
| Extraction process peak RSS (`VmHWM`) | 383,096 kB |
| FTS insertion and commit | 14.45 ms; 2,000 rows |
| SQLite database | 2,523,136 bytes |

Extraction time includes opening the PDF and writing JSONL, and peak RSS is
for the entire F# interactive process, including runtime overhead. Index
time excludes loading JSONL into memory and creating the table. These are
single runs, not benchmarks or guarantees; the printed query timings are
single warm-process measurements, not latency percentiles.

Ten of twelve hand-written query expectations matched. Case and accent
variants of `constituição`, exact legal terms and the phrase `danos morais`
returned the expected physical page; the blank page was indexed with zero
characters. `trabalhadores` did not find `trabalhador`, and `indenizar` did
not find `indenização`. These deliberately optimistic relevance expectations
highlight that this tokenizer does not provide Portuguese stemming or
semantic matching. The two negative phrase checks are not a broad relevance
evaluation. Page references are the generator's physical page numbers, not
validated printed folios.

## Limits and next questions

This tiny, highly repetitive, digitally generated file is not representative
of large legal PDFs, complex layouts, OCR, corrupt files, or realistic search
relevance. The empty page is not a scan test: image-only pages need OCR or
must be surfaced as unavailable text. No end-to-end LexScholar import,
summary, supported-browser, concurrency, disk-pressure or failure test was
performed. Results cannot justify a 100 MB import default or a 2,000-page
operational limit. Import byte/page defaults and the existing 100-page
summary limit must be assessed separately.

For F008, compare FTS5 with a Portuguese-aware stemming or controlled query
expansion approach on a larger labelled, lawfully obtained corpus, including
inflections, synonyms, cross-page passages, scans and page-reference checks.
Measure extraction memory and time on representative files near proposed
limits and supported hardware. OCR is an option for image-only pages but was
not evaluated. No vector database or retrieval architecture is selected.

## Reproduce

Copy the three adjacent scripts into an **empty scratch directory**; they
overwrite `synthetic-2000.pdf` and `pages.jsonl`, and `index.py` drops the
`pages` table in `pages.sqlite`. Do not run them against user data. With
Python 3 (SQLite built with FTS5), .NET SDK 10 and NuGet access/cache:

```sh
python3 generate.py
dotnet fsi extract.fsx
python3 index.py
```

The F# interactive script emits FS0524 warnings for top-level `use` bindings;
the run above completed despite them. Output and timing vary by environment.
The scripts report failed relevance expectations but do not return a failing
exit status for them.
