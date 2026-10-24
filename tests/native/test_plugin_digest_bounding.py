"""Plugin digest traversal must check boundaries before descending."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


class DigestBoundaryTests(unittest.TestCase):
    def test_plugin_digest_prevents_recursive_reparse_and_unbounded_sort(self) -> None:
        text = (ROOT / "src/WinCare.Application/Plugins/PluginSecurityPolicy.cs").read_text(encoding="utf-8")
        method = text.split("public static string ComputePluginPayloadDigest", 1)[1].split(
            "public static string GetMachineTrustRoot", 1)[0]
        self.assertIn("SearchOption.TopDirectoryOnly", method)
        self.assertIn("FileAttributes.ReparsePoint", method)
        self.assertIn("entries.Count >= maxEntries", method)
        self.assertLess(method.index("entries.Count >= maxEntries"), method.index("entries.OrderBy"))
        self.assertIn("maxBytes", method)


if __name__ == "__main__":
    unittest.main()
