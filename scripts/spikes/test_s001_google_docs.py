"""Offline synthetic checks; no Google account or network is used."""

import importlib.util
from pathlib import Path
import unittest
from unittest.mock import Mock, patch

spec = importlib.util.spec_from_file_location(
    "spike", Path(__file__).with_name("s001-google-docs.py")
)
spike = importlib.util.module_from_spec(spec)
spec.loader.exec_module(spike)


def tab(text, children=()):
    return {
        "documentTab": {"body": {"content": [{"paragraph": {"elements": [
            {"textRun": {"content": text}}
        ]}}]}},
        "childTabs": list(children),
    }


class TabsTests(unittest.TestCase):
    def test_nested_child_and_top_level_markers(self):
        doc = {"tabs": [
            tab("S001_TOP_ONE", [tab("", [tab("S001_CHILD")])]),
            tab("S001_TOP_TWO"),
        ]}
        self.assertTrue(spike.check_document(doc))
        doc["tabs"][0]["childTabs"] = []
        self.assertFalse(spike.check_document(doc))

    def test_marker_in_wrong_tab_is_not_enough(self):
        self.assertFalse(spike.check_document({"tabs": [
            tab("S001_TOP_ONE S001_CHILD", [tab("")]),
            tab("S001_TOP_TWO"),
        ]}))

    @patch.object(spike.requests, "get")
    def test_read_uses_exact_id_and_all_tabs(self, get):
        get.return_value = Mock(status_code=200, json=lambda: {
            "documentId": "id",
            "tabs": [tab("S001_TOP_ONE", [tab("S001_CHILD")]), tab("S001_TOP_TWO")],
        })
        self.assertTrue(spike.read_document(Mock(token="test"), "id"))
        self.assertEqual(
            get.call_args.args[0],
            "https://docs.googleapis.com/v1/documents/id?includeTabsContent=true",
        )
        self.assertFalse(get.call_args.kwargs["allow_redirects"])


if __name__ == "__main__":
    unittest.main()
