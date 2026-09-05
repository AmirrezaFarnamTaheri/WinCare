from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.finalize_native_release import stage_native_source


class ReleaseSourceCompletenessTests(unittest.TestCase):
    def test_native_source_archive_contains_security_and_reproducibility_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            staged = Path(td) / "native-source"
            stage_native_source(staged)

            required = (
                "NuGet.Config",
                ".github/CODEOWNERS",
                ".github/dependabot.yml",
                ".github/pull_request_template.md",
                "tests/WinCare.Infrastructure.Tests/WinCare.Infrastructure.Tests.csproj",
                "tests/WinCare.Infrastructure.Tests/PluginInstallerServiceTests.cs",
                "tests/WinCare.Infrastructure.Tests/packages.lock.json",
            )
            for relative in required:
                self.assertTrue((staged / relative).is_file(), relative)

            variants = list(staged.glob("src/*/packages.portable.*.lock.json"))
            self.assertEqual(10, len(variants))


if __name__ == "__main__":
    unittest.main()
