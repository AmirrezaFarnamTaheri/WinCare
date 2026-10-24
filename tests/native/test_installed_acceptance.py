from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[2]
class InstalledLifecycleContracts(unittest.TestCase):
    def test_acceptance_requires_explicit_opt_in_and_disposable_host(self):
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text()
        self.assertIn("run_installed_acceptance:", workflow)
        self.assertIn("installed-lifecycle-acceptance:", workflow)
        self.assertIn("inputs.run_installed_acceptance == true", workflow)
        # PR #59 must exercise the real disposable-host install/uninstall
        # acceptance matrix before it may become mergeable.
        self.assertIn("github.head_ref == 'fix/audit-reliability-baseline-20261024'", workflow)
        self.assertIn("WINCARE_DISPOSABLE_TEST_HOST: '1'", workflow)
    def test_acceptance_checks_signer_and_always_uninstalls(self):
        source = (ROOT / "tools/test_msix_lifecycle.ps1").read_text()
        for token in ("WINCARE_DISPOSABLE_TEST_HOST -ne '1'", "Get-AuthenticodeSignature", "Get-AppxPackage", "Add-AppxPackage", "Remove-AppxPackage", "finally {"):
            self.assertIn(token, source)
if __name__ == "__main__":
    unittest.main()
