from __future__ import annotations

import functools
import io
import random
import json
import os
import stat
import tempfile
import unittest
import zipfile
from pathlib import Path

try:
    from . import finalize_release, prepare_standalone_payload, verify_release_v3
except ImportError:
    import finalize_release
    import prepare_standalone_payload
    import verify_release_v3


def zip_info(name: str) -> zipfile.ZipInfo:
    info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
    info.create_system = 3
    info.external_attr = (stat.S_IFREG | 0o644) << 16
    info.compress_type = zipfile.ZIP_DEFLATED
    return info


@functools.lru_cache(maxsize=8)
def fake_pe(subsystem: int, marker: int) -> bytes:
    data = bytearray(random.Random(marker).randbytes(20 * 1024 * 1024 + marker))
    data[:2] = b"MZ"
    pe_offset = 0x80
    data[0x3C:0x40] = pe_offset.to_bytes(4, "little")
    data[pe_offset:pe_offset + 4] = b"PE\0\0"
    data[pe_offset + 4:pe_offset + 6] = (0x8664).to_bytes(2, "little")
    data[pe_offset + 20:pe_offset + 22] = (0xF0).to_bytes(2, "little")
    optional = pe_offset + 24
    data[optional:optional + 2] = (0x20B).to_bytes(2, "little")
    data[optional + 68:optional + 70] = subsystem.to_bytes(2, "little")
    data[-1] = marker
    return bytes(data)


def write_core(path: Path) -> None:
    files = {
        "src/WinCare/WinCare.psd1": b"@{ ModuleVersion = '1.2.3'; GUID = 'f43eb775-7d08-42ec-9888-8b1bd79e90a3' }\n",
        "src/WinCare/WinCare.psm1": b"function Start-WinCare {}\n",
        "src/WinCare/Install/WinCare.Installation.psm1": b"function Install-WinCarePackage {}\n",
        "WinCare.ps1": b"#requires -Version 7.2\n",
        "WinCare-GUI.ps1": b"#requires -Version 7.2\n",
        "WinCare-TUI.ps1": b"#requires -Version 7.2\n",
        "bin/WinCare.Launcher.exe": b"legacy",
        "bin/WinCare.Native.dll": b"native-runtime",
        "bin/WinCare.Native.build.json": b"{\"SchemaVersion\":2}",
    }
    receipt = {
        "schema": "wincare.build.receipt/v2",
        "product": "WinCare",
        "version": "1.2.3",
        "source": {"gitCommit": "a" * 40, "dirty": False, "treeSha256": "b" * 64, "members": 10},
        "packageProfile": "production",
        "native": {"status": "source-built-and-verified"},
    }
    files["BUILD-RECEIPT.json"] = json.dumps(receipt).encode()
    files["RELEASE-MANIFEST.sha256"] = prepare_standalone_payload.payload_manifest(
        {name: data for name, data in files.items() if name != "RELEASE-MANIFEST.sha256"}
    )
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(files.items()):
            archive.writestr(zip_info("WinCare-1.2.3/" + name), data)




def write_standalone_output(directory: Path) -> None:
    records = []
    for index, (name, subsystem) in enumerate(verify_release_v3.EXPECTED_EXES.items(), 1):
        data = fake_pe(subsystem, index)
        (directory / name).write_bytes(data)
        records.append({
            "Name": name,
            "Sha256": verify_release_v3.sha256(data),
            "Bytes": len(data),
            "Subsystem": subsystem,
            "RuntimeIdentifier": "win-x64",
            "SelfTestExitCode": 0,
        })
    manifest = {
        "SchemaVersion": 1,
        "Version": "1.2.3",
        "RuntimeIdentifier": "win-x64",
        "Configuration": "Release",
        "SelfContained": True,
        "SingleFile": True,
        "PayloadSha256": "a" * 64,
        "PayloadManifestSha256": "b" * 64,
        "Artifacts": records,
    }
    (directory / "WinCare.Standalone.build.json").write_text(json.dumps(manifest), encoding="utf-8")


class StandaloneReleaseTests(unittest.TestCase):
    def setUp(self) -> None:
        os.environ["SOURCE_DATE_EPOCH"] = "315532800"

    def test_payload_is_deterministic_and_filters_legacy_metadata(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            core = root / "core.zip"
            write_core(core)
            first = root / "first.zip"
            second = root / "second.zip"
            archive_root, files = prepare_standalone_payload.read_archive(core)
            files["PAYLOAD-MANIFEST.sha256"] = prepare_standalone_payload.payload_manifest(files)
            prepare_standalone_payload.write_payload(first, archive_root, files)
            prepare_standalone_payload.write_payload(second, archive_root, files)
            self.assertEqual(first.read_bytes(), second.read_bytes())
            with zipfile.ZipFile(first) as archive:
                names = {item.filename.casefold() for item in archive.infolist()}
            self.assertFalse(any("launcher" in name for name in names))
            self.assertFalse(any(name.endswith("build-receipt.json") for name in names))

    def test_payload_rejects_traversal_backslash_and_case_collisions(self) -> None:
        bad_names = [
            ["WinCare-1/../escape.txt"],
            ["WinCare-1/dir\\file.txt"],
            ["WinCare-1/A.txt", "WinCare-1/a.TXT"],
        ]
        for names in bad_names:
            with self.subTest(names=names), tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "bad.zip"
                with zipfile.ZipFile(path, "w") as archive:
                    for name in names:
                        archive.writestr(name, b"x")
                with self.assertRaises(ValueError):
                    prepare_standalone_payload.read_archive(path)

    def test_final_release_closes_legacy_graph_and_verifies(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            core = root / "core.zip"
            exes = root / "exes"
            output = root / "output"
            exes.mkdir()
            write_core(core)
            write_standalone_output(exes)
            result = finalize_release.finalize(core, exes, output)
            archive = Path(str(result["archive"]))
            validation = verify_release_v3.validate(archive, output)
            self.assertEqual(validation["status"], "passed", validation)
            _, files = verify_release_v3.parse_archive(archive)
            self.assertFalse(any("launcher" in Path(name).name.casefold() for name in files))
            self.assertEqual(json.loads(files["BUILD-RECEIPT.json"])["schema"], "wincare.build.receipt/v3")

    def test_external_asset_tamper_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            core = root / "core.zip"
            exes = root / "exes"
            output = root / "output"
            exes.mkdir()
            write_core(core)
            write_standalone_output(exes)
            result = finalize_release.finalize(core, exes, output)
            (output / "WinCare.exe").write_bytes(b"tampered")
            validation = verify_release_v3.validate(Path(str(result["archive"])), output)
            self.assertEqual(validation["status"], "failed")
            self.assertTrue(any("differs" in error for error in validation["errors"]))

    def test_verifier_rejects_case_variant_legacy_launcher(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            core = root / "core.zip"
            exes = root / "exes"
            output = root / "output"
            exes.mkdir()
            write_core(core)
            write_standalone_output(exes)
            result = finalize_release.finalize(core, exes, output)
            archive = Path(str(result["archive"]))
            release_root, files = verify_release_v3.parse_archive(archive)
            files["bin/wincare.LAUNCHER.exe"] = b"legacy"
            files["RELEASE-MANIFEST.sha256"] = prepare_standalone_payload.payload_manifest(
                {name: data for name, data in files.items() if name != "RELEASE-MANIFEST.sha256"}
            )
            tampered = root / "tampered.zip"
            finalize_release.write_zip(tampered, release_root, files, (1980, 1, 1, 0, 0, 0))
            validation = verify_release_v3.validate(tampered)
            self.assertEqual(validation["status"], "failed")
            self.assertTrue(any("legacy launcher" in error for error in validation["errors"]))

    def test_verifier_rejects_wrong_pe_subsystem(self) -> None:
        error = verify_release_v3.validate_pe(fake_pe(2, 1), 3)
        self.assertIn("unexpected subsystem", error or "")


if __name__ == "__main__":
    unittest.main()
