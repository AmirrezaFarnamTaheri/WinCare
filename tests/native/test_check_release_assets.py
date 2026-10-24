"""Release idempotence: existing assets must exactly match staged artifacts."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.check_release_assets import validate_release_asset_set


class CheckReleaseAssetsTests(unittest.TestCase):
    def test_accepts_identical_asset_sets(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            expected, published = root / 'expected', root / 'published'
            expected.mkdir(); published.mkdir()
            (expected / 'WinCare.msix').write_bytes(b'a')
            (published / 'WinCare.msix').write_bytes(b'a')
            validate_release_asset_set(expected, published)

    def test_rejects_same_name_with_different_signed_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            expected, published = root / 'expected', root / 'published'
            expected.mkdir(); published.mkdir()
            (expected / 'WinCare.msix').write_bytes(b'signer-new')
            (published / 'WinCare.msix').write_bytes(b'signer-old')
            with self.assertRaisesRegex(ValueError, 'digest'):
                validate_release_asset_set(expected, published)

    def test_rejects_partial_and_extra_publications(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            expected, published = root / 'expected', root / 'published'
            expected.mkdir(); published.mkdir()
            (expected / 'x64.msix').write_bytes(b'a')
            with self.assertRaisesRegex(ValueError, 'asset set'):
                validate_release_asset_set(expected, published)
            (published / 'x64.msix').write_bytes(b'a')
            (published / 'old.cer').write_bytes(b'old')
            with self.assertRaisesRegex(ValueError, 'asset set'):
                validate_release_asset_set(expected, published)


if __name__ == '__main__':
    unittest.main()
