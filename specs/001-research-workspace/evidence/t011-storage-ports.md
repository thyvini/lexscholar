# T011 storage and document-content boundaries

## Decisions

- `ProjectOperations` and `LibraryOperations` remain the existing application workflow capabilities in `Capabilities.fs`; they are not duplicated as repository interfaces. `ImportJournalOperations` and `OpenVerifiedContent` retain their existing namespace, signatures, and scaffolding consumers but are located in `Ports.fs` as effect boundaries. `PendingImport`, `VerifiedContent`, and the existing UUID-based types are reused.
- `Ports.fs` adds separate function types to open operation-owned staging streams for writing or reading, promote staged bytes, and discard only that operation's staging artifact. They accept the existing internal `PendingImport`, not a path or a user-provided filename as a storage identifier. T026 must generate and validate opaque private keys; the existing string fields do not enforce this by themselves. Published originals must not be opened for writing or overwritten.
- A successful stream open transfers ownership to the caller, who disposes it even if subsequent reads or writes fail or are cancelled. A provider owns cleanup when opening fails or is cancelled before successful transfer. Open tokens cover opening/verification; callers supply cancellation tokens to later stream operations. Import and validation borrow caller-owned input streams and must neither dispose nor retain them after the task completes. Promotion cancellation may race with a durable effect; T028 recovery must reconcile it.
- The existing verified-content port requires a read-only handle that remains protected from changes, with verification and delivery bound to that same handle. It is independent of HTTP and browser behavior. T035–T038 must implement and test verification, range delivery, and error mapping; this contract is not evidence that those behaviors exist.
- Project persistence, shared-document metadata, and association persistence signatures are intentionally deferred until the T014/T024 domain types and T015/T025 schemas exist; T016/T030 will define the narrow operations their workflows actually need. The current workflow/HTTP scaffolding is not a database model. Key allocation, recovery of promoted-but-uncommitted content, and detailed transaction/error semantics remain for T026–T029 rather than inventing a premature aggregate or generic repository.

## Verification

The application test project references Application (and its Domain dependency), not Infrastructure or Server. Its fake capabilities exercise independent injection, token forwarding, read-only stream consumption, borrowed input, successful-open disposal including a cancellation race, cancellation during opening and later reading, and provider cleanup. An assembly-reference check also excludes Infrastructure, Server, EF Core, SQLite, and ASP.NET Core. These are contract/ownership examples, not tests of real storage behavior; no artificial RED storage test was introduced.

Commands run from the repository root on Linux with .NET SDK `10.0.114`:

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release
dotnet test --project tests/LexScholar.Application.Tests/LexScholar.Application.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T011' --output Detailed
```

Restore succeeded; the Release build had **0 warnings and 0 errors**. The complete solution passed **119/119**, with no skips; the T011-focused application tests passed **9/9**. Existing no-behavior stubs and T005–T010 tests remain in place. No T012+ functionality is claimed.
