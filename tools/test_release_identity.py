from __future__ import annotations

import hashlib
import json
import struct
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BRAND_DIRECTORY = ROOT / "src" / "WinCare" / "Data" / "Gui"
SHORTCUTS = ROOT / "src" / "WinCare" / "Install" / "Private" / "30-Shortcuts.ps1"


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

    def test_brand_manifest_closes_every_canonical_asset(self) -> None:
        manifest_path = BRAND_DIRECTORY / "WinCare.Brand.json"
        self.assertTrue(manifest_path.is_file())
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(manifest["schema"], "wincare.brand/v1")
        self.assertEqual(manifest["name"], "WinCare")
        self.assertEqual(
            set(manifest["assets"]),
            {"markSvg", "logoSvg", "logoPng", "appPng", "appIcon"},
        )
        for record in manifest["assets"].values():
            asset = BRAND_DIRECTORY / record["path"]
            self.assertTrue(asset.is_file(), asset)
            digest = hashlib.sha256(asset.read_bytes()).hexdigest()
            self.assertEqual(digest, record["sha256"], asset.name)

    def test_brand_icon_contains_the_required_windows_frames(self) -> None:
        manifest = json.loads(
            (BRAND_DIRECTORY / "WinCare.Brand.json").read_text(encoding="utf-8")
        )
        icon = (BRAND_DIRECTORY / "WinCare.ico").read_bytes()
        reserved, icon_type, count = struct.unpack_from("<HHH", icon, 0)
        self.assertEqual((reserved, icon_type), (0, 1))
        observed: list[int] = []
        for index in range(count):
            offset = 6 + (16 * index)
            observed.append(icon[offset] or 256)
        required = [16, 24, 32, 48, 64, 128, 256]
        self.assertEqual(observed, required)
        self.assertEqual(manifest["assets"]["appIcon"]["frames"], required)

    def test_installer_shortcuts_bind_the_logo_bearing_executables(self) -> None:
        source = SHORTCUTS.read_text(encoding="utf-8")
        self.assertIn("ExpectedIconLocation", source)
        self.assertIn("$shortcut.IconLocation", source)
        self.assertIn("IconLocation", source)
        self.assertIn("WinCare-GUI.exe", source)
        self.assertIn("WinCare-TUI.exe", source)


if __name__ == "__main__":
    unittest.main()
