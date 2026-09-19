from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
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
        command_count = source["commandCount"]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            incomplete = root / "incomplete.json"
            incomplete.write_text(json.dumps(source), encoding="utf-8")
            with self.assertRaisesRegex(ProductionBlockedError, rf"{command_count} command"):
                evaluate_readiness(incomplete, mode="production")

            for command in source["commands"]:
                command["migrationStatus"] = "BehaviorVerified"
            complete = root / "complete.json"
            complete.write_text(json.dumps(source), encoding="utf-8")
            readiness = evaluate_readiness(complete, mode="production")
            self.assertTrue(readiness.production_ready)
            self.assertEqual(command_count, readiness.behavior_verified)
            self.assertEqual(0, readiness.production_blockers)

    def test_rc_finalization_separates_native_source_and_legacy_oracle(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            command_count = json.loads((ROOT / "src/WinCare.CommandCatalog/Data/commands.json").read_text(encoding="utf-8"))["commandCount"]
            output = Path(directory)
            result = finalize_release(ROOT, output, version="2.5.0-rc3", mode="rc")

            self.assertTrue(result.native_archive.is_file())
            self.assertTrue(result.oracle_archive.is_file())
            self.assertTrue(result.report_path.is_file())
            self.assertTrue(result.manifest_path.is_file())
            self.assertEqual(command_count, result.readiness.cataloged)
            self.assertEqual(command_count, result.readiness.implemented)
            self.assertEqual(0, result.readiness.behavior_verified)
            self.assertEqual(0, result.readiness.implementation_blockers)
            self.assertEqual(command_count, result.readiness.production_blockers)

            with zipfile.ZipFile(result.native_archive) as archive:
                names = archive.namelist()
                self.assertTrue(names)
                self.assertFalse(any(name.lower().endswith((".ps1", ".psm1", ".psd1")) for name in names))
                self.assertFalse(any(name.lower().endswith((".pfx", ".p12", ".key", ".pem", ".snk", ".secret", ".token")) for name in names))
                self.assertFalse(any("__pycache__" in name or name.lower().endswith((".pyc", ".pyo")) for name in names))
                self.assertIn("migration/oracle/legacy-command-ids.json", names)
                self.assertIn("docs/migration/finalization-status.md", names)
                self.assertIn("tools/finalize_native_release.py", names)
                for required in ("tests/__init__.py", "PRODUCT.md", "UX-CONTRACT.md", "FINAL-VALIDATION.md"):
                    self.assertIn(required, names)
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
            self.assertEqual("2.5.0-rc3", manifest["version"])
            self.assertEqual("rc", manifest["mode"])
            self.assertEqual(command_count, manifest["readiness"]["cataloged"])
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

    def test_stage_release_assets_keeps_architecture_specific_certificates(self) -> None:
        from tools.stage_release_assets import stage_assets
        with tempfile.TemporaryDirectory() as src_dir, tempfile.TemporaryDirectory() as dst_dir:
            src = Path(src_dir)
            dst = Path(dst_dir)
            x64 = src / "WinCare-x64" / "artifacts" / "signing"
            arm64 = src / "WinCare-ARM64" / "artifacts" / "signing"
            x64.mkdir(parents=True)
            arm64.mkdir(parents=True)
            (x64 / "WinCare.cer").write_text("CERT_X64_CONTENT", encoding="utf-8")
            (arm64 / "WinCare.cer").write_text("CERT_ARM64_CONTENT", encoding="utf-8")

            staged = stage_assets(src, dst, version="2.5.0-rc1")

            self.assertEqual(
                [
                    dst.resolve() / "WinCare-v2.5.0-rc1-ARM64.cer",
                    dst.resolve() / "WinCare-v2.5.0-rc1-x64.cer",
                ],
                staged,
            )

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

    def test_stage_release_assets_stages_portable_without_fabricating_installer(self) -> None:
        from tools.stage_release_assets import stage_assets
        with tempfile.TemporaryDirectory() as src_dir, tempfile.TemporaryDirectory() as dst_dir:
            src = Path(src_dir)
            dst = Path(dst_dir)
            portable_dir = src / "win-x64" / "portable"
            portable_dir.mkdir(parents=True)
            (portable_dir / "WinCare.App.exe").write_bytes(b"PORTABLE_EXE")
            installer_dir = src / "installer"
            installer_dir.mkdir(parents=True)
            # The pipeline compiles no Inno installer: a stray Setup.exe must not be promoted into
            # a release asset under a name the release page never produced.
            (installer_dir / "WinCare-Setup.exe").write_bytes(b"SETUP_EXE")

            staged = stage_assets(src, dst, version="2.5.0-rc1")
            staged_names = [path.name for path in staged]
            self.assertIn("WinCare-v2.5.0-rc1-x64.exe", staged_names)
            self.assertEqual(1, len(staged))
            self.assertFalse(any("Setup" in name or "Installer" in name for name in staged_names))

    def test_staging_cli_accepts_repeated_manifest_paths_and_rejects_swapped_certificates(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            downloads = root / "downloads"
            manifests = []
            for arch in ("x64", "ARM64"):
                package = downloads / f"package-{arch}"
                signing = package / "artifacts/signing"
                release = package / "artifacts/release"
                signing.mkdir(parents=True)
                release.mkdir(parents=True)
                assets = {
                    signing / "WinCare.cer": arch.encode(),
                    release / f"WinCare-{arch}.exe": f"exe-{arch}".encode(),
                    release / f"WinCare-{arch}-portable.zip": f"zip-{arch}".encode(),
                    release / f"WinCare.App_1.0.0.0_{arch}.msix": f"msix-{arch}".encode(),
                    package / "install_msix.py": b"shared installer",
                }
                for path, payload in assets.items():
                    path.write_bytes(payload)
                manifest = release / "SHA256SUMS"
                manifest.write_text("".join(
                    f"{hashlib.sha256(payload).hexdigest()}  {path.name}" + chr(10)
                    for path, payload in assets.items()
                ), encoding="utf-8")
                manifests.append(manifest)
            command = [sys.executable, str(ROOT / "tools/stage_release_assets.py"),
                       "--downloads", str(downloads), "--output", str(root / "out"),
                       "--version", "1.0.0"]
            for manifest in manifests:
                command.extend(["--expected-manifest", str(manifest)])
            result = subprocess.run(command, capture_output=True, text=True, timeout=30)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(9, len((root / "out/SHA256SUMS").read_text().splitlines()))

            # A digest from the other architecture must not authorize this certificate.
            (downloads / "package-x64/artifacts/signing/WinCare.cer").write_bytes(b"ARM64")
            result = subprocess.run(command, capture_output=True, text=True, timeout=30)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("but the build-time digest was", result.stderr)

    def test_stage_release_assets_fails_closed_on_digest_mismatch(self) -> None:
        from tools.stage_release_assets import stage_assets
        with tempfile.TemporaryDirectory() as src_dir, tempfile.TemporaryDirectory() as dst_dir:
            src = Path(src_dir)
            dst = Path(dst_dir)
            portable_dir = src / "win-x64" / "portable"
            portable_dir.mkdir(parents=True)
            (portable_dir / "WinCare.App.exe").write_bytes(b"PORTABLE_EXE")
            manifest = src / "SHA256SUMS"
            wrong_digest = hashlib.sha256(b"bytes the build runner did not produce").hexdigest()
            manifest.write_text(f"{wrong_digest}  WinCare.App.exe\n", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "but the build-time digest was"):
                stage_assets(src, dst, version="2.5.0-rc1", expected_manifests=[manifest])

    def test_stage_release_assets_fails_closed_on_missing_digest_entry(self) -> None:
        from tools.stage_release_assets import stage_assets
        with tempfile.TemporaryDirectory() as src_dir, tempfile.TemporaryDirectory() as dst_dir:
            src = Path(src_dir)
            dst = Path(dst_dir)
            portable_dir = src / "win-x64" / "portable"
            portable_dir.mkdir(parents=True)
            (portable_dir / "WinCare.App.exe").write_bytes(b"PORTABLE_EXE")
            manifest = src / "SHA256SUMS"
            digest = hashlib.sha256(b"PORTABLE_EXE").hexdigest()
            manifest.write_text(f"{digest}  some-other-asset.zip\n", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "no build-time digest"):
                stage_assets(src, dst, version="2.5.0-rc1", expected_manifests=[manifest])

    def test_stage_release_assets_publishes_verified_digest_manifest(self) -> None:
        from tools.stage_release_assets import stage_assets
        with tempfile.TemporaryDirectory() as src_dir, tempfile.TemporaryDirectory() as dst_dir:
            src = Path(src_dir)
            dst = Path(dst_dir)
            portable_dir = src / "win-x64" / "portable"
            portable_dir.mkdir(parents=True)
            (portable_dir / "WinCare.App.exe").write_bytes(b"PORTABLE_EXE")
            digest = hashlib.sha256(b"PORTABLE_EXE").hexdigest()
            manifest = src / "SHA256SUMS"
            manifest.write_text(f"{digest}  WinCare.App.exe\n", encoding="utf-8")

            staged = stage_assets(src, dst, version="2.5.0-rc1", expected_manifests=[manifest])

            published_manifest = dst / "SHA256SUMS"
            self.assertTrue(published_manifest.is_file())
            self.assertNotIn(published_manifest, staged)
            self.assertEqual(
                f"{digest}  WinCare-v2.5.0-rc1-x64.exe\n",
                published_manifest.read_text(encoding="utf-8"),
            )

    def test_stage_release_assets_verifies_each_architecture_against_its_own_manifest(self) -> None:
        from tools.stage_release_assets import stage_assets
        with tempfile.TemporaryDirectory() as src_dir, tempfile.TemporaryDirectory() as dst_dir:
            src = Path(src_dir)
            dst = Path(dst_dir)
            x64_dir = src / "package-x64"
            arm64_dir = src / "package-ARM64"
            x64_dir.mkdir(parents=True)
            arm64_dir.mkdir(parents=True)

            # Each architecture builds its own exe, zip, and runner-local signing certificate,
            # and its SHA256SUMS records only its own digests.
            x64_cer = "CERT_X64_CONTENT"
            arm64_cer = "CERT_ARM64_CONTENT"
            x64_manifest = src / "package-x64" / "SHA256SUMS"
            arm64_manifest = src / "package-ARM64" / "SHA256SUMS"
            artifacts = {
                x64_dir: [("WinCare.App.exe", b"X64_EXE"), ("WinCare.cer", x64_cer.encode())],
                arm64_dir: [("WinCare.App.exe", b"ARM64_EXE"), ("WinCare.cer", arm64_cer.encode())],
            }
            digests_by_dir: dict[Path, dict[str, str]] = {}
            for directory, files in artifacts.items():
                digests_by_dir[directory] = {}
                for name, payload in files:
                    (directory / name).write_bytes(payload)
                    digests_by_dir[directory][name] = hashlib.sha256(payload).hexdigest()
            x64_manifest.write_text(
                "".join(f"{digest}  {name}\n" for name, digest in digests_by_dir[x64_dir].items()),
                encoding="utf-8",
            )
            arm64_manifest.write_text(
                "".join(f"{digest}  {name}\n" for name, digest in digests_by_dir[arm64_dir].items()),
                encoding="utf-8",
            )

            staged = stage_assets(
                src,
                dst,
                version="2.5.0-rc1",
                expected_manifests=[x64_manifest, arm64_manifest],
            )

            staged_names = [path.name for path in staged]
            self.assertIn("WinCare-v2.5.0-rc1-x64.exe", staged_names)
            self.assertIn("WinCare-v2.5.0-rc1-ARM64.exe", staged_names)
            published_manifest = dst / "SHA256SUMS"
            published_lines = published_manifest.read_text(encoding="utf-8").splitlines()
            self.assertIn(
                f"{hashlib.sha256(b'X64_EXE').hexdigest()}  WinCare-v2.5.0-rc1-x64.exe",
                published_lines,
            )
            self.assertIn(
                f"{hashlib.sha256(b'ARM64_EXE').hexdigest()}  WinCare-v2.5.0-rc1-ARM64.exe",
                published_lines,
            )

    def test_stage_release_assets_rejects_bytes_absent_from_every_manifest(self) -> None:
        from tools.stage_release_assets import stage_assets
        with tempfile.TemporaryDirectory() as src_dir, tempfile.TemporaryDirectory() as dst_dir:
            src = Path(src_dir)
            dst = Path(dst_dir)
            x64_dir = src / "package-x64"
            arm64_dir = src / "package-ARM64"
            x64_dir.mkdir(parents=True)
            arm64_dir.mkdir(parents=True)
            (x64_dir / "WinCare.App.exe").write_bytes(b"X64_EXE")
            substituted = arm64_dir / "WinCare.App.exe"
            substituted.write_bytes(b"TAMPERED_ARM64_EXE")
            x64_manifest = src / "package-x64" / "SHA256SUMS"
            arm64_manifest = src / "package-ARM64" / "SHA256SUMS"
            x64_manifest.write_text(
                f"{hashlib.sha256(b'X64_EXE').hexdigest()}  WinCare.App.exe\n", encoding="utf-8"
            )
            arm64_manifest.write_text(
                f"{hashlib.sha256(b'ARM64_EXE').hexdigest()}  WinCare.App.exe\n", encoding="utf-8"
            )

            with self.assertRaisesRegex(ValueError, "but the build-time digest was"):
                stage_assets(src, dst, version="2.5.0-rc1", expected_manifests=[x64_manifest, arm64_manifest])

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
                    "2.5.0-rc3",
                    "--mode",
                    "rc",
                ],
                cwd=ROOT,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertTrue((Path(directory) / "WinCare-2.5.0-rc3-native-source.zip").is_file())

    def test_release_metadata_is_pinned_across_release_surfaces(self) -> None:
        props_path = ROOT / "Directory.Build.props"
        props = ET.parse(props_path).getroot()
        manifest = (ROOT / "src/WinCare.App/Package.appxmanifest").read_text(encoding="utf-8")
        csproj = (ROOT / "src/WinCare.App/WinCare.App.csproj").read_text(encoding="utf-8")
        cargo = (ROOT / "native/wincare-core/Cargo.toml").read_text(encoding="utf-8")
        rust_source = (ROOT / "native/wincare-core/src/lib.rs").read_text(encoding="utf-8")

        prefix = props.findtext(".//VersionPrefix")
        suffix = props.findtext(".//VersionSuffix")
        informational = props.findtext(".//InformationalVersion")
        self.assertEqual("3.0.0", prefix)
        self.assertIsNone(suffix)
        self.assertEqual(prefix, informational)

        # The checked-in manifest carries the numeric fallback (VersionPrefix + ".0"); the
        # build-time StampAppxManifestVersion target derives the packaged version instead of
        # trusting a literal.
        self.assertIn(f'Version="{prefix}.0"', manifest)
        self.assertIn("StampAppxManifestVersion", csproj)
        self.assertIn("AppxPackageVersion", csproj)
        self.assertIn(f'version = "{prefix}"', cargo)
        self.assertIn(f'const VERSION: &[u8] = b"{prefix}";', rust_source)

    def test_native_workflow_pins_actions_and_rust_toolchain(self) -> None:
        workflow_path = ROOT / ".github/workflows/native-winui.yml"
        removed_workflow = ROOT / ".github/workflows/native-release-candidate.yml"
        workflow = workflow_path.read_text(encoding="utf-8")
        uses_lines = [line.strip() for line in workflow.splitlines() if "uses:" in line]
        self.assertTrue(uses_lines)
        self.assertFalse(any("@v" in line or "@stable" in line for line in uses_lines), uses_lines)
        self.assertIn("toolchain: 1.97.1", workflow)
        self.assertFalse(removed_workflow.exists())

        toolchain = (ROOT / "rust-toolchain.toml").read_text(encoding="utf-8")
        self.assertIn('channel = "1.97.1"', toolchain)
        self.assertIn('components = ["rustfmt", "clippy"]', toolchain)

    def test_release_workflow_publishes_verified_versioned_assets(self) -> None:
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        self.assertIn("Resolve release metadata", workflow)
        self.assertIn('mode = "rc" if "-" in version else "production"', workflow)
        self.assertIn("Finalize release source", workflow)
        self.assertIn('--mode "$FINALIZE_MODE"', workflow)
        self.assertIn("Stage packaged release assets", workflow)
        self.assertIn("pattern: package-*", workflow)
        self.assertIn("WinCare-release-${{ needs.verify.outputs.product_version }}", workflow)
        self.assertIn("contents: write", workflow)
        self.assertIn("Publish GitHub release", workflow)
        self.assertIn("GH_TOKEN: ${{ github.token }}", workflow)
        self.assertIn("gh release create", workflow)
        self.assertNotIn("--clobber", workflow)
        self.assertNotIn("git tag -f", workflow)
        self.assertNotIn("git push origin -f", workflow)

    def test_workflow_is_consolidated_without_internal_artifact_hops(self) -> None:
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        self.assertIn("\n  build:\n", workflow)
        self.assertNotIn("\n  rust:\n", workflow)
        self.assertNotIn("native-${{ matrix.target }}", workflow)
        self.assertNotIn("WinCare-screenshots", workflow)
        self.assertNotIn("python tools/capture_screenshots.py\n", workflow)
        self.assertIn("python tools/capture_screenshots.py --verify-only", workflow)
        self.assertNotIn("WinCare-native-source-finalization", workflow)
        self.assertIn("python -m unittest discover -s tests -t . -v", workflow)
        self.assertIn("default: false", workflow)

    def test_runner_local_signing_identity_never_leaves_the_packaging_job(self) -> None:
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        self.assertNotIn("release-signing-identity", workflow)
        self.assertNotIn("artifacts/signing/*", workflow)
        self.assertNotIn("temp_signing_key.pfx", workflow)
        self.assertNotIn("PackageCertificatePassword=", workflow)
        self.assertIn("Sign and verify MSIX", workflow)
        self.assertNotIn("WINCARE_SIGNING_CERT_BASE64", workflow)
        self.assertNotIn("WINCARE_SIGNING_CERT_PASSWORD", workflow)
        self.assertNotIn("WINCARE_DEVELOPMENT_SIGNING", workflow)
        self.assertIn("WinCare.App_*.msix", workflow)
        self.assertIn("Dependencies", workflow)
        self.assertNotIn("AppPackages/**/*.msix", workflow)
        self.assertNotIn("Cert:\\CurrentUser\\Root", workflow)
        self.assertNotIn("Cert:\\LocalMachine\\Root", workflow)
        self.assertIn("CustomRootTrust", workflow)
        self.assertIn("CustomTrustStore", workflow)
        self.assertIn("timeout-minutes: 20", workflow)

        verification_start = workflow.index("Sign and verify MSIX")
        publish_start = workflow.index("Publish portable executable")
        verification_block = workflow[verification_start:publish_start]
        self.assertIn("timeout-minutes: 3", verification_block)
        self.assertIn("CustomRootTrust", verification_block)
        self.assertIn("CustomTrustStore", verification_block)
        self.assertIn("tamper smoke test", verification_block)
        self.assertIn('Remove-Item -Force "Cert:\\CurrentUser\\My\\$($cert.Thumbprint)"', verification_block)
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
        self.assertIn("WindowsPowerShell", installer)
        self.assertIn('run_env["PSModulePath"]', installer)
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
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        self.assertIn("branches: [master, main]", workflow)
        self.assertIn("python -m unittest discover -s tests -t . -v", workflow)
        self.assertIn("finalize_native_release.py", workflow)
        self.assertIn('--mode "$FINALIZE_MODE"', workflow)
        self.assertIn('mode = "rc" if "-" in version else "production"', workflow)
        self.assertNotIn("--mode rc", workflow)
        self.assertNotIn("--mode production", workflow)

    def test_native_workflow_supports_manual_and_tagged_releases(self) -> None:
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        self.assertIn("workflow_dispatch:", workflow)
        self.assertIn("publish_release:", workflow)
        self.assertIn("release_tag:", workflow)
        self.assertIn("default: false", workflow)
        self.assertIn("startsWith(github.ref, 'refs/tags/v')", workflow)
        self.assertIn("github.event_name == 'workflow_dispatch'", workflow)


if __name__ == "__main__":
    unittest.main()
