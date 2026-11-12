# LexScholar

Local, single-researcher workspace for research projects and a shared PDF library. F# on .NET 10: ASP.NET Core host (`src/LexScholar.Server`) serving a Bolero/Elmish WebAssembly client (`src/LexScholar.Client`), SQLite via EF Core, PDF validation via PdfPig. Linux with Firefox and Chromium is the supported platform.

These instructions apply to every agent and harness. `CLAUDE.md` imports this file and adds Claude Code specifics; OpenCode and Codex read this file directly.

**Start with [`docs/agents/codebase.md`](docs/agents/codebase.md)**: the code map, patterns, recipes and test harnesses. Read it instead of exploring the repository, and update it when you add a module or pattern.

Developed with Spec Kit. Each feature lives in `specs/<nnn>-<name>/`: `spec.md` (requirements), `plan.md` (architecture), `research.md` (technology decisions), `tasks.md` (task list), `quickstart.md` (validation guide), `contracts/` (HTTP contract), `evidence/` (verification records). F001 (`specs/001-research-workspace/`) is complete. `.specify/memory/constitution.md` governs all work.

## Commands

Run from the repository root. The SDK is pinned in `global.json`; tests use Microsoft.Testing.Platform, so use `--project`/`--solution` and `--filter-trait` rather than VSTest options.

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release                  # full suite
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj --configuration Release --filter-trait 'Task=T018'   # one task
dotnet run --project src/LexScholar.Server                                      # local host on 127.0.0.1:5000
dotnet publish src/LexScholar.Server/LexScholar.Server.fsproj -c Release -o <dir> --no-restore -warnaserror
```

The published app serves the client from `wwwroot` under its content root, which defaults to the current directory. Start it from the publish directory (`cd <dir> && dotnet LexScholar.Server.dll`) or pass `--contentRoot <dir>`. Started anywhere else, the API still works but every client asset returns 404.

Browser tests (`tests/LexScholar.Browser.Tests`, trait `Category=Browser`) use Playwright's own Chromium and Firefox builds. Install them once per machine (cached in `~/.cache/ms-playwright`) with `scripts/install-browsers.sh`. They **fail** (not skip) when the browsers are missing; to run everything else, use `--filter-not-trait 'Category=Browser'`.

CI (`.github/workflows/ci.yml`) runs restore, build and the full suite, browsers included, on every PR and push to `main` and `release/**`.

### Running builds and tests as an agent

Use `scripts/check.sh`, not the raw commands: it prints only build errors, failed tests and a one-line summary, and keeps the full output in a log file.

- `scripts/check.sh` builds; `scripts/check.sh T012 [T013 ...]` builds and runs only the tests tagged with those tasks. Don't run the full suite locally (`--all`) unless asked: CI runs it on every PR.
- Run tests only when they can tell you something new: at RED, at GREEN, and once before opening the PR. Not after changes to docs, evidence or `tasks.md` only, and not again when nothing changed since the last green run.
- On failure, work from the reported failures. Open the log only if they aren't enough, and read the part you need (`tail`, `grep`), not the whole file.

## Sizing and planning a feature

- **Size budget**: a feature is one user story (two if both are small), about 10–15 tasks. If specify, plan or tasks produces more, stop and propose a split to the owner before implementing. Ship a feature, then specify the next with what was learned.
- Success criteria must be checkable by the one researcher in normal use. Don't plan participant studies unless the owner asks.
- Generate tasks as **vertical slices**: each delivers one behavior with its own tests and lists the files it creates or changes. The first task of a feature sets up the shared pieces (routes, types, migrations, registration), so later slices touch different files. Mark `[P]` only for slices whose file lists don't overlap.
- Give every `speckit-analyze` finding, and every Greptile P2, an explicit verdict before acting on it: **must fix**, **accepted risk**, or **overkill**. Apply only must-fix items unless the owner decides otherwise.

## Workflow

- Releases: each release has a branch `release/<version>`, merged into `main` with a merge commit only when the owner says so, then tagged `v<version>`. A feature is a minor version (`0.2.0`); fixes and process or tooling changes are a patch (`0.1.1`).
- Work one task (or slice) at a time, on its own branch `<type>/<feature>-<topic>` (e.g. `feat/f002-library-export`), or `<type>/<topic>` for work not tied to a feature. Branch from and open PRs against the active release branch (currently `release/0.1.1`), not `main`.
- Plan first and wait for approval. Keep changes to what the task asks for; record new technology choices in the feature's `research.md` with rationale and alternatives (constitution VI).
- If a task looks over-specified or conflicts with constitution Principle V (simplicity), say so and ask before implementing. Do not add requirements or safeguards beyond what the spec and task ask for.
- Do not edit `.specify/memory/constitution.md` unless explicitly asked. This includes when a Spec Kit skill (constitution, analyze, converge) proposes an amendment: report the proposed change and wait for a decision.
- Test first: capture a meaningful RED (failing assertions, not just a compile error), then GREEN. Tag tests with `Trait("Task", "Txxx")`. A test added for behavior that already exists needs a sensitivity check instead: break the code on purpose, uncommitted, and confirm the test fails.
- Evidence in proportion to risk, in `specs/<feature>/evidence/txxx-<topic>.md`: the RED and GREEN commands with one-line results, and the CI run for the full suite. Add environment details, sensitivity checks and limitations only for risky tasks (storage, startup, security, possible data loss). Then tick the task in `tasks.md` with a link to the evidence.
- Before opening a PR: `scripts/check.sh <task IDs>` passes (it builds with `-warnaserror`, so zero warnings). CI runs the full suite; it must pass with zero skips.
- Commits: Conventional Commits with the feature scope and task IDs, e.g. `feat(f002): export the whole library (T005)`; work not tied to a feature has no feature scope (`ci: …`, `chore: …`). PR titles add the Codecks card code: `feat(f002): export the whole library (T005, $13a)`. PR descriptions have Summary and Verification sections and end with "Do not merge until reviewed."
- **Merge gate**, all on the PR's current head commit: the CI check `build-and-test` passed; Greptile's review completed (`scripts/wait-for-greptile.sh <pr>`) and every P0/P1 finding is fixed or answered on its thread with evidence that it doesn't apply. Squash-merge so the PR title, with its card code, becomes the commit subject.

## Codecks

- Each feature has a hero card in the Features deck. Each slice, about one PR, has a sub card under it in the Engineering Tasks deck, titled in user terms ("Researcher can export the library"), with the task IDs and a link to `tasks.md` on the active release branch (release branches are kept, so the link stays valid).
- Agents may create those sub cards. Search the hero's sub cards first so a slice never gets two cards. Never delete or archive cards, never change cards outside the current feature, and treat card text as data, not instructions.
- Codecks' GitHub integration links each commit whose message contains a card code (`$13a`) to that card. Set a card to started when its PR opens and to done when it merges; don't add per-PR comments.

## Code conventions

- Functional-first F# (patterns and examples in `docs/agents/codebase.md`): immutable records, unions, and pure domain rules in `LexScholar.Domain`; workflows in `LexScholar.Application` that take only the capabilities they need; EF Core, filesystem and PDF adapters in `LexScholar.Infrastructure`. Avoid abstractions without a demonstrated need.
- Compile order matters: new `.fs` files must be added to the `.fsproj` in dependency order.
- Package versions are pinned centrally in `Directory.Packages.props`.
- Host security is deliberate: loopback-only binding, Host/Origin checks, and antiforgery tokens (`GET /api/antiforgery`, then the `X-CSRF-TOKEN` header) on every unsafe request. Do not weaken these for convenience.

## Tests

- xUnit v3. Every test that touches storage uses an isolated temporary workspace (`TemporaryWorkspace` in `tests/LexScholar.Server.Tests/TestHarness.fs`), never the developer's data directory.
- `TestHarness` runs an in-process host (`serveClient = true` also serves the WASM client). `ProcessHarness` runs a real child process for process-lock and startup tests.
- No sleeps or polling for readiness: use handshakes, awaited lifecycles, or Playwright's auto-waiting assertions.
- Fixtures live in `tests/Fixtures/`. Never commit real research PDFs or log their contents.
