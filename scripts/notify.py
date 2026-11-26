"""Controlled private ntfy delivery; no arbitrary message or destination argument."""
import json
import os
from pathlib import Path
import re
import stat
import urllib.request
from urllib.parse import urlsplit

KINDS = {
    "permission": ("Approve or deny the native permission?", "Inspect the targeted session permission panel.", "Writer awaits approval."),
    "question": ("Answer the native question?", "Inspect the targeted session form.", "Writer awaits an answer."),
    "failure": ("Investigate the failed session?", "Inspect locally; fence the old writer before recovery.", "Assigned work may be incomplete."),
    "observation_unavailable": ("Restore session visibility?", "Check the local service; do not replace the writer.", "Execution state is unknown."),
    "status_unknown": ("Check the assigned writer?", "Inspect the existing writer; do not replace it based on metadata.", "Execution state is unknown."),
    "blocked": ("Resolve the recorded blocker?", "Read the private assignment and record a decision.", "Assigned work cannot proceed."),
    "checkpoint_expired": ("Request a fresh checkpoint?", "Check the existing writer; expiry is not failure.", "Progress has not been confirmed on time."),
    "wait_expired": ("Reconcile the expected wait?", "Check CI, command or external result and renew the checkpoint.", "The declared wait budget elapsed."),
    "quota_limit": ("Check ChatGPT usage?", "Inspect ChatGPT Settings > Usage before retrying plan requests.", "A ChatGPT plan or app-specific usage limit blocked a request; the reset time is unknown."),
}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def configuration(path):
    path = Path(path).absolute()
    if any((parent / ".git").exists() for parent in path.resolve().parents):
        raise ValueError("credentials must be outside repositories")
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(fd) as source:
        info = os.fstat(source.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
            raise ValueError("credentials must be owner-only")
        config = json.load(source)
    if set(config) != {"url", "token", "private_topic_confirmed"} or config["private_topic_confirmed"] is not True:
        raise ValueError("explicit private ACL confirmation required")
    url = urlsplit(config["url"])
    if url.scheme != "https" or not url.hostname or url.username or url.password or url.query or url.fragment or not re.fullmatch(r"/[A-Za-z0-9_-]+", url.path):
        raise ValueError("one fixed HTTPS topic required")
    if not re.fullmatch(r"[A-Za-z0-9_-]{16,512}", config["token"]):
        raise ValueError("invalid token")
    return config


def send(config, message):
    request = urllib.request.Request(config["url"], data=message.encode("utf-8"),
                                     headers={"Authorization": "Bearer " + config["token"],
                                              "Content-Type": "text/plain; charset=utf-8"}, method="POST")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    with opener.open(request, timeout=10) as response:
        if not 200 <= response.status < 300:
            raise ValueError("notification not acknowledged")


def deliver(config_path, incident, alerts, persist, transport=None):
    if not config_path:
        return "unconfigured"
    config = configuration(config_path)
    if incident["id"] in alerts:
        return alerts[incident["id"]]
    kind, card, pr = incident["kind"], incident["card"], incident["pr"]
    if kind not in KINDS or not re.fullmatch(r"\$[a-z0-9]+", card) or (pr is not None and (type(pr) is not int or pr < 1)):
        raise ValueError("invalid controlled incident")
    question, recommendation, impact = KINDS[kind]
    message = f"Kind: {kind}\nQuestion: {question}\nRecommendation: {recommendation}\nImpact: {impact}\nCard: {card}\nPR: {pr or 'not opened'}"
    # Persist BEFORE network I/O: crash/timeout remains uncertain, never retried.
    alerts[incident["id"]] = "send_uncertain"
    persist()
    try:
        (transport or send)(config, message)
    except Exception:
        return "send_uncertain"
    alerts[incident["id"]] = "delivered"
    persist()
    return "delivered"
