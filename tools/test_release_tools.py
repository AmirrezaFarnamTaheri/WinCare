#!/usr/bin/env python3
"""Negative and reproducibility tests for WinCare release tooling."""
from __future__ import annotations

import hashlib
import json
import stat
import tempfile
import unittest
import zipfile
from pathlib import Path

from tools.build_release import manifest, write_zip
from tools.verify_release import validate


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class ReleaseToolTests(unittest.TestCase):
    def test_zip_writer_is_deterministic(self) -> None:
        files = {"b.txt": b"beta\n", "a.ps1": b"Write-Output 'alpha'\n"}
        timestamp = (2026, 1, 2, 3, 4, 6)
        with tempfile.TemporaryDirectory() as directory:
            first = Path(directory) / "first.zip"
            second = Path(directory) / "second.zip"
            write_zip(first, "WinCare-4.0.0", files, timestamp)
            write_zip(second, "WinCare-4.0.0", files, timestamp)
            self.assertEqual(sha256(first), sha256(second))
            self.assertEqual(first.read_bytes(), second.read_bytes())

    def test_manifest_is_sorted_and_exact(self) -> None:
        files = {"z.txt": b"z", "A.txt": b"a"}
        lines = manifest(files).decode("ascii").splitlines()
        self.assertTrue(lines[0].endswith("  A.txt"))
        self.assertTrue(lines[1].endswith("  z.txt"))
        self.assertEqual(len(lines), 2)

    def test_verifier_rejects_path_traversal(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("WinCare-4.0.0/../escape.txt", b"bad")
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertTrue(any("Unsafe member path" in error for error in report["errors"]))

    def test_verifier_rejects_case_collisions(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("WinCare-4.0.0/a.txt", b"a")
                output.writestr("WinCare-4.0.0/A.txt", b"A")
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertTrue(any("Case-colliding member" in error for error in report["errors"]))

    def test_verifier_rejects_symbolic_links(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            with zipfile.ZipFile(archive, "w") as output:
                info = zipfile.ZipInfo("WinCare-4.0.0/link")
                info.create_system = 3
                info.external_attr = (stat.S_IFLNK | 0o777) << 16
                output.writestr(info, b"target")
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertTrue(any("Symbolic link member" in error for error in report["errors"]))


    def test_verifier_rejects_duplicate_members(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("WinCare-4.0.0/a.txt", b"first")
                output.writestr("WinCare-4.0.0/a.txt", b"second")
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertTrue(any("Duplicate member" in error for error in report["errors"]))

    def test_verifier_rejects_suspicious_compression_ratio(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
                output.writestr("WinCare-4.0.0/bomb.txt", b"0" * (2 * 1024 * 1024))
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertTrue(any("Suspicious compression ratio" in error for error in report["errors"]))

    def test_verifier_rejects_duplicate_manifest_paths(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            root = "WinCare-4.0.0/"
            digest = hashlib.sha256(b"a").hexdigest()
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr(root + "a.txt", b"a")
                output.writestr(root + "RELEASE-MANIFEST.sha256", f"{digest}  a.txt\n{digest}  A.txt\n".encode("ascii"))
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertTrue(any("Duplicate/case-colliding manifest path" in error for error in report["errors"]))

    def test_verifier_rejects_receipt_version_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            root = "WinCare-4.0.0/"
            receipt = json.dumps({"version": "9.9.9", "source": {"dirty": False}}).encode()
            sbom = json.dumps({"spdxVersion": "SPDX-2.3", "files": [
                {"fileName": "./WinCare.ps1"},
                {"fileName": "./src/WinCare/WinCare.psd1"},
            ]}).encode()
            members = {
                "WinCare.ps1": b"#requires -Version 7.2\n",
                "src/WinCare/WinCare.psd1": b"@{ ModuleVersion='4.0.0' }\n",
                "SBOM.spdx.json": sbom,
                "BUILD-RECEIPT.json": receipt,
            }
            with zipfile.ZipFile(archive, "w") as output:
                for name, data in members.items():
                    output.writestr(root + name, data)
                output.writestr(root + "RELEASE-MANIFEST.sha256", manifest(members))
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertIn("Build receipt version does not match archive root.", report["errors"])

    def test_verifier_rejects_unmanifested_members(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            root = "WinCare-4.0.0/"
            receipt = json.dumps({"version": "4.0.0", "source": {"dirty": False}}).encode()
            sbom = json.dumps({"spdxVersion": "SPDX-2.3", "files": []}).encode()
            members = {
                "WinCare.ps1": b"#requires -Version 7.2\n",
                "src/WinCare/WinCare.psd1": b"@{ ModuleVersion='4.0.0' }\n",
                "SBOM.spdx.json": sbom,
                "BUILD-RECEIPT.json": receipt,
                "extra.txt": b"unlisted",
            }
            listed = {key: value for key, value in members.items() if key != "extra.txt"}
            with zipfile.ZipFile(archive, "w") as output:
                for name, data in members.items():
                    output.writestr(root + name, data)
                output.writestr(root + "RELEASE-MANIFEST.sha256", manifest(listed))
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertTrue(any("Manifest membership mismatch" in error for error in report["errors"]))


if __name__ == "__main__":
    unittest.main()
