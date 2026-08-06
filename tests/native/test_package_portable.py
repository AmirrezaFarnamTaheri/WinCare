from __future__ import annotations

import tempfile
import unittest
import zipfile
from pathlib import Path

from tools.package_portable import create_portable_archive


class PortablePackageTests(unittest.TestCase):
    def test_archive_is_sorted_and_uses_fixed_metadata(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "publish"
            source.mkdir()
            (source / "z.txt").write_text("last", encoding="utf-8")
            nested = source / "folder"
            nested.mkdir()
            (nested / "a.txt").write_text("first", encoding="utf-8")
            output = root / "WinCare.zip"

            create_portable_archive(source, output)

            with zipfile.ZipFile(output) as archive:
                self.assertEqual(["folder/a.txt", "z.txt"], archive.namelist())
                for item in archive.infolist():
                    self.assertEqual((1980, 1, 1, 0, 0, 0), item.date_time)
                    self.assertEqual(0o100644, item.external_attr >> 16)

    def test_archive_rejects_symbolic_links(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "publish"
            source.mkdir()
            target = source / "real.txt"
            target.write_text("data", encoding="utf-8")
            link = source / "link.txt"
            try:
                link.symlink_to(target)
            except OSError:
                self.skipTest("symbolic links are unavailable")

            with self.assertRaisesRegex(ValueError, "symbolic link"):
                create_portable_archive(source, root / "WinCare.zip")


if __name__ == "__main__":
    unittest.main()
