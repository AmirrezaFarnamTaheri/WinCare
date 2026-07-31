#!/usr/bin/env python3
"""Regression contracts for WinCare brand, installer, and binary identity."""
from __future__ import annotations

import importlib.util
import json
import re
import struct
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MAX_BYTES = 32 * 1024 * 1024


def read_bounded(path: Path, maximum_bytes: int = MAX_BYTES) -> bytes:
    metadata = path.stat(follow_symlinks=False)
    if not path.is_file() or path.is_symlink() or metadata.st_size > maximum_bytes:
        raise AssertionError(f"unsafe or oversized test input: {path}")
    with path.open("rb") as handle:
        payload = handle.read(maximum_bytes + 1)
    after = path.stat(follow_symlinks=False)
    if len(payload) > maximum_bytes or len(payload) != metadata.st_size:
        raise AssertionError(f"test input size changed while reading: {path}")
    if (metadata.st_size, metadata.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise AssertionError(f"test input changed while reading: {path}")
    return payload


def read_text(path: Path, maximum_bytes: int = 8 * 1024 * 1024) -> str:
    return read_bounded(path, maximum_bytes).decode("utf-8-sig")


def load_generator():
    path = ROOT / "tools" / "generate_wincare_brand_assets.py"
    specification = importlib.util.spec_from_file_location("wincare_brand_generator_tests", path)
    if specification is None or specification.loader is None:
        raise AssertionError("Unable to load WinCare brand generator.")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


class WinCareBrandIdentityTests(unittest.TestCase):
    def test_committed_assets_are_deterministic_and_manifested(self) -> None:
        generator = load_generator()
        expected = generator.build_assets()
        for relative, payload in expected.items():
            path = ROOT / relative
            self.assertTrue(path.is_file(), relative)
            self.assertFalse(path.is_symlink(), relative)
            self.assertEqual(payload, read_bounded(path), relative)

        manifest = json.loads(read_text(ROOT / "design" / "WinCare-Brand.manifest.json"))
        self.assertEqual("wincare.brand.identity/v1", manifest["schema"])
        self.assertEqual("WinCare", manifest["product"])
        self.assertEqual(list(generator.ICON_SIZES), manifest["iconSizes"])
        self.assertGreaterEqual(len(manifest["assets"]), 5)

    def test_ico_contains_required_windows_resolutions(self) -> None:
        payload = read_bounded(ROOT / "design" / "WinCare.ico")
        reserved, icon_type, count = struct.unpack_from("<HHH", payload, 0)
        self.assertEqual((0, 1), (reserved, icon_type))
        self.assertEqual(9, count)
        sizes: list[int] = []
        for index in range(count):
            width, height, _, _, planes, bit_count, length, offset = struct.unpack_from(
                "<BBBBHHII", payload, 6 + index * 16
            )
            decoded_width = 256 if width == 0 else width
            decoded_height = 256 if height == 0 else height
            self.assertEqual(decoded_width, decoded_height)
            self.assertEqual(1, planes)
            self.assertEqual(32, bit_count)
            self.assertGreater(length, 100)
            self.assertLessEqual(offset + length, len(payload))
            self.assertEqual(b"\x89PNG\r\n\x1a\n", payload[offset : offset + 8])
            sizes.append(decoded_width)
        self.assertEqual([16, 20, 24, 32, 40, 48, 64, 128, 256], sizes)

    def test_every_icon_copy_matches_the_canonical_binary_identity(self) -> None:
        canonical = read_bounded(ROOT / "design" / "WinCare.ico")
        icons = [
            path
            for path in ROOT.rglob("WinCare.ico")
            if ".git" not in path.parts and "artifacts" not in path.parts and not path.is_symlink()
        ]
        self.assertGreaterEqual(len(icons), 1)
        for path in icons:
            self.assertEqual(canonical, read_bounded(path), str(path.relative_to(ROOT)))

    def test_executable_projects_reference_the_canonical_icon(self) -> None:
        reference_count = 0
        paths = list(ROOT.rglob("*.csproj")) + list(ROOT.rglob("Directory.Build.props"))
        for path in paths:
            if ".git" in path.parts or "artifacts" in path.parts or path.is_symlink():
                continue
            text = read_text(path)
            for reference in re.findall(
                r"<ApplicationIcon>([^<]+)</ApplicationIcon>",
                text,
                flags=re.IGNORECASE,
            ):
                reference_count += 1
                candidate = reference.strip()
                if candidate.lower().endswith("wincare.ico"):
                    continue
                match = re.fullmatch(r"\$\(([A-Za-z][A-Za-z0-9_.-]*)\)", candidate)
                self.assertIsNotNone(match, candidate)
                property_name = re.escape(match.group(1))
                values = re.findall(
                    rf"<{property_name}>([^<]+)</{property_name}>",
                    text,
                    flags=re.IGNORECASE,
                )
                self.assertTrue(
                    any(value.strip().lower().endswith("wincare.ico") for value in values),
                    candidate,
                )
        self.assertGreaterEqual(reference_count, 1)

    def test_installer_uses_installed_brand_assets_for_shortcuts(self) -> None:
        installer = read_text(ROOT / "Install-WinCare.ps1")
        shortcuts = read_text(
            ROOT / "src" / "WinCare" / "Install" / "Private" / "30-Shortcuts.ps1"
        )
        self.assertIn("wincare.brand.package-check/v1", installer)
        self.assertIn("design\\WinCare-Brand.manifest.json", installer)
        self.assertIn("wincare.brand.shortcut-icon/v1", shortcuts)
        self.assertIn(".IconLocation", shortcuts)
        self.assertIn("Join-Path $Destination 'design\\WinCare.ico'", shortcuts)
        self.assertIn("ExpectedIconLocation", shortcuts)

    def test_release_receipts_include_closed_brand_identity(self) -> None:
        finalizer = read_text(ROOT / "tools" / "finalize_release.py")
        self.assertIn("wincare.brand.integration/v1", finalizer)
        self.assertIn('"schema": "wincare.brand.identity/v1"', finalizer)
        self.assertGreaterEqual(finalizer.count('"brand": brand'), 3)
        self.assertIn('WinCare-{version}-BRAND.json', finalizer)
        self.assertIn('"brandManifest"', finalizer)

    def test_documentation_uses_the_canonical_wordmark(self) -> None:
        readme = read_text(ROOT / "README.md")
        self.assertIn("design/WinCare-Wordmark.svg", readme)
        brand = read_text(ROOT / "design" / "BRAND.md")
        self.assertIn("without imitating Microsoft's Windows trademark", brand)
        self.assertIn("16 px or larger", brand)


if __name__ == "__main__":
    unittest.main()
