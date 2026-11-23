# T002 — Consistent export snapshot

- RED: `scripts/check.sh T002` — 73 tests, 1 failed: independent reads observed a role changed between reads.
- GREEN / pre-PR: `scripts/check.sh T002` — 74 passed, zero skipped.
- Full CI: pending PR run.

The test uses an isolated temporary workspace and confirms SQLite reports `wal` journal mode. A second connection synchronously commits document, association, role and stored-summary changes while the read transaction is held; completion before the snapshot callback returns is the handshake proving writer progress. A stopwatch measures the commit interval in the test; no latency limit is asserted. The held snapshot retains the old state, including an unassociated document and the old summary; a new capture sees the committed state. An unknown stored-summary version is rejected. This proves database snapshot behavior, not filesystem coordination or power-loss behavior.
