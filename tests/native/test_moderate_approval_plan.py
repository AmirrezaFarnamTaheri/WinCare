"""Moderate system mutations require one-time target-bound approvals."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]

class ModerateReviewContractTests(unittest.TestCase):
    def test_moderate_admission_checks_issued_plan_not_boolean_alone(self):
        source = (ROOT / "src/WinCare.Application/Commands/CommandDispatcher.cs").read_text()
        moderate = source.split("else if (riskTier == RiskTier.Moderate)", 1)[1].split("else if (riskTier != RiskTier.Safe)", 1)[0]
        self.assertIn("!TryConsumeIssuedReviewPlan(request, definition)", moderate)
        self.assertNotIn("request.Approval is not null &&", moderate)

    def test_tool_ui_previews_moderate_before_applying(self):
        source = (ROOT / "src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs").read_text()
        self.assertIn("bool apply = IsMutatingTool && (IsSafeTool || IsReviewApproved)", source)
        self.assertIn("IsModerateTool || IsDestructiveTool", source)
        self.assertIn('IsModerateTool) return IsReviewApproved ? "Apply reviewed change" : "Preview impact"', source)

if __name__ == "__main__":
    unittest.main()
