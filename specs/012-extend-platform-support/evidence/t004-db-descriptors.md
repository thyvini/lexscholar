# T004 — failed SQLite initialization descriptors (partial)

- Linux observer sensitivity: `LOG=/tmp/opencode/f012-t004-red.log scripts/check.sh T004` — 52 tests, 1 failed, 0 skipped when the observer was deliberately changed to return no handles. The intentionally open disposable database was not detected. The mutation was removed.
- Linux GREEN: `LOG=/tmp/opencode/f012-t004-final.log scripts/check.sh T004` — warning-free Release build; 52 passed, 0 skipped.
- The test detects a deliberately held database descriptor before exercising corrupt initialization and retry. The macOS observer uses `lsof` on this process and canonicalizes the temporary directory; failed inspection fails the test.
- Native macOS execution and its observer sensitivity remain pending. This is not evidence of macOS support.
- Full-suite CI: pending.
