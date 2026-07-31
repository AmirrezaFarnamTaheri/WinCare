from __future__ import annotations

import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "WinCare"


class ClosureCanonicalizationTests(unittest.TestCase):
    def test_late_loaded_closure_files_are_removed(self) -> None:
        paths = [
            SRC / "Core" / "99-CompatibilityClosure.ps1",
            SRC / "Core" / "99-SecurityHardening.ps1",
            SRC / "Providers" / "999a-CoreClosure.ps1",
            SRC / "Providers" / "999z-FinalClosure.ps1",
        ]
        self.assertEqual([], [str(path.relative_to(ROOT)) for path in paths if path.exists()])

    def test_loaded_sources_do_not_replace_functions_at_runtime(self) -> None:
        findings: list[str] = []
        patterns = (
            re.compile(r"\$\{function:[A-Za-z0-9_-]+\}\s*=", re.IGNORECASE),
            re.compile(r"Set-Item\s+-Path\s+['\"]Function:\\", re.IGNORECASE),
        )
        for directory in ("Core", "Providers", "Host", "Install", "UI"):
            for path in sorted((SRC / directory).rglob("*.ps1")):
                text = path.read_text(encoding="utf-8")
                if any(pattern.search(text) for pattern in patterns):
                    findings.append(str(path.relative_to(ROOT)))
        self.assertEqual([], findings)

    def test_canonical_owners_preserve_stronger_contracts(self) -> None:
        action_contracts = (SRC / "Core" / "11-ActionContracts.ps1").read_text(encoding="utf-8")
        menu = (SRC / "Core" / "00-12-MainMenu.ps1").read_text(encoding="utf-8")
        network = (SRC / "Core" / "00-04-NetworkExperiments.ps1").read_text(encoding="utf-8")
        playbooks = (SRC / "Core" / "00-08-PlaybookCatalog.ps1").read_text(encoding="utf-8")
        logging = (SRC / "Core" / "02-Logging.ps1").read_text(encoding="utf-8")
        vault = (SRC / "Core" / "01-ConfigVault.ps1").read_text(encoding="utf-8")
        transactions = (SRC / "Core" / "09-TransactionIntegrity.ps1").read_text(encoding="utf-8")
        remote = (SRC / "Providers" / "98a-RemoteSupportCatalog.ps1").read_text(encoding="utf-8")
        legacy = (SRC / "Core" / "00-10-LegacyProfiles.ps1").read_text(encoding="utf-8")

        for token in ("RequiredParameters", "AllowedParameters"):
            self.assertIn(token, action_contracts)
        for token in ("remote-support", "cleaner-preview"):
            self.assertIn(token, menu)
        for token in (
            "ReceiveSideScaling",
            "ReceiveSegmentCoalescing",
            "AutoTuningLevel",
            "EcnCapability",
        ):
            self.assertIn(token, network)
        self.assertNotRegex(playbooks, r"Test-WinCarePlaybookCompatibility[^}]*\$true")
        for token in ("minimumBuild", "maximumBuild", "architectures"):
            self.assertIn(token, playbooks)
        self.assertIn("Get-WinCarePropertyValue $script:WinCareState 'SessionId' ''", logging)
        self.assertIn("ProtectedData]::Unprotect", vault)
        for token in ("RecordHash", "ReceiptHash", "LastEventHash"):
            self.assertIn(token, transactions)
        for token in ("rustdesk", "anydesk", "teamviewer", "splashtop"):
            self.assertIn(token, remote.lower())
        for token in (
            "diagnostic-window",
            "optimize-debloat-ultimate",
            "personalization-legacy",
        ):
            self.assertIn(token, legacy)

    def test_source_manifests_reference_canonical_files_only(self) -> None:
        core = (SRC / "SourceManifests" / "Core.psd1").read_text(encoding="utf-8")
        providers = (SRC / "SourceManifests" / "Providers.psd1").read_text(encoding="utf-8")
        for removed in ("99-CompatibilityClosure.ps1", "99-SecurityHardening.ps1"):
            self.assertNotIn(removed, core)
        for removed in ("999a-CoreClosure.ps1", "999z-FinalClosure.ps1"):
            self.assertNotIn(removed, providers)
        self.assertIn("Core/01-ConfigVault.ps1", core)
        self.assertIn("Core/09-TransactionIntegrity.ps1", core)


if __name__ == "__main__":
    unittest.main()
