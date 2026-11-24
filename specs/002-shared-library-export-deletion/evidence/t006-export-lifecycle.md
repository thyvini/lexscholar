# T006 — complete exports and private file lifecycle

Environment: Linux x64, pinned .NET 10 SDK, isolated `TemporaryWorkspace` per test. Export remains internal/test-only; production composition has no export route. No new package or technology.

## RED / GREEN

- RED: `scripts/check.sh T006` — 61 tests, 3 assertion failures, zero skips: missing/wrong-length responses lacked document identity; restart retained abandoned exports. Log `/tmp/lexscholar-check.3z2Epo`.
- Additional fault coverage: deterministic `ExportFiles` source-open/read/EOF and archive-create/write faults; a disk-full fault is injected after opening the PDF so it cannot be mistaken for a source failure.
- Sensitivity: temporarily enlarged the copy buffer, disabled copied-length checks, and suppressed document-specific failures. `scripts/check.sh T006` — 71 tests, 8 assertion failures, zero skips (oversized reads, short/overlong successful ZIPs and incorrect error statuses). Mutations reverted. Log `/tmp/lexscholar-check.h7SOob`.
- GREEN: `scripts/check.sh T006 T004 T005` — 186 tests, zero failures, zero skips. Builds with `-warnaserror`; only task-selected projects/tests, not the full suite.
- CI full suite: pending coordinator PR; no push/PR authorized for this worktree.

## Coverage and limitations

- Real missing and wrong-length files; injected unreadable/open failure, read failure and length changes during copying. Responses identify only document UUID/display name; no injected paths leak.
- Archive creation and simulated disk-full writes return `500 export_failed`, never a partial successful ZIP. All staged files removed.
- One-gigabyte declared source uses bounded reads (at most 81,920 bytes), cancellation after three reads, and disposes source/archive. No giant source buffer or full ZIP in memory; metadata still scales with library metadata.
- Real file-backed completed ZIP, permissions, rewind, and disposal deletion. Real HTTP transfer held after its first chunk: client disconnect cancels server read and disposes staging. Injected transfer read failure cannot yield a completed client download.
- Restart through `Program.create` removes abandoned archive staging while retaining original/library metadata. This simulates an interrupted leftover, not hardware power loss or forced process termination.
- A failed export leaves an actual registered running regeneration token uncancelled, its stored version unchanged, and its project association/library row unchanged.
- Disk full and unreadability use deterministic injected I/O faults, not actual disk exhaustion or user-dependent chmod behavior. Concurrent export/deletion/import barriers are T007; browser download feedback is T008. Same-length external changes remain the documented availability limitation.

## PR #148 review follow-up — inaccessible leftovers

- GPT P1 verdict: **must fix**. Export enumeration/deletion errors escaped startup, making an otherwise valid workspace unavailable on every restart.
- RED: `scripts/check.sh T006` — 74 tests, 2 assertion failures, zero skips. Real Linux directory permissions denied enumeration (0100) or unlink (0500); tests first proved the permission failures, then asserted startup must not throw. Log `/tmp/lexscholar-check.DFRzjE`.
- GREEN: `scripts/check.sh T006 T022` — 87 tests, zero failures, zero skips; warnings-as-errors build. Covers both repeated restarts, unchanged permissions/private leftovers/library/originals, successful cleanup on a later permitted restart, per-entry continuation, and existing F001 cleanup tests.
- Integrated merged T010 (`cc6cb00`) from `release/0.5.0`. Final `scripts/check.sh T006 T022 T010` — 163 tests, zero failures, zero skips; warnings-as-errors build. The first integrated invocation exceeded the tool's 120-second limit; rerunning with a 300-second limit passed.
- Cleanup now catches only filesystem I/O/access failures in export leftovers, leaves unsuccessful entries untouched, reports `ExportCleanupIncomplete`, and logs one fixed warning with no paths, filenames or exception text. No permission changes, new public endpoint, or change to import/original cleanup policy. Tests run as ordinary Linux UID 1000; no permission-sensitive tests were skipped.
