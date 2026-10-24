"""Fail-closed MSIX asset selection regression coverage."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.install_msix import find_canonical_package


class MsixSelectionTests(unittest.TestCase):
    def test_rejects_multiple_matching_versions_instead_of_choosing_an_old_release(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "WinCare.App_4.0.0.0_x64.msix").touch()
            (root / "WinCare.App_5.0.0.0_x64.msix").touch()
            self.assertIsNone(find_canonical_package(root, "x64"))

    def test_does_not_fall_back_to_an_incompatible_architecture(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "WinCare.App_5.0.0.0_ARM64.msix").touch()
            self.assertIsNone(find_canonical_package(root, "x64"))

    def test_selects_only_a_single_matching_package(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            matching = root / "WinCare.App_5.0.0.0_x64.msix"
            matching.touch()
            (root / "WinCare.App_5.0.0.0_ARM64.msix").touch()
            self.assertEqual(matching, find_canonical_package(root, "x64"))


if __name__ == "__main__":
    unittest.main()
