from __future__ import annotations

import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
EXPECTED_LOCKFILES = {
    "src/WinCare.App/packages.lock.json",
    "src/WinCare.Application/packages.lock.json",
    "src/WinCare.CommandCatalog/packages.lock.json",
    "src/WinCare.Domain/packages.lock.json",
    "src/WinCare.Infrastructure/packages.lock.json",
    "tests/WinCare.Application.Tests/packages.lock.json",
    "tests/WinCare.CommandCatalog.Tests/packages.lock.json",
    "tests/WinCare.Infrastructure.Tests/packages.lock.json",
}


class ToolchainDeterminismTests(unittest.TestCase):
    def test_dotnet_sdk_is_strictly_pinned(self) -> None:
        global_json = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))
        sdk = global_json["sdk"]

        self.assertEqual("8.0.416", sdk["version"])
        self.assertEqual("disable", sdk["rollForward"])
        self.assertFalse(sdk["allowPrerelease"])

    def test_ci_provisions_the_pinned_dotnet_sdk(self) -> None:
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        self.assertIn("global-json-file: global.json", workflow)
        self.assertRegex(workflow, r"dotnet-version:\s*[\"']?8\.0\.416[\"']?")

    def test_nuget_dependency_graph_is_committed_and_locked_in_ci(self) -> None:
        found = {
            path.relative_to(ROOT).as_posix()
            for path in ROOT.rglob("packages.lock.json")
            if "obj" not in path.parts and "bin" not in path.parts
        }
        self.assertEqual(EXPECTED_LOCKFILES, found)

        for relative in sorted(EXPECTED_LOCKFILES):
            lock = json.loads((ROOT / relative).read_text(encoding="utf-8-sig"))
            self.assertEqual(2, lock["version"], relative)
            self.assertIn("dependencies", lock, relative)

        props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
        self.assertIn("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", props)
        self.assertIn("<RestoreLockedMode Condition=\"'$(CI)' == 'true'\">true</RestoreLockedMode>", props)

    def test_nuget_audits_all_transitive_packages_at_moderate_or_higher(self) -> None:
        props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
        self.assertIn("<NuGetAudit>true</NuGetAudit>", props)
        self.assertIn("<NuGetAuditMode>all</NuGetAuditMode>", props)
        self.assertIn("<NuGetAuditLevel>moderate</NuGetAuditLevel>", props)
        self.assertNotIn("NU190", props)

    def test_dependabot_monitors_every_package_ecosystem(self) -> None:
        config = (ROOT / ".github/dependabot.yml").read_text(encoding="utf-8")
        for ecosystem in ("github-actions", "nuget", "cargo", "npm"):
            self.assertRegex(config, rf'package-ecosystem:\s*[\"\']{re.escape(ecosystem)}[\"\']')

    def test_no_lockfile_bootstrap_workflow_remains(self) -> None:
        self.assertFalse((ROOT / ".github/workflows/bootstrap-nuget-lockfiles.yml").exists())


if __name__ == "__main__":
    unittest.main()
