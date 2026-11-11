"""Exercise the real Release server entry point and clean up the test workspace."""

import contextlib
import http.client
import os
import pathlib
import queue
import re
import signal
import sqlite3
import subprocess
import tempfile
import threading
import time

server = pathlib.Path.cwd() / "src/LexScholar.Server/bin/Release/net10.0/LexScholar.Server.dll"
processes = []
temp_root = pathlib.Path("/tmp/opencode")
with tempfile.TemporaryDirectory(prefix="lexscholar-t010-", dir=temp_root if temp_root.is_dir() else None) as name:
    root = pathlib.Path(name)
    env = os.environ.copy()
    env.update({"LexScholar__DataDirectory": name, "LexScholar__Port": "0",
                "ASPNETCORE_ENVIRONMENT": "Production", "DOTNET_ENVIRONMENT": "Production",
                "Logging__LogLevel__Default": "Information"})

    def start():
        process = subprocess.Popen(["dotnet", str(server)], cwd=root, env=env,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        processes.append(process)
        lines = queue.Queue()

        def capture():
            for line in process.stdout:
                lines.put(line)
            lines.put(None)

        threading.Thread(target=capture, daemon=True).start()
        deadline = time.monotonic() + 20
        seen = []
        while True:
            line = lines.get(timeout=max(0.1, deadline - time.monotonic()))
            if line is None:
                raise RuntimeError("Host exited before listening: " + "".join(seen))
            seen.append(line)
            match = re.search(r"Now listening on: http://127\.0\.0\.1:(\d+)", line)
            if match:
                return process, int(match.group(1))

    def stop(process):
        process.send_signal(signal.SIGTERM)
        code = process.wait(timeout=10)
        assert code == 0, code
        print("host shutdown: exit 0")

    def check_http(port):
        connection = http.client.HTTPConnection("127.0.0.1", port, timeout=5)
        connection.request("GET", "/api/projects")
        response = connection.getresponse()
        assert response.status == 501, response.status
        response.read()
        connection.close()
        print("GET /api/projects: 501 stub")

    try:
        first, port = start()
        db = root / "workspace.sqlite"
        assert db.is_file()
        with contextlib.closing(sqlite3.connect(db)) as connection:
            history = connection.execute("SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory").fetchall()
            assert history == [("20261108000000_Initial", "10.0.14")], history
            tables = connection.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").fetchall()
            assert all(row[0].startswith("__EF") for row in tables), tables
            connection.execute("CREATE TABLE test_marker (value TEXT NOT NULL)")
            connection.execute("INSERT INTO test_marker VALUES ('persisted')")
            connection.commit()
        print("first startup: migration history present; no domain tables")
        check_http(port)
        contender = subprocess.run(["dotnet", str(server)], cwd=root, env=env,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=15)
        assert contender.returncode == 1 and "already in use" in contender.stdout, contender.stdout
        print("second process: rejected by data-directory lock")
        stop(first)
        second, port = start()
        with contextlib.closing(sqlite3.connect(db)) as connection:
            assert connection.execute("SELECT value FROM test_marker").fetchone() == ("persisted",)
            assert connection.execute("SELECT count(*) FROM __EFMigrationsHistory").fetchone() == (1,)
        print("restart: marker preserved; migration applied exactly once")
        check_http(port)
        stop(second)
    finally:
        for process in processes:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=10)
            process.stdout.close()
assert not root.exists()
print("temporary workspace and SQLite files removed")
