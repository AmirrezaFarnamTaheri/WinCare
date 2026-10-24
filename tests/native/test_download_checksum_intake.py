"""Source/UI regression: verified file downloads accept optional expected SHA-256."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]

class VerifiedDownloadsIntakeContracts(unittest.TestCase):
    def test_checksum_is_optional_but_strictly_validated_in_downloads_view(self):
        source = (ROOT / "src/WinCare.App/ViewModels/Pages/DownloadsPageViewModel.cs").read_text(encoding="utf-8")
        self.assertIn("public string ExpectedSha256", source)
        self.assertIn("digest.Length == 64 && digest.All(Uri.IsHexDigit)", source)
        self.assertIn("IsExpectedSha256Valid;", source)
        self.assertIn("link, ExpectedSha256", source)
        self.assertIn("OnPropertyChanged(nameof(CanAdd))", source)

    def test_optional_hash_input_is_advanced_and_explains_conditional_resume(self):
        markup = (ROOT / "src/WinCare.App/Views/Pages/DownloadsPage.xaml").read_text(encoding="utf-8")
        self.assertIn('Advanced: verify with SHA-256 (optional)', markup)
        self.assertIn('IsExpanded="False"', markup)
        self.assertIn("ViewModel.ExpectedSha256", markup)
        self.assertIn("ViewModel.ChecksumHint", markup)
        self.assertIn('MaxLength="64"', markup)
        self.assertIn('MaxLength="2048"', markup)

if __name__ == "__main__":
    unittest.main()
