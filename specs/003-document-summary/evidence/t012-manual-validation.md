# T012: manual validation on the researcher's setup (partial)

Status: **partial, blocked on a working summarization service.** T012 stays open; v0.2.0 is not released until it is complete.

Run by the owner on 2026-11-12 and 2026-11-13, following the quickstart's "Manual validation".

## Checked and working

- **Not configured (FR-010):** asking for a summary with no service set up explains how to configure one.
- **Service refusal (T013):** a refused request shows the service's own short message (Anthropic's OpenAI-compatible endpoint: no API credits on the account).
- **Confirmation for a service elsewhere (FR-014):** a cloud endpoint asks before sending, naming the host and model.

## Not checked: no working service

No configured service returned a full summary, so nothing that needs one could be checked:

- Anthropic's OpenAI-compatible endpoint: refused, no API credits.
- Gemini's free tier: did not work; the error was not captured.
- A local model (Ollama): slow on CPU, and one run ended with an error Ollama reported ("abandoned"); set aside until local models are tuned (see `docs/notes/summary-service-access.md`).

Still to do, all with a working service:

| Check | Covers | Automated coverage so far |
|---|---|---|
| A summary with citations, labelled as generated | FR-002, FR-006, FR-008 | T003, T004 with the fake service |
| Citation shows the page, Firefox and Chromium | SC-003 | T005 checks the frame URL (`#page=N`); the native viewer showing that page is manual |
| Progress and dismiss on a long document | FR-012, FR-009 | T006, T008 with the fake service |
| Edge-case refusals on the real host | FR-007, FR-011 | T002, T011 |
| Accuracy: 9 of 10 sampled points supported by the cited page | SC-002 | none (needs a real model) |
| Checking a point in under 30 s | SC-004 | none |
| Only loopback connections with a local model (`strace`) | SC-006 | T007, T009 (classifier, connect guard, no request before confirmation) |
| 40 pages within 2 minutes, with the hardware recorded | SC-007 | none |
| Portuguese document gives Portuguese points | FR-002 | none (needs a real model) |
| Library, project associations and stored files unchanged after summaries (spot-check in the app) | SC-005, FR-013 | T011 compares database rows and original-file hashes before and after |

## Ways to unblock

- A cloud service with API credits (for example Claude Haiku 5.5 or an OpenAI API key), using the reference fixtures only.
- A tuned local model (smaller model, larger context or lower `MaxChunkCharacters`), which SC-006's `strace` check needs anyway.
- Her ChatGPT plan through F005 "Sign in with ChatGPT" (spike passed). F003 speaks Chat Completions, and plan usage needs the Responses API, so this route needs F005 first.
