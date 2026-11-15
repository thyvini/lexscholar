# Research: F004 Background Summaries

Decisions for [plan.md](plan.md). F003 (`specs/003-document-summary/research.md`) and F005 decisions not mentioned here are unchanged.

## 1. Who owns the work

- **Decision**: an in-memory `SummaryJobs` registry in Application, created once in `Endpoints.fs` and shared by `SummaryEndpoints` and `LibraryEndpoints`. A job runs on a task started by the POST after all checks; its token is its own `CancellationTokenSource`, linked to `ApplicationStopping`, never to `RequestAborted`.
- **Rationale**: FR-001 needs the work to outlive the request; one researcher and one process (accepted risk 1) need nothing more. Matches F005's `Vault`/`SignInSession`: small classes holding the only mutable state, with a lock.
- **Alternatives**: `IHostedService` with a `Channel` queue — adds a queue and a worker for no requirement (jobs run concurrently, FR-011); persisted job table — needed only to resume after restart, which the spec rules out; client-held stream kept alive across views — the client view is not the problem, a project switch or reload would still lose it.

## 2. Sign-out reaches background plan jobs

- **Decision**: reuse F005's `Vault.BeginRun(outer)`, passing the job's token as `outer` (today it is `RequestAborted`). `prepareSend` still runs inside the POST under the vault lock, so confirmation, refresh and run registration stay one step (FR-006, FR-007). The POST's own `RequestAborted` covers only `prepareSend`; the run token covers the job.
- **Rationale**: one registry of plan runs; `signOut`, `endSignIn` and terminal refresh errors already cancel it.
- **Alternatives**: a second registry in `SummaryJobs` that sign-out walks — duplicates `EndRuns` and its locking.
- **Consequence**: `prepareSend` needs separate tokens for "the request" and "the run's outer token" (small signature change, covered by F005's existing tests).

## 3. Stop, finish and removal races

- **Decision**: every job has an id. `Complete`, `Fail` and `Progress` change state only if the registry still holds that id for its key, and `Complete` saves to the database inside the registry lock. `Stop` (and removal) take the same lock, cancel the token and remove the job; if a stored summary exists afterwards, `DELETE` removes it too.
- **Service calls go through the registry**: the job's `Complete` capability is wrapped so each call starts inside the registry lock, after checking that the job id is still current and its token not cancelled; Stop cancels inside the same lock. A token check followed by an unlocked call would leave a window where Stop returns and the call still starts.
- **Removal**: `RemoveAssociation` stops the job and deletes the association in one locked step, and `Start` checks the association inside that lock, so a POST that passed its early checks cannot start a job for a pair that was just removed (it gets `not_found`).
- **Second POST**: `Start` checks, under the lock, for a running job and then for a stored summary (saving also happens under the lock), so a job that finished between the POST's early check and `Start` is returned, never duplicated (FR-011).
- **Guarantees**: after Stop (or removal) returns, no new service call starts, and a call already in flight is aborted by the cancelled token; a stopped job can never be saved. If finishing won the race, Stop deletes the stored row: nothing stopped is ever shown (accepted risk 3).
- **Alternatives**: optimistic "saved then check" — leaves a window where a stopped summary is visible.

## 4. Markers and state without reloading (FR-008, FR-002)

- **Decision**: **client polling**. `GET /api/summaries` returns `[{projectId, documentId, status}]`; the client polls it every 2 s while any listed status is `running` (and once at start, after a POST, and after a DELETE). While the open document's job is running, the same tick fetches `GET .../summary` for its progress. Elmish timer effect (`Cmd.OfAsync` with `Task.Delay` → `Tick`), stopped when nothing runs.
- **Rationale**: simplest with the existing request/response `Api` and Elmish effects; no new protocol, packages or long-lived connections; stateless on the server beyond the registry. The no-polling rule in AGENTS.md is about tests: tests wait with handshakes and Playwright's auto-waiting assertions, never on the app's interval.
- **Alternatives**:
  - Keep NDJSON streaming, re-attached on return (`GET` streaming per job) — needs fan-out of events to several subscribers, still needs a list endpoint for markers, and every switch must cancel and reopen streams. More code than polling.
  - Server-Sent Events channel for all statuses — one long-lived connection, but needs a broadcaster with subscriber lifetimes and reconnect handling in the WASM client.
  - SignalR / WebSockets — new package and hub, overkill for one user.
- **Consequence**: F003's NDJSON streaming in the summary POST and the client's streaming fetch are removed; progress comes from the job state.

## 5. Storing a finished summary

- **Decision**: table `DocumentSummaries (ProjectId, DocumentId, Content TEXT, GeneratedAt TEXT)`, primary key (ProjectId, DocumentId), `FOREIGN KEY (ProjectId, DocumentId) REFERENCES ProjectDocuments ON DELETE CASCADE`. `Content` is the JSON of F003's summary object (points, pagesCovered, unreadablePages, omittedPoints, serviceHost, model), versioned with a `"version": 1` field. Handwritten migration as in F001.
- **Rationale**: the pair key matches the route and FR-011; cascade makes removal from the project delete it without extra code paths (FR-013); JSON keeps the shape identical to what the client already renders and is trivially exportable by F002 (one row = one exportable record with its project and document ids). Full workspace deletion removes it with its association.
- **Alternatives**: normalized `SummaryPoints` table — no query needs points individually; files under the data directory — would need their own cleanup and export path.
- **Check**: EF Core turns `PRAGMA foreign_keys` on for its SQLite connections (F001 relies on RESTRICT), so the cascade works; T001's schema test asserts it.

## 6. Failures

- **Decision**: a failure is the job's terminal state in memory (code, detail, omitted count — the same fields as F003/F005's `failed` event) until dismissed, retried (a new POST replaces it) or the app restarts.
- **Rationale**: FR-004 asks to show it on return; FR-009 stores only finished summaries.

## 7. Contract changes

- POST `.../summary`: after the association check, a running or finished summary for the pair is returned (`200`) before any destination, confirmation or sign-in check, so asking again never meets a confirmation or a sign-in error (FR-011). Otherwise same checks, same 4xx/409 answers; success is `202` (`SummaryState`, running) instead of a `200` NDJSON stream. Plan-path outcomes that F005 sent as an immediate `failed` stream (`signed_out` from `SignInEnded`, `service_failed` from `RefreshUnreachable`) become a job created directly in the failed state, so the client shows them through the same path.
- New: `GET .../summary`, `DELETE .../summary`, `GET /api/summaries`. See [contracts/summary-api.yaml](contracts/summary-api.yaml).

## 8. Testing the riskiest assumption first

- **Decision**: T002 is a Playwright journey (Chromium, Firefox) with `FakeChatService` holding its answer behind a handshake: ask on A, open B, release the fake, open A, expect the summary. Plus a contract test: the POST returns `202` before the fake answers, and closing the POST's connection does not cancel the fake. If either can't be made to pass with the registry design, stop and return to the owner.
- **Owner smoke check** in T003 (first slice that runs a background summary through her ChatGPT plan): ask, switch, return on her real plan; result recorded in the evidence.
