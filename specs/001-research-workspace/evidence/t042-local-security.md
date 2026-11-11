# T042 local security checks

## Scope

New `tests/LexScholar.Server.Tests/LocalSecurityTests.fs`: 5 tests, 20 cases, Trait `Task=T042`, plus its `Compile` line in `LexScholar.Server.Tests.fsproj`. No product code changed.

Following Principle V and the lead's approval (2026-11-11), the file adds only what was not already tested. The table below maps every T042 clause to a new test or to an existing test, by name.

| T042 clause | New test (LocalSecurityTests) | Existing tests |
|---|---|---|
| **Local only** (FR-014) | "original bytes are preserved and stored only under the data directory": with the data directory set apart from the content root, the host writes nothing into the content root. Inside the data directory there is only `workspace.sqlite` (plus SQLite side files), `.lexscholar.lock` and `originals/<key>` (empty `staging/` and `quarantine/` directories hold no files). | HostSecurityTests: "Kestrel configuration cannot open a wildcard network listener", "real server ignores wildcard environment and command-line listeners", "configured loopback ports are honored", "forged host and foreign origin are rejected including port mismatches", "forwarded headers cannot replace the actual host or allow foreign origins" (T009). |
| **Path traversal** (FR-012, FR-014) | "encoded traversal ids never reach a file": 4 ids (`..%2F..%2Fworkspace.sqlite`, `%2e%2e%2f%2e%2e%2fetc%2fpasswd`, `originals%2F<hex>`, `..%5C..%5Cworkspace.sqlite`) on content GET, PATCH, DELETE and project-documents GET. Each returns the routing 404, with no `%PDF` or `SQLite format` in the body. "a storage key is never accepted as a document id": a real 32-hex storage key is a valid UUID, so it reaches the lookup, and returns 404 `document_not_found`. "a client filename never chooses a storage path": `../../escape.pdf`, `/tmp/abs.pdf` and `..\..\win.pdf` are stored under `originals/<generated key>`. The recorded names are `escape.pdf`, `abs.pdf` and `win.pdf`, exactly what `ImportDocument.sanitizeFileName` produces; no sanitizing rule was added. No other file appears in the data directory or its parent. | HostSecurityTests: "Bolero static assets load and workspace data is not served", "runtime files are not served even if public and private roots overlap", "manifest assets cannot resolve through a symlink into the data directory", "compressed variants cannot expose a private original asset", "a manifest asset physically inside the data directory is not served", "a symlinked data directory still protects its real asset paths" (T009). LibraryTests: "the stored filename is a display-only basename" (T029). |
| **Original-byte preservation** (FR-012) | "original bytes are preserved and stored only under the data directory": over HTTP, the steps are import, identical re-import under another name (200, reused), associations with two projects, a role change, an association removal, and a host restart. After each step, the served bytes equal the uploaded fixture, the stored file is mode 0400, and its SHA-256 equals the recorded value. | ReaderContractTests: "GET content returns the exact original inline" (T034). LibraryTests: "a new PDF is stored as one read-only original and one document row" (T021). LibraryContractTests: "POST rejects invalid or over-limit uploads safely and changes nothing" (T023). |
| **Error non-disclosure** (FR-012, FR-017, SC-007) | "error responses carry a contract code and disclose no stored data": 11 cases (no file, not a PDF, corrupt, encrypted, too large, too many pages, invalid role, unknown project, unknown document, duplicate association, unavailable original). Each returns the contract status and `code` as problem+json. The body contains none of: the data directory, the content root, the storage key, the document's SHA-256, the uploaded filename (`secret-name`), `originals`, or the content markers. | HostSecurityTests: "security errors use safe problem JSON without echoing untrusted input", "unexpected endpoint exceptions return safe problem JSON" (T009). ReaderContractTests: "GET content for an unavailable original is 503 with records unchanged and no path disclosed" (T034). LibraryContractTests checks that every problem omits the data directory (T023). |
| **Unavailable records kept** (FR-017) | (covered by the 503 case above for disclosure) | ContentAvailabilityTests (T035): missing, unreadable or wrong-length originals, before and after restart, with records unchanged. ReaderContractTests 503 theory (T034). |

**No external service is invoked** is not tested. A test can't meaningfully prove an absence of outbound calls. From the code:
- no file under `src/LexScholar.Server`, `Infrastructure`, `Application` or `Domain` uses `HttpClient`, `WebClient`, `TcpClient`, `Socket` or `WebRequest` (checked with grep);
- the client's `HttpClient` calls only relative same-origin `api/...` paths (`src/LexScholar.Client/App.fs`);
- Kestrel listens only on loopback (`HostSecurity.configure`).

## Environment

CachyOS (Linux 7.3.3-1-cachyos), .NET SDK 10.0.114, Microsoft.Playwright 1.64.1. Base `b96caa4` (release/0.1.0 after #24). Tests ran as uid 1000.

## No committed RED

T042 checks behavior that already exists, from T009, T026/T029, T031, T034/T038 and T037. The tests passed on their first run (20/20), and there is no product change to drive from RED. A committed RED would need a deliberately false assertion, which the lead ruled out. Instead, these temporary, uncommitted sensitivity checks show the tests can fail. Each file was restored and the solution rebuilt afterwards; `git status` showed only the new test file and the fsproj line.

| Temporary change | Result |
|---|---|
| `sanitizeFileName` keeps the whole client name (`let basename = raw`) | 3 failed: all three "a client filename never chooses a storage path" cases. |
| The 503 title includes `originals/<storage key>` | 1 failed: "error responses …" case `unavailable`. |
| The content route loses its `:guid` constraint | 4 failed: every "encoded traversal ids never reach a file" case. The handler's GUID binding answers 400 instead of the routing 404. |

## Verification

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T042'
dotnet test --solution LexScholar.sln --configuration Release --no-build
```

- Restore and build: 0 warnings, 0 errors.
- T042 filter: 20/20 passed, in about 5 s.
- Full suite: 326/326 passed with 0 skipped, browser journeys included (306 on `b96caa4` plus 20 new).

## Limitations

- **Outbound calls:** "no external service" rests on the code scan above, not on a test.
- **Traversal ids:** only percent-encoded traversal in route segments is tested. Raw `../` in a URL is normalized by the HTTP client before it is sent, so it never reaches the server as a segment.
- **Absolute filename:** for `/tmp/abs.pdf`, the test checks that the stored name is generated and that no new file appears in the data directory. It does not inspect `/tmp` itself, which other processes share.
- **Local-only scope:** the check covers the host's own writes during the HTTP flow. It does not cover files the .NET runtime may create elsewhere. For example, at startup ASP.NET Core logs a default key-repository location under `~/.aspnet`, even though this host configures an ephemeral data-protection provider. That location was not inspected.
