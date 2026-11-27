# Candidate launch contract (proposed)

| Input | Required observable result |
|---|---|
| Native Linux x86-64 or macOS ARM64 archive from a recorded revision; matching native ASP.NET Core 10 runtime; installed Firefox or Chromium | Unpack away from source and research data. Launch `dotnet LexScholar.Server.dll` **from the extracted publish root** (or explicitly supply that root with `--contentRoot`). No SDK, source checkout, installer or signing workaround is part of the researcher launch. List only configurations verified by candidate evidence; a tested OS version is not a minimum or supported range. |
| Optional `LexScholar__DataDirectory` absolute private local path | Existing configuration and permissions; local UI and WASM assets load, researcher can create a project. Default and explicit paths must not expose workspace contents through `wwwroot`. |
| Second process, same directory, different free port | Refused for ownership before serving; not a port collision. Original process remains usable; its successor opens the same data unaided. |
| Fixture imported, exported, confirmed deleted, app relaunched | Bytes match on open and export; deletion stays complete on restart and the independent export still matches. No live summary or sign-in needed. |

No change to HTTP, authentication/consent, schema or UI contracts. Follow the [precedence index](../../../docs/specification-index.md#explicit-precedence), not the historical in-app reader. A host/API response without an interactive client, a CI run without a candidate digest, or a CI Mac in place of the owner's physical machine does not satisfy the distribution contract.
