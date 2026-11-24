# F002 validation quickstart

Owner manual validation is recorded in [T018 evidence](evidence/t018-owner-validation.md).
Use Linux, Firefox and Chromium, synthetic PDFs from `tests/Fixtures/` only, and a
new isolated workspace for each browser. Never point these steps at your research
workspace. No real account or external summary service is needed.

## Automated checks

```sh
scripts/install-browsers.sh # only if the bundled test browsers are missing
env TMPDIR=/tmp/opencode scripts/check.sh T018
```

This builds with `-warnaserror` and runs task-tagged security/privacy tests and
browser journeys. For targeted deterministic failure/recovery/cancellation proofs,
use `scripts/check.sh T006 T011 T014 T016 T017` as needed; these are not manual
acceptance. CI runs the full suite. See [T018 evidence](evidence/t018-owner-validation.md)
for sensitivity checks, scope and pending evidence.

## Isolated manual setup (repeat for each browser)

From the repository root, keep this shell and its `F002_DATA` value for the whole
session:

```sh
mkdir -p /tmp/opencode
export F002_DATA="$(mktemp -d /tmp/opencode/f002-owner.XXXXXX)"
export LexScholar__DataDirectory="$F002_DATA"
export LexScholar__Port=5000
dotnet run --project src/LexScholar.Server
```

Open `http://127.0.0.1:5000` in the browser being validated, skip optional sign-in,
and create two projects. Import `tests/Fixtures/valid.pdf` and `distinct.pdf`
(not `valid-renamed.pdf`, which deduplicates). Associate `valid.pdf` to both
projects with different roles; leave `distinct.pdf` unassociated.

Stop the host with Ctrl-C. Seed deterministic stored summaries in **this fixture
workspace only**, without sending anything externally:

```sh
python3 -c '
import datetime, json, os, pathlib, sqlite3
root = pathlib.Path(os.environ["F002_DATA"]).resolve()
assert root.parent == pathlib.Path("/tmp/opencode") and root.name.startswith("f002-owner.")
with sqlite3.connect(root / "workspace.sqlite") as db:
    db.execute("PRAGMA foreign_keys=ON")
    pairs = db.execute("""SELECT p.ProjectId, p.DocumentId
        FROM ProjectDocuments p JOIN LibraryDocuments d ON d.Id=p.DocumentId
        WHERE d.OriginalFileName=?""", ("valid.pdf",)).fetchall()
    assert len(pairs) == 2, "Create the two fixture associations first"
    for index, (project, document) in enumerate(pairs, 1):
        content = dict(version=1, points=[dict(text=f"Ponto sintético do projeto {index}", pages=[1,2])],
                       pagesCovered=[1,2], unreadablePages=[], omittedPoints=0,
                       serviceHost="local-fixture", model="manual-fixture")
        db.execute("""INSERT OR REPLACE INTO DocumentSummaries
            (ProjectId, DocumentId, Content, GeneratedAt) VALUES (?,?,?,?)""",
            (project, document, json.dumps(content),
             datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f") + "0Z"))
'
dotnet run --project src/LexScholar.Server
```

## Owner checklist (Firefox and Chromium separately)

1. **Export and citations.** Export the library and save the ZIP outside the data
   directory. Inspect with a standard ZIP tool and text editor, not LexScholar.
   Compare original PDF bytes (`sha256sum`), project/role associations, distinct
   summary points, one-based citations `[1,2]`, source paths, generation context
   and generated/unverified notice against [the format](contracts/export-format.md).
   Open the original PDF and check cited pages manually. Confirm the UI explains
   included material, no in-app restore, and no credentials/settings. Keep the ZIP:
   later deletion must not remove this external copy.
2. **Cancellation.** Select shared and unassociated documents, inspect the named
   projects, roles, two summaries and external-copy/backup/prior-send caveats.
   Cancel. Verify both PDFs, associations and saved summaries still exist; export
   again if useful. This is cancellation *before confirmation*, not an undo after
   admission. F001 “remove from project” must remain distinct from library deletion.
3. **Changed impact.** Preview again; in another tab change a role. Confirm in the
   first tab: refreshed impact must require a new confirmation, not delete silently.
   Cancel again before the controlled failure below.
4. **Partial failure and restart.** With only the two fixture documents selected,
   deny unlink of the shared original while making the unassociated original
   provably missing. In a second shell use the exact `F002_DATA` printed by the
   setup shell (`printf '%s\n' "$F002_DATA"`), then:

   ```sh
   # In this second shell, replace the example with the setup shell's printed path.
   export F002_DATA='/tmp/opencode/f002-owner.REPLACE_WITH_ACTUAL_SUFFIX'
   # Fixture workspace only. The guard rejects any other data directory.
   python3 -c '
   import os, pathlib, sqlite3
   root = pathlib.Path(os.environ["F002_DATA"]).resolve()
   assert root.parent == pathlib.Path("/tmp/opencode") and root.name.startswith("f002-owner.")
   with sqlite3.connect(root / "workspace.sqlite") as db:
       key, = db.execute("SELECT StorageKey FROM LibraryDocuments WHERE OriginalFileName=?", ("distinct.pdf",)).fetchone()
   assert len(key) == 32 and all(c in "0123456789abcdefABCDEF" for c in key)
   (root / "originals" / key).unlink()
   (root / "originals").chmod(0o500)
   '
   ```

   As a normal non-root user, preview and confirm both. Expect a partial result:
   missing original deleted, protected original not completed; no false complete.
   Reload and restart the host with permissions unchanged: the durable partial
   result remains, not a successful deletion or quarantined pending file. Restore
   `chmod 700 "$F002_DATA/originals"` and use Retry (or restart for automatic
   recovery). Expect complete, no managed originals, associations or summaries,
   and the retained external ZIP unchanged. Acknowledge only after completion.
   Do not run this as root: it defeats the permission fault.
5. **Privacy and failed transfer.** Inspect browser Network activity during export,
   preview/cancel, confirm, retry and acknowledge: workspace-origin requests only.
   Inspect errors and operation-time server logs: no PDF/summary content, credentials,
   or private storage paths. Interrupt a download using browser offline/request
   blocking; an incomplete transfer must not be described as a completed export.
   Retry online. A browser may retain an incomplete external file; LexScholar cannot
   erase it. Use fresh fixtures if prior steps removed the library.

Races, lost-response reconciliation, summary preparation/job cancellation, disk-full,
read faults and process interruption at individual journal boundaries use the
deterministic tests above, not timing sleeps or production fault switches. Do not
claim these were manually exercised merely because the tests passed.

Record browser versions, Linux/runtime, commit, fixture names, steps/observations,
any limitation/failure and the full-suite CI URL in
[T018 evidence](evidence/t018-owner-validation.md). **Only the owner can record
manual acceptance and authorize checking T018.** Stop the host before removing
the disposable workspace; restore directory write permission first.
