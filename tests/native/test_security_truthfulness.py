"""Checkup must report unknown Defender service state as unknown, not unsafe."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


class SecurityTruthfulnessTests(unittest.TestCase):
    def test_defender_probe_failure_is_not_an_asserted_protection_failure(self) -> None:
        executor = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.System.cs").read_text(encoding="utf-8")
        checkup = (ROOT / "src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs").read_text(encoding="utf-8")

        self.assertIn("ClassifyDefenderServiceState", executor)
        self.assertIn("defenderServiceStateKnown", executor)
        self.assertIn("defenderServiceStateKnown", checkup)
        self.assertNotIn('findings.Add("Windows Defender isn\'t running")', checkup)
        self.assertIn("could not be confirmed", checkup)


if __name__ == "__main__":
    unittest.main()
