"""Migration acceptance: all projects and committed NuGet locks target supported .NET 10 LTS.

The 4.0.0-rc2 release branch remains on .NET 8 until this independent
migration passes actual Windows x64/ARM64 builds and runtime smoke.
"""
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

class Dotnet10MigrationTests(unittest.TestCase):
    def test_every_dotnet_project_targets_supported_lts(self):
        projects = sorted((ROOT / "src").rglob("*.csproj"))
        projects += sorted((ROOT / "tests").rglob("*.csproj"))
        projects += sorted((ROOT / "extensions").rglob("*.csproj"))
        self.assertGreaterEqual(len(projects), 12)
        for path in projects:
            with self.subTest(project=str(path.relative_to(ROOT))):
                content = path.read_text(encoding="utf-8")
                self.assertRegex(content, r"<TargetFramework>net10\.0(?:-windows10\.0\.19041\.0)?</TargetFramework>")
                self.assertNotIn("<TargetFramework>net8.0", content)

    def test_net10_avoids_implicit_registry_package_reference(self):
        infra = (ROOT / "src/WinCare.Infrastructure/WinCare.Infrastructure.csproj").read_text(encoding="utf-8")
        central = (ROOT / "Directory.Packages.props").read_text(encoding="utf-8")
        self.assertNotIn('PackageReference Include="Microsoft.Win32.Registry"', infra)
        self.assertNotIn('PackageVersion Include="Microsoft.Win32.Registry"', central)

    def test_ci_uses_exact_current_dotnet10_sdk(self):
        sdk = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))["sdk"]
        self.assertEqual("10.0.402", sdk["version"])
        self.assertEqual("disable", sdk["rollForward"])
        workflow = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        self.assertIn('dotnet-version: "10.0.402"', workflow)

    def test_all_nuget_locks_have_net10_target_graphs(self):
        locks = sorted((ROOT / "src").rglob("packages*.lock.json"))
        locks += sorted((ROOT / "tests").rglob("packages*.lock.json"))
        self.assertGreaterEqual(len(locks), 18)
        for path in locks:
            with self.subTest(lock=str(path.relative_to(ROOT))):
                doc = json.loads(path.read_text(encoding="utf-8"))
                targets = doc.get("dependencies", {})
                self.assertTrue(any(key.startswith("net10.0") for key in targets),
                                f"{path} is not a .NET 10 dependency graph: {list(targets)}")
                self.assertFalse(any(key.startswith("net8.0") for key in targets))

if __name__ == "__main__":
    unittest.main()
