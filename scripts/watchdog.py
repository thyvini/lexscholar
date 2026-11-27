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
import quota_mail

FIELDS = set("card task role session worktree branch pr dependencies decisions state progress checkpoint blockers attempts wait alert_epoch".split())


def quota_limit(message):
    try:
        if message["type"] != "assistant" or message["model"]["providerID"] != "openai":
            return False
        body = json.loads(message["error"]["response"]["body"])
        return isinstance(body, dict) and isinstance(body.get("error"), dict) and body["error"].get("code") == "subscription_sharing_usage_limit_exceeded"
    except (KeyError, TypeError, ValueError):
        return False


def quota_notify(state, persist, config, card, session, message_id, transport=None):
    quota = state.get("quota", {"epoch": 0, "baseline": {}})
    if quota["baseline"].get(session) == message_id:
        return "baseline"
    return quota_mail.deliver(config, f'quota:{quota["epoch"]}', state["alerts"], persist, transport)


def quota_latest(session, request=None):
    # Assigned sessions only. Do not persist or return content or response bodies.
    request = request or api
    messages = request("/api/session/" + session + "/message?type=assistant&order=desc&limit=1")
    if not isinstance(messages, list) or len(messages) > 1:
        raise ValueError("invalid message page")
    if not messages:
        return None, False
    message = messages[0]
    if not isinstance(message, dict) or not isinstance(message.get("id"), str) or not re.fullmatch(r"msg_[A-Za-z0-9]+", message["id"]):
        raise ValueError("invalid message identity")
    return message["id"], quota_limit(message)


def active_sessions():
    active = api("/api/session/active")
    if not isinstance(active, dict):
        raise ValueError("invalid active sessions")
    return active


def quota_check(state, persist, config, latest=quota_latest, active=active_sessions):
    if "quota" not in state:
        return {"quota": "reset_required"}
    results = {}
    for card, task in state["tasks"].items():
        if task["state"] == "done":
            continue
        try:
            message_id, limited = latest(task["session"])
            if not limited:
                results[card] = "no_signal"
            elif task["session"] in active():
                results[card] = "running"
            else:
                results[card] = quota_notify(state, persist, config, card, task["session"], message_id)
        except (ValueError, OSError, KeyError, TypeError, subprocess.SubprocessError):
            results[card] = "unavailable"
    return results


def quota_reset(state, persist, latest=quota_latest, active=active_sessions):
    baseline = {}
    running = active()
    for task in state["tasks"].values():
        if task["state"] != "done":
            if task["session"] in running:
                raise ValueError("cannot reset while an assigned session is running")
            message_id, limited = latest(task["session"])
            if message_id and limited:
                baseline[task["session"]] = message_id
    if any(t["session"] in active() for t in state["tasks"].values() if t["state"] != "done"):
        raise ValueError("cannot reset while an assigned session is running")
    current = state.get("quota", {"epoch": 0})
    state["quota"] = {"epoch": current["epoch"] + 1, "baseline": baseline}
    persist()


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
    if now >= task["checkpoint"]:
        return "checkpoint_expired"
    return "status_unknown" if native.get("status_unknown") else "healthy"


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
    """Retry each targeted read once, preserving independently observed signals."""
    result = {}
    root = "/api/session/" + session
    for suffix in ("", "/permission", "/form"):
        for attempt in range(2):
            try:
                value = request(root + suffix)
                if suffix == "":
                    outcome = value.get("outcome")
                    result["failed"] = None if outcome is None else outcome == "failed"
                    # Session timestamps do not prove current execution state.
                    result["busy"] = None
                    result["status_unknown"] = True
                elif suffix == "/permission":
                    result["permission"] = bool(value)
                else:
                    result["form"] = bool(value)
                break
            except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError):
                if attempt == 1:
                    result["unavailable"] = True
    return result


def check(state, persist, config=None, observer=observe, now=None):
    results = {}
    for card, task in state["tasks"].items():
        if task["state"] == "done":
            results[card] = {"kind": "done"}
            continue
        native = observer(task["session"])
        observed_at = time.time() if now is None else now
        kind = classify(task, native, observed_at)
        if kind == "healthy" and any(state["tasks"][d]["state"] != "done" for d in task["dependencies"]):
            kind = "dependency_wait"
        state["observations"][card] = {"kind": kind, "at": observed_at, **native}
        results[card] = {"kind": kind}
        persist()
        if kind in notify.KINDS:
            incident_kind = "observation_unavailable_v2" if kind == "observation_unavailable" else kind
            incident = {"id": f'{card}:{task["session"]}:{task["alert_epoch"]}:{incident_kind}',
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
    quota = sub.add_parser("quota-check", help="check latest assigned assistant errors for the known usage-limit code")
    quota.add_argument("--email-config")
    sub.add_parser("quota-reset", help="explicitly start a new quota incident after recording current message baselines")
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
        elif args.command == "quota-check":
            print(json.dumps(quota_check(state, persist, args.email_config), sort_keys=True))
        elif args.command == "quota-reset":
            quota_reset(state, persist)
            print(json.dumps({"quota": "reset"}))
        else:
            print(json.dumps(state, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, TypeError, subprocess.SubprocessError):
        raise SystemExit("Watchdog stopped: invalid/inaccessible state or configuration; inspect locally.")
