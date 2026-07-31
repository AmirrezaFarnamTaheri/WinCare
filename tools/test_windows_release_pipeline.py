from __future__ import annotations

import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
VALIDATION = ROOT / "tools" / "Invoke-WindowsValidation.ps1"
BUILD_RELEASE = ROOT / "tools" / "Build-Release.ps1"
LIFECYCLE = ROOT / "tools" / "Test-InstallationLifecycle.ps1"
WORKFLOW = ROOT / ".github" / "workflows" / "windows-release-validation.yml"
NATIVE_PROJECT = ROOT / "src" / "WinCare" / "Native" / "WinCare.Native.csproj"
SHELL_HARDWARE = ROOT / "src" / "WinCare" / "Native" / "WinCare.ShellHardware.cs"


class WindowsReleasePipelineTests(unittest.TestCase):
    def test_pester_gates_accept_newer_compatible_versions(self) -> None:
        for path in (VALIDATION, BUILD_RELEASE):
            text = path.read_text(encoding="utf-8")
            self.assertNotIn("Import-Module Pester -RequiredVersion 5.5.0", text, path.name)
            self.assertIn("Import-Module Pester -MinimumVersion 5.5.0", text, path.name)

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
            "RELEASE-RECEIPT.json",
            ".zip.sha256",
            ".zip.intoto.jsonl",
            "WinCare.Standalone.build.json",
            "-build-result.json",
        ):
            self.assertIn(required, text)
        self.assertIn("supply-chain-inventory.json", text)

    def test_validation_threads_previous_release_into_production_lifecycle(self) -> None:
        validation = VALIDATION.read_text(encoding="utf-8")
        build_release = BUILD_RELEASE.read_text(encoding="utf-8")
        lifecycle = LIFECYCLE.read_text(encoding="utf-8")
        self.assertIn("[string]$PreviousReleaseArchivePath", validation)
        self.assertIn("-PreviousReleaseArchivePath", validation)
        self.assertIn("[string]$PreviousReleaseArchivePath", build_release)
        self.assertIn("PreviousArchivePath", build_release)
        self.assertIn("[string]$PreviousArchivePath", lifecycle)
        self.assertIn("'version-upgrade'", lifecycle)
        self.assertIn("UpgradeVerified = $true", lifecycle)
        self.assertIn("UpgradeRollbackVerified = $true", lifecycle)

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
