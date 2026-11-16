# T012 evidence: manual validation

- **Date**: 2026-11-15.
- **Build tested**: `release/0.3.0` at `1efae03` (merge of PR #104).
- **Run by**: the owner, on her real setup (her ChatGPT plan and her configured service), in Firefox and Chromium, following quickstart "Manual validation".
- **Owner report**: "all passed in both browsers". The owner gave no per-check detail, so no numbers, timings or message texts are recorded.

| Check | Result |
| --- | --- |
| SC-001 | pass (owner report: all passed, Firefox and Chromium) |
| SC-002 | pass (owner report: all passed, Firefox and Chromium) |
| SC-003 | pass (owner report: all passed, Firefox and Chromium) |
| SC-004 | pass (owner report: all passed, Firefox and Chromium) |
| SC-005 | pass (owner report: all passed, Firefox and Chromium) |
| SC-006 | pass (owner report: all passed, Firefox and Chromium) |
| SC-007 | pass (owner report: all passed, Firefox and Chromium) |
| SC-008 | pass (owner report: all passed, Firefox and Chromium) |
| Extra: Stop on a running summary | pass (owner report: all passed, Firefox and Chromium) |
| Extra: dismiss while the ask is in flight | pass (owner report: all passed, Firefox and Chromium) |
| Extra: remove a document from the project while its summary runs | pass (owner report: all passed, Firefox and Chromium) |

T003's owner smoke check on her ChatGPT plan was run the same day: see [`t003-plan-background.md`](t003-plan-background.md).

## Known limits

- The dismiss-versus-job-creation race after a client-side POST error is not closed (gpt-review round 3 on PR #104). Accepted risk; an optional server-side cancel token is a possible later follow-up.
- Stored summaries cannot be exported or deleted workspace-wide until F002 (`$12y`).
