"""Offline Bridge regression tests ($16c); never calls a live merge endpoint."""
import copy
import datetime as dt
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("bridge", ROOT / "scripts/integrate-pr.py")
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)


class IntegrationTests(unittest.TestCase):
    def setUp(self):
        self.snapshot = json.loads((ROOT / "tests/tooling/fixtures/integration/ready.json").read_text())
        self.policy = json.loads((ROOT / "scripts/integration-authorizations.json").read_text())
        self.now = dt.datetime(2026, 11, 26, 11, tzinfo=dt.timezone.utc)

    def validate(self):
        return bridge.validate(self.snapshot, self.policy, self.now)

    def test_ready(self):
        self.assertEqual(self.validate()["card"], "$16c")

    def test_refuse_unsafe_inputs(self):
        mutations = [
            lambda s: s["pr"]["base"].update(ref="main"),
            lambda s: s["pr"]["head"]["repo"].update(full_name="attacker/lexscholar"),
            lambda s: s["pr"]["head"].update(ref="unknown"),
            lambda s: s["pr"].update(state="closed"),
            lambda s: s["pr"].update(draft=True),
            lambda s: s["pr"].update(mergeable=None),
            lambda s: s["pr"].update(title="chore: wrong card ($16a)"),
            lambda s: s["files"].append({"filename": "src/Unsafe.fs"}),
            lambda s: s["files"][0].update(previous_filename="src/Unsafe.fs"),
            lambda s: s.update(files=[]),
            lambda s: s.update(checks=[]),
            lambda s: s["checks"].append(copy.deepcopy(s["checks"][0])),
            lambda s: s["checks"][0].update(head_sha="c" * 40),
            lambda s: s["checks"][0]["app"].update(id=1),
            lambda s: s["checks"][0].update(status="in_progress"),
            lambda s: s["checks"][0].update(conclusion="failure"),
            lambda s: s["checks"][0].update(conclusion="cancelled"),
            lambda s: s["checks"][0].update(completed_at="2026-11-25T00:00:00Z"),
            lambda s: s["checks"][0].update(started_at="2026-11-26T08:00:00Z"),
            lambda s: s["checks"][0].update(completed_at="2026-11-27T00:00:00Z"),
            lambda s: s["reviews"].append({"id": 10, "user": {"login": "owner"}, "state": "CHANGES_REQUESTED", "submitted_at": "2026-11-26T10:00:00Z"}),
            lambda s: s["threads"].append({"id": "thread1", "isResolved": False}),
            lambda s: s["comments"].append({"id": 12, "body": "[P1] unsafe", "user": {"login": "greptile[bot]"}}),
        ]
        original = copy.deepcopy(self.snapshot)
        for mutation in mutations:
            with self.subTest(mutation=mutations.index(mutation)):
                self.snapshot = copy.deepcopy(original)
                mutation(self.snapshot)
                with self.assertRaises(bridge.Blocked):
                    self.validate()

    def test_exception_is_exact_and_cannot_override_gates(self):
        self.snapshot["comments"] = [{"id": 12, "body": "[P1] unsafe", "user": {"login": "greptile[bot]"}}]
        exception = {"pr": 180, "head": "a" * 40, "kind": "comment", "id": 12, "reason": "Owner accepted risk"}
        self.policy["exceptions"] = [exception]
        self.assertEqual(self.validate()["card"], "$16c")
        exception["head"] = "c" * 40
        with self.assertRaises(bridge.Blocked):
            self.validate()
        exception["head"] = "a" * 40
        self.snapshot["checks"][0]["conclusion"] = "failure"
        with self.assertRaises(bridge.Blocked):
            self.validate()

    def test_latest_approval_clears_formal_review_not_open_thread(self):
        self.snapshot["reviews"] = [
            {"id": 10, "user": {"login": "owner"}, "state": "CHANGES_REQUESTED", "submitted_at": "2026-11-26T10:00:00Z"},
            {"id": 11, "user": {"login": "owner"}, "state": "APPROVED", "submitted_at": "2026-11-26T10:30:00Z"}]
        self.validate()
        self.snapshot["threads"] = [{"id": "thread1", "isResolved": False}]
        with self.assertRaises(bridge.Blocked):
            self.validate()

    def test_comment_does_not_clear_changes_requested(self):
        self.snapshot["reviews"] = [
            {"id": 10, "user": {"login": "owner"}, "state": "CHANGES_REQUESTED", "submitted_at": "2026-11-26T10:00:00Z"},
            {"id": 11, "user": {"login": "owner"}, "state": "COMMENTED", "submitted_at": "2026-11-26T10:30:00Z"}]
        with self.assertRaises(bridge.Blocked):
            self.validate()

    def test_scope_wildcard_does_not_cross_directory_boundary(self):
        self.snapshot["files"] = [{"filename": "tests/tooling/fixtures/integration/nested/unapproved.json"}]
        with self.assertRaises(bridge.Blocked):
            self.validate()

    def orchestration(self, execute=False, snapshots=None, release=None, api=None):
        original_validate = bridge.validate
        with (patch.object(bridge, "trusted_policy", return_value=self.policy),
              patch.object(bridge, "read_snapshot", side_effect=snapshots or [self.snapshot, self.snapshot]),
              patch.object(bridge, "release_sha", side_effect=release or ["b" * 40] * 10),
              patch.object(bridge, "verify_diff", return_value="expected-tree"),
              patch.object(bridge, "validate", side_effect=lambda s, p, n: original_validate(s, p, self.now)),
              patch.object(bridge, "api", side_effect=api) as request):
            result = bridge.integrate(180, execute)
            return result, request

    def test_dry_run_has_no_mutation_or_receipt(self):
        result, request = self.orchestration()
        self.assertEqual(result["status"], "validated-only")
        self.assertNotIn("receipt", result)
        request.assert_not_called()

    def test_changed_head_or_base_prevents_merge(self):
        changed = copy.deepcopy(self.snapshot)
        changed["pr"]["head"]["sha"] = "c" * 40
        for c in changed["checks"]:
            c["head_sha"] = "c" * 40
        with self.assertRaises(bridge.Blocked):
            self.orchestration(True, [self.snapshot, changed])
        with self.assertRaises(bridge.Blocked):
            self.orchestration(True, release=["b" * 40, "c" * 40])

    def test_base_change_after_validation_blocks_before_request(self):
        with self.assertRaisesRegex(bridge.Blocked, "release changed"):
            self.orchestration(True, release=["b" * 40, "b" * 40, "b" * 40, "c" * 40])

    def test_base_change_during_request_has_no_receipt(self):
        merged = copy.deepcopy(self.snapshot["pr"])
        merged.update(merged=True, merge_commit_sha="d" * 40)
        def request(path, method="GET", payload=None):
            if method == "PUT":
                return {"merged": True, "sha": "d" * 40}
            if "/git/commits/" in path:
                return {"parents": [{"sha": "c" * 40}], "message": merged["title"],
                        "tree": {"sha": "expected-tree"}}
            return merged
        with self.assertRaisesRegex(bridge.Blocked, "UNCERTAIN"):
            self.orchestration(True, release=["b" * 40] * 4, api=request)

    def test_cas_squash_and_verified_receipt(self):
        merged = copy.deepcopy(self.snapshot["pr"])
        merged.update(merged=True, merge_commit_sha="d" * 40)
        calls = []
        def request(path, method="GET", payload=None):
            calls.append((path, method, payload))
            if method == "PUT":
                return {"merged": True, "sha": "d" * 40}
            if "/git/commits/" in path:
                return {"parents": [{"sha": "b" * 40}], "message": self.snapshot["pr"]["title"],
                        "tree": {"sha": "expected-tree"}}
            return merged
        result, _ = self.orchestration(True, release=["b" * 40] * 4 + ["d" * 40], api=request)
        self.assertEqual(result["receipt"], "verified-squash")
        self.assertEqual(calls[0][2]["sha"], "a" * 40)
        self.assertEqual(calls[0][2]["merge_method"], "squash")

    def test_uncertain_merge_no_automatic_retry(self):
        import subprocess
        with self.assertRaisesRegex(bridge.Blocked, "UNCERTAIN"):
            self.orchestration(True, api=subprocess.TimeoutExpired("gh", 60))

    def test_malformed_merge_response_is_uncertain(self):
        with self.assertRaisesRegex(bridge.Blocked, "UNCERTAIN"):
            self.orchestration(True, api=lambda *args: {})

    def test_unverified_merge_never_gets_receipt(self):
        merged = copy.deepcopy(self.snapshot["pr"])
        merged.update(merged=True, merge_commit_sha="d" * 40)
        with patch.object(bridge, "api", side_effect=[
                merged, {"parents": [{"sha": "c" * 40}], "message": merged["title"]}]):
            with self.assertRaisesRegex(bridge.Blocked, "UNCERTAIN"):
                bridge.verify_merge(180, self.validate(), "d" * 40)

    def test_wrong_result_tree_blocks_receipt(self):
        merged = copy.deepcopy(self.snapshot["pr"])
        merged.update(merged=True, merge_commit_sha="d" * 40)
        with (patch.object(bridge, "api", side_effect=[
                merged, {"parents": [{"sha": "b" * 40}], "message": merged["title"],
                         "tree": {"sha": "wrong-tree"}}]),
              patch.object(bridge, "release_sha", return_value="d" * 40)):
            with self.assertRaises(bridge.Blocked):
                bridge.verify_merge(180, {**self.validate(), "tree": "expected-tree"}, "d" * 40)


if __name__ == "__main__":
    unittest.main()
