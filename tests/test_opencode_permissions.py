"""Representative project permission rules; not a shell scanner or sandbox test."""
import fnmatch
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def rules():
    text = (ROOT / "opencode.jsonc").read_text()
    return json.loads("\n".join(line for line in text.splitlines()
                               if not line.lstrip().startswith("//")))["permissions"]


def effect(action, resource):
    result = "allow"  # OpenCode V2's base policy
    for rule in rules():
        pattern = rule["resource"]
        matches = fnmatch.fnmatchcase(resource, pattern)
        if action == "shell" and pattern.endswith(" *"):
            matches |= resource == pattern[:-2]
        if fnmatch.fnmatchcase(action, rule["action"]) and matches:
            result = rule["effect"]
    return result


class PermissionsTest(unittest.TestCase):
    def test_routine_commands_and_boundaries(self):
        for command in ("git status --short", "scripts/check.sh T001",
                        "git push -u origin fix/example", "gh pr view 1",
                        "dotnet run --project src/LexScholar.Server",
                        "git commit -F /home/user/message"):
            with self.subTest(command=command):
                self.assertEqual("allow", effect("shell", command))
        for command in ("python3 --version", "curl https://example.com",
                        "rm -rf /", "git push origin unexpected"):
            with self.subTest(command=command):
                self.assertEqual("ask", effect("shell", command))
        for command in ("git push origin fix/example --force",
                        "gh pr merge 1", "scripts/gpt-review.sh 1 --post"):
            with self.subTest(command=command):
                self.assertEqual("deny", effect("shell", command))
        self.assertEqual("ask", effect("external_directory", "/etc/*"))
        self.assertEqual("allow", effect("external_directory", "/tmp/opencode/bridge/*"))


if __name__ == "__main__":
    unittest.main()
