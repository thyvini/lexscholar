# Research: Document Summary with Page References (F003)

Decisions for [plan.md](plan.md). Each has the decision, why, and what else was considered.

## 1. Talking to the researcher's chosen service

- **Decision**: One client for the **OpenAI-compatible Chat Completions** protocol (`POST {Endpoint}/chat/completions`, non-streaming), written with `HttpClient` and `System.Text.Json`. **No new package.**
- **Covers**: local servers Ollama (`http://127.0.0.1:11434/v1`), llama.cpp `llama-server` (`:8080/v1`), LM Studio (`:1234/v1`), vLLM (`:8000/v1`); cloud services OpenAI, Mistral, Groq, OpenRouter, Google Gemini's OpenAI-compatible endpoint and Anthropic's OpenAI SDK compatibility endpoint. Anything else that speaks the protocol works the same way.
- **Settings** extend the existing `LexScholar` section (same sources as today: `appsettings.json` in the content root, environment variables, command line):

  ```json
  "LexScholar": {
    "Summary": {
      "Endpoint": "http://127.0.0.1:11434/v1",
      "Model": "qwen3:8b",
      "ApiKey": null,
      "MaxChunkCharacters": 12000,
      "Temperature": null,
      "MaxOutputTokens": null
    }
  }
  ```

  `Endpoint` and `Model` are required to enable summaries; with no `Summary:Endpoint` the feature reports "not configured" (FR-010). `Endpoint` must be an absolute `http` or `https` URL without user info, query or fragment, otherwise startup fails with a configuration error like the existing ones. `MaxChunkCharacters` is optional (decision 4). `Temperature` (a number from 0 to 2) and `MaxOutputTokens` (a positive whole number) are optional and sent only when set, as `temperature` and `max_tokens`; an invalid value fails startup.
- **Request body**: by default only `model` and `messages`. OpenAI's reasoning models (o-series, GPT-5 family) reject `max_tokens` and non-default `temperature` with `400 unsupported_parameter`, and local servers work without them. The field is `max_tokens` because every listed local server and most compatible cloud services accept it; for an OpenAI reasoning model, leave `MaxOutputTokens` unset. Alternative: a setting for the field name, or `max_completion_tokens` (not accepted by every local server); rejected as more settings for a value that is optional anyway.
- **Secrets**: `ApiKey` is sent only as `Authorization: Bearer` to the configured endpoint. Set it with the environment variable `LexScholar__Summary__ApiKey` (never in the repository). It is never logged, never part of a response or problem detail, and `describeStartupError` never echoes it. The outgoing `HttpClient` is created directly (not through `IHttpClientFactory`), so the framework's request logging is not attached. Logs record only service host, model, page counts and duration, never document text, prompts or model output.
- **Only the configured service**: the handler has `AllowAutoRedirect = false` (a redirect is a service failure), and every request URI is built from `Endpoint`. Nothing else in the feature opens outgoing connections.
- **Alternatives**: the OpenAI .NET SDK (a package for one POST; cloud-oriented options we don't need); Microsoft.Extensions.AI (an abstraction layer over the same call, plus packages); Ollama's native API (local only); one client per provider (more code and settings for no requirement). Rejected under Principle V.

## 2. "On her machine" and per-summary confirmation (FR-014)

- **Decision**: the service is **local** when the endpoint host is the name `localhost` or an IP literal in `127.0.0.0/8` or `::1`. Everything else, including LAN addresses and other names, is **elsewhere**. The rule is a pure function in Domain (`ServiceLocation.classify`).
- For a local service the handler sets `UseProxy = false` (an environment proxy would carry the text off the machine) and a `ConnectCallback` that refuses any resolved address that is not loopback, so `localhost` cannot be redirected elsewhere by name resolution.
- **Confirmation** is enforced by the server, per request: `POST .../summary` carries `{"confirmSend": true|false}`. For an elsewhere service without `confirmSend: true`, the server answers `409 confirmation_required` with the endpoint host and model, after its local checks (decision 3) and before any connection to the service. The client shows "The text of *file* will be sent to *host* (model *m*). Send?"; **Send** repeats the request with `confirmSend: true`; **Cancel** sends nothing. Nothing is remembered, so each summary asks again. A local service ignores `confirmSend`.
- **Bound to the shown service**: the request also carries `confirmedHost` and `confirmedModel`, the values the dialog showed. The server treats it as confirmed only when `confirmSend` is true and both equal the current configuration, otherwise it answers `409` again with the current host and model. Without this, a confirmation given for one service could authorise a send to another if the configuration changed between dialog and request, or if a bare `confirmSend: true` were replayed.
- **Accepted risk**: a loopback port that forwards elsewhere (SSH tunnel, local proxy) counts as local. That is the researcher's own setup; the quickstart says so.
- **Alternatives**: treating private ranges (`192.168.x`) as local (the text would leave her machine); a settings flag `IsLocal` (a typo would silently skip the confirmation); confirming once per session (the spec asks for each summary); client-only confirmation (the server must not depend on the UI to keep FR-014).

## 3. Text per page

- **Decision**: open the stored original through `VerifiedDocumentContent.openRead` (same availability checks and `document_unavailable` answer as the reader), then PdfPig: `PdfDocument.Open`, check `NumberOfPages <= 100` **before** extracting (FR-011), then `ContentOrderTextExtractor.GetText(page)` for pages 1..N (included in the existing `PdfPig` package). The result is a list of `{ Number; Text }` with `Number` = page position (FR-003). A page whose text has no letter or digit is **unreadable**.
- Rules (pure, Domain): no readable page → `no_text`; fewer than 200 readable characters in total → `not_enough_text`, without calling the service; otherwise the summary covers readable pages and lists the unreadable ones (FR-007).
- Text is normalised (control characters removed, whitespace collapsed) and sent inside page markers `[Page 7]`. Page numbers never come from the text itself.
- **Alternatives**: PdfPig `page.Text` (raw content-stream order, worse for multi-column papers); a separate PDF library or `pdftotext` (new dependency, PdfPig already present); OCR (out of scope: no-text pages are reported, not recovered).

## 4. Fitting 100 pages; progress

- **Estimate**: an academic page is about 3,000–4,000 characters, roughly 700–1,000 tokens; 100 pages is 70–100k tokens. Local servers run with small default contexts (Ollama and llama.cpp commonly 4k–8k unless raised), and even 128k-context models need much more memory and minutes of prompt processing on a laptop. One request does not fit typical local setups.
- **Decision**: split into **chunks of consecutive whole pages** up to `MaxChunkCharacters` (default 12,000, about 3k tokens, so a chunk plus instructions and answer fits an 8k context). A page longer than the limit is split into parts that keep its page number. Then:
  - one chunk: one call asks for the final 5–10 points;
  - several chunks: each chunk call returns at most 5 main points of its pages (*map*); the points, with their pages, are then grouped up to `MaxChunkCharacters` and each group is combined into at most 5 points, repeating until one group remains, which gives the final 5–10 points (*combine*). Every combine call is bounded by the same limit as a chunk, so a smaller context or a longer document adds rounds instead of overflowing the last call. Each round must reduce the number of points; a round that does not, or more than 5 rounds, ends with `service_failed` instead of calling the service again. `MaxChunkCharacters` must be at least 4,000 (startup check), so a group always holds several points.
- **Progress** (FR-012) is reported after each chunk as pages processed out of the document's page count; unreadable pages count as processed from the start, and the combine step shows "Combining points". The response is streamed as NDJSON events (contract), so one request carries progress and result.
- **Alternatives**: one request with the whole document (fails on typical local contexts); asking her to set a context size (the default should work); token counting with a tokenizer package (characters are a good enough proxy, and the limit is configurable); a single unbounded combine call (30 chunks of points can exceed a small context, so the last call would fail after all chunks were done).

## 5. Asking for checkable points and validating them

- **Decision**: a system message with the instructions, a user message with the page-marked text. The model must answer only with JSON:

  ```json
  {"points": [{"text": "…", "pages": [3, 4]}]}
  ```

  in the language of most of the text, 5–10 points, each citing the page markers it comes from, `{"points": []}` when the text has no main points. No sampling or length parameters unless configured (§1); the response body is read up to 1 MB, beyond which it is `service_failed`. No `response_format`, because support differs between servers; the parser is the safety net.
- **Validation** (pure, Domain, model output is untrusted): strip `<think>…</think>` blocks and code fences, take the outermost JSON object, parse with `System.Text.Json`. Unparseable → `service_failed` (retry offered). A `400` from the service → `service_failed` with LexScholar's own message: the service rejected the request, check `Model`, and remove `Temperature` or `MaxOutputTokens` if set. The service's response body is never echoed or logged. Each point: `text` a non-empty string (trimmed, at most 1,000 characters); `pages` a non-empty array of integers, **all** in the call's allowed set. A **map** call may cite only the readable pages of its chunk; a **combine** call may cite only the pages cited by the (already validated) points in its group. So every final citation traces back to a page that was actually sent. `parsePoints` takes that set; the workflow computes it from the chunk or the group. Valid pages are de-duplicated and sorted; a point failing any rule is omitted and counted (FR-004). Zero valid points → `not_enough_text`, with the omitted count.
- The client renders point text as plain text (Bolero escapes it); no Markdown or HTML from the model.
- **Alternatives**: JSON Schema structured outputs (not uniformly supported; would still need validation); tool calls (same); free-text with "(p. 3)" parsing (fragile); dropping only the bad page from a point (a point citing a non-existent page is suspect as a whole).

## 6. Abandoning a request (FR-009)

- **Decision**: client side, the existing request-number pattern: the summary model stores the request number and document; dismissing, closing or switching document or project increments it, so late events are dropped, and emits a `Cancel` effect that cancels the `CancellationTokenSource` of the fetch (in WebAssembly this aborts the browser `fetch`). Server side, `HttpContext.RequestAborted` is linked into every service call, so the outgoing request is cancelled and no further chunk is sent. While one request runs, asking again for the same document is ignored (`Busy` guard).
- **Alternatives**: a server job with an ID and a cancel endpoint (background work is F004; more state); ignoring late results only on the client (the service would keep working and receiving chunks).

## 7. Testing without a real model

- **Fake service**: `FakeChatService` in `tests/LexScholar.Server.Tests`, an `HttpMessageHandler` that parses the request, records it (URI, headers, body) and answers from a script (fixed JSON, invalid citations, garbage, an error status, or waiting until cancelled). The host takes the outgoing handler from an optional DI registration, so `TestHarness.Start(summaryHandler = fake)` uses it; production composition builds the real handler. Application workflow tests use plain function fakes.
- **Reference set** in `tests/Fixtures/summary/`, all generated by a committed script (`scripts/make-summary-fixtures.fsx`, PdfPig's `PdfDocumentBuilder`) from public-domain text (Project Gutenberg sources named in the README with their URLs), plus one openly licensed (CC BY 4.0) real article under 1 MB for realistic layout, with source URL, licence and SHA-256 in `tests/Fixtures/README.md`:
  `article-en.pdf` (12 pages), `article-pt.pdf` (8 pages, Portuguese), `long-40.pdf` (40 pages, SC-007), `scanned.pdf` (no text layer), `partial.pdf` (pages 2 and 5 without text), `little-text.pdf` (one short line), `too-long.pdf` (101 pages).
- **SC-001** (automated): every reference document through the endpoint with the fake returning citations that include out-of-range and unreadable pages; asserts every shown page exists and was readable, the omitted count, and `no_text` for `scanned.pdf` without any service call. A Domain test gives `parsePoints` a combine output citing a page outside its group's citations and expects that point omitted. A contract test checks the default request body has only `model` and `messages`, and that a `400` gives `service_failed` without the service's body.
- **SC-005** (automated): database rows and original-file hashes compared before and after request, confirm, decline and abandon.
- **SC-006**: automated: the classifier on its edge cases (`localhost`, `127.0.0.2`, `::1`, `0.0.0.0`, `192.168.1.10`, `localhost.example`); the connect guard refuses a non-loopback address; with an elsewhere endpoint, a request without confirmation returns `409` and the fake records **zero** requests; the declined dialog emits no effect (state test). Manual, with a real local server: `strace -f -e trace=connect` on the published host during summaries of the reference set shows only loopback connections (recorded in evidence).
- **SC-002, SC-004, SC-007** are manual on the researcher's configured service (quickstart). SC-003: a browser journey asserts the reader's frame URL ends with `#page=N` after a citation click; that the native viewer shows the page is checked manually in Firefox and Chromium, as in F001.

## Citation navigation

- **Decision**: the reader frame's `src` becomes `{contentUrl}#page={n}` (PDF open parameters, honoured by Firefox's pdf.js and Chromium's viewer). The frame is keyed by a navigation counter so every click re-creates it, which also works for a repeated click on the same page.
- **Alternatives**: changing only the fragment (no navigation when the same page is clicked twice, and behaviour differs between viewers); embedding pdf.js (new dependency, replaces the F001 reader decision).
