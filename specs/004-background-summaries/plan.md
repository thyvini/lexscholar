# Implementation Plan: Background Summaries

**Branch**: `docs/f004-plan` | **Date**: 2026-11-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/004-background-summaries/spec.md`

## Summary

A summary stops belonging to the HTTP request that asked for it. The server keeps one in-memory **summary job** per (project, document) pair in a registry; the POST runs every F003/F005 check and confirmation exactly as today, then starts the job with its own cancellation token and answers `202` at once. The client learns state by asking: `GET .../summary` for the open document and `GET /api/summaries` for the list markers, polled every 2 seconds while something it knows of is running. A finished summary is written to a new `DocumentSummaries` table (handwritten migration) and served from there until dismissed; a failure stays in memory until dismissed, retried or the app restarts. Stop and dismiss are `DELETE .../summary`. Sign-out reaches plan jobs through F005's existing `Vault.BeginRun`/`EndRuns`. Decisions: [research.md](research.md); model: [data-model.md](data-model.md); endpoints: [contracts/summary-api.yaml](contracts/summary-api.yaml).

## Accepted risks

Reviewers: these are deliberate. Do not report them as P0/P1 unless the code breaks the stated limit.

1. **One researcher, one app instance.** The registry is in memory in the one host process (the data-directory lock already prevents a second instance). No cross-process coordination.
2. **Work running when the app closes is lost.** On restart the document shows no summary and she can ask again (spec, edge cases). Running jobs and failures are not persisted; only finished summaries are.
3. **Stop and finishing may race.** Either outcome is acceptable — the summary is stopped, or it finished just before Stop and Stop then dismisses it — provided that (a) no new request for the job starts after Stop returns, and (b) a stopped summary is never stored or shown. (a) comes from cancelling the job's token, which every call receives; (b) from the job-id check under the registry lock (research §3).
4. **Two browser tabs** may both show the same summary and its markers, and either may dismiss it; the other catches up when it next opens that document or project (each view change fetches state and statuses); a tab with nothing running does not poll, so it does not see the other tab's changes before then.
5. **Polling** costs one small local request every 2 seconds per tab, only while a job the client knows of is running. Markers can lag by up to that interval (FR-008 asks for no reload, not for instant updates).
6. **A failed summary is not stored.** It is shown until dismissed, retried or the app restarts. The spec requires storing only finished summaries (FR-009).
7. **Shutdown cancels running jobs** via the host's stopping token; a job cancelled that way stores nothing (same as risk 2).
8. **No export of stored summaries yet.** FR-010 export and full deletion belong to F002 (`$12y`), which is not built; the table is designed so F002 can include it (research §5). Until then the constitution's Principle II tracking for `$12y` covers summaries as derived data, and the quickstart says so.
9. **Sign-out and finishing may race.** If a plan summary finishes at the moment she signs out, it may be kept: it was finished on her plan before the sign-out. FR-007 and SC-005 are about work still running: a job whose run token is already cancelled when its result arrives ends as `failed signed_out` and stores nothing. No ordering between the sign-in lock and the registry lock is needed for this.
10. **Cancellation aborts, it doesn't un-send.** Stop, removal and sign-out cancel the job's token. A request already on its way is aborted, so the service may have received that one request; no new request starts on a cancelled token, because .NET's HTTP client checks the token before sending. Service calls are not wrapped in the registry lock.

## Technical Context

**Language/Version**: F# on .NET 10 (SDK pinned in `global.json`)

**Primary Dependencies**: existing only (ASP.NET Core, Bolero, EF Core SQLite, PdfPig). **No new packages.**

**Storage**: SQLite, one new table `DocumentSummaries` keyed by (ProjectId, DocumentId), FK to `ProjectDocuments` with `ON DELETE CASCADE`; handwritten migration (research §5).

**Testing**: xUnit v3; `FakeChatService` and `FakeOpenAi` with their existing handshakes (`WaitUntilCancelled`, `Waiting`, `Cancelled`); Playwright journeys in Chromium and Firefox; restarts with `TestHarness(workspace = …)`.

**Target Platform**: Linux, Firefox and Chromium.

**Project Type**: local web application (ASP.NET Core host + Bolero WebAssembly client).

**Performance Goals**: switching documents or projects no slower with jobs running (SC-006): jobs run on the thread pool and polling is one small request.

**Constraints**: FR-006 — every check and confirmation before sending is unchanged and still happens inside the POST, before the job exists; host security unchanged; no document text, prompt or model output in logs (F003/F005 rules); stored summary contains only what F003 already returns to the client.

**Scale/Scope**: one researcher; a handful of concurrent jobs; documents up to 100 pages.

## Constitution Check

*Checked before research and again after design: passes, with Principle II's export and full-deletion controls deferred and tracked (not full compliance).*

| Principle | How the design meets it |
|---|---|
| I. Integrity | The finished summary is F003's validated result, stored as is; citations are checked before storing, never re-derived. |
| II. Privacy and control | Confirmation rules unchanged (FR-006). The stored summary is local derived data: dismiss deletes it, removal from the project deletes it (cascade). Export and full deletion are deferred to F002 (`$12y`), documented as unavailable (Accepted risk 8, quickstart) and tracked by the constitution's user-control section; Principle II remains outstanding until F002, so this is a tracked deferral, not full compliance. |
| III. Human oversight | Same labels, citations and dismiss as F003 (FR-003). |
| IV. Testability | Deterministic fakes and handshakes for every race that matters (Stop, sign-out, removal); the riskiest assumption is tested first (T002). Stored summary keeps service host, model and time. |
| V. Simplicity | One in-memory registry, one table, polling instead of a push channel (research §4), F005's run registry reused for sign-out. |
| VI. Decisions recorded | research.md. |

## Design

**Domain** (`Summaries.fs`, pure): `JobState = Running of Progress option | Finished of StoredSummary | Failed of code * detail * omitted option`; `SummaryKey = { Project: Guid; Document: Guid }`; status projection for markers (`Running | Finished | Failed`) and the per-project roll-up (any running → running, else any failed → failed, else finished).

**Application** (`SummaryJobs.fs`, new): `SummaryJobs` registry — a dictionary of `SummaryKey → Job` under one lock. `Start key run` decides under the lock, in this order: association gone → `NotAssociated`; a running job for the key → `AlreadyRunning`; a failed job is replaced (retry); a stored summary for the key (`Find` capability, read inside the lock, the same lock `Complete` saves under) → `AlreadyFinished`; else `Started` (FR-011); the job's work is a function `CancellationToken -> report -> Task<Result>` supplied by the endpoint. `Progress`, `Complete`, `Fail` update the job only if it is still the current one (same job id); `Complete` checks, inside the registry lock, that the job's effective token (for a plan job, the `Vault` run token, which sign-out cancels) is not cancelled — if it is, the job ends as `Failed signed_out` and nothing is saved, F005's existing rule — and only then calls the `Save` capability inside the lock, so Stop either happens before (nothing saved) or after (it then deletes) — research §3. Every service call receives the job's token, so a cancelled job starts no new call (accepted risks 3 and 10). `Stop key` cancels and removes; `RemoveAssociation key delete` runs `Stop` and the association delete under the same lock, and `Start` checks the association under that lock, so no job can start for a removed pair (FR-013); `Snapshot ()` lists states for markers. Plan jobs take their token from `Vault.BeginRun(jobToken)`, so `signOut` cancels them and they end as `Failed signed_out`.

**Infrastructure**: `Migrations/DocumentSummaries.fs` + snapshot + row in `WorkspaceDb.fs`; `SummaryRepository.fs` (`save`, `find`, `delete`, `listKeys`).

**Server** (`SummaryEndpoints.fs`): POST first checks the association, then asks the registry for the pair: if a summary is running or finished it answers `200` with that state at once, before any destination, confirmation or sign-in check (FR-011). Otherwise it keeps every check up to and including `prepareSend` (plan) or the elsewhere confirmation (configured), then `jobs.Start` with the existing `summarizeOnce` logic reworked to report into the job instead of writing NDJSON; answers `202` with the job state. `GET .../summary` (job state, else stored, else 404 `no_summary`), `DELETE .../summary` (Stop or dismiss, 204), `GET /api/summaries` (statuses). Job tokens link to `IHostApplicationLifetime.ApplicationStopping`. `LibraryEndpoints` removal goes through `jobs.RemoveAssociation` (cascade deletes the stored row). If a job for the pair appears between the early registry check and `Start` (a second POST racing), or finishes and is saved in that window, `Start` returns `AlreadyRunning` or `AlreadyFinished` and the POST answers `200` with that state.

**Client**: `Summary.fs` loses the NDJSON stream and the abandon-on-leave rule (FR-009 of F003 replaced): on `ReaderChanged` it requests the open document's state; `Summarize` posts and, on `202`/`200`, shows the state; a `Poll` effect while the open document is running; `Stop` and `Dismiss` send `DELETE`. Leaving while `Confirming` returns to idle and sends nothing (FR-006, unchanged rule). New `SummaryStatus.fs` holds the statuses list from `GET /api/summaries`, polled while any is running, and feeds markers in the document and project lists in `App.fs`.

## Project Structure

```text
specs/004-background-summaries/
├── plan.md  research.md  data-model.md  quickstart.md
├── contracts/summary-api.yaml
└── tasks.md                                   # next: /speckit-tasks

src/LexScholar.Domain/Summaries.fs                             # changed
src/LexScholar.Application/SummaryJobs.fs                      # new
src/LexScholar.Infrastructure/Migrations/DocumentSummaries.fs  # new
src/LexScholar.Infrastructure/{WorkspaceDb,Migrations/WorkspaceDbModelSnapshot}.fs  # changed
src/LexScholar.Infrastructure/SummaryRepository.fs             # new
src/LexScholar.Server/{SummaryEndpoints,LibraryEndpoints,Endpoints}.fs  # changed
src/LexScholar.Client/Summary.fs, App.fs                       # changed
src/LexScholar.Client/SummaryStatus.fs                         # new
tests/LexScholar.*.Tests/…                                     # per layer
docs/agents/codebase.md                                        # registry, table, polling
```

## Size check

About 12 vertical slices; `/speckit-tasks` produces the real list:

1. **Setup**: migration, row, schema test; Domain job types; `SummaryJobs` skeleton; routes stubbed per contract; client types; codebase.md.
2. **Riskiest assumption** (spec Assumptions): configured service, job detached from the request, `202`, `GET` state; client asks, switches document, returns, sees the summary — browser journey against `FakeChatService`. **If it fails, stop and re-plan with the owner.**
3. **Plan summaries in the background and sign-out**: plan job token from `Vault.BeginRun`, sign-out ends a job for a document she isn't viewing as `failed signed_out` (FR-007, SC-005). **Owner smoke check** on her ChatGPT plan: ask, switch, return, see the summary (recorded in the evidence).
4. Progress while running and on return; summary appears without asking again (FR-002).
5. Failures kept and shown on return with F003/F005 messages and actions (FR-004).
6. Finished summary stored; shown after restart; dismiss deletes (FR-009, SC-007). **Risky** (storage).
7. One job per document in a project; asking again shows the running one; several documents at once; same document in two projects is two summaries (FR-011, FR-005).
8. Stop: nothing more sent, nothing kept, race rule (FR-012, Accepted risk 3). **Risky** (concurrency).
9. Removal from the project stops the job and deletes the stored summary (FR-013). **Risky** (storage, concurrency).
10. Markers in document and project lists, updating while viewing another document (FR-008, SC-008).
11. Pre-send rules unchanged: refusals, confirmations, leaving during confirmation sends nothing — regression checks with zero fake requests (FR-006). Sensitivity check (behaviour exists).
12. Manual validation: SC-001–SC-008 in Firefox and Chromium on her real setup.

## Complexity Tracking

None. The in-memory registry is the minimum needed for FR-001 (work outlives the request) and FR-011 (one per pair).
