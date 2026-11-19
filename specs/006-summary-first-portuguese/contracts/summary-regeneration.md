# Summary replacement lifecycle (T024)

T024 defines the backend lifecycle; T025 adds the replacement-aware feedback client described below.

## Feedback client (T025)

- A saved summary offers a transient optional feedback field, bounded to 2,000 characters. It is serialized as the existing `instruction` field on `POST /regenerate`, not a new `feedback` property. Blank feedback uses the default summary. The existing validation, user-role treatment and forwarding through single/map/intermediate/final-combine stages apply unchanged.
- Every regeneration requires an explicit fresh confirmation naming the document/account and disclosing that feedback and extracted document text leave the machine. Confirmation cancel sends nothing and retains the draft. The confirmed request uses `destination: "chatgpt"` and `confirmSend: true`.
- The client first loads the durable version through base GET, then polls `GET /regenerate`. The old version stays visible during consent, submission, progress, failure and cancellation; only a finished response replaces it. Returning after navigation resumes this lifecycle. Drafts clear on document/project change and after admission; neither browser storage nor logs contain a separate copy.
- Stop is `POST /cancel` with antiforgery, never DELETE. If the POST is still in flight, the client also cancels after its eventual answer and waits for outstanding cancellation requests before allowing another replacement. This prevents a late cancel from deleting or stopping newly requested work. Explicit summary deletion remains a separate legacy operation.
- No instruction history, rating/flags, saved feedback metadata or automatic retry. Generated text may reflect feedback; this does not store the raw draft separately.

### Admission and uncertainty reconciliation

- Cancellation counts, pending-admission guards and uncertainty are pair-owned. An inactive pair's pending or completed cancellation cannot block or advance the open pair's lifecycle. A cancelled POST still drains against its original pair.
- Ignored/stale legacy summary responses do not initiate replacement discovery or change replacement/consent state. If an uncertain POST invalidates a pending durable fetch before it renders, cancellation completion first reloads the durable version, then performs replacement-aware reconciliation.

- Submission request identities survive document/project navigation. Returning while admission is pending cannot start another replacement, and the eventual POST response invalidates pre-admission discovery results.
- A POST transport failure may be a lost successful response. The client therefore fences pending admission with POST `/cancel` before allowing another submission. A failed/unknown cancellation remains unresolved across navigation: even a finished discovery response cannot prove a delayed admission has ended. Stop can retry the nondestructive fence; fresh generation stays unavailable until successful cancellation and reconciliation.
- After successful cancellation, GET `/regenerate` reconciles the actual saved state, including a replacement whose atomic completion beat cancellation. It is not routed through legacy fetch handling that ignores results while a saved summary is shown.
- Discovery of the same durable version preserves feedback typed while the GET was pending. Terminal replacement failures permit explicit, confirmed summary deletion; uncertain/running replacements do not.

Base: `/api/projects/{projectId}/documents/{documentId}/summary`.

| Method / suffix | Behavior |
|---|---|
| `POST /regenerate` | Explicit replacement of an existing saved summary. Same destination, local validation and fresh send-confirmation rules/body as ordinary POST. `202` starts replacement; `200` returns an already running job without sending again; `404 no_summary` if no saved summary remains at registration; `409 cancelled` if cancellation invalidated this request during preparation. |
| `GET /regenerate` | Replacement-aware polling: running/failed state, or the durable finished version after completion/cancellation/restart. Explicitly opt in here; legacy clients continue using the base GET. |
| `GET /saved` | `200` with the existing `finished` summary-state shape for the durable version, including during replacement and after failure; `404 no_summary` if none. |
| `POST /cancel` | `204`, idempotent. Invalidates all in-flight replacement preparations and drops/cancels the current transient job/failure but **never deletes** a saved summary, including if completion won the race. |

Ordinary POST/GET and status listing show the durable finished summary during replacement, including after a replacement fails. Ordinary POST bypasses sign-in/destination/consent checks when a durable summary exists. This isolates the legacy client's destructive Stop/Close actions from replacement jobs. Replacement-aware callers poll `GET /regenerate` instead. After success, ordinary routes show the new saved version. Destructive DELETE remains backward compatible: it cancels running work and pending replacement preparation and deletes the saved row. Association removal also invalidates preparation.

All unsafe routes retain Host/Origin and antiforgery enforcement. Replacement preparation registers a scoped admission before the first await; registration and cancellation check/invalidate it under the same registry lock. Thus an extractor that ignores cancellation or a pending plan refresh cannot register a job after cancellation. The admission is removed/disposed when the HTTP request ends; no persistent tombstones accumulate. Existing effective cancellation tokens, job-id checks and atomic repository updates are retained. Failure, cancellation, sign-out and shutdown preserve the old row. OAuth refresh may finish and save rotated credentials; a cancelled admission still cannot start model/summary calls.

Integration with T023: both ordinary and regeneration routes validate and forward the optional instruction through the same workflow and consent rules. Nonblank instructed calls discard service error prose before retaining failures; raw instructions are not separately stored or logged. Neither the durable summary nor stored metadata is modified at start. Feedback content/prompt composition and regeneration UI are deferred to the next slice.
