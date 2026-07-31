from __future__ import annotations

import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
VALIDATION = ROOT / "tools" / "Invoke-WindowsValidation.ps1"
BUILD_RELEASE = ROOT / "tools" / "Build-Release.ps1"
BUILD_RELEASE_PY = ROOT / "tools" / "build_release.py"
GIT_ATTRIBUTES = ROOT / ".gitattributes"
WINDOWS_VALIDATION = ROOT / "tools" / "Invoke-WindowsValidation.ps1"
PREVIOUS_FIXTURE = ROOT / "tools" / "Build-PreviousReleaseFixture.ps1"
LIFECYCLE = ROOT / "tools" / "Test-InstallationLifecycle.ps1"
UPGRADE = ROOT / "tools" / "Test-UpgradeLifecycle.ps1"
FINALIZER = ROOT / "tools" / "finalize_release.py"
WORKFLOW = ROOT / ".github" / "workflows" / "windows-release-validation.yml"
NATIVE_PROJECT = ROOT / "src" / "WinCare" / "Native" / "WinCare.Native.csproj"
SHELL_HARDWARE = ROOT / "src" / "WinCare" / "Native" / "WinCare.ShellHardware.cs"
BRAND_MANIFEST = ROOT / "design" / "WinCare-Brand.manifest.json"
SHORTCUTS = ROOT / "src" / "WinCare" / "Install" / "Private" / "30-Shortcuts.ps1"


class WindowsReleasePipelineTests(unittest.TestCase):
    def test_pester_gates_accept_newer_compatible_versions(self) -> None:
        for path in (VALIDATION, BUILD_RELEASE):
            text = path.read_text(encoding="utf-8")
            self.assertNotIn("Import-Module Pester -RequiredVersion 5.5.0", text, path.name)
            self.assertIn("Import-Module Pester -MinimumVersion 5.5.0", text, path.name)

    def test_production_release_gate_does_not_skip_tests(self) -> None:
        text = VALIDATION.read_text(encoding="utf-8")
        start = text.index("$release = Invoke-ValidationGate -Name '05-release'")
        end = text.index("$cleanInstall = Invoke-ValidationGate", start)
        self.assertNotIn("-SkipTests", text[start:end])

    def test_production_package_contains_closed_canonical_brand_assets(self) -> None:
        text = BUILD_RELEASE_PY.read_text(encoding="utf-8")
        required = {
            "design/BRAND.md",
            "design/WinCare-Logo.svg",
            "design/WinCare-Wordmark.svg",
            "design/WinCare-Logo-512.png",
            "design/WinCare.ico",
            "design/WinCare-Brand.manifest.json",
        }
        self.assertIn("PRODUCTION_BRAND_FILES", text)
        self.assertIn("required = PRODUCTION_ROOT_FILES | PRODUCTION_CONFIG_FILES | PRODUCTION_BRAND_FILES", text)
        for asset in required:
            self.assertIn(f'"{asset}"', text)

    def test_brand_vector_assets_are_lf_normalized_across_windows_clones(self) -> None:
        text = GIT_ATTRIBUTES.read_text(encoding="utf-8")
        self.assertEqual(text.count("*.svg text eol=lf"), 1)

    def test_windows_validation_timeout_contract_supports_public_maximum(self) -> None:
        text = WINDOWS_VALIDATION.read_text(encoding="utf-8")
        self.assertIn("[ValidateRange(60,7200)][int]$GateTimeoutSeconds", text)
        self.assertIn("[ValidateRange(1,7200)][int]$TimeoutSeconds", text)
        self.assertNotIn("[ValidateRange(1,3600)][int]$TimeoutSeconds", text)

    def test_release_workflow_preserves_a_clean_tree_until_validation_finishes(self) -> None:
        text = WORKFLOW.read_text(encoding="utf-8")
        clean_check = text.index("git status --porcelain")
        validation = text.index("Invoke-WindowsValidation.ps1")
        self.assertLess(clean_check, validation)
        self.assertNotIn("AllowDirty", text)
        self.assertNotIn("Remove-Item", text[:validation])

    def test_release_workflow_is_pinned_and_uploads_digest_backed_evidence(self) -> None:
        text = WORKFLOW.read_text(encoding="utf-8")
        action_uses = re.findall(r"uses:\s*([^\s]+)", text)
        self.assertGreaterEqual(len(action_uses), 3)
        for action in action_uses:
            self.assertRegex(action, r"^[^@]+@[0-9a-f]{40}$")
        self.assertIn("id: release-evidence", text)
        self.assertIn("steps.release-evidence.outputs.artifact-digest", text)
        self.assertIn("artifacts/windows-validation", text)

    def test_release_workflow_requires_complete_supply_chain_evidence(self) -> None:
        text = WORKFLOW.read_text(encoding="utf-8")
        for required in (
            "SBOM.spdx.json",
            "BUILD-RECEIPT.json",
            "-release-receipt.json",
            ".zip.sha256",
            ".zip.intoto.jsonl",
            "WinCare.Standalone.build.json",
            "-build-result.json",
        ):
            self.assertIn(required, text)
        self.assertIn("supply-chain-inventory.json", text)

    def test_release_brand_is_closed_across_source_installer_and_binaries(self) -> None:
        self.assertTrue(BRAND_MANIFEST.is_file())
        manifest = json.loads(BRAND_MANIFEST.read_text(encoding="utf-8"))
        self.assertEqual(manifest["schema"], "wincare.brand.identity/v1")
        self.assertEqual(
            manifest["iconSizes"],
            [16, 20, 24, 32, 40, 48, 64, 128, 256],
        )
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("-BRAND.json", workflow)
        self.assertIn("EmbeddedIconVerified", workflow)
        self.assertIn("IconSha256", workflow)
        shortcuts = SHORTCUTS.read_text(encoding="utf-8")
        self.assertIn("ExpectedIconLocation", shortcuts)
        self.assertIn("$shortcut.IconLocation", shortcuts)

    def test_volatile_branch_evidence_is_excluded_from_release_bytes(self) -> None:
        text = FINALIZER.read_text(encoding="utf-8")
        self.assertIn('folded == "docs/windows-validation-evidence.md"', text)

    def test_previous_release_fixture_is_isolated_committed_and_verified(self) -> None:
        text = PREVIOUS_FIXTURE.read_text(encoding="utf-8")
        self.assertIn("git clone --no-hardlinks", text)
        self.assertIn("validate_source_references.py", text)
        self.assertIn("git commit", text)
        self.assertIn("Build-Release.ps1", text)
        self.assertIn("previous-release-fixture.json", text)
        self.assertIn("CHANGELOG.md", text)
        self.assertIn("Previous-release changelog", text)
        self.assertIn('"## $previous"', text)

    def test_previous_release_fixture_proves_built_archive_version(self) -> None:
        text = PREVIOUS_FIXTURE.read_text(encoding="utf-8")
        self.assertIn("[Text.RegularExpressions.MatchEvaluator]", text)
        self.assertIn("$committedVersion", text)
        self.assertIn("Get-WinCareFixtureArchiveManifestVersion", text)
        self.assertIn("if ($archives.Count -ne 1)", text)
        self.assertIn("if ($archiveVersion -ne $previous)", text)
        self.assertIn("previous-release-verification.json", text)

    def test_validation_threads_previous_release_into_focused_upgrade_lifecycle(self) -> None:
        validation = VALIDATION.read_text(encoding="utf-8")
        build_release = BUILD_RELEASE.read_text(encoding="utf-8")
        lifecycle = LIFECYCLE.read_text(encoding="utf-8")
        upgrade = UPGRADE.read_text(encoding="utf-8")
        self.assertIn("[string]$PreviousReleaseArchivePath", validation)
        self.assertIn("-PreviousReleaseArchivePath", validation)
        self.assertIn("[string]$PreviousReleaseArchivePath", build_release)
        self.assertIn("PreviousArchivePath", build_release)
        self.assertIn("[string]$PreviousArchivePath", lifecycle)
        self.assertIn("Test-UpgradeLifecycle.ps1", lifecycle)
        self.assertIn("'version-upgrade'", upgrade)
        self.assertIn("UpgradeVerified = $true", upgrade)
        self.assertIn("UpgradeRollbackVerified = $true", upgrade)

    def test_upgrade_archive_diagnostics_cannot_contaminate_source_paths(self) -> None:
        upgrade = UPGRADE.read_text(encoding="utf-8")
        self.assertIn("$verificationOutput = @(", upgrade)
        self.assertIn("$verificationExitCode = $LASTEXITCODE", upgrade)
        self.assertIn("Write-Host ([string]$line)", upgrade)
        self.assertIn("return [string]$roots[0].FullName", upgrade)
        self.assertNotIn(
            "& $python (Join-Path $PSScriptRoot 'verify_release.py') $Archive --extract-to $Destination\n",
            upgrade,
        )

    def test_upgrade_lifecycle_emits_one_verdict_only(self) -> None:
        upgrade = UPGRADE.read_text(encoding="utf-8")
        self.assertIn("$null = & $previousInstaller", upgrade)
        self.assertIn("$null = & (Join-Path $tamperedSource 'Install-WinCare.ps1')", upgrade)
        self.assertIn("$currentInstallOutput = @(", upgrade)
        self.assertIn("$resultCandidates = @(", upgrade)
        self.assertIn("$resultCandidates.Count -ne 1", upgrade)
        self.assertIn("$null = & $currentUninstaller", upgrade)
        self.assertEqual(upgrade.count("Schema = 'wincare.installation.upgrade/v1'"), 1)

    def test_dotnet_build_outputs_are_ignored(self) -> None:
        ignores = (ROOT / ".gitignore").read_text(encoding="utf-8").splitlines()
        self.assertEqual(ignores.count("**/bin/"), 1)
        self.assertEqual(ignores.count("**/obj/"), 1)

    def test_native_build_treats_nullable_warnings_as_errors(self) -> None:
        project = NATIVE_PROJECT.read_text(encoding="utf-8")
        self.assertIn("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", project)

    def test_shell_hardware_has_explicit_non_null_defaults_and_nullable_interop(self) -> None:
        source = SHELL_HARDWARE.read_text(encoding="utf-8")
        for declaration in (
            "public string DeviceName { get; set; } = string.Empty;",
            "public string Description { get; set; } = string.Empty;",
            "public string PackageFullName { get; set; } = string.Empty;",
            "public string DeviceName = string.Empty;",
        ):
            self.assertIn(declaration, source)
        self.assertIn("StringBuilder? packageFullName", source)
        self.assertIn("string? arguments", source)


if __name__ == "__main__":
    unittest.main()
