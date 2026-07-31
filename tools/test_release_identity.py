from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class ReleaseIdentityTests(unittest.TestCase):
    def read(self, relative: str) -> str:
        return (ROOT / relative).read_text(encoding="utf-8-sig")

    def test_compatibility_host_forwards_headless_arguments_by_contract_name(self) -> None:
        source = self.read("src/WinCare/Core/00-12-HostCompatibility.ps1")
        self.assertIn(
            "Invoke-WinCareHeadlessCommand -Command $Command -Arguments $Parameters",
            source,
        )
        self.assertNotIn(
            "Invoke-WinCareHeadlessCommand -Command $Command -Parameters $Parameters",
            source,
        )

    def test_standalone_projects_embed_the_canonical_product_icon(self) -> None:
        props = self.read("src/WinCare/Standalone/Directory.Build.props")
        build = self.read("tools/Build-Exe.ps1")
        self.assertIn("<WinCareApplicationIcon>", props)
        self.assertIn("<ApplicationIcon>$(WinCareApplicationIcon)</ApplicationIcon>", props)
        self.assertIn("WinCareApplicationIcon", build)
        self.assertIn("IconSha256", build)
        self.assertIn("IconFrameSizes", build)
        self.assertIn("ExtractAssociatedIcon", build)
        self.assertIn("ExpectedIconPath", build)
        self.assertIn("EmbeddedIconPixelSha256", build)
        self.assertIn("icon pixels do not match", build)


if __name__ == "__main__":
    unittest.main()
