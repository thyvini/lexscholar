# T001 — Linux package baseline

- RED: `scripts/check.sh T001` — failed the assertion that the candidate package builder exists (45 tests, one failure, zero skipped).
- GREEN: `scripts/check.sh T001` — 46 tests passed, zero skipped. The Chromium test published a fresh Release archive, extracted it outside the checkout, started its host from the publish root with a disposable data directory, and created a project through the client. It also blocked requested framework assets while confirming the API still responded; the workspace did not become ready.
- An explicitly missing candidate archive is rejected rather than replaced with a locally built one. Set `LEXSCHOLAR_CANDIDATE_ARCHIVE` to the exact archive path to exercise a supplied candidate; without it the targeted test builds a local archive.
- CI full suite: pending PR. This Linux local result is not native CI, physical-machine validation, or a support claim for another platform.
