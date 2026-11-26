#!/usr/bin/env python3
"""Release-owned Bridge. No third-party Python dependencies."""
import argparse
import datetime as dt
import fnmatch
import json
import re
import subprocess
import sys
import time
from pathlib import Path

REPO = "thyvini/lexscholar"
RELEASE = "release/0.5.3"


class Blocked(Exception):
    pass


def matches_path(path, pattern):
    parts, patterns = path.split("/"), pattern.split("/")
    return (len(parts) == len(patterns)
            and all(fnmatch.fnmatchcase(part, allowed) for part, allowed in zip(parts, patterns)))


def validate(snapshot, policy, now):
    """Pure decision; all remotely supplied metadata is untrusted."""
    def require(condition, reason):
        if not condition:
            raise Blocked(reason)
    p = snapshot["pr"]
    require(policy["repository"] == REPO and policy["release"] == RELEASE, "wrong policy")
    require(p["head"]["repo"]["full_name"] == REPO
            and p["base"]["repo"]["full_name"] == REPO, "foreign repository")
    require(p["base"]["ref"] == RELEASE, "wrong release")
    require(p["state"] == "open" and not p["draft"] and not p["merged"], "PR not open/ready")
    require(p["mergeable"] is True and p["mergeable_state"] == "clean", "mergeability not clean")
    slice_ = policy["slices"].get(p["head"]["ref"])
    require(slice_ is not None, "unauthorized branch")
    require(re.fullmatch(r"(?:chore|docs|test|fix|feat|ci)(?:\([^)]+\))?: [^\n]+", p["title"])
            and re.findall(r"\$[0-9a-z]+", p["title"]) == [slice_["card"]], "subject/card mismatch")
    require(bool(snapshot["files"]), "empty diff")
    for f in snapshot["files"]:
        for path in [f["filename"]] + ([f["previous_filename"]] if "previous_filename" in f else []):
            require(not path.startswith("/") and ".." not in path.split("/")
                    and any(matches_path(path, pattern) for pattern in slice_["files"]),
                    "out-of-scope file: " + path)
    head = p["head"]["sha"]
    for name, app in [("build-and-test", 15368), ("gpt-review", 5758986)]:
        checks = [c for c in snapshot["checks"] if c["name"] == name]
        require(len(checks) == 1, name + ": missing/ambiguous")
        c = checks[0]
        require(c["app"]["id"] == app and c["head_sha"] == head, name + ": wrong app/head")
        require(c["status"] == "completed" and c["conclusion"] == "success", name + ": not successful")
        started, completed = timestamp(c["started_at"]), timestamp(c["completed_at"])
        require(timestamp(snapshot["head_date"]) <= started <= completed <= now
                and (now - completed).total_seconds() <= policy["max_gate_age_seconds"],
                name + ": stale/invalid timestamp")
    def excepted(kind, identifier):
        return any(e["pr"] == p["number"] and e["head"] == head and e["kind"] == kind
                   and str(e["id"]) == str(identifier) and bool(e["reason"].strip())
                   for e in policy["exceptions"])
    latest = {}
    for review in sorted(snapshot["reviews"], key=lambda r: r["submitted_at"] or ""):
        if review["state"] in ("APPROVED", "CHANGES_REQUESTED", "DISMISSED"):
            latest[review["user"]["login"]] = review
    for review in latest.values():
        require(review["state"] != "CHANGES_REQUESTED" or excepted("review", review["id"]),
                "blocking review: " + str(review["id"]))
    for thread in snapshot["threads"]:
        require(thread["isResolved"] or excepted("thread", thread["id"]),
                "unresolved review thread: " + thread["id"])
    for comment in snapshot["comments"]:
        if comment["user"]["login"] == "thyvinibots-pull-request-reviewer[bot]":
            continue
        require(not re.search(r"\bP[01]\b", comment["body"], re.I)
                or excepted("comment", comment["id"]), "blocking comment: " + str(comment["id"]))
    return {"card": slice_["card"], "head": head, "base": p["base"]["sha"], "subject": p["title"]}


def timestamp(value):
    return dt.datetime.fromisoformat(value.replace("Z", "+00:00"))


def run(*args):
    return subprocess.check_output(args, text=True, timeout=60).strip()


def api(path, method="GET", payload=None):
    args = ["gh", "api", "--hostname", "github.com", "-X", method, path]
    if payload is not None:
        result = subprocess.run(args + ["--input", "-"], input=json.dumps(payload),
                                text=True, capture_output=True, timeout=60, check=True)
        return json.loads(result.stdout)
    return json.loads(run(*args))


def pages(path, field=None):
    result = []
    for page in range(1, 101):
        data = api(path + ("&" if "?" in path else "?") + f"per_page=100&page={page}")
        batch = data[field] if field else data
        result.extend(batch)
        if len(batch) < 100:
            return result
    raise Blocked("pagination limit exceeded")


def read_snapshot(pr):
    prefix = f"repos/{REPO}"
    p = api(f"{prefix}/pulls/{pr}")
    head = p["head"]["sha"]
    query = """query($owner:String!,$name:String!,$number:Int!) {
      repository(owner:$owner,name:$name) { pullRequest(number:$number) {
        reviewThreads(first:100) { pageInfo { hasNextPage } nodes { id isResolved } }
      } } }"""
    threads = api("graphql", "POST", {"query": query, "variables": {
        "owner": REPO.split("/")[0], "name": REPO.split("/")[1], "number": pr}})
    threads = threads["data"]["repository"]["pullRequest"]["reviewThreads"]
    if threads["pageInfo"]["hasNextPage"]:
        raise Blocked("too many review threads")
    return {"pr": p, "files": pages(f"{prefix}/pulls/{pr}/files"),
            "checks": pages(f"{prefix}/commits/{head}/check-runs?filter=all", "check_runs"),
            "head_date": api(f"{prefix}/commits/{head}")["commit"]["committer"]["date"],
            "reviews": pages(f"{prefix}/pulls/{pr}/reviews"),
            "comments": pages(f"{prefix}/issues/{pr}/comments"), "threads": threads["nodes"]}


def release_sha():
    return api(f"repos/{REPO}/git/ref/heads/{RELEASE}")["object"]["sha"]


def trusted_policy(base):
    remote = run("git", "remote", "get-url", "origin")
    if remote not in (f"git@github.com:{REPO}.git", f"https://github.com/{REPO}.git",
                      f"https://github.com/{REPO}"):
        raise Blocked("origin is not the canonical repository")
    run("git", "fetch", "--quiet", "--no-write-fetch-head", "origin", base)
    current_release = release_sha()
    run("git", "fetch", "--quiet", "--no-write-fetch-head", "origin", current_release)
    # Recovery must not load a caller-supplied PR commit as trusted policy.
    run("git", "merge-base", "--is-ancestor", base, current_release)
    trusted_script = run("git", "show", f"{base}:scripts/integrate-pr.py")
    if trusted_script != Path(__file__).read_text().strip():
        raise Blocked("run the Bridge script from trusted release state (bootstrap required)")
    return json.loads(run("git", "show", f"{base}:scripts/integration-authorizations.json"))


def verify_diff(snapshot, base):
    head = snapshot["pr"]["head"]["sha"]
    run("git", "fetch", "--quiet", "--no-write-fetch-head", "origin", head)
    paths = run("git", "diff", "--no-renames", "--name-only", "-z", f"{base}...{head}").split("\0")
    actual = set(filter(None, paths))
    advertised = {f["filename"] for f in snapshot["files"]}
    advertised.update(f["previous_filename"] for f in snapshot["files"] if "previous_filename" in f)
    if actual != advertised:
        raise Blocked("actual Git diff differs from GitHub files")
    return run("git", "merge-tree", "--write-tree", base, head).splitlines()[0]


def verify_merge(pr, decision, response_sha):
    p = api(f"repos/{REPO}/pulls/{pr}")
    sha = p["merge_commit_sha"]
    if not p["merged"] or p["head"]["sha"] != decision["head"] or sha != response_sha:
        raise Blocked("UNCERTAIN: PR merge/head/result not verified; do not retry merge blindly")
    commit = api(f"repos/{REPO}/git/commits/{sha}")
    if ([parent["sha"] for parent in commit["parents"]] != [decision["base"]]
            or commit["message"].splitlines()[0] != decision["subject"]
            or commit["tree"]["sha"] != decision["tree"]
            or release_sha() != sha):
        raise Blocked("UNCERTAIN: squash parent/subject/release result mismatch; no card receipt")
    return {"receipt": "verified-squash", "repository": REPO, "release": RELEASE,
            "pr": pr, "head": decision["head"], "base": decision["base"],
            "merge": sha, "card": decision["card"]}


def integrate(pr, execute=False, wait=0):
    base = release_sha()
    policy = trusted_policy(base)
    deadline = time.monotonic() + wait
    expected_head = None
    while True:
        snapshot = read_snapshot(pr)
        head = snapshot["pr"]["head"]["sha"]
        if expected_head is not None and expected_head != head:
            raise Blocked("head changed; restart validation explicitly")
        expected_head = head
        if release_sha() != base or snapshot["pr"]["base"]["sha"] != base:
            raise Blocked("release changed; restart validation explicitly")
        try:
            decision = validate(snapshot, policy, dt.datetime.now(dt.timezone.utc))
            break
        except Blocked as error:
            print(f"BLOCKED head={head} base={base}: {error}", file=sys.stderr, flush=True)
            if time.monotonic() >= deadline:
                raise
            time.sleep(min(10, max(0, deadline - time.monotonic())))
    tree = verify_diff(snapshot, base)
    if not execute:
        return {"status": "validated-only", **decision}
    fresh = read_snapshot(pr)
    current = validate(fresh, policy, dt.datetime.now(dt.timezone.utc))
    if current != decision or release_sha() != base:
        raise Blocked("head/base/subject changed before merge")
    decision["tree"] = tree
    # Save this line as intent.json for read-only recovery after uncertain transport.
    print("MERGE_INTENT " + json.dumps({"pr": pr, **decision}), file=sys.stderr, flush=True)
    if release_sha() != base:
        raise Blocked("release changed before merge request; discard intent and restart")
    try:
        response = api(f"repos/{REPO}/pulls/{pr}/merge", "PUT", {
            "sha": decision["head"], "merge_method": "squash",
            "commit_title": decision["subject"], "commit_message": ""})
    except (subprocess.SubprocessError, TimeoutError, ValueError, OSError) as error:
        raise Blocked("UNCERTAIN: merge request lost; inspect PR and release; no receipt/retry") from error
    if not isinstance(response, dict) or response.get("merged") not in (True, False):
        raise Blocked("UNCERTAIN: malformed merge response; inspect PR and release; no receipt/retry")
    if response["merged"] is False:
        raise Blocked("merge refused; no receipt: " + response.get("message", "unknown"))
    try:
        return verify_merge(pr, decision, response["sha"])
    except (KeyError, ValueError, subprocess.SubprocessError, OSError) as error:
        raise Blocked("UNCERTAIN: merge result could not be verified; no receipt/retry") from error


def recover(intent):
    """Read-only reconciliation; never resubmits a merge request."""
    pr = intent["pr"]
    policy = trusted_policy(intent["base"])
    snapshot = read_snapshot(pr)
    p = snapshot["pr"]
    if not p["merged"]:
        raise Blocked("not merged; discard intent and restart ordinary validation")
    result = p["merge_commit_sha"]
    # Validate authorization, files, gates and reviews normally; normalize only
    # the historical open-state fields that a successful merge changed.
    p.update(state="open", merged=False, draft=False, mergeable=True, mergeable_state="clean")
    p["base"]["sha"] = intent["base"]
    decision = validate(snapshot, policy, dt.datetime.now(dt.timezone.utc))
    decision["tree"] = verify_diff(snapshot, intent["base"])
    if decision != {k: v for k, v in intent.items() if k != "pr"}:
        raise Blocked("recovery intent differs from independently validated result")
    return verify_merge(pr, decision, result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pr", type=int, nargs="?")
    parser.add_argument("--recover", type=Path, help="read-only verify saved MERGE_INTENT JSON")
    parser.add_argument("--execute", action="store_true", help="perform authorized squash merge")
    parser.add_argument("--wait", type=int, default=0, help="bounded remote gate wait, 0..1800 seconds")
    args = parser.parse_args()
    if ((args.recover is None and (args.pr is None or args.pr < 1))
            or not 0 <= args.wait <= 1800
            or (args.recover is not None and (args.pr is not None or args.execute or args.wait))):
        parser.error("positive PR required; --wait must be 0..1800")
    try:
        result = recover(json.loads(args.recover.read_text())) if args.recover else integrate(args.pr, args.execute, args.wait)
        print(json.dumps(result, sort_keys=True))
    except (Blocked, KeyError, ValueError, subprocess.SubprocessError, OSError) as error:
        print("BLOCKED: " + str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
