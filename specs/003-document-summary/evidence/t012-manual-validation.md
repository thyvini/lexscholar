# T012: manual validation on the researcher's setup

Status: **complete**, for now through her ChatGPT plan (owner decision, 2026-11-14). SC-006's `strace` check is not part of T012 for now (see below).

Run by the owner on 2026-11-12 and 2026-11-13 with a settings-file service, and on 2026-11-14 through a ChatGPT plan (F005, see its [T012 evidence](../../005-chatgpt-sign-in/evidence/t012-manual-validation.md) for the setup), following the quickstart's "Manual validation". Documents: the reference set in `tests/Fixtures/summary/` only.

## Settings-file service (2026-11-12 and 13)

- **Not configured (FR-010):** asking for a summary with no service set up explains how to configure one.
- **Service refusal (T013):** a refused request shows the service's own short message (Anthropic's OpenAI-compatible endpoint: no API credits on the account).
- **Confirmation for a service elsewhere (FR-014):** a cloud endpoint asks before sending, naming the host and model.

No settings-file service returned a full summary (no API credits; Gemini's free tier failed; a CPU-only local model was too slow, see `docs/notes/summary-service-access.md`), so the checks below were run through the ChatGPT plan.

## Through a ChatGPT plan (2026-11-14, model `gpt-6.1-sol`)

| Check | Covers | Result |
|---|---|---|
| A summary with citations, labelled as generated, with document, pages, time, model and service | FR-002, FR-006, FR-008 | Pass (`article-en.pdf`, `article-pt.pdf`) |
| Citation opens the cited page, Firefox and Chromium | FR-005, SC-003 | Pass |
| Checking a point against its page | SC-004 | Pass: under 30 s |
| Accuracy: 10 sampled points across `article-en.pdf` and `article-pt.pdf` | SC-002 | Pass: 9 of 10 supported by the cited page |
| Portuguese document gives Portuguese points | FR-002 | Pass |
| `scanned.pdf`: no readable text, nothing sent | FR-007 | Pass |
| `partial.pdf`: summary of pages 1, 3–4 and 6, pages 2 and 5 listed as unreadable, no citation to them | FR-007 | Pass |
| `little-text.pdf`: not enough text | FR-007 | Pass |
| `too-long.pdf`: more than 100 pages not supported, nothing sent | FR-011 | Pass |
| Progress on `long-40.pdf`, then dismiss by switching project: no summary appears later | FR-012, FR-009 | Pass |
| 40 pages within 2 minutes | SC-007 | Pass: about 1 minute (a cloud service, so local hardware is not a factor) |
| Library, project associations and stored files unchanged (spot-check in the app, also across a restart) | SC-005, FR-013 | Pass; T011 compares database rows and original-file hashes |

The reference articles repeat short passages across pages, so almost any point is supported somewhere; the accuracy check is weak on them. A real openly licensed article remains deferred (fixtures README).

## Not part of T012 for now: SC-006 (only loopback connections with a local model)

The `strace` check needs a working local model, and none was usable on the owner's hardware. Through the plan, text goes to OpenAI after her confirmation by design (F005 FR-010), so SC-006 doesn't apply to that route. It is deferred to when local models are tuned (F004 or later); T007 and T009 cover the classifier, the connect guard and no request before confirmation.

## Follow-ups (non-blocking)

- Refusals that retrying can't change (no readable text, too long) still show **Try again**.
- The "not enough text" message doesn't say "Nothing was sent", unlike the other refusals.
