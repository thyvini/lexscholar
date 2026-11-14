# LexScholar codebase guide (for agents)

Read this instead of exploring the repository. It covers where code lives, the patterns to follow and how to test it. Open a source file only once you know which one you need. If you add a module, a project or a pattern, update this guide in the same PR.

The F001 spec artifacts (`specs/001-research-workspace/`) are the historical record of how this was decided. You rarely need them; look up a specific decision in `research.md` by heading when a task asks about it.

## Map

Dependency direction: Domain ← Application ← Infrastructure ← Server. The client references only Domain. The server hosts the client's static files.

| Project | Owns | Key files |
|---|---|---|
| `src/LexScholar.Domain` | Pure types and rules, no I/O | `Projects.fs` (`ResearchProject`, `ProjectName`), `Documents.fs` (`LibraryDocument`, `ProjectDocument`, `DocumentRole`), `Summaries.fs` (F003 types: `PageText`, `SummaryPoint`, `DocumentSummary`; `readability`, `chunk`, `groupPoints`, `parsePoints`; `ServiceLocation.classify`: the one local-or-elsewhere rule, used by the 409 confirmation and the outgoing handler), `ChatGptSignIn.fs` (F005 types `Credentials`, `SignInOutcome`, `ModelEntry`, `Identity`, `IdTokenProblem`; module `ChatGptSignIn`: `challengeOf`, `authorizationUrl`, `validateIdToken` (claims JSON, signature already checked), `grantsPlanUsage`, `needsRefresh`, `pickModel`) |
| `src/LexScholar.Application` | Workflows over capability functions, contract views | `Contracts.fs` (views, `ImportLimits`, rejections), `Ports.fs` + `Capabilities.fs` (capability function types), `Projects.fs`, `Library.fs`, `DocumentRoles.fs` (workflows with a `Store` record), `ImportDocument.fs` (the import pipeline), `Summarize.fs` (F003 capabilities `Capabilities`, failures, `summarize`: one call, or map per chunk then bounded combine rounds, reporting `Progress`; the endpoint writes NDJSON events as they happen), `ChatGptAccess.fs` (F005: capability records `CredentialStore`, `AuthClient`; `Vault` = the one lock around the stored record, every read/save/delete goes through `Vault.Locked`; `SignInSession` = the single in-memory pending sign-in; `startSignIn`, `completeSignIn`, `signOut` (under `Vault.Locked`: `SignInSession.Clear` supersedes and cancels a pending or mid-exchange attempt, best-effort revoke, always delete); `prepareSend`: ONE locked step for a summary through the plan = confirmation check (before any network call), refresh when the token expires within 10 minutes, save of the result, then `Prepared of Credentials * SendRun | AwaitingConfirmation | NoSignIn | SignInEnded | RefreshUnreachable`; a terminal refresh error deletes the record and gives `SignInEnded`; `Vault.BeginRun`/`EndRuns` keep the cancellation tokens of running plan summaries, registered inside `prepareSend`'s lock and cancelled by `signOut`, `endSignIn` and a terminal refresh error; `endSignIn vault credentials` deletes the record only if it is still that sign-in (HostId)) |
| `src/LexScholar.Infrastructure` | SQLite (EF Core), files, PDF parsing | `WorkspaceDb.fs` (rows, `DbContext`, `initialize`), `Migrations/`, `ProjectRepository.fs`, `LibraryRepository.fs`, `DocumentStore.fs` (staging/originals/quarantine), `PdfValidator.fs` (PdfPig), `StorageCleanup.fs` (startup recovery), `VerifiedDocumentContent.fs`, `PdfText.fs` (F003 per-page text, page cap), `ChatCompletions.fs` (F003 chat-completions client, 1 MB cap; `createHandler`: no redirects, local endpoint = no proxy + loopback-only connect), `ChatGptCredentials.fs` (F005: `read`/`save`/`delete` of `chatgpt-signin.json` by data directory, 0600 temp file renamed over the target, malformed file deleted; `isShown`/`markShown` for the welcome marker), `OpenAiAuth.fs` (F005 sign-in service client: `createHandler` (no redirects), `exchange`, `refresh`, `revoke`, `verifyIdToken` (RS256 by `kid`, then `ChatGptSignIn.validateIdToken`); take an `HttpClient` and a clock; errors never carry response bodies), `ResponsesApi.fs` (F005 ChatGPT Responses client: `complete` has the shape of `ChatCompletions.complete` with `Settings = { Model; AccessToken }`, reads the SSE stream under the 1 MB cap and maps `error.code` to `UsageLimitReached`/`NoPlanAccess`/`SignedOut`, else F003's sanitized `ErrorStatus`; `listModels` gives `ModelEntry` list in server order for `ChatGptSignIn.pickModel`; `createHandler`: no redirects), `DataDirectoryLock.fs` (`flock`) |
| `src/LexScholar.Server` | ASP.NET Core host and HTTP endpoints | `Program.fs` (startup order), `Configuration.fs` (incl. optional `Summary` section, F003), `HostSecurity.fs`, `Errors.fs`, `ProjectEndpoints.fs`, `LibraryEndpoints.fs`, `ReaderEndpoints.fs`, `SummaryEndpoints.fs` (F003; F005: `resolveDestination` first, then the local checks, then `summarizeConfigured` (F003 rule) or `summarizeViaPlan` (`prepareSend`, `listModels`/`pickModel`, `ResponsesApi.complete`; `failureCode` maps `UsageLimitReached`/`SignedOut`/`NoPlanAccess` to the `failed` codes, the last two end the sign-in; the run token ends a running summary with `failed signed_out`); the request body is parsed once into `Body`), `ChatGptEndpoints.fs` (F005: `GET /api/summary/destinations`, `POST /api/chatgpt/sign-in`, `GET /auth/callback` (reads the issued client id from its `client_id` query parameter; never logged: `Program.fs` filters the request-start log line); `POST /api/chatgpt/sign-out` (`{revoked}`), `POST /api/chatgpt/welcome/dismiss` (204, marker file)), `Endpoints.fs` (registers them; `ChatGptEndpoints.map` returns the `Vault`, which `SummaryEndpoints.map` takes so both use the one lock; `ChatGptEndpoints` is compiled before `SummaryEndpoints`) |
| `src/LexScholar.Client` | Bolero/Elmish WebAssembly UI | `Projects.fs`, `Library.fs`, `Reader.fs`, `Summary.fs` (pure state machines; F005: `Available`/`Choice`/`Destination` (one available destination is used, two need her choice), `ConfirmTarget` (`Service` or `Plan`), failures `UsageLimitReached`/`SignedOut`/`NoPlanAccess`/`DestinationRequired`, `reloadsDestinations` and `offersSignIn`; `App.update` feeds `DestinationsChanged` from the `ChatGpt` state and reloads destinations after a signed-out failure; `Summary` follows the reader's open document and renders in the reader card, citations are `button.citation` with `data-page`), `ChatGpt.fs` (F005 sign-in state: `Status`, sign-in/sign-out guards, welcome, one-time `?chatgpt=` return `Notice`; `App.fs` reads and strips the query at start, `ChatGptApi` calls the endpoints, and `Navigate` sends the tab to `authorizationUrl`), `App.fs` (HTTP `Api`, effect interpretation, view) |

HTTP routes are listed in `specs/001-research-workspace/quickstart.md` ("HTTP routes") and specified in `contracts/http-api.yaml`.

## Functional-first patterns

**Domain: smart constructors returning `Result`.** No exceptions for expected failures.

```fsharp
module ProjectName =
    let create (name: string) : Result<ProjectName, ProjectNameError> =
        if String.IsNullOrWhiteSpace(name) then Error InvalidProjectName
        else
            let display = name.Trim()
            Ok { Display = display; Normalized = display.ToUpperInvariant() }
```

**Application: workflows take only the functions they need**, as a record of functions (`Store`) or function types (`Ports.fs`). No interfaces or DI container.

```fsharp
type Store =
    { ResolveWorkspace: unit -> Guid
      List: Guid -> ResearchProject list
      ActiveProject: Guid -> Guid option
      Create: Guid -> Guid -> string -> DateTimeOffset -> Result<ResearchProject, ProjectCreationError>
      Select: Guid -> Guid -> Result<unit, SelectionError> }

let select (store: Store) id = store.Select (store.ResolveWorkspace()) id
```

Errors are unions, often `[<RequireQualifiedAccess>]` (`Library.AssociationError.ProjectNotFound`).

**Server: composition at the edge.** Each `*Endpoints.map` builds the records from Infrastructure functions partially applied to the data directory, then maps routes and turns `Result` cases into HTTP responses:

```fsharp
let store : Projects.Store =
    { ResolveWorkspace = fun () -> ProjectRepository.resolveWorkspace directory
      List = ProjectRepository.list directory
      ... }
app.MapPut("/api/projects/{projectId:guid}/active", Func<Guid, IResult>(fun projectId ->
    match Projects.select store projectId with
    | Ok () -> Results.NoContent()
    | Error Projects.ProjectNotFound -> problem 404 "not_found" "Project not found")) |> ignore
```

Errors are ProblemDetails with a `code` extension (`problem` helpers in the endpoint modules, `Errors.problem` in middleware). Never put exception details or filesystem paths in a response.

**Client: Elmish with effects as data.** Each feature module has `Model`, `Message`, `Effect`, `initial` and a pure `update : Message -> Model -> Model * Effect option`. `App.fs` turns effects into HTTP calls (`Api` module) and results back into messages. Rules the existing modules follow:

- `Busy`/operation guards: messages are ignored while another operation runs (`| Create when model.Busy.IsNone -> ...`).
- Request numbers (`Library`, `Reader`): each fetch carries a counter, and responses for an older request are dropped.
- Results for a project that is no longer active don't change the current view.
- The client has its own small types; it doesn't reference Application. JSON parsing lives in `App.Api`.

**Sign-in button asset (F005).** `src/LexScholar.Client/wwwroot/chatgpt-logo-white.svg` is OpenAI's white ChatGPT logo, downloaded unmodified from `https://developers.openai.com/assets/siwc/sign-in-buttons/chatgpt-logo-white.svg` (listed on `https://developers.openai.com/siwc/website`, which also prescribes the label "Continue with ChatGPT" and the `data-oai-siwc-button` attribute). No licence text is published with it; it is used only as that page directs. The `.siwc-button` styles in `app.css` (black button, white logo) are ours.

## Request flow

Client `update` returns an `Effect` → `App.fs` runs the HTTP call (`Api.*`) → host middleware (`HostSecurity.useSecurity`: error handler, Host and Origin checks, antiforgery on unsafe methods) → endpoint → Application workflow → Infrastructure functions → SQLite or files → `Result` → HTTP status and JSON → `App.fs` maps it to a `Message` → `update`.

Startup order (`Program.fs`, don't reorder casually): configuration → data-directory lock → `WorkspaceDb.initialize` (migrations) → host security config → build → `StorageCleanup.run` → middleware → static assets → endpoints.

Import (`ImportDocument.create`, `POST /api/documents`): stage at most MaxBytes + 1 bytes → validate (size, `%PDF-`, PdfPig, SHA-256) → serialized commit: duplicate check by SHA-256 then byte comparison → promote (fsync, read-only, move without overwrite) → insert row → always discard staging. `201` for new, `200` for reused bytes.

## Recipes

**Add an endpoint.**
1. Domain types and rules first, if any, with tests in `tests/LexScholar.Domain.Tests`.
2. Workflow in Application taking a `Store` record or capability functions; add the store field it needs.
3. Infrastructure function(s) in the matching repository; schema changes need a migration (below).
4. Map the route in the matching `*Endpoints.fs` (or a new module, registered in `Endpoints.fs` and the `.fsproj` before `Endpoints.fs`). Update `contracts/http-api.yaml` of the active feature.
5. Contract test in `tests/LexScholar.Server.Tests/*ContractTests.fs` (see Tests).

**Add a schema change.** Migrations are handwritten F# with raw SQL (`migration.Sql`): copy `Migrations/Documents.fs`, give it a `Migration("<timestamp>_<Name>")` later than the last one, update `WorkspaceDbModelSnapshot.fs` and the row types in `WorkspaceDb.fs`, register the file in the `.fsproj` after the existing migrations, and add a schema test (`tests/LexScholar.Infrastructure.Tests/*SchemaTests.fs`).

**Add a client feature.** A new `Model`/`Message`/`Effect`/`update` module (or extend one), registered in the client `.fsproj` before `App.fs`; wire its effects and view in `App.fs`. Test `update` first (state tests); add a browser journey only for what state tests can't show.

## Tests

| Project | What goes there |
|---|---|
| `tests/LexScholar.Domain.Tests` | Pure domain rules |
| `tests/LexScholar.Application.Tests` | Workflows and capability contracts with in-memory fakes |
| `tests/LexScholar.Infrastructure.Tests` | Repositories, schema, PDF validation, storage and cleanup, against a temporary data directory |
| `tests/LexScholar.Server.Tests` | HTTP contracts, host security, startup, process lock, restarts |
| `tests/LexScholar.Browser.Tests` | Client state tests (`*StateTests.fs`, pure `update`, no browser) and Playwright journeys (`*JourneyTests.fs`, trait `Category=Browser`) |
| `tests/LexScholar.Corpus.Tests` | SC-002 import corpus; outside the solution, run manually |

- **Run tests with `scripts/check.sh <Txxx>`** (see AGENTS.md). Tag every test `[<Fact; Trait("Task", "Txxx")>]`.
- In-process host: `use harness = TestHarness.Start()` gives `harness.Client` (an `HttpClient`) and `harness.Root` (a temporary data directory). Options: `settings` (config overrides), `serveClient = true` for browser tests, `workspace` to reuse a `TemporaryWorkspace` across restarts.
- Unsafe requests in contract tests need `GET /api/antiforgery`, then the `X-CSRF-TOKEN` header and an `Origin` header; copy the `token`/`request`/`send` helpers at the top of `LibraryContractTests.fs`.
- Summary service (F003): `FakeChatService(script)` is an `HttpMessageHandler` that records requests and answers from a script (`Reply`, `Status`, `WaitUntilCancelled`); `TestHarness.Start(summaryHandler = fake, settings = [ "LexScholar:Summary:Endpoint", ...; "LexScholar:Summary:Model", ... ])`. Reference PDFs: `tests/Fixtures/summary/` (see its README entry in `tests/Fixtures/README.md`, regenerate with `scripts/make-summary-fixtures.fsx`).
- OpenAI (F005): `FakeOpenAi(script)` in `FakeOpenAi.fs` is an `HttpMessageHandler` routing by host (`auth.openai.com`: token, revocation, discovery, JWKS signed by a test RSA key via `TestIdToken`; `api.openai.com`: models, SSE Responses scripted with `ResponsesScript.Completed | Failed | Status | WaitUntilCancelled`). It records `Requests`, exposes `Waiting`/`Cancelled`/`TokenArrived` handshakes and `HoldTokens()` for race tests; set `Nonce`, `ClientId`, `Scope` etc. per test. Inject it through the same seam: `TestHarness.Start(summaryHandler = fake)`. `FakeOpenAi.fs` is also linked into `LexScholar.Infrastructure.Tests` (as Browser.Tests links `FakeChatService.fs`). A client sending Host `127.0.0.1:<port>` is accepted (tested in `ChatGptContractTests`). `ChatGptContractTests` helpers (`sendJson`, `documentPathFor`, `signInThrough`, `events`, ...) are public; `ChatGptHygieneTests` reuses them and adds a log provider through the `configure` callback (`ILoggerFactory.AddProvider`).
- Real child process (locks, startup failures): `ProcessHarness.HostProcess.Start(root, overrides)`.
- State tests: drive `update` with messages and assert on the model and the returned effect; see `LibraryStateTests.fs` for helpers (`step`, `assertState`, request-number extractors).
- Plan sign-in journey (`ChatGptJourneyTests.fs`): `FakeOpenAi` as `summaryHandler`; `page.RouteAsync("https://auth.openai.com/**", Func<IRoute, Task>(...))` captures the `authorizationUrl` (set `fake.Nonce` from its `nonce`), then `page.GotoAsync` the host's `/auth/callback?state=...&code=...&client_id=...`. The welcome dismissal is a background POST: wait for it (`WaitForResponseAsync`) before reloading.
- Browser journeys: Playwright's auto-waiting `Assertions.Expect(...)`, never sleeps; helpers at the top of `ReaderJourneyTests.fs`.
- Fixtures: `tests/Fixtures/` (`valid.pdf` and friends, provenance in its README).
