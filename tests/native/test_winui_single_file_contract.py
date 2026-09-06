from __future__ import annotations

import unittest
from pathlib import Path


class WinUiSingleFileContractTests(unittest.TestCase):
    def test_portable_publish_has_required_windows_app_sdk_single_file_properties(self) -> None:
        root = Path(__file__).resolve().parents[2]
        project = (root / "src/WinCare.App/WinCare.App.csproj").read_text(encoding="utf-8")

        self.assertIn("<EnableMsixTooling>true</EnableMsixTooling>", project)
        self.assertIn(
            "<IncludeAllContentForSelfExtract Condition=\"'$(PublishSingleFile)' == 'true'\">true</IncludeAllContentForSelfExtract>",
            project,
        )

        profiles = root / "src/WinCare.App/Properties/PublishProfiles"
        for name in ("portable-x64.pubxml", "portable-ARM64.pubxml"):
            profile = (profiles / name).read_text(encoding="utf-8")
            self.assertIn("<WindowsPackageType>None</WindowsPackageType>", profile, name)
            self.assertIn("<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>", profile, name)
            self.assertIn("<SelfContained>true</SelfContained>", profile, name)
            self.assertIn("<PublishSingleFile>true</PublishSingleFile>", profile, name)
            self.assertIn("<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>", profile, name)


if __name__ == "__main__":
    unittest.main()
