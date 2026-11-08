# T009 host-security evidence

Environment: Linux CachyOS x86_64, kernel `7.3.1-1-cachyos`, .NET SDK `10.0.114`, ASP.NET Core runtime `10.0.14`. Commands below ran from the repository root on 2026-11-08. This is T009 evidence, not the broader T042 security verification.

## Test-first RED

After adding the original T009 tests and building successfully, before production implementation:

```sh
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T009' --output Detailed > /tmp/opencode/lexscholar-t009-red.log 2>&1
```

Result: **11 total, 9 failed, 2 passed**. A forged Host and tokenless JSON, multipart, text, and form requests reached the `501` stubs; the antiforgery endpoint returned `404`; an exception response lacked problem JSON; a configured Kestrel endpoint actually listened on `0.0.0.0`. The document GET and local asset controls passed. The initial tests expected `400` for host/token failures; the final tests expect `403` so these security preconditions do not reuse the contract's domain-specific `400` problem-code enum.

Additional focused RED checks during implementation used the same test command, with output redirected to the following logs:

- `/tmp/opencode/lexscholar-t009-static-red.log`: **18 total, 1 failed**; a manifest asset symlink into the data directory returned `200`.
- `/tmp/opencode/lexscholar-t009-nested-link-red.log`: **26 total, 1 failed**; a symlink whose target contained a symlinked directory returned `500` instead of a safe `404`. The resolver now also resolves parent components in symlink targets.
- `/tmp/opencode/lexscholar-t009-release-assets-red.log`: **28 total, 2 failed**; real Release/Production subprocesses could not serve `index.html` from the build manifest. Both environment and command-line launch cases now load it.
- `/tmp/opencode/lexscholar-t009-compressed-red.log`: **29 total, 1 failed**; a compressed copy was served even though the corresponding original resolved into the private data directory.

The logs are local transient artifacts; the relevant failure observations are retained above.

## GREEN

```sh
dotnet restore LexScholar.sln -warnaserror
dotnet build LexScholar.sln -c Release --no-restore -warnaserror
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --no-build --filter-trait 'Task=T009' --output Detailed > /tmp/opencode/lexscholar-t009-green.log 2>&1
dotnet test --solution LexScholar.sln --configuration Release --no-build --output Normal > /tmp/opencode/lexscholar-t009-suite.log 2>&1
dotnet publish src/LexScholar.Server/LexScholar.Server.fsproj -c Release --no-restore -o /tmp/opencode/lexscholar-t009-publish -warnaserror > /tmp/opencode/lexscholar-t009-publish.log 2>&1
```

Restore succeeded; Release build had **0 warnings and 0 errors**; focused T009 tests passed **29/29**; the entire solution passed **92/92** with no skips; Release publish succeeded. Existing route smoke tests now obtain a real token and cookie instead of bypassing antiforgery.

Tests use Kestrel and real sockets, including actual production-entrypoint subprocesses under wildcard environment and CLI settings, a configured port and port `0`, forged Host/Origin/forwarded headers, CORS preflight, missing/invalid token and cookie, valid JSON and actual multipart body reading, every contract mutation method, native-viewer-style range GET, Testing and Development exception responses, and local client assets. Static checks cover unlisted SQLite/PDF/staging files, manifest assets in the private directory, a symlinked data directory, asset symlinks (including a symlink in the target's parent), and compressed variants of private originals.

## PR #6 antiforgery cookie collision follow-up

The regression test starts two independent processes using different configured data directories and the same content root. Both use port `0` and listen on `127.0.0.1` at different actual ports; two HTTP clients share one `CookieContainer`, which matches host cookies across ports. With the original fixed cookie name, A's valid mutation reached its `501` stub before B obtained a token, and then returned `403` after B obtained its token. The final same-content-root test was also verified with the original name temporarily reinstated:

```sh
dotnet test --project tests/LexScholar.Server.Tests/LexScholar.Server.Tests.fsproj --configuration Release --filter-display-name '*independent hosts do not overwrite*' --output Detailed
```

Result: **1 failed** at A's post-B mutation, expected `501`, actual `403`. The fix uses a SHA-256 digest of the normalized, locked data-directory path in the cookie name; it does not depend on the listening port or disclose the raw path. The test now verifies both hosts remain usable and reject each other's request tokens. A separate restart test verifies the name remains stable, stale tokens are rejected, and obtaining a fresh token replaces the stale cookie rather than accumulating names.

With the fix restored, `dotnet restore LexScholar.sln -warnaserror` and `dotnet build LexScholar.sln -c Release --no-restore -warnaserror` succeeded with **0 warnings and 0 errors**. The focused `Task=T009` command above passed **31/31**, and the complete Release solution test command passed **94/94**, with no skips.

## Independent published Linux host probe

The following exact command was run after the publish above. It creates and removes its own temporary workspace, verifies the process's actual kernel socket with `ss`, tries the host's non-loopback interface, checks published static assets and private paths, exercises security/token responses, and stops the server.

```sh
python3 - <<'PY' | tee /tmp/opencode/lexscholar-t009-published-final.log
import http.client, json, os, pathlib, re, signal, socket, subprocess, tempfile
publish = pathlib.Path('/tmp/opencode/lexscholar-t009-publish')
with tempfile.TemporaryDirectory(prefix='lexscholar-t009-', dir='/tmp/opencode') as tmp:
    root = pathlib.Path(tmp)
    (root / 'workspace.sqlite').write_text('private sqlite marker')
    (root / 'private.pdf').write_text('private pdf marker')
    (root / 'staging').mkdir()
    (root / 'staging' / 'draft.txt').write_text('private staging marker')
    env = os.environ.copy()
    env.update({'LexScholar__DataDirectory': tmp, 'LexScholar__Port': '0',
                'ASPNETCORE_URLS': 'http://0.0.0.0:0',
                'Kestrel__Endpoints__Untrusted__Url': 'http://0.0.0.0:0',
                'ASPNETCORE_FORWARDEDHEADERS_ENABLED': 'true',
                'ASPNETCORE_ENVIRONMENT': 'Production', 'DOTNET_ENVIRONMENT': 'Production'})
    p = subprocess.Popen(['dotnet', str(publish / 'LexScholar.Server.dll'), '--urls', 'http://0.0.0.0:0', '--Kestrel:Endpoints:Extra:Url', 'http://0.0.0.0:0'], cwd=publish, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    try:
        while True:
            line = p.stdout.readline()
            if not line: raise RuntimeError('Host exited before listening')
            match = re.search(r'Now listening on: http://127\.0\.0\.1:(\d+)', line)
            if match: port = int(match.group(1)); break
        lines = subprocess.check_output(['ss', '-ltnp'], text=True).splitlines()
        sockets = [line for line in lines if f'pid={p.pid},' in line]
        assert len(sockets) == 1 and f'127.0.0.1:{port}' in sockets[0], sockets
        print(f'PID {p.pid}, listener: {sockets[0]}')
        addresses = subprocess.check_output(['ip', '-4', '-o', 'addr', 'show', 'scope', 'global'], text=True)
        candidates = re.findall(r'inet (\d+\.\d+\.\d+\.\d+)/', addresses)
        if candidates:
            with socket.socket() as s:
                s.settimeout(3)
                result = s.connect_ex((candidates[0], port))
            print(f'non-loopback {candidates[0]}:{port}: connect errno={result}')
            assert result != 0
        else: print('No non-loopback IPv4 interface; external connect not tested')
        def request(method, path, headers=None, body=None):
            c = http.client.HTTPConnection('127.0.0.1', port, timeout=5)
            c.request(method, path, body=body, headers=headers or {})
            r = c.getresponse()
            result = r.status, dict(r.getheaders()), r.read()
            c.close()
            return result
        for path in ['/index.html', '/_framework/blazor.webassembly.js', '/workspace.sqlite', '/private.pdf', '/staging/draft.txt']:
            status, _, body = request('GET', path)
            print(f'GET {path}: {status}, {len(body)} bytes')
            assert status == (200 if path in ['/index.html', '/_framework/blazor.webassembly.js'] else 404)
        status, headers, _ = request('GET', '/index.html', {'Accept-Encoding': 'gzip'})
        assert status == 200 and headers['Content-Encoding'] == 'gzip'
        print('GET /index.html with gzip: 200')
        assert request('GET', '/api/projects', {'Host': f'evil.example:{port}', 'X-Forwarded-Host': f'127.0.0.1:{port}'})[0] == 403
        assert request('GET', '/api/projects', {'Origin': 'http://evil.example'})[0] == 403
        assert request('POST', '/api/projects', {'Content-Type': 'application/json'}, '{}')[0] == 403
        status, headers, body = request('GET', '/api/antiforgery')
        assert status == 200
        token, cookie = json.loads(body)['requestToken'], headers['Set-Cookie'].split(';')[0]
        assert request('POST', '/api/projects', {'Content-Type': 'application/json', 'Origin': f'http://127.0.0.1:{port}', 'Cookie': cookie, 'X-CSRF-TOKEN': token}, '{}')[0] == 501
        print('forged Host=403, foreign Origin=403, tokenless JSON=403, valid token+cookie JSON=501')
    finally:
        if p.poll() is None:
            p.send_signal(signal.SIGTERM)
            p.wait(timeout=10)
        print(f'host exit code={p.returncode}; temporary workspace removed on exit')
        assert p.returncode == 0
PY
```

Observed: one socket, `127.0.0.1:38727`; connection to the machine's `192.168.15.15:38727` was refused (`errno=111`); index (including gzip) and WebAssembly bootstrap returned `200`, all three private paths returned `404`; forged Host, foreign Origin and tokenless JSON returned `403`, and valid token plus cookie reached the existing `501` stub. SIGTERM produced exit code `0`. Ports, PIDs and interface addresses naturally vary.

## Scope and limitations

- This is a local single-user host, not authentication or protection from arbitrary processes running as the same user. Filesystem hard-link aliases and a local process racing a symlink replacement between checking and opening a static file are not prevented by the path check; keep private data outside public/build asset trees with appropriate filesystem permissions. The runtime static-file fallback and runtime asset rewriting are disabled, and unresolved static paths fail closed. A `.gz` or `.br` asset is served only when its corresponding original is present and safe.
- Tokens use ephemeral ASP.NET Core Data Protection keys and become invalid on restart. The future client must refresh them. Only IPv4 loopback is bound; `localhost` and `127.0.0.1` remain different origins.
- Security-specific `403` and generic `500` problems are host-level responses, not new domain codes in the approved API contract. As with normal HTTP streaming, an exception after headers/body have started cannot be replaced with a problem response; future streaming endpoints must not write sensitive error details.
- The PDF endpoint and domain routes are still stubs; the GET test only establishes that security does not block a native viewer/range request. Browser tests are scaffolding, not completed Firefox/Chromium journeys. T010, T011, later feature work, and T042 remain open.
