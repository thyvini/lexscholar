# Candidate verification guide — not a support announcement

**Status:** procedure for future execution. No candidate or physical Mac result yet. Use only a fresh private directory, a clean browser profile and `tests/Fixtures/valid.pdf`. No real PDFs, accounts or external processing. Keep the export outside the workspace and publish root.

1. Builder on each native machine: identify source revision, OS/build, `uname -m`, `dotnet --info`, pinned SDK (`global.json`) and browser builds. Restore and publish `src/LexScholar.Server/LexScholar.Server.fsproj` in Release with warnings as errors; archive the **whole** clean output, including `wwwroot` and native runtime assets. Calculate SHA-256 of archive and fixture. In CI run the full solution with native Chromium and Firefox and retain counts (zero skips). Targeted test work uses `scripts/check.sh <task IDs>`; no full suite is required for this planning change.
2. Tester: verify archive digest; extract to a fresh directory on the matching architecture. Confirm the native ASP.NET Core 10 runtime is installed (`dotnet --list-runtimes`). From the extracted directory, **not** from the repository, run in a dedicated terminal:

   ```sh
   export LexScholar__DataDirectory="$(mktemp -d)"
   printf 'Disposable workspace: %s\n' "$LexScholar__DataDirectory"
   dotnet LexScholar.Server.dll
   ```

   Reuse that exact path on restart. Visit `http://127.0.0.1:5000` (or the configured loopback port). The app must render the workspace and accept input, not just return HTML. Skip optional setup/sign-in, create a project, import and associate the fixture. Open it: summary first, then **Abrir PDF**; check opened/downloaded original bytes against the fixture. State what the browser actually displayed; a download doesn't prove native viewer rendering.
3. While the first process owns the directory, run from the same extracted root in another terminal with the **same absolute data directory** and `--LexScholar:Port=0`. Expect nonzero refusal for directory in use, not address in use. Verify the original remains usable. Stop normally; restart with the same directory and candidate. Verify the project, association and identical original. Automated process tests also terminate the owner abruptly after confirmation and reopen without lock-file surgery.
4. Export from the library, save the ZIP outside the data directory and compare the archived original with the fixture (`cmp` on extracted copies is sufficient). Confirm **library deletion**, not just project removal. Require completed result. Restart; no deleted document or association returns; the exported bytes still match. Do not interpret export as restore or deletion as secure erase.
5. Run independently in both browsers per target using clean disposable workspaces. Store source SHA, archive digest, native machine/OS, runtime and browser versions, steps, expected/actual outcomes, failure or limitation and test run IDs. Keep the CI evidence separate from the **owner's** physical Apple Silicon smoke for scenarios 1–3 on that candidate. No real document content, tokens or user-identifying paths in evidence.

Failures of contention, native loading, directory sync, handle observation, browser startup, missing assets, skips, or missing owner evidence keep the corresponding criterion open. In launch guidance and CI coverage, name only target/version combinations that passed on the candidate. Cite the run for each; label the physical Mac smoke separately. Do not give a minimum or range or extrapolate to another OS or browser version. Repeat affected checks if the candidate changes.
