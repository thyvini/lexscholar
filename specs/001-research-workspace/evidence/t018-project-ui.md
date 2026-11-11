# T018 project UI verification

## RED → GREEN

Six compile-ready state tests initially failed on meaningful assertions against a no-behavior `Projects.update` scaffold: initial loading, active-project loading, creation, selection, validation/duplicate feedback, and failure recovery. The RED run was:

```sh
dotnet test --project tests/LexScholar.Browser.Tests/LexScholar.Browser.Tests.fsproj --configuration Release --filter-trait 'Task=T018' --output Normal
```

The completed implementation passes these and two additional tests for safe HTTP problem-code mapping and stale/repeated actions. Elmish serializes operations, disables controls during requests, and refreshes after successful mutations. Each POST/PUT first obtains a same-origin antiforgery token; browser cookies remain under the existing host security policy.

Final verification:

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --solution LexScholar.sln --configuration Release
dotnet publish src/LexScholar.Server/LexScholar.Server.fsproj -c Release -o /tmp/opencode/t018-publish --no-restore -warnaserror
```

Restore/build succeeded with zero warnings; the full suite passed 171/171, zero skipped. Host tests also verify the root mount element, WebAssembly bootstrap script and stylesheet are served. The publish succeeded. Like the existing host configuration, the published app resolves its static content from the content root: start it from the published directory (or configure the content root explicitly).

## Browser smoke (not T019 automated coverage)

Using headless Chromium and ChromeDriver against a real isolated host, the built application mounted, showed the empty state, created a project, selected it, and restored its active marker after a browser reload. The same smoke was repeated against the published app started from its publish directory. This is a one-off smoke check, not a dedicated browser acceptance suite or usability evaluation; Firefox/Chromium journeys remain T019.
