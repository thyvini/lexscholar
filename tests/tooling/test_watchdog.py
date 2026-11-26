"""Offline $16e regression tests (stdlib unittest)."""
import importlib.util
import pathlib
import sys
import tempfile
import unittest
from unittest.mock import patch
import json
import os
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import watchdog
import notify


def task(**changes):
    result = dict(card="$16e", task="watchdog", role="developer", session="ses_test",
                  worktree="/tmp/opencode/test", branch="chore/test", pr=None,
                  dependencies=[], decisions=[], state="working", progress="tests",
                  checkpoint=100, blockers=[], attempts=0, wait=None, alert_epoch=0)
    result.update(changes)
    return result


class ClassificationTests(unittest.TestCase):
    def test_expired_is_not_failure(self):
        self.assertEqual("checkpoint_expired", watchdog.classify(task(), {"busy": True}, 101))

    def test_native_permission_and_failure(self):
        self.assertEqual("permission", watchdog.classify(task(), {"permission": True}, 50))
        self.assertEqual("failure", watchdog.classify(task(), {"failed": True}, 50))

    def test_expected_wait_and_wait_expiry(self):
        for kind in ("ci", "command", "external"):
            t = task(wait={"kind": kind, "until": 200})
            self.assertEqual("expected_wait", watchdog.classify(t, {}, 150))
            self.assertEqual("wait_expired", watchdog.classify(t, {}, 201))

    def test_dependencies_do_not_stop_independent_task(self):
        self.assertEqual("healthy", watchdog.classify(task(checkpoint=200), {}, 100))


class NotifyTests(unittest.TestCase):
    def test_unconfigured_is_not_delivery(self):
        alerts = {}
        self.assertEqual("unconfigured", notify.deliver(None, {"id": "x"}, alerts, lambda: None))
        self.assertEqual({}, alerts)

    def test_uncertain_is_persisted_before_send_and_never_resent(self):
        alerts, snapshots, sends = {}, [], []
        incident = dict(id="incident", kind="failure", card="$16e", pr=180)
        def send(config, message):
            self.assertEqual("send_uncertain", snapshots[-1]["incident"])
            sends.append(message)
            raise TimeoutError()
        with patch.object(notify, "configuration", return_value={}):
            for _ in range(2):
                self.assertEqual("send_uncertain", notify.deliver("configured", incident, alerts, lambda: snapshots.append(alerts.copy()), send))
        self.assertEqual(1, len(sends))
        self.assertIn("Question:", sends[0])
        self.assertIn("Impact:", sends[0])
        self.assertIn("PR: 180", sends[0])

    def test_success_deduplicates(self):
        alerts, sends = {}, []
        with patch.object(notify, "configuration", return_value={}):
            for _ in range(2):
                result = notify.deliver("configured", dict(id="i", kind="permission", card="$16e", pr=None), alerts, lambda: None, lambda c, m: sends.append(m))
                self.assertEqual("delivered", result)
        self.assertEqual(1, len(sends))

    def test_private_https_configuration_only(self):
        with tempfile.TemporaryDirectory(prefix="watchdog-") as directory:
            path = pathlib.Path(directory) / "ntfy.json"
            config = dict(url="https://notify.example/private-topic", token="tk_" + "a" * 20, private_topic_confirmed=True)
            path.write_text(json.dumps(config))
            os.chmod(path, 0o600)
            self.assertEqual(config, notify.configuration(path))
            for url in ("http://notify.example/topic", "https://user:pass@notify.example/topic", "https://notify.example/topic?x=1"):
                path.write_text(json.dumps(dict(config, url=url)))
                with self.assertRaises(ValueError):
                    notify.configuration(path)
            path.write_text(json.dumps(config))
            os.chmod(path, 0o644)
            with self.assertRaises(ValueError):
                notify.configuration(path)

    def test_no_redirect(self):
        self.assertIsNone(notify.NoRedirect().redirect_request(None, None, 302, "", {}, "https://elsewhere.example"))


class StateTests(unittest.TestCase):
    def test_duplicate_writer_rejected(self):
        for field in ("session", "worktree", "branch"):
            second = task(card="$16f", session="ses_other", worktree="/tmp/opencode/other", branch="chore/other")
            second[field] = task()[field]
            with self.assertRaisesRegex(ValueError, "duplicate writer"):
                watchdog.validate({"tasks": {"$16e": task(), "$16f": second}})

    def test_cycles_rejected(self):
        first = task(dependencies=["$16f"])
        second = task(card="$16f", session="ses_other", worktree="/tmp/opencode/other", branch="chore/other", dependencies=["$16e"])
        with self.assertRaisesRegex(ValueError, "cyclic"):
            watchdog.validate({"tasks": {"$16e": first, "$16f": second}})

    def test_atomic_round_trip_and_permissions(self):
        with tempfile.TemporaryDirectory(prefix="watchdog-") as directory:
            path = pathlib.Path(directory) / "state.json"
            with watchdog.locked(path) as (state, persist):
                state["tasks"]["$16e"] = task()
                persist()
            with watchdog.locked(path) as (state, persist):
                self.assertEqual("watchdog", state["tasks"]["$16e"]["task"])
            self.assertEqual(0o600, path.stat().st_mode & 0o777)

    def test_bounded_read_recovery_is_targeted_and_read_only(self):
        calls = []
        def request(path):
            calls.append(path)
            raise TimeoutError()
        self.assertEqual({"unavailable": True}, watchdog.observe("ses_test", request))
        self.assertEqual([route for suffix in ("", "/permission", "/form")
                          for route in ["/api/session/ses_test" + suffix] * 2], calls)

    def test_observation_uses_native_v2_envelope_projection(self):
        routes = []
        def request(path):
            routes.append(path)
            return {"time": {"busy": 10, "idle": 5}, "outcome": "failed"} if path.endswith("ses_test") else []
        observation = watchdog.observe("ses_test", request)
        self.assertIsNone(observation["busy"])
        self.assertTrue(observation["failed"])
        self.assertEqual(["/api/session/ses_test", "/api/session/ses_test/permission", "/api/session/ses_test/form"], routes)

    def test_metadata_without_lifecycle_is_unknown_not_idle_or_healthy(self):
        def request(path):
            return {"time": {"created": 1, "updated": 1}} if path.endswith("ses_test") else []
        native = watchdog.observe("ses_test", request)
        self.assertIsNone(native["busy"])
        self.assertIsNone(native["failed"])
        self.assertEqual("observation_unavailable", watchdog.classify(task(checkpoint=200), native, 100))
        self.assertEqual("checkpoint_expired", watchdog.classify(task(checkpoint=100), native, 101))

    def test_partial_observation_preserves_permission_and_failure(self):
        for info, permissions, expected in [
            ({"time": {}, "outcome": None}, [object()], "permission"),
            ({"time": {}, "outcome": "failed"}, [], "failure"),
        ]:
            def request(path):
                if path.endswith("/form"):
                    raise TimeoutError()
                return permissions if path.endswith("/permission") else info
            native = watchdog.observe("ses_test", request)
            self.assertTrue(native["unavailable"])
            self.assertEqual(expected, watchdog.classify(task(), native, 50))

        def missing_permission(path):
            if path.endswith("/permission"):
                raise TimeoutError()
            return [object()] if path.endswith("/form") else {"time": {}}
        native = watchdog.observe("ses_test", missing_permission)
        self.assertTrue(native["unavailable"])
        self.assertEqual("question", watchdog.classify(task(), native, 50))

    def test_later_assignment_uses_time_after_earlier_observation(self):
        first = task(checkpoint=200)
        second = task(card="$16f", session="ses_other", worktree="/tmp/opencode/other",
                      branch="chore/other", checkpoint=105)
        state = dict(tasks={"$16e": first, "$16f": second}, observations={}, alerts={})
        with patch.object(watchdog.time, "time", side_effect=[100, 110]):
            result = watchdog.check(state, lambda: None, observer=lambda s: {})
        self.assertEqual("healthy", result["$16e"]["kind"])
        self.assertEqual("checkpoint_expired", result["$16f"]["kind"])

    def test_independent_work_continues_past_blocker(self):
        first = task(blockers=["owner decision"])
        second = task(card="$16f", session="ses_other", worktree="/tmp/opencode/other", branch="chore/other", checkpoint=200)
        state = dict(tasks={"$16e": first, "$16f": second}, observations={}, alerts={})
        result = watchdog.check(state, lambda: None, observer=lambda s: {}, now=150)
        self.assertEqual("blocked", result["$16e"]["kind"])
        self.assertEqual("healthy", result["$16f"]["kind"])
        self.assertEqual("unconfigured", result["$16e"]["notification"])

    def test_optimistic_revision_rejects_stale_writer(self):
        with tempfile.TemporaryDirectory(prefix="watchdog-") as directory:
            path = pathlib.Path(directory) / "state.json"
            assignment = pathlib.Path(directory) / "assignment.json"
            assignment.write_text(json.dumps(task()))
            command = [sys.executable, "-B", str(ROOT / "scripts/watchdog.py"), "--state", str(path), "put", "--revision", "0", "--assignment", str(assignment)]
            self.assertEqual(0, subprocess.run(command, capture_output=True).returncode)
            self.assertNotEqual(0, subprocess.run(command, capture_output=True).returncode)
            self.assertEqual(1, json.loads(path.read_text())["revision"])


if __name__ == "__main__":
    unittest.main()
