# Implementation plan: use the local workspace on Linux and Mac

**Planning branch:** `docs/platform-expansion-phase3` · **2026-11-26** · [One P1 story](spec.md). **Scoped implementation approved; design and verification gates remain open. No support claim yet.**

The owner approved scoped implementation for Linux x86-64 and macOS ARM64 while preserving shipped Linux support. Firefox and Chromium are the roadmap targets. The [owner's clarification](spec.md#clarifications) settles version wording: report only targets and versions actually verified on the candidate; do not infer a minimum or range. Sage recorded that rule and chose the 012 prefix and disposable fixture. No release or risk exception was approved.

## Summary

Keep the existing server, browser client and workspace format. Use a native build of the existing publish-directory distribution on each target. Exercise the extracted candidate, not a development server. Concentrate platform-specific changes at the two filesystem boundaries: exclusive directory ownership and removal of a managed original. Most application behavior should need **tests, not a port**. [Research and alternatives](research.md) distinguish proposed designs from capabilities not yet demonstrated.

## Technical context and structure

| Boundary | Existing components | Proposed change |
|---|---|---|
| Local application | F#/.NET 10 (`global.json`); ASP.NET Core hosts Bolero/Elmish WASM, PdfPig, EF Core/SQLite | Preserve routes, migrations, language, summary-first UI, consent and security. Use `src/LexScholar.Server/LexScholar.Server.fsproj` publish output including `wwwroot`. |
| Disk | `src/LexScholar.Infrastructure/{DataDirectoryLock,DocumentStore}.fs` | Keep Linux behavior. Select verified platform calls/flags locally; no general OS provider or fallback that weakens protection. |
| Native checks | `tests/LexScholar.{Infrastructure,Server}.Tests`; xUnit v3, MTP, temporary workspaces, child processes | Verify actual contention, restart, anchored removal and sync. Replace Linux-only test observations on Mac with equivalently sensitive observations. |
| Distribution and CI | `scripts/`, `tests/LexScholar.Browser.Tests`, `.github/workflows/ci.yml` | Preserve required Linux job behavior and furnish separately identifiable native results; launch candidate with its content root in both browsers. Record artifact digest, source revision and actual architecture. |
| User guidance | `docs/local-workspace.md`, `docs/product/vision-roadmap.md` | Distinguish shipped Linux guidance, candidate instructions and eventual evidence-backed support. Preserve current default workspace and overrides; no move to a new Mac-specific directory. |

Scale stays one researcher per local workspace; no new performance target, installer, packaging service, browser or distribution channel. [Data model](data-model.md) and [distribution contract](contracts/distribution.md) describe what remains stable. For operator steps see [quickstart](quickstart.md).

## Constitution check — before and after design

I/III: no new scholarly output, citation behavior or account flow. II: current controls and limits apply to both OSes; use private temporary fixtures, never real research or credentials in evidence. IV: link each gate to a native observable result and separate CI from owner smoke. V: one story; reuse existing process, journal and browser checks; no new repository layer. VI: requirements remain in Sage's spec; choices and alternatives are here and in research. **Design within boundary; verification gates open.** No amendment, exemption, hardware-failure guarantee or new risk acceptance.

## Handoff and gates

1. Start from a reproducible Linux archive, digest and unpacked launch; define the candidate input once. Keep regular Linux tests and existing Linux distribution usable throughout.
2. In independent files establish lock ownership, anchored removal, and failed-database-handle observation. Complete inherited-handle/restart tests after locking and crash recovery after both storage paths. Start the published UI check on Linux without waiting for Darwin.
3. Run the full solution and the candidate journey natively on both architectures, including both browsers. Require actual job success with zero skipped protections, not an optional Mac job. Perform owner smoke on the same Mac artifact.
4. Final guidance waits for **evidence**, not a version decision. Link each verified configuration to its candidate results; distinguish native CI coverage from the owner's physical Mac smoke. Do not combine OS and browser versions from separate runs into an untested configuration. If the candidate changes, repeat affected checks. Release remains separate.

Sage resolved the [SC-002 deletion boundary](spec.md#edge-cases). On both targets, changing the directory path must not redirect unlink or sync; an unrelated managed original and a fresh LexScholar reimport must survive; a storage error must leave the affected item incomplete. These are candidate evidence gates, not an open product decision. External substitution at the same key inside the opened directory is not covered by either protection; no guarantee or risk acceptance is implied.

Conductor: the remaining Darwin feasibility questions are in [research](research.md). Refer risk acceptance and publication to the owner; the v0.5.1 lock exception does not carry forward. The owner-approved feature permits ready slices in [tasks](tasks.md), subject to their feasibility, evidence, review and CI gates.
