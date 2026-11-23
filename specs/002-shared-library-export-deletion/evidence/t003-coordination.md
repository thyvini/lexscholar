# T003 — Library operation coordination

- RED: `scripts/check.sh T003` — 80 tests, 1 failed: summary persistence did not enter the workspace mutation boundary.
- GREEN: `scripts/check.sh T003` — 83 tests passed, zero skipped, after the host import handshake and lease checks.
- Pre-PR: `scripts/check.sh T003 T009 T021 T024` — 229 tests passed, zero skipped.
- Full CI: pending PR run.

Tests use in-memory capabilities and an isolated host workspace with a fixture PDF. The host test holds the same file lease resolved from the host, waits for the injected real PDF validation to finish, confirms the import has not committed, then releases the lease and verifies import completes. A second host-workspace test saves a summary through the registry into SQLite while a simulated deletion waits on the file lease, then removes an association under the mutation and registry boundaries. Application tests cover lease cancellation and release, summary admission and persistence under the mutation boundary, and summary completion while a simulated deletion waits for an export lease. The checked lock order is workspace mutation before the summary registry and SQLite; no asynchronous file lease is acquired under the registry lock. This does not implement or prove actual library deletion, delayed admission fencing or restart recovery; those remain later slices.
