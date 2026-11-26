"""One-shot Conductor watchdog; never starts, resumes or replaces a writer."""
import argparse
import contextlib
import fcntl
import json
import math
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time
import notify

FIELDS = set("card task role session worktree branch pr dependencies decisions state progress checkpoint blockers attempts wait alert_epoch".split())


def classify(task, native, now):
    if task["state"] == "done":
        return "done"
    for flag, kind in (("permission", "permission"), ("form", "question"), ("failed", "failure"), ("unavailable", "observation_unavailable")):
        if native.get(flag):
            return kind
    if task["blockers"] or task["state"] == "blocked":
        return "blocked"
    if task["wait"]:
        return "expected_wait" if now < task["wait"]["until"] else "wait_expired"
    return "checkpoint_expired" if now >= task["checkpoint"] else "healthy"


def validate(state):
    tasks = state["tasks"]
    owners = {"session": set(), "worktree": set(), "branch": set()}
    for key, t in tasks.items():
        if set(t) != FIELDS or key != t["card"] or not re.fullmatch(r"\$[a-z0-9]+", key):
            raise ValueError("invalid assignment fields/card")
        for name in ("task", "role", "progress", "branch"):
            if not isinstance(t[name], str) or not t[name].strip():
                raise ValueError("missing assignment text")
        if not re.fullmatch(r"ses_[A-Za-z0-9]+", t["session"]):
            raise ValueError("invalid session")
        if not Path(t["worktree"]).is_absolute() or str(Path(t["worktree"]).resolve()) != t["worktree"]:
            raise ValueError("worktree must be canonical absolute path")
        if t["state"] not in {"working", "blocked", "done"} or type(t["checkpoint"]) not in (int, float) or not math.isfinite(t["checkpoint"]):
            raise ValueError("invalid state/checkpoint")
        for name in ("attempts", "alert_epoch"):
            if type(t[name]) is not int or t[name] < 0:
                raise ValueError("invalid counter")
        for name in ("dependencies", "decisions", "blockers"):
            if not isinstance(t[name], list) or any(not isinstance(x, str) for x in t[name]):
                raise ValueError("invalid list")
        if t["pr"] is not None and (type(t["pr"]) is not int or t["pr"] < 1):
            raise ValueError("PR must be a positive number or null")
        wait = t["wait"]
        if wait is not None and (set(wait) != {"kind", "until"} or wait["kind"] not in {"ci", "command", "external"} or type(wait["until"]) not in (int, float) or not math.isfinite(wait["until"])):
            raise ValueError("invalid bounded wait")
        if t["state"] != "done":
            for field, used in owners.items():
                if t[field] in used:
                    raise ValueError("duplicate writer assignment")
                used.add(t[field])
        if any(d not in tasks or d == key for d in t["dependencies"]):
            raise ValueError("unknown/self dependency")
    def visit(key, seen):
        if key in seen:
            raise ValueError("cyclic dependencies")
        for dependency in tasks[key]["dependencies"]:
            visit(dependency, seen | {key})
    for key in tasks:
        visit(key, set())


def atomic_write(path, value):
    fd, temporary = tempfile.mkstemp(prefix=".watchdog-", dir=path.parent)
    try:
        with os.fdopen(fd, "w") as output:
            json.dump(value, output, indent=2, allow_nan=False)
            output.write("\n")
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, path)
        directory = os.open(path.parent, os.O_DIRECTORY)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


@contextlib.contextmanager
def locked(path):
    path = Path(path).absolute()
    if not path.parent.is_dir() or path.parent.stat().st_mode & 0o077:
        raise ValueError("create private state directory (0700) first")
    fd = os.open(str(path) + ".lock", os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        if path.is_symlink():
            raise ValueError("state cannot be a symlink")
        state = json.loads(path.read_text()) if path.exists() else {"version": 1, "revision": 0, "tasks": {}, "alerts": {}, "observations": {}}
        if state["version"] != 1:
            raise ValueError("unknown state version")
        validate(state)
        yield state, lambda: atomic_write(path, state)


def api(path):
    result = subprocess.run(["opencode", "api", "get", path], capture_output=True, text=True, timeout=10, check=True)
    return json.loads(result.stdout)["data"]


def observe(session, request=api):
    """Two read-only attempts, targeted routes only; never reads messages/logs."""
    for attempt in range(2):
        try:
            info = request("/api/session/" + session)
            permissions = request("/api/session/" + session + "/permission")
            forms = request("/api/session/" + session + "/form")
            timing = info["time"]
            return dict(permission=bool(permissions), form=bool(forms),
                        failed=info.get("outcome") == "failed",
                        busy=timing.get("busy", 0) > timing.get("idle", 0),
                        read_attempts=attempt + 1)
        except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError):
            pass
    return {"unavailable": True, "read_attempts": 2}


def check(state, persist, config=None, observer=observe, now=None):
    now = time.time() if now is None else now
    results = {}
    for card, task in state["tasks"].items():
        if task["state"] == "done":
            results[card] = {"kind": "done"}
            continue
        native = observer(task["session"])
        kind = classify(task, native, now)
        if kind == "healthy" and any(state["tasks"][d]["state"] != "done" for d in task["dependencies"]):
            kind = "dependency_wait"
        state["observations"][card] = {"kind": kind, "at": now, **native}
        results[card] = {"kind": kind}
        persist()
        if kind in notify.KINDS:
            incident = {"id": f'{card}:{task["session"]}:{task["alert_epoch"]}:{kind}',
                        "kind": kind, "card": card, "pr": task["pr"]}
            try:
                results[card]["notification"] = notify.deliver(config, incident, state["alerts"], persist)
            except (ValueError, OSError, KeyError, TypeError):
                results[card]["notification"] = "configuration_error"
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--state", required=True)
    sub = parser.add_subparsers(dest="command", required=True)
    put = sub.add_parser("put", help="atomic full assignment; optimistic revision required")
    put.add_argument("--revision", required=True, type=int)
    put.add_argument("--assignment", required=True, help="private JSON file")
    sub.add_parser("show")
    run = sub.add_parser("check")
    run.add_argument("--notify-config")
    args = parser.parse_args()
    with locked(args.state) as (state, persist):
        if args.command == "put":
            if state["revision"] != args.revision:
                raise ValueError("stale revision; reread state")
            assignment = json.loads(Path(args.assignment).read_text())
            old = state["tasks"].get(assignment["card"])
            if old and old["state"] != "done" and any(old[k] != assignment[k] for k in ("session", "worktree", "branch")):
                raise ValueError("writer handoff requires old assignment marked done after fencing")
            state["tasks"][assignment["card"]] = assignment
            validate(state)
            state["revision"] += 1
            persist()
            print(json.dumps({"revision": state["revision"]}))
        elif args.command == "check":
            print(json.dumps(check(state, persist, args.notify_config), sort_keys=True))
        else:
            print(json.dumps(state, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, TypeError):
        raise SystemExit("Watchdog stopped: invalid/inaccessible state or configuration; inspect locally.")
