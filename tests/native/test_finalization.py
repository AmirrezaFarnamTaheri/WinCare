from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

from tools.finalize_native_release import (
    ProductionBlockedError,
    evaluate_readiness,
    finalize_release,
)

ROOT = Path(__file__).resolve().parents[2]


class FinalizationTests(unittest.TestCase):
    def test_frozen_oracle_has_exact_catalog_and_provenance(self) -> None:
        command_fixture = ROOT / "migration/oracle/legacy-command-ids.json"
        provenance_path = ROOT / "migration/oracle/provenance.json"
        self.assertTrue(command_fixture.is_file())
        self.assertTrue(provenance_path.is_file())

        commands = json.loads(command_fixture.read_text(encoding="utf-8"))
        provenance = json.loads(provenance_path.read_text(encoding="utf-8"))
        self.assertEqual(1, commands["schemaVersion"])
        self.assertEqual(259, commands["commandCount"])
        self.assertEqual(259, len(commands["commands"]))
        self.assertEqual(259, len(set(commands["commands"])))
        self.assertEqual("83567c4dbf3cf85e44217855d39604b0193623e3", provenance["sourceCommit"])
        self.assertEqual(
            {
                "src/WinCare/UI/97-AdvancedCapabilities.ps1",
                "src/WinCare/UI/98-Headless.ps1",
                "src/WinCare/Data/Catalog/rules.json",
                "src/WinCare/Data/Catalog/presets.json",
            },
            set(provenance["sourceFiles"]),
        )
        for digest in provenance["sourceFiles"].values():
            self.assertRegex(digest, r"^[0-9a-f]{64}$")

    def test_production_gate_rejects_incomplete_and_accepts_complete_catalog(self) -> None:
        source = json.loads(
            (ROOT / "src/WinCare.CommandCatalog/Data/commands.json").read_text(encoding="utf-8")
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            incomplete = root / "incomplete.json"
            incomplete.write_text(json.dumps(source), encoding="utf-8")
            with self.assertRaisesRegex(ProductionBlockedError, "259 command"):
                evaluate_readiness(incomplete, mode="production")

            for command in source["commands"]:
                command["migrationStatus"] = "BehaviorVerified"
            complete = root / "complete.json"
            complete.write_text(json.dumps(source), encoding="utf-8")
            readiness = evaluate_readiness(complete, mode="production")
            self.assertTrue(readiness.production_ready)
            self.assertEqual(259, readiness.behavior_verified)
            self.assertEqual(0, readiness.production_blockers)

    def test_rc_finalization_separates_native_source_and_legacy_oracle(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            result = finalize_release(ROOT, output, version="2.4.0-rc1", mode="rc")

            self.assertTrue(result.native_archive.is_file())
            self.assertTrue(result.oracle_archive.is_file())
            self.assertTrue(result.report_path.is_file())
            self.assertTrue(result.manifest_path.is_file())
            self.assertEqual(259, result.readiness.cataloged)
            self.assertEqual(8, result.readiness.implemented)
            self.assertEqual(0, result.readiness.behavior_verified)
            self.assertEqual(251, result.readiness.implementation_blockers)
            self.assertEqual(259, result.readiness.production_blockers)

            with zipfile.ZipFile(result.native_archive) as archive:
                names = archive.namelist()
                self.assertTrue(names)
                self.assertFalse(any(name.lower().endswith((".ps1", ".psm1", ".psd1")) for name in names))
                self.assertFalse(any("__pycache__" in name or name.lower().endswith((".pyc", ".pyo")) for name in names))
                self.assertIn("migration/oracle/legacy-command-ids.json", names)
                self.assertIn("docs/migration/finalization-status.md", names)
                self.assertIn("tools/finalize_native_release.py", names)
                self.assertNotIn("docs/RELEASE.md", names)
                self.assertNotIn("design/WinCare-GUI-Preview.png", names)
                for entry in archive.infolist():
                    self.assertEqual((1980, 1, 1, 0, 0, 0), entry.date_time)

            with zipfile.ZipFile(result.oracle_archive) as archive:
                names = archive.namelist()
                self.assertTrue(any(name.lower().endswith(".psm1") for name in names))
                self.assertIn("migration/oracle/provenance.json", names)
                for entry in archive.infolist():
                    self.assertEqual((1980, 1, 1, 0, 0, 0), entry.date_time)

            manifest = json.loads(result.manifest_path.read_text(encoding="utf-8"))
            self.assertEqual("2.4.0-rc1", manifest["version"])
            self.assertEqual("rc", manifest["mode"])
            self.assertEqual(259, manifest["readiness"]["cataloged"])
            self.assertEqual("AmirrezaFarnamTaheri/WinCare", manifest["oracleProvenance"]["repository"])
            self.assertEqual(
                "83567c4dbf3cf85e44217855d39604b0193623e3",
                manifest["oracleProvenance"]["commit"],
            )
            self.assertRegex(manifest["artifacts"]["nativeSource"]["sha256"], r"^[0-9a-f]{64}$")
            self.assertRegex(manifest["artifacts"]["legacyOracle"]["sha256"], r"^[0-9a-f]{64}$")

    def test_finalizer_rejects_unsafe_version_labels(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "version label"):
                finalize_release(ROOT, Path(directory), version="../escape", mode="rc")

    def test_finalizer_cli_runs_from_repository_root(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run(
                [
                    sys.executable,
                    "tools/finalize_native_release.py",
                    "--output",
                    directory,
                    "--version",
                    "2.4.0-rc1",
                    "--mode",
                    "rc",
                ],
                cwd=ROOT,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertTrue((Path(directory) / "WinCare-2.4.0-rc1-native-source.zip").is_file())

    def test_release_metadata_is_pinned_to_native_release_candidate(self) -> None:
        props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
        manifest = (ROOT / "src/WinCare.App/Package.appxmanifest").read_text(encoding="utf-8")
        cargo = (ROOT / "native/wincare-core/Cargo.toml").read_text(encoding="utf-8")
        rust_source = (ROOT / "native/wincare-core/src/lib.rs").read_text(encoding="utf-8")
        self.assertIn("<VersionPrefix>2.4.0</VersionPrefix>", props)
        self.assertIn("<VersionSuffix>rc1</VersionSuffix>", props)
        self.assertIn("<InformationalVersion>2.4.0-rc1</InformationalVersion>", props)
        self.assertIn('Version="2.4.0.0"', manifest)
        self.assertIn('version = "2.4.0"', cargo)
        self.assertIn('const VERSION: &[u8] = b"2.4.0";', rust_source)

    def test_native_workflows_pin_actions_and_rust_toolchain(self) -> None:
        for relative in (
            ".github/workflows/native-winui.yml",
            ".github/workflows/native-release-candidate.yml",
        ):
            workflow = (ROOT / relative).read_text(encoding="utf-8")
            uses_lines = [line.strip() for line in workflow.splitlines() if "uses:" in line]
            self.assertTrue(uses_lines)
            self.assertFalse(any("@v" in line or "@stable" in line for line in uses_lines), uses_lines)
            if relative.endswith("native-winui.yml"):
                self.assertIn("toolchain: 1.97.1", workflow)

        toolchain = (ROOT / "rust-toolchain.toml").read_text(encoding="utf-8")
        self.assertIn('channel = "1.97.1"', toolchain)
        self.assertIn('components = ["rustfmt", "clippy"]', toolchain)

    def test_ci_runs_finalization_gates_on_master_and_main(self) -> None:
        native_workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        release_workflow = (ROOT / ".github/workflows/native-release-candidate.yml").read_text(encoding="utf-8")
        self.assertIn("branches: [master, main]", native_workflow)
        self.assertIn("python -m unittest discover -s tests/native -v", native_workflow)
        self.assertIn("finalize_native_release.py", native_workflow)
        self.assertIn("--mode rc", native_workflow)
        self.assertIn("--mode production", release_workflow)
        self.assertIn("workflow_dispatch", release_workflow)


if __name__ == "__main__":
    unittest.main()
