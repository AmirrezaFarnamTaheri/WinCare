"""Publishing a draft must not mark it latest until it becomes public."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


class ReleaseDraftTests(unittest.TestCase):
    def test_release_is_created_as_draft_without_latest_flag(self) -> None:
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        create = workflow.split('gh release create "$RELEASE_TAG" release_assets/*', 1)[1]
        create = create.split('gh release edit "$RELEASE_TAG"', 1)[0]
        self.assertIn("--draft", create)
        self.assertNotIn('"$RELEASE_FLAGS"', create)

    def test_retry_recovers_only_fully_verified_existing_draft(self) -> None:
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        branch = workflow.split('if gh release view "$RELEASE_TAG"', 1)[1]
        self.assertLess(branch.index("check_release_assets.py"), branch.index("--json isDraft"))
        self.assertIn('"$RELEASE_FLAGS" --draft=false', branch)


if __name__ == "__main__":
    unittest.main()
