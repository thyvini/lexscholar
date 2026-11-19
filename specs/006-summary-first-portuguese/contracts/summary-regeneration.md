# Summary replacement lifecycle (T024)

Backend lifecycle only; no regeneration controls or feedback prompt are introduced here.

Base: `/api/projects/{projectId}/documents/{documentId}/summary`.

| Method / suffix | Behavior |
|---|---|
| `POST /regenerate` | Explicit replacement of an existing saved summary. Same destination, local validation and fresh send-confirmation rules/body as ordinary POST. `202` starts replacement; `200` returns an already running job without sending again; `404 no_summary` if no saved summary remains at registration; `409 cancelled` if cancellation invalidated this request during preparation. |
| `GET /regenerate` | Replacement-aware polling: running/failed state, or the durable finished version after completion/cancellation/restart. Explicitly opt in here; legacy clients continue using the base GET. |
| `GET /saved` | `200` with the existing `finished` summary-state shape for the durable version, including during replacement and after failure; `404 no_summary` if none. |
| `POST /cancel` | `204`, idempotent. Invalidates all in-flight replacement preparations and drops/cancels the current transient job/failure but **never deletes** a saved summary, including if completion won the race. |

Ordinary POST/GET and status listing show the durable finished summary during replacement, including after a replacement fails. Ordinary POST bypasses sign-in/destination/consent checks when a durable summary exists. This isolates the legacy client's destructive Stop/Close actions from replacement jobs. Replacement-aware callers poll `GET /regenerate` instead. After success, ordinary routes show the new saved version. Destructive DELETE remains backward compatible: it cancels running work and pending replacement preparation and deletes the saved row. Association removal also invalidates preparation.

All unsafe routes retain Host/Origin and antiforgery enforcement. Replacement preparation registers a scoped admission before the first await; registration and cancellation check/invalidate it under the same registry lock. Thus an extractor that ignores cancellation or a pending plan refresh cannot register a job after cancellation. The admission is removed/disposed when the HTTP request ends; no persistent tombstones accumulate. Existing effective cancellation tokens, job-id checks and atomic repository updates are retained. Failure, cancellation, sign-out and shutdown preserve the old row. OAuth refresh may finish and save rotated credentials; a cancelled admission still cannot start model/summary calls.

Integration with T023: forward the parsed request instructions through both ordinary and regeneration routes; neither the durable summary nor stored metadata is modified at start. Feedback content/prompt composition and UI are deferred to the next slice.
