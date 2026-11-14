# T012: manual validation on a real ChatGPT plan

Run by the owner on 2026-11-14, following the quickstart's "Validation" table, on `release/0.2.0` after T013 (`0224c97`), published with `dotnet publish -c Release` and started from the publish directory with a clean data directory (`LexScholar__DataDirectory=/tmp/lexscholar-t012`). Account: the owner's own ChatGPT account on a paid plan (the email is not recorded here). Model chosen automatically: `gpt-6.1-sol`. Documents: the reference set in `tests/Fixtures/summary/` only.

The first run found a defect: every summary through the plan failed with "did not give a usable answer", because the stream includes an undocumented `response.metadata` event. Fixed in T013 (#85, Codecks $13z); the run below was repeated after the fix.

## Results

| # | Check | Covers | Result |
|---|---|---|---|
| 1 | Sign in with ChatGPT in Firefox | FR-001, FR-002 | Pass: returned signed in, with the account shown |
| 2 | Welcome shown once | FR-007 | Pass |
| 3 | Summary of `article-en.pdf`: confirmation names the plan, citations, "Using ChatGPT plan", context names the plan and model | FR-006, FR-010, FR-014, SC-004 | Pass: points citing pages 1–12, "Generated … by gpt-6.1-sol using ChatGPT plan"; citations open the cited page |
| 4 | Summarize again: no confirmation | FR-010 | Pass |
| 5 | Restart: still signed in | FR-003 | Pass |
| 6 | Sign out, then search the data directory and the log for the account's email and token text (`eyJ`) | FR-004, FR-012, SC-002, SC-006 | Pass: no `chatgpt-signin.json`, no match. The log file covered the run since the last restart (`tee` overwrote it); the earlier part, read before the restart, showed only the request summary lines |
| 7 | Disconnect LexScholar in ChatGPT's settings, then summarize | FR-008, SC-003 | Pass: "Signed out" with a sign-in action, nothing technical |
| 8 | Usage limit reached | FR-009, SC-005 | Not run: optional on the real account by owner decision (quickstart). Covered by the fake service in T008 and T011 |
| 9 | Steps 1–4 in Chromium | SC-007 | Pass |
| 10 | `long-40.pdf` | F003 SC-007 | Pass: progress out of 40, about 1 minute |

F003's acceptance scenarios through the plan (SC-007) are recorded in F003's [T012 evidence](../../003-document-summary/evidence/t012-manual-validation.md).

SC-001 (first opening to first summary in under 5 minutes) was not timed separately; the flow needed no file, address, model or key.

"No plan access" (free tier) is covered by the fake service only, as planned.

## Follow-ups (non-blocking)

- Refusals that retrying can't change (no readable text, too long) still show **Try again**.
- The "not enough text" message doesn't say "Nothing was sent", unlike the other refusals.
- After a restart, the first request logs a `fail` from antiforgery (the old cookie can't be decrypted with the new ephemeral key); a new token is issued and the request succeeds. Noise only.
