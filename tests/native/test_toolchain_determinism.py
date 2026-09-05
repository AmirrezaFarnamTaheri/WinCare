from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ToolchainDeterminismTests(unittest.TestCase):
    def test_dotnet_sdk_is_strictly_pinned(self) -> None:
        global_json = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))
        sdk = global_json["sdk"]

        self.assertEqual("8.0.416", sdk["version"])
        self.assertEqual("disable", sdk["rollForward"])
        self.assertFalse(sdk["allowPrerelease"])

    def test_ci_provisions_the_pinned_dotnet_sdk(self) -> None:
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        self.assertIn("global-json-file: global.json", workflow)
        self.assertIn("dotnet-version: 8.0.416", workflow)


if __name__ == "__main__":
    unittest.main()
