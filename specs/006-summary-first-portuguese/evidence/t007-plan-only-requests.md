# T007 — Plan-only requests

- RED: `scripts/check.sh T007` — two new state assertions failed: both destinations did not send to the plan, and a configured service alone sent a request.
- GREEN: `scripts/check.sh T007` — build passed with warnings as errors; 156 tests passed, zero skipped, including Chromium and Firefox journeys.
- Full suite: CI on the PR (pending).

The journeys now use the fake ChatGPT plan for new requests, including held and cancelled background responses. The historical configured-service regression seeds a stored summary with a known host, model, time, covered and unreadable pages and omitted points, signs in, and checks the original context without plan attribution. The failure journey uses an unstructured fake response containing `service detail`, matching the old fake, so the assertion that this body is not displayed remains meaningful.

Deleted assertions: the signed-out explanation contains `LexScholar:Summary:Model`; the old state assertions that a configured-only destination sends to the configured service, that two destinations require choosing, that the selected choice is stored/dropped, and that changing the choice clears a failure and sends to the configured service; and the `NotConfigured` failure text mentions the settings file and `LexScholar:Summary:Endpoint`. These describe the removed destination choice or settings-file explanation. The former remote-service confirmation's host/model assertions now check the plan confirmation, while historical host/model assertions are retained in the stored-summary regression. Request counts, cancellation, context, error and citation assertions remain in browser journeys.
