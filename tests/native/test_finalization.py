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
LEGACY_EXECUTABLE_ORACLE = ROOT / "src/WinCare/WinCare.psm1"


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
            result = finalize_release(ROOT, output, version="2.5.0-rc1", mode="rc")

            self.assertTrue(result.native_archive.is_file())
            self.assertTrue(result.oracle_archive.is_file())
            self.assertTrue(result.report_path.is_file())
            self.assertTrue(result.manifest_path.is_file())
            self.assertEqual(259, result.readiness.cataloged)
            self.assertEqual(259, result.readiness.implemented)
            self.assertEqual(0, result.readiness.behavior_verified)
            self.assertEqual(0, result.readiness.implementation_blockers)
            self.assertEqual(259, result.readiness.production_blockers)

            with zipfile.ZipFile(result.native_archive) as archive:
                names = archive.namelist()
                self.assertTrue(names)
                self.assertFalse(any(name.lower().endswith((".ps1", ".psm1", ".psd1")) for name in names))
                self.assertFalse(any(name.lower().endswith((".pfx", ".p12", ".key", ".pem", ".snk", ".secret", ".token")) for name in names))
                self.assertFalse(any("__pycache__" in name or name.lower().endswith((".pyc", ".pyo")) for name in names))
                self.assertIn("migration/oracle/legacy-command-ids.json", names)
                self.assertIn("docs/migration/finalization-status.md", names)
                self.assertIn("tools/finalize_native_release.py", names)
                self.assertNotIn("tools/validate_gui.py", names)
                self.assertNotIn("tools/test_gui.py", names)
                self.assertNotIn("docs/RELEASE.md", names)
                self.assertNotIn("design/WinCare-GUI-Preview.png", names)
                for entry in archive.infolist():
                    self.assertEqual((1980, 1, 1, 0, 0, 0), entry.date_time)

            with zipfile.ZipFile(result.oracle_archive) as archive:
                names = archive.namelist()
                self.assertIn("migration/oracle/provenance.json", names)
                for entry in archive.infolist():
                    self.assertEqual((1980, 1, 1, 0, 0, 0), entry.date_time)

            manifest = json.loads(result.manifest_path.read_text(encoding="utf-8"))
            self.assertEqual("2.5.0-rc1", manifest["version"])
            self.assertEqual("rc", manifest["mode"])
            self.assertEqual(259, manifest["readiness"]["cataloged"])
            self.assertEqual("AmirrezaFarnamTaheri/WinCare", manifest["oracleProvenance"]["repository"])
            self.assertEqual(
                "83567c4dbf3cf85e44217855d39604b0193623e3",
                manifest["oracleProvenance"]["commit"],
            )
            self.assertRegex(manifest["artifacts"]["nativeSource"]["sha256"], r"^[0-9a-f]{64}$")
            self.assertRegex(manifest["artifacts"]["legacyOracle"]["sha256"], r"^[0-9a-f]{64}$")

    def test_stage_native_source_strictly_rejects_secret_signing_keys(self) -> None:
        from tools.finalize_native_release import stage_native_source
        with tempfile.TemporaryDirectory() as src_dir, tempfile.TemporaryDirectory() as dst_dir:
            src = Path(src_dir)
            dst = Path(dst_dir)
            (src / "src/WinCare.App").mkdir(parents=True)
            (src / "src/WinCare.App/compromised_key.pfx").write_text("SECRET", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "secret/signing key"):
                stage_native_source(src, dst / "staged")

    def test_stage_release_assets_rejects_conflicting_certificates(self) -> None:
        from tools.stage_release_assets import stage_assets
        with tempfile.TemporaryDirectory() as src_dir, tempfile.TemporaryDirectory() as dst_dir:
            src = Path(src_dir)
            dst = Path(dst_dir)
            job_a = src / "job_a"
            job_b = src / "job_b"
            job_a.mkdir()
            job_b.mkdir()
            (job_a / "WinCare.cer").write_text("CERT_A_CONTENT", encoding="utf-8")
            (job_b / "WinCare.cer").write_text("CERT_B_CONTENT", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Conflicting release assets"):
                stage_assets(src, dst, version="2.5.0-rc1")

    def test_stage_release_assets_ignores_vendor_dependency_msix(self) -> None:
        from tools.stage_release_assets import stage_assets
        with tempfile.TemporaryDirectory() as src_dir, tempfile.TemporaryDirectory() as dst_dir:
            src = Path(src_dir)
            dst = Path(dst_dir)
            package_dir = src / "WinCare-x64" / "AppPackages" / "WinCare.App_2.5.0.0_x64_Test"
            dependency_dir = package_dir / "Dependencies" / "x64"
            dependency_dir.mkdir(parents=True)
            app_msix = package_dir / "WinCare.App_2.5.0.0_x64.msix"
            dependency_msix = dependency_dir / "Microsoft.WindowsAppRuntime.2.msix"
            app_msix.write_bytes(b"WINCARE")
            dependency_msix.write_bytes(b"VENDOR")

            staged = stage_assets(src, dst, version="2.5.0-rc1")
            self.assertEqual([dst.resolve() / "WinCare-v2.5.0-rc1-x64.msix"], staged)
            self.assertEqual(b"WINCARE", staged[0].read_bytes())
            self.assertFalse(any("WindowsAppRuntime" in path.name for path in staged))

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
                    "2.5.0-rc1",
                    "--mode",
                    "rc",
                ],
                cwd=ROOT,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertTrue((Path(directory) / "WinCare-2.5.0-rc1-native-source.zip").is_file())

    def test_release_metadata_is_pinned_to_native_release_candidate(self) -> None:
        props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
        manifest = (ROOT / "src/WinCare.App/Package.appxmanifest").read_text(encoding="utf-8")
        cargo = (ROOT / "native/wincare-core/Cargo.toml").read_text(encoding="utf-8")
        rust_source = (ROOT / "native/wincare-core/src/lib.rs").read_text(encoding="utf-8")
        self.assertIn("<VersionPrefix>2.5.0</VersionPrefix>", props)
        self.assertIn("<VersionSuffix>rc1</VersionSuffix>", props)
        self.assertIn("<InformationalVersion>2.5.0-rc1</InformationalVersion>", props)
        self.assertIn('Version="2.5.0.0"', manifest)
        self.assertIn('version = "2.5.0"', cargo)
        self.assertIn('const VERSION: &[u8] = b"2.5.0";', rust_source)

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

    def test_release_workflows_enforce_immutability_and_version_checks(self) -> None:
        native_workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        release_workflow = (ROOT / ".github/workflows/native-release-candidate.yml").read_text(encoding="utf-8")
        for workflow in (native_workflow, release_workflow):
            self.assertIn("SemVer tagged releases are immutable", workflow)
            self.assertIn("Validate", workflow)
            self.assertNotIn("--clobber", workflow)

        self.assertIn('gh release create "$RELEASE_TAG" release_assets/*', native_workflow)
        self.assertIn('gh release create "$TAG_NAME" artifacts/finalization/*', release_workflow)
        self.assertIn("Require primary branch dispatch", release_workflow)
        self.assertIn("refs/heads/master", release_workflow)
        self.assertIn("refs/heads/main", release_workflow)
        self.assertIn('actual = os.environ["WINCARE_VERSION"].removeprefix("v")', release_workflow)
        self.assertNotIn("actual = '${WINCARE_VERSION#v}'", release_workflow)
        self.assertNotIn("${{ github.ref }}", release_workflow)
        self.assertIn("$GITHUB_REF", release_workflow)
        self.assertIn("$GITHUB_SHA", release_workflow)

    def test_signing_private_keys_never_cross_job_or_non_release_boundaries(self) -> None:
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        self.assertNotIn("release-signing-identity", workflow)
        self.assertNotIn("artifacts/signing/*", workflow)
        self.assertNotIn("temp_signing_key.pfx", workflow)
        self.assertNotIn("PackageCertificatePassword=", workflow)
        self.assertIn("Prepare development signing identity", workflow)
        self.assertIn("Prepare release signing identity", workflow)
        self.assertIn("Tagged releases require WINCARE_SIGNING_CERT_BASE64", workflow)
        self.assertIn("WINCARE_DEVELOPMENT_SIGNING=true", workflow)
        self.assertIn("WINCARE_DEVELOPMENT_SIGNING=false", workflow)
        self.assertIn("WinCare.App_*.msix", workflow)
        self.assertIn("Dependencies", workflow)
        self.assertNotIn("AppPackages/**/*.msix", workflow)
        self.assertNotIn("Cert:\\CurrentUser\\Root", workflow)
        self.assertNotIn("Cert:\\LocalMachine\\Root", workflow)
        self.assertIn("CustomRootTrust", workflow)
        self.assertIn("CustomTrustStore", workflow)
        self.assertIn("cancel-in-progress: true", workflow)
        self.assertIn("timeout-minutes: 20", workflow)

        development_start = workflow.index("Prepare development signing identity")
        release_start = workflow.index("Prepare release signing identity")
        development_block = workflow[development_start:release_start]
        self.assertNotIn("WINCARE_SIGNING_CERT_BASE64", development_block)
        self.assertNotIn("WINCARE_SIGNING_CERT_PASSWORD", development_block)

        verification_start = workflow.index("Verify MSIX signature and publisher")
        cleanup_start = workflow.index("Clean runner-local signing identity")
        verification_block = workflow[verification_start:cleanup_start]
        self.assertIn("timeout-minutes: 3", verification_block)
        self.assertIn("CustomRootTrust", verification_block)
        self.assertIn("CustomTrustStore", verification_block)
        self.assertIn("WINCARE_DEVELOPMENT_SIGNING", verification_block)
        self.assertIn("Runtime negative test", verification_block)
        self.assertNotIn("Cert:\\CurrentUser\\Root", verification_block)
        self.assertNotIn("Cert:\\LocalMachine\\Root", verification_block)

    def test_installer_pins_signer_before_trust_and_revalidates_after_import(self) -> None:
        installer = (ROOT / "tools/install_msix.py").read_text(encoding="utf-8")
        initial_index = installer.index("$initialSig = Get-AuthenticodeSignature")
        thumbprint_index = installer.index("does not match package signer thumbprint")
        trusted_people_index = installer.index("$trustedPeoplePath =")
        final_index = installer.index("$finalSig = Get-AuthenticodeSignature")
        self.assertLess(initial_index, thumbprint_index)
        self.assertLess(thumbprint_index, trusted_people_index)
        self.assertLess(trusted_people_index, final_index)
        self.assertIn("LocalMachine", installer)
        self.assertIn("TrustedPeople", installer)
        self.assertIn("elevated Administrator terminal", installer)
        self.assertIn("no matching --certificate was provided", installer)
        self.assertNotIn("Cert:\\\\CurrentUser\\\\Root", installer)
        self.assertNotIn("Cert:\\\\LocalMachine\\\\Root", installer)

    def test_no_trusted_root_store_mutation_exists(self) -> None:
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        installer = (ROOT / "tools/install_msix.py").read_text(encoding="utf-8")
        self.assertNotIn("Cert:\\CurrentUser\\Root", workflow)
        self.assertNotIn("Cert:\\LocalMachine\\Root", workflow)
        self.assertNotIn("Cert:\\\\CurrentUser\\\\Root", installer)
        self.assertNotIn("Cert:\\\\LocalMachine\\\\Root", installer)

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
