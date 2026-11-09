# LexScholar

Local, single-researcher workspace for research projects and a shared PDF library. F# on .NET 10: ASP.NET Core host (`src/LexScholar.Server`) serving a Bolero/Elmish WebAssembly client (`src/LexScholar.Client`), SQLite via EF Core, PDF validation via PdfPig. Linux with Firefox and Chromium is the supported platform.

Developed with Spec Kit. The active feature is F001 in `specs/001-research-workspace/`: `spec.md` (requirements), `plan.md` (architecture), `research.md` (technology decisions), `tasks.md` (task list), `quickstart.md` (validation guide), `contracts/http-api.yaml` (HTTP contract). `.specify/memory/constitution.md` governs all work.

## Commands

Run from the repository root. The SDK is pinned in `global.json`; tests use Microsoft.Testing.Platform, so use `--project`/`--solution` and `--filter-trait` rather than VSTest options.

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release                  # full suite
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj --configuration Release --filter-trait 'Task=T018'   # one task
dotnet run --project src/LexScholar.Server                                      # local host on 127.0.0.1:5000
```

Browser tests (`tests/LexScholar.Browser.Tests`, trait `Category=Browser`) use Playwright's own Chromium and Firefox builds. Install them once per machine (cached in `~/.cache/ms-playwright`):

```sh
scripts/install-browsers.sh
```

The full suite includes the browser tests, and they **fail** (not skip) when the browsers are not installed. To run everything else, exclude them with `--filter-not-trait 'Category=Browser'`.

## Workflow

- Work one task from `tasks.md` at a time, on its own branch. Branch from and open PRs against the active release branch (currently `release/0.1.0`), not `main`.
- Plan first and wait for approval. Keep changes to what the task asks for; record new technology choices in `research.md` with rationale and alternatives (constitution VI).
- Test first: capture a meaningful RED (failing assertions, not just a compile error), then GREEN. Tag tests with `Trait("Task", "Txxx")`.
- Record verification in `specs/001-research-workspace/evidence/txxx-<topic>.md`: commands run, RED and GREEN results, test counts, environment details, and limitations. Follow the existing evidence files. Then tick the task in `tasks.md` with a link to the evidence.
- Before finishing: restore and build with `-warnaserror` (zero warnings), and the full suite passes with zero skips.
- Commits: Conventional Commits with the feature scope and task IDs, e.g. `feat(f001): implement project management UI (T018)`, `test(f001): …`, `chore(f001): …`. PR descriptions have Summary and Verification sections and end with "Do not merge until reviewed."

## Code conventions

- Functional-first F# (see `plan.md`, "Functional-first F# design"): immutable records, unions, and pure domain rules in `LexScholar.Domain`; workflows in `LexScholar.Application` that take only the capabilities they need; EF Core, filesystem and PDF adapters in `LexScholar.Infrastructure`. Avoid abstractions without a demonstrated need.
- Compile order matters: new `.fs` files must be added to the `.fsproj` in dependency order.
- Package versions are pinned centrally in `Directory.Packages.props`.
- Host security is deliberate: loopback-only binding, Host/Origin checks, and antiforgery tokens (`GET /api/antiforgery`, then the `X-CSRF-TOKEN` header) on every unsafe request. Do not weaken these for convenience.

## Tests

- xUnit v3. Every test that touches storage uses an isolated temporary workspace (`TemporaryWorkspace` in `tests/LexScholar.Server.Tests/TestHarness.fs`), never the developer's data directory.
- `TestHarness` runs an in-process host (`serveClient = true` also serves the WASM client). `ProcessHarness` runs a real child process for process-lock and startup tests.
- No sleeps or polling for readiness: use handshakes, awaited lifecycles, or Playwright's auto-waiting assertions.
- Fixtures live in `tests/Fixtures/`. Never commit real research PDFs or log their contents.
