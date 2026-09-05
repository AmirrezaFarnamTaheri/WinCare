from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PROJECTS = (
    "WinCare.App",
    "WinCare.Application",
    "WinCare.CommandCatalog",
    "WinCare.Domain",
    "WinCare.Infrastructure",
)
RUNTIMES = ("win-x64", "win-arm64")


class PortableLockContractTests(unittest.TestCase):
    def test_every_runtime_has_a_committed_portable_lock_graph(self) -> None:
        expected = {
            f"src/{project}/packages.portable.{runtime}.lock.json"
            for project in PROJECTS
            for runtime in RUNTIMES
        }
        found = {
            path.relative_to(ROOT).as_posix()
            for path in ROOT.glob("src/*/packages.portable.*.lock.json")
        }
        self.assertEqual(expected, found)

        for relative in sorted(expected):
            lock = json.loads((ROOT / relative).read_text(encoding="utf-8-sig"))
            self.assertEqual(2, lock["version"], relative)
            self.assertIn("dependencies", lock, relative)

    def test_msbuild_stages_runtime_variant_before_locked_portable_restore(self) -> None:
        props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
        self.assertIn('Target Name="StagePortablePublishLockFiles"', props)
        self.assertIn('BeforeTargets="Restore"', props)
        self.assertIn("packages.portable.$(RuntimeIdentifier).lock.json", props)
        self.assertIn("Staged locked portable dependency graphs for $(RuntimeIdentifier).", props)
        self.assertNotIn("NuGetLockFilePath", props)

    def test_explicit_publish_rid_does_not_retain_the_multi_rid_restore_set(self) -> None:
        project = (ROOT / "src/WinCare.App/WinCare.App.csproj").read_text(encoding="utf-8")
        self.assertIn(
            '<RuntimeIdentifiers Condition="\'$(RuntimeIdentifier)\' == \'\'">win-x64;win-arm64</RuntimeIdentifiers>',
            project,
        )
        self.assertNotIn("<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>", project)

    def test_no_portable_lock_bootstrap_workflow_remains(self) -> None:
        self.assertFalse((ROOT / ".github/workflows/bootstrap-portable-lockfiles.yml").exists())


if __name__ == "__main__":
    unittest.main()
