#!/usr/bin/env python3
"""Negative and reproducibility tests for WinCare release tooling."""
from __future__ import annotations

import hashlib
import re
import json
import stat
import tempfile
import unittest
import zipfile
from pathlib import Path

from tools.build_release import (
    assert_empty_output_directory, expected_native_source_names, load_native_artifacts,
    manifest, sha256_bytes, source_files, tree_hash, write_zip,
)
from tools.verify_release import extract_validated, validate


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class ReleaseToolTests(unittest.TestCase):
    def make_native_fixture(self, directory: Path) -> tuple[Path, Path]:
        root = directory / "root"
        native_source = root / "src/WinCare/Native"
        native_source.mkdir(parents=True)
        (root / "global.json").write_text(
            json.dumps({"sdk": {"version": "8.0.408"}}), encoding="utf-8"
        )
        for name in ("Build.ps1", "Native.cs", "Native.csproj"):
            (native_source / name).write_text(name + "\n", encoding="utf-8")
        output = directory / "native-output"
        output.mkdir()
        artifacts = {
            "WinCare.Native.dll": b"native",
            "WinCare.Launcher.exe": b"launcher",
            "WinCare.TuiLauncher.exe": b"tui",
        }
        source_records = []
        source_payload = {}
        for path in sorted(
            [root / "global.json", *native_source.iterdir()],
            key=lambda item: item.as_posix().casefold(),
        ):
            relative = path.relative_to(root).as_posix()
            data = path.read_bytes()
            source_payload[relative] = data
            source_records.append({
                "Path": relative, "Sha256": sha256_bytes(data), "Bytes": len(data),
            })
        artifact_records = []
        for name, data in artifacts.items():
            (output / name).write_bytes(data)
            artifact_records.append({
                "Path": name, "Sha256": sha256_bytes(data), "Bytes": len(data),
            })
        build_manifest = {
            "SchemaVersion": 2,
            "Configuration": "Release",
            "BuiltAt": "2026-01-01T00:00:00Z",
            "DotNetVersion": "8.0.408",
            "DotNetHostSha256": "a" * 64,
            "TargetFramework": "net8.0-windows",
            "SourceTreeSha256": tree_hash(source_payload),
            "SourceCount": len(source_records),
            "TotalSourceBytes": sum(record["Bytes"] for record in source_records),
            "SourceMetadataBytes": sum(
                len(record["Path"].encode("utf-8")) + 32 + 2
                for record in source_records
            ),
            "Sources": source_records,
            "Artifacts": artifact_records,
        }
        (output / "WinCare.Native.build.json").write_text(
            json.dumps(build_manifest), encoding="utf-8"
        )
        return root, output

    def make_valid_development_archive(self, directory: Path) -> tuple[Path, dict[str, bytes]]:
        archive = directory / "WinCare-9.9.9.zip"
        root = "WinCare-9.9.9/"
        package_inputs = {
            "WinCare.ps1": b"#requires -Version 7.2\n",
            "src/WinCare/WinCare.psd1": b"@{ ModuleVersion='9.9.9' }\n",
            "README.md": b"WinCare\n",
        }
        sbom = json.dumps({
            "spdxVersion": "SPDX-2.3",
            "files": [{"fileName": "./" + name} for name in package_inputs],
        }).encode("utf-8")
        receipt = json.dumps({
            "version": "9.9.9",
            "packageProfile": "development",
            "source": {"dirty": False},
            "packageInputs": {
                "treeSha256": tree_hash(package_inputs),
                "members": len(package_inputs),
            },
        }).encode("utf-8")
        members = dict(package_inputs)
        members["SBOM.spdx.json"] = sbom
        members["BUILD-RECEIPT.json"] = receipt
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as output:
            for name, data in members.items():
                output.writestr(root + name, data)
            output.writestr(root + "RELEASE-MANIFEST.sha256", manifest(members))
        return archive, members

    def test_validated_extraction_streams_exact_manifested_members(self) -> None:
        with tempfile.TemporaryDirectory() as directory_name:
            directory = Path(directory_name)
            archive, members = self.make_valid_development_archive(directory)
            destination = directory / "extracted"
            report = extract_validated(archive, destination)
            self.assertEqual("passed", report["status"], report["errors"])
            for name, data in members.items():
                self.assertEqual(data, (destination / "WinCare-9.9.9" / name).read_bytes())
            self.assertTrue((destination / "WinCare-9.9.9" / "RELEASE-MANIFEST.sha256").is_file())

    def test_validated_extraction_refuses_existing_destination(self) -> None:
        with tempfile.TemporaryDirectory() as directory_name:
            directory = Path(directory_name)
            archive, _ = self.make_valid_development_archive(directory)
            destination = directory / "existing"
            destination.mkdir()
            report = extract_validated(archive, destination)
            self.assertEqual("failed", report["status"])
            self.assertTrue(any("already exists" in error for error in report["errors"]))

    def test_release_verifier_never_materializes_all_archive_members(self) -> None:
        source = (Path(__file__).parent / "verify_release.py").read_text(encoding="utf-8")
        self.assertNotIn("archive.read(info)", source)
        self.assertNotIn("extractall", source)
        self.assertIn("hash_member", source)
        self.assertIn("read_member_bounded", source)

    def test_zip_writer_is_deterministic(self) -> None:
        files = {"b.txt": b"beta\n", "a.ps1": b"Write-Output 'alpha'\n"}
        timestamp = (2026, 1, 2, 3, 4, 6)
        with tempfile.TemporaryDirectory() as directory:
            first = Path(directory) / "first.zip"
            second = Path(directory) / "second.zip"
            write_zip(first, "WinCare-9.9.9", files, timestamp)
            write_zip(second, "WinCare-9.9.9", files, timestamp)
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
                output.writestr("WinCare-9.9.9/../escape.txt", b"bad")
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertTrue(any("Unsafe member path" in error for error in report["errors"]))

    def test_verifier_rejects_case_collisions(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("WinCare-9.9.9/a.txt", b"a")
                output.writestr("WinCare-9.9.9/A.txt", b"A")
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertTrue(any("Case-colliding member" in error for error in report["errors"]))

    def test_verifier_rejects_symbolic_links(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            with zipfile.ZipFile(archive, "w") as output:
                info = zipfile.ZipInfo("WinCare-9.9.9/link")
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
                output.writestr("WinCare-9.9.9/a.txt", b"first")
                output.writestr("WinCare-9.9.9/a.txt", b"second")
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertTrue(any("Duplicate member" in error for error in report["errors"]))

    def test_verifier_rejects_suspicious_compression_ratio(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
                output.writestr("WinCare-9.9.9/bomb.txt", b"0" * (2 * 1024 * 1024))
            report = validate(archive)
            self.assertEqual(report["status"], "failed")
            self.assertTrue(any("Suspicious compression ratio" in error for error in report["errors"]))

    def test_verifier_rejects_duplicate_manifest_paths(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            root = "WinCare-9.9.9/"
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
            root = "WinCare-9.9.9/"
            receipt = json.dumps({"version": "9.9.8", "source": {"dirty": False}}).encode()
            sbom = json.dumps({"spdxVersion": "SPDX-2.3", "files": [
                {"fileName": "./WinCare.ps1"},
                {"fileName": "./src/WinCare/WinCare.psd1"},
            ]}).encode()
            members = {
                "WinCare.ps1": b"#requires -Version 7.2\n",
                "src/WinCare/WinCare.psd1": b"@{ ModuleVersion='9.9.9' }\n",
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


    def test_verifier_rejects_production_profile_with_development_members(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            root = "WinCare-9.9.9/"
            package_inputs = {
                "WinCare.ps1": b"#requires -Version 7.2\n",
                "src/WinCare/WinCare.psd1": b"@{ ModuleVersion='9.9.9' }\n",
                "tests/Unsafe.Tests.ps1": b"It 'must not ship' {}\n",
            }
            sbom = json.dumps({"spdxVersion": "SPDX-2.3", "files": [
                {"fileName": "./" + name} for name in package_inputs
            ]}).encode()
            receipt = json.dumps({
                "version": "9.9.9",
                "packageProfile": "production",
                "source": {"dirty": False},
                "native": {"status": "source-built-and-verified"},
                "packageInputs": {
                    "treeSha256": __import__("tools.verify_release", fromlist=["tree_hash"]).tree_hash(package_inputs),
                    "members": len(package_inputs),
                },
            }).encode()
            members = dict(package_inputs)
            members["SBOM.spdx.json"] = sbom
            members["BUILD-RECEIPT.json"] = receipt
            with zipfile.ZipFile(archive, "w") as output:
                for name, data in members.items():
                    output.writestr(root + name, data)
                output.writestr(root + "RELEASE-MANIFEST.sha256", manifest(members))
            report = validate(archive)
            self.assertEqual("failed", report["status"])
            self.assertTrue(any("development-only members" in error for error in report["errors"]))

    def test_verifier_rejects_dirty_production_profile(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            root = "WinCare-9.9.9/"
            package_inputs = {
                "WinCare.ps1": b"#requires -Version 7.2\n",
                "src/WinCare/WinCare.psd1": b"@{ ModuleVersion='9.9.9' }\n",
            }
            sbom = json.dumps({"spdxVersion": "SPDX-2.3", "files": [
                {"fileName": "./" + name} for name in package_inputs
            ]}).encode()
            receipt = json.dumps({
                "version": "9.9.9",
                "packageProfile": "production",
                "source": {"dirty": True},
                "native": {"status": "source-built-and-verified"},
                "packageInputs": {
                    "treeSha256": __import__("tools.verify_release", fromlist=["tree_hash"]).tree_hash(package_inputs),
                    "members": len(package_inputs),
                },
            }).encode()
            members = dict(package_inputs)
            members["SBOM.spdx.json"] = sbom
            members["BUILD-RECEIPT.json"] = receipt
            with zipfile.ZipFile(archive, "w") as output:
                for name, data in members.items():
                    output.writestr(root + name, data)
                output.writestr(root + "RELEASE-MANIFEST.sha256", manifest(members))
            report = validate(archive)
            self.assertEqual("failed", report["status"])
            self.assertIn("Production archive was built from a dirty source state.", report["errors"])

    def test_verifier_rejects_unmanifested_members(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            root = "WinCare-9.9.9/"
            receipt = json.dumps({"version": "9.9.9", "source": {"dirty": False}}).encode()
            sbom = json.dumps({"spdxVersion": "SPDX-2.3", "files": []}).encode()
            members = {
                "WinCare.ps1": b"#requires -Version 7.2\n",
                "src/WinCare/WinCare.psd1": b"@{ ModuleVersion='9.9.9' }\n",
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

    def test_production_source_profile_excludes_development_surfaces(self) -> None:
        files = source_files(Path(__file__).resolve().parents[1], "production")
        self.assertIn("WinCare.ps1", files)
        self.assertIn("src/WinCare/WinCare.psd1", files)
        self.assertFalse(any(name.startswith("tests/") for name in files))
        self.assertFalse(any(name.startswith("tools/") for name in files))
        self.assertFalse(any(name.startswith(".github/") for name in files))
        self.assertFalse(any(name.startswith("src/WinCare/Native/") for name in files))

    def test_native_loader_requires_exact_source_membership(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root, output = self.make_native_fixture(Path(directory))
            manifest_path = output / "WinCare.Native.build.json"
            value = json.loads(manifest_path.read_text(encoding="utf-8"))
            value["Sources"] = value["Sources"][:-1]
            value["SourceCount"] = len(value["Sources"])
            value["TotalSourceBytes"] = sum(record["Bytes"] for record in value["Sources"])
            value["SourceMetadataBytes"] = sum(
                len(record["Path"].encode("utf-8")) + 32 + 2
                for record in value["Sources"]
            )
            manifest_path.write_text(json.dumps(value), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "source membership mismatch"):
                load_native_artifacts(output, root, True)

    def test_native_loader_rejects_extra_output_subtrees(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root, output = self.make_native_fixture(Path(directory))
            (output / "hidden").mkdir()
            with self.assertRaisesRegex(RuntimeError, "directory membership mismatch"):
                load_native_artifacts(output, root, True)

    def test_native_loader_accepts_complete_bounded_provenance(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root, output = self.make_native_fixture(Path(directory))
            files, evidence = load_native_artifacts(output, root, True)
            self.assertEqual("source-built-and-verified", evidence["status"])
            self.assertEqual(4, evidence["sourceMembers"])
            self.assertIn("bin/WinCare.Native.dll", files)
            self.assertIn("bin/WinCare.Native.build.json", files)


    def test_native_loader_rejects_aggregate_provenance_tampering(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root, output = self.make_native_fixture(Path(directory))
            manifest_path = output / "WinCare.Native.build.json"
            value = json.loads(manifest_path.read_text(encoding="utf-8"))
            value["TotalSourceBytes"] += 1
            manifest_path.write_text(json.dumps(value), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "aggregate provenance"):
                load_native_artifacts(output, root, True)

    def test_native_source_membership_rejects_unsupported_entries(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root, _ = self.make_native_fixture(Path(directory))
            native_source = root / "src/WinCare/Native"
            (native_source / "unexpected.bin").write_bytes(b"not source")
            with self.assertRaisesRegex(RuntimeError, "Unsupported native source entry"):
                expected_native_source_names(root)

    def test_release_output_directory_must_be_absent_or_empty(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "root"
            root.mkdir()
            populated = Path(directory) / "artifacts"
            populated.mkdir()
            (populated / "old.zip").write_bytes(b"stale")
            with self.assertRaisesRegex(RuntimeError, "absent or empty"):
                assert_empty_output_directory(populated, root)
            empty = Path(directory) / "new-artifacts"
            assert_empty_output_directory(empty, root)
            self.assertTrue(empty.is_dir())
            self.assertFalse(any(empty.iterdir()))

    def test_installer_accepts_the_build_receipt_schema_the_builder_emits(self) -> None:
        """The packaged BUILD-RECEIPT.json must be installable and uninstallable.

        Regression test for a producer/consumer split that shipped in a single
        commit and went unnoticed because no gate exercised it: build_release.py
        emitted 'wincare.build.receipt/v2' while Install-WinCare.ps1 and
        Uninstall-WinCare.ps1 both hard-required 'wincare.build.receipt/v1',
        so every production archive threw 'Unsupported build receipt schema.'
        at install time. tools/verify_release.py deliberately does not inspect
        receipt['schema'], so archive validation stayed green while the
        Windows clean-install gate failed.
        """
        repo_root = Path(__file__).resolve().parent.parent
        builder = (repo_root / "tools/build_release.py").read_text(encoding="utf-8")
        emitted = set(re.findall(r'"schema":\s*"(wincare\.build\.receipt/v\d+)"', builder))
        self.assertTrue(emitted, "build_release.py no longer emits a recognizable build-receipt schema")

        for script_name in ("Install-WinCare.ps1", "Uninstall-WinCare.ps1"):
            script = (repo_root / script_name).read_text(encoding="utf-8-sig")
            accepted = set(re.findall(r"'(wincare\.build\.receipt/v\d+)'", script))
            self.assertTrue(accepted, f"{script_name} does not gate on a build-receipt schema at all")
            missing = emitted - accepted
            self.assertFalse(
                missing,
                f"{script_name} rejects build-receipt schema(s) that build_release.py emits: "
                f"{sorted(missing)} (accepted: {sorted(accepted)})",
            )


if __name__ == "__main__":
    unittest.main()
