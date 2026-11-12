# Quickstart: Document Summary (F003)

How to configure a summarization service and validate the feature. Decisions are in [research.md](research.md), the endpoint in [contracts/summary-api.yaml](contracts/summary-api.yaml).

## Configure a service

Summaries need an OpenAI-compatible Chat Completions service. Add to `appsettings.json` in the directory you start LexScholar from (or use environment variables such as `LexScholar__Summary__Endpoint`):

| Key | Required | Example | Notes |
|---|---|---|---|
| `LexScholar:Summary:Endpoint` | yes | `http://127.0.0.1:11434/v1` | Base URL; `/chat/completions` is appended. |
| `LexScholar:Summary:Model` | yes | `qwen3:8b` | Model name as the service knows it. |
| `LexScholar:Summary:ApiKey` | cloud only | — | Set as the environment variable `LexScholar__Summary__ApiKey`. Never commit it. |
| `LexScholar:Summary:MaxChunkCharacters` | no | `12000` | Lower it for models with a small context. |

Examples: Ollama `http://127.0.0.1:11434/v1`, llama.cpp `http://127.0.0.1:8080/v1`, LM Studio `http://127.0.0.1:1234/v1`, OpenAI `https://api.openai.com/v1`, Mistral `https://api.mistral.ai/v1`.

**Where the text goes**: with an endpoint on `localhost`, `127.x.x.x` or `::1`, the document's text stays on this machine and no confirmation is asked. With any other endpoint, including another computer on your network, LexScholar asks before every summary and sends nothing unless you confirm. A local port that forwards to another machine (an SSH tunnel, a proxy) counts as local. Summaries are never saved.

## Automated checks

```sh
scripts/check.sh <task IDs>          # per slice, see tasks.md
```

Tests use a fake service and the reference set in `tests/Fixtures/summary/` (provenance in `tests/Fixtures/README.md`); no model is needed.

## Manual validation (real service)

Prerequisites: a local server (for example Ollama with a model pulled), LexScholar published and started from its publish directory, the reference set imported into a project.

1. **Summary and citations (SC-001, SC-003, SC-004)**: open `article-en.pdf`, request a summary. Expect 5–10 points in English, each with page links, labelled as generated, with document, pages, time, host and model. Click each citation: the reader shows that page, in Firefox and Chromium. Time checking a few points against their pages.
2. **Language (FR-002)**: `article-pt.pdf` gives points in Portuguese.
3. **Support (SC-002)**: for 10 points across the set, record whether the cited page supports the point; expect at least 9.
4. **Edge cases**: `scanned.pdf` → no readable text; `partial.pdf` → summary lists pages 2 and 5 as unreadable; `little-text.pdf` → not enough text; `too-long.pdf` → not supported yet, nothing sent.
5. **Timing and progress (SC-007, FR-012)**: `long-40.pdf` shows pages processed out of 40 and finishes within 2 minutes of the request.
6. **Abandon (FR-009)**: start a summary of `long-40.pdf`, then switch project; no summary appears later, and the server log shows the request cancelled.
7. **Nothing leaves the machine (SC-006)**: run the host under `strace -f -e trace=connect -o connect.log dotnet LexScholar.Server.dll`, summarize the set, and check `connect.log` has only loopback (`127.0.0.1`, `::1`) and local socket addresses.
8. **Service elsewhere (FR-014)**: point `Endpoint` at a cloud service (or any non-loopback address). Requesting a summary shows the confirmation with host and model; **Cancel** sends nothing (no connection in `strace`); **Send** produces the summary.
9. **Not configured (FR-010)**: remove `Summary:Endpoint`; requesting a summary explains how to configure one.
10. **Unchanged data (SC-005)**: library, project associations and stored files are the same before and after (automated; spot-check in the UI).
