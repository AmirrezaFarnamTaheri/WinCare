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

    def test_msbuild_uses_runtime_variant_without_overwriting_canonical_locks(self) -> None:
        props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
        self.assertIn("packages.portable.$(RuntimeIdentifier).lock.json", props)
        self.assertIn("<NuGetLockFilePath>", props)
        self.assertNotIn('Target Name="StagePortablePublishLockFiles"', props)
        self.assertNotIn('BeforeTargets="Restore"', props)
        self.assertNotIn("DestinationFiles=", props)
        self.assertNotIn("Staged locked portable dependency graphs", props)

        # The app's normal lock may legitimately contain RID sections because the project
        # declares RuntimeIdentifiers. Portable-only linker packages must not leak into any
        # canonical lock, and no build target may overwrite canonical lockfiles.
        for project in PROJECTS:
            canonical = ROOT / "src" / project / "packages.lock.json"
            canonical_text = canonical.read_text(encoding="utf-8-sig")
            self.assertNotIn('"Microsoft.NET.ILLink.Tasks"', canonical_text, project)

    def test_catalog_lock_graphs_include_domain_project_reference(self) -> None:
        relative_paths = [
            "src/WinCare.CommandCatalog/packages.lock.json",
            *[
                f"src/WinCare.CommandCatalog/packages.portable.{runtime}.lock.json"
                for runtime in RUNTIMES
            ],
        ]
        for relative in relative_paths:
            lock = json.loads((ROOT / relative).read_text(encoding="utf-8-sig"))
            base_graph = lock["dependencies"]["net8.0"]
            self.assertEqual("Project", base_graph["wincare.domain"]["type"], relative)

    def test_app_portable_lock_variants_cover_the_declared_runtime_set(self) -> None:
        project = (ROOT / "src/WinCare.App/WinCare.App.csproj").read_text(encoding="utf-8")
        self.assertIn("<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>", project)

        expected_runtimes = set(RUNTIMES)
        for runtime in RUNTIMES:
            relative = f"src/WinCare.App/packages.portable.{runtime}.lock.json"
            lock = json.loads((ROOT / relative).read_text(encoding="utf-8-sig"))
            runtime_keys = {
                dependency_key.rsplit("/", 1)[-1]
                for dependency_key in lock["dependencies"]
                if "/" in dependency_key
            }
            self.assertEqual(expected_runtimes, runtime_keys, relative)

    def test_no_portable_lock_bootstrap_workflow_remains(self) -> None:
        self.assertFalse((ROOT / ".github/workflows/bootstrap-portable-lockfiles.yml").exists())


if __name__ == "__main__":
    unittest.main()
