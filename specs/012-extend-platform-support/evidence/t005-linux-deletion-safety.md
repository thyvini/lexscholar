# T005 — partial Linux deletion safety

- Linux-only sensitivity: `LOG=/tmp/opencode/f012-unlink-sensitivity.log scripts/check.sh T005` — 65 tests, 1 failed, 0 skipped when unlink used the absolute pathname instead of the opened directory; the replacement-directory regression detected it.
- Linux-only sensitivity: `LOG=/tmp/opencode/f012-sync-sensitivity.log scripts/check.sh T005` — 65 tests, 1 failed, 0 skipped when sync reopened the unavailable pathname instead of using the opened directory.
- Reimport sensitivity: `LOG=/tmp/opencode/f012-reimport-sensitivity.log scripts/check.sh T015` — 15 tests, 2 failed, 0 skipped after deliberate post-deletion corruption of the fresh original. The temporary mutation was removed.
- GREEN: `LOG=/tmp/opencode/f012-linux-final.log scripts/check.sh T005 T013 T015` — warning-free build, 130 tests passed, 0 skipped. Includes existing failure, retry and reimport coverage.
- Darwin candidate Linux regression: `LOG=/tmp/opencode/f012-t005-linux.log scripts/check.sh T005 T013 T015` — warning-free build, 130 passed, 0 skipped.
- Full-suite CI and candidate-native Darwin verification: pending PR.

These checks do not establish protection against substitution of the same key inside the opened directory. T005 remains open pending native gates.
