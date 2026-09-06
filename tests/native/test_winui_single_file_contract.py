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
        self.assertIn(
            "<ProjectPriFileName Condition=\"'$(PublishSingleFile)' == 'true'\">resources.pri</ProjectPriFileName>",
            project,
        )
        self.assertIn(
            "Condition=\"'$(Configuration)' == 'Release' and '$(PublishSingleFile)' == 'true' and '$(WindowsPackageType)' == 'None'\"",
            project,
        )
        self.assertIn("<PublishTrimmed>true</PublishTrimmed>", project)
        self.assertIn("<TrimMode>partial</TrimMode>", project)
        self.assertIn("<SuppressTrimAnalysisWarnings>true</SuppressTrimAnalysisWarnings>", project)
        self.assertIn("<PublishReadyToRun>false</PublishReadyToRun>", project)

        profiles = root / "src/WinCare.App/Properties/PublishProfiles"
        for name in ("portable-x64.pubxml", "portable-ARM64.pubxml"):
            profile = (profiles / name).read_text(encoding="utf-8")
            self.assertIn("<WindowsPackageType>None</WindowsPackageType>", profile, name)
            self.assertIn("<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>", profile, name)
            self.assertIn("<SelfContained>true</SelfContained>", profile, name)
            self.assertIn("<PublishSingleFile>true</PublishSingleFile>", profile, name)
            self.assertIn("<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>", profile, name)
            self.assertIn("<PublishTrimmed>true</PublishTrimmed>", profile, name)
            self.assertIn("<TrimMode>partial</TrimMode>", profile, name)
            self.assertIn("<PublishReadyToRun>false</PublishReadyToRun>", profile, name)

    def test_desktop_smoke_reads_process_command_line(self) -> None:
        root = Path(__file__).resolve().parents[2]
        app = (root / "src/WinCare.App/App.xaml.cs").read_text(encoding="utf-8")

        self.assertIn("Environment.GetCommandLineArgs()", app)
        self.assertIn("PortableSmokeArgument", app)
        self.assertIn(".Any(argument => string.Equals(", app)
        self.assertNotIn("args.Arguments?.Trim()", app)
        self.assertNotIn("!string.IsNullOrWhiteSpace(args.Arguments)", app)


if __name__ == "__main__":
    unittest.main()
