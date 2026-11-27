# Bridge integration ($16c)

Owner-approved scope: tooling-only bounded slices into **release/0.5.3** in
`thyvini/lexscholar`. Never main, never a release merge, never a bypass.
`scripts/integration-authorizations.json` records the approved branch/card/files
mapping. Labels, PR bodies, and policy edits on a PR head grant no authority.
No additional helper-script patterns are implicitly approved. Wildcards match
within one path component only, never across directory boundaries.

For later approved slices, submit a separate authorization PR on the already
authorized `chore/bridge-integration` branch, changing only the trusted
manifest. List each exact task branch, card and owned paths. Bridge validates
that PR against the *previous* release manifest; the added entry cannot authorize
that PR or any candidate until the authorization PR has passed the normal gates
and been integrated. Reuse the branch without force-pushing. Changes that the
current manifest cannot authorize require the documented human-reviewed
bootstrap, with the same CI and review gates; never load policy from a candidate.

## Bootstrap and invocation

Python 3, Git (with `merge-tree --write-tree`) and authenticated `gh` are required.
The initial Bridge PR needs the existing human-reviewed integration process:
Bridge cannot authorize its own bootstrap before its code/policy are on the
release branch. The lead owns CI wiring and the codebase-guide link.

Run a trusted copy, **not code from the candidate PR**:

```sh
git fetch --no-write-fetch-head origin release/0.5.3
gh api repos/thyvini/lexscholar/git/ref/heads/release/0.5.3 --jq .object.sha
# Replace <BASE> below with the independently returned full SHA.
git fetch --no-write-fetch-head origin <BASE>
mkdir -p ~/.local/state/lexscholar-team/bridge
chmod 700 ~/.local/state/lexscholar-team/bridge
git show <BASE>:scripts/integrate-pr.py > ~/.local/state/lexscholar-team/bridge/integrate-pr.py
python3 ~/.local/state/lexscholar-team/bridge/integrate-pr.py 123 --wait 600
# Only after integration is authorized:
python3 ~/.local/state/lexscholar-team/bridge/integrate-pr.py 123 --wait 600 --execute \
  2> ~/.local/state/lexscholar-team/bridge/pr-123.log
```

Use a canonical repository checkout as cwd. The script checks its own contents
against the release copy and reads policy from the fetched release object,
never the working tree. Runtime trust begins with the caller choosing this
release-owned script, not with a self-check in arbitrary untrusted code.

Default output is `validated-only`, **not a card receipt**. `--execute` is the
only mutation; it submits one squash request with an expected-head SHA. No
admin override, auto-merge, branch deletion, or Codecks API call exists.

## What blocks

- Wrong repo/release/source branch, closed/draft/non-clean PR, wrong conventional
  subject/card, empty diff, out-of-scope paths (including rename sources).
- GitHub file list differing from the actual fetched Git diff.
- Either named gate absent, duplicated, pending, failed, cancelled, wrong-app,
  wrong-head, future-dated, predating head commit, or older than 24 hours.
  App IDs are **15368** (`build-and-test`) and **5758986** (`gpt-review`).
  All runs are examined, not just GitHub's latest projection. Duplicate gate
  runs intentionally require investigation; rerunning blindly cannot fix ambiguity.
- GPT review additionally requires exactly one PR whose current head is that
  SHA, including closed PRs, and exactly one unedited reviewer-app comment on
  this PR matching the successful check's model, review text and timing. A
  shared head, missing or ambiguous evidence blocks; GitHub's check is attached
  to a commit, not intrinsically to a PR.
- Latest formal changes-requested review, any unresolved review thread, or
  P0/P1 issue comment other than the mandatory reviewer app's check-backed
  rounds. Greptile is optional: absence is fine; an existing blocker is not.
  A later ordinary review comment does not clear a changes-requested review.
- A changed head or release snapshot invalidates the attempt. A fresh full
  snapshot is checked immediately before mutation.
- GitHub may report the PR's older base snapshot after the release advances.
  Bridge accepts it only if Git proves it is an ancestor of the current
  independently fetched release; authorization, actual diff, merge tree and
  receipt still use that current release. A non-ancestor or unverifiable base
  blocks. This does not waive head, review, scope or check requirements.

Owner exceptions belong in the **trusted release manifest**, with exact
`pr`, `head`, `kind` (`review`, `thread`, `comment`), `id`, and nonblank `reason`.
Example: `{"pr":123,"head":"<40 hex>","kind":"comment","id":456,"reason":"Owner accepted ..."}`
They cannot override a gate, scope, repository, or branch failure.
Old P0/P1 issue comments are conservatively blocking until explicitly disposed;
a later prose reply is not automatically interpreted as permission.

Remote waiting is explicit (`--wait 0..1800`), prints blocked head/base/reason,
and rechecks at most every ten seconds. Each external command has a 60-second
timeout; pagination has a finite cap (100 pages; >100 review threads blocks).
The wait deadline bounds retry delays, not total network request duration.

## Merge verification, races and recovery

GitHub's squash API provides **head CAS, not base CAS**. Bridge checks release
SHA again immediately before the request and verifies the resulting commit has exactly that base
as its sole parent, the requested subject, and the independently computed Git
merge tree; release must still point at that result. Serialize Bridge usage
and do not run other release writers concurrently. An external base writer
can still win the API race: Bridge cannot undo the merge and will emit **no
receipt**. This is an API limitation, not an override mechanism: post-merge
verification detects a changed parent but cannot prevent or undo that merge.
Strict atomic base CAS would require a different integration mechanism.

Before mutation stderr prints `MERGE_INTENT {JSON}`. Preserve the log. Only
stdout JSON containing `"receipt": "verified-squash"` permits the lead to mark that exact card done.
No receipt means no Codecks completion, even if GitHub reports a merge.

If transport is uncertain, **do not retry the merge blindly**. Extract the JSON
after `MERGE_INTENT ` into an intent file and use read-only reconciliation:

```sh
python3 ~/.local/state/lexscholar-team/bridge/integrate-pr.py --recover ~/.local/state/lexscholar-team/bridge/intent.json
```

Recovery independently revalidates release ancestry/policy, PR source/head/files,
fresh gates and reviews, merge tree, parent and final release pointer. It never
resubmits the merge. If not merged, discard the intent and start a new normal
validation. Stale gates, a later release advance, policy/script mismatch or
wrong result block automated recovery: escalate to the owner with the saved
intent, PR, and observed release SHA. Never reset/force-push to repair uncertainty.

## Offline regression and evidence

```sh
python3 -m unittest discover -s tests/tooling -p test_integration.py
scripts/check.sh
```

RED: initial permissive validator produced 23 failing unsafe-input assertions
(plus missing-card error). Additional tree-verification regression failed with
`Blocked not raised` before tree checks were implemented. Regressions for a
comment following changes-requested and a wildcard crossing directories also
failed before their fixes. The focused base-change regression failed before
the final pre-request read was added;
the separate simulated in-request race checks that a changed parent gets no receipt.
GREEN: the focused
offline suite covers authorization, actual merge payload, dry-run, changed
head/base, exact exceptions, blocking review disposition, freshness/identity,
uncertain transport and verified result. All merge API calls in tests are mocks;
no unsafe or successful live merge was attempted. The fixture is synthetic,
contains no research data, and requires no token.

`scripts/check.sh` builds the .NET solution with `-warnaserror`; Python tests are
separate (the lead wires them into CI). Full-suite CI and posted GPT review are
required on this PR's final head before bootstrap integration.
