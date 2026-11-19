# Summary replacement lifecycle (T024)

Backend lifecycle only; no regeneration controls or feedback prompt are introduced here.

Base: `/api/projects/{projectId}/documents/{documentId}/summary`.

| Method / suffix | Behavior |
|---|---|
| `POST /regenerate` | Explicit replacement of an existing saved summary. Same destination, local validation and fresh send-confirmation rules/body as ordinary POST. `202` starts replacement; `200` returns an already running job without sending again; `404 no_summary` if no saved summary remains at registration. |
| `GET /saved` | `200` with the existing `finished` summary-state shape for the durable version, including during replacement and after failure; `404 no_summary` if none. |
| `POST /cancel` | `204`, idempotent. Drops/cancels the current transient job/failure but **never deletes** a saved summary, including if completion won the race. |

Ordinary POST/GET, status listing and destructive DELETE are unchanged. GET on the base reports replacement progress/failure while the durable version remains separately available through `/saved`. After success, base GET returns the new saved version. After cancellation/restart, it returns the durable version.

All unsafe routes retain Host/Origin and antiforgery enforcement. Replacement uses the same per-pair lock, effective cancellation token and job-id checks as ordinary generation. It never deletes the old row before saving: only successful validated completion performs the atomic repository update. Failure, cancellation, sign-out and shutdown preserve the old row. Explicit DELETE and association removal remain destructive.

Integration with T023: forward the parsed request instructions through both ordinary and regeneration routes; neither the durable summary nor stored metadata is modified at start. Feedback content/prompt composition and UI are deferred to the next slice.
