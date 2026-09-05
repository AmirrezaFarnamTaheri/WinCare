import os
import shutil
import tempfile
import unittest
import subprocess
import zipfile
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]

class PluginCliTests(unittest.TestCase):
    def pack_direct(self, folder, output):
        builder = str(ROOT / "tools/wincare-plugin-cli/src/packager/zipBuilder.js")
        return subprocess.run(
            ["node", "-e", "require(process.argv[1]).packPlugin(process.argv[2], process.argv[3])",
             builder, str(folder), str(output)], capture_output=True, text=True, timeout=15)

    def test_unicode_archive_paths_round_trip(self):
        folder = Path(self.test_dir) / "plugin"
        folder.mkdir()
        (folder / "راهنما.txt").write_text("unicode content", encoding="utf-8")
        output = Path(self.test_dir) / "plugin.zip"
        result = self.pack_direct(folder, output)
        self.assertEqual(result.returncode, 0, result.stderr)
        with zipfile.ZipFile(output) as archive:
            self.assertEqual(archive.read("راهنما.txt"), b"unicode content")

    def test_pack_rejects_links_to_external_files(self):
        folder = Path(self.test_dir) / "plugin"
        folder.mkdir()
        outside = Path(self.test_dir) / "private.txt"
        outside.write_text("must not be packaged", encoding="utf-8")
        try:
            (folder / "linked.txt").symlink_to(outside)
        except OSError as error:
            self.skipTest(f"Symlink creation unavailable: {error}")
        output = Path(self.test_dir) / "plugin.zip"
        result = self.pack_direct(folder, output)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(output.exists())

    def test_pack_rejects_more_entries_than_installer_accepts(self):
        folder = Path(self.test_dir) / "plugin"
        folder.mkdir()
        for i in range(501):
            (folder / f"{i}.txt").touch()
        output = Path(self.test_dir) / "plugin.zip"
        result = self.pack_direct(folder, output)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("installer entry", result.stderr)
        self.assertFalse(output.exists())

    @classmethod
    def setUpClass(cls):
        cls.cli_path = str(ROOT / "tools/wincare-plugin-cli/bin/wincare-plugin.js")

    def setUp(self):
        self.test_dir = tempfile.mkdtemp(prefix="wincare_cli_test_")

    def tearDown(self):
        shutil.rmtree(self.test_dir, ignore_errors=True)

    def test_scaffold_json_pack_and_validate(self):
        plugin_name = "test_cleaner"
        out_dir = os.path.join(self.test_dir, plugin_name)
        
        # 1. Create plugin
        res = subprocess.run(["node", self.cli_path, "create", plugin_name, "--template", "json-pack", "--outDir", out_dir], capture_output=True, text=True)
        self.assertEqual(res.returncode, 0, msg=res.stderr)
        self.assertTrue(os.path.exists(os.path.join(out_dir, "wincare-plugin.json")))
        self.assertTrue(os.path.exists(os.path.join(out_dir, "scripts", "clean_temp.cmd")))

        # 2. Validate plugin
        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertEqual(val_res.returncode, 0, msg=val_res.stderr)
        self.assertIn("Plugin manifest and security checks passed cleanly", val_res.stdout)

        # 3. Pack plugin
        pack_res = subprocess.run(["node", self.cli_path, "pack", out_dir], capture_output=True, text=True)
        self.assertEqual(pack_res.returncode, 0, msg=pack_res.stderr)
        
        archive_path = os.path.join(out_dir, "com.community.testcleaner-1.0.0.wincare-plugin")
        self.assertTrue(os.path.exists(archive_path))
        
        # Verify ZIP contains valid manifest
        with zipfile.ZipFile(archive_path, 'r') as zf:
            names = zf.namelist()
            self.assertIn("wincare-plugin.json", names)
            self.assertIn("scripts/clean_temp.cmd", names)
            with zf.open("wincare-plugin.json") as mf:
                manifest_data = json.loads(mf.read().decode('utf-8'))
                self.assertEqual(manifest_data["id"], "com.community.testcleaner")

    def test_linter_rejects_path_traversal(self):
        plugin_name = "bad_plugin"
        out_dir = os.path.join(self.test_dir, plugin_name)
        os.makedirs(out_dir, exist_ok=True)

        bad_manifest = {
            "id": "com.evil.exploit",
            "name": "Evil Exploit",
            "version": "1.0.0",
            "author": "Attacker",
            "category": "Security",
            "tools": [
                {
                    "id": "com.evil.exploit.pwn",
                    "name": "Pwn Tool",
                    "riskLevel": "Mutating",
                    "executionType": "Script",
                    "scriptPath": "../../Windows/System32/calc.exe"
                }
            ]
        }

        with open(os.path.join(out_dir, "wincare-plugin.json"), "w", encoding="utf-8") as f:
            json.dump(bad_manifest, f)

        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertNotEqual(val_res.returncode, 0)
        self.assertIn("illegal path traversal", val_res.stderr)

    def test_linter_rejects_invalid_semver_and_id(self):
        plugin_name = "bad_metadata"
        out_dir = os.path.join(self.test_dir, plugin_name)
        os.makedirs(out_dir, exist_ok=True)

        bad_manifest = {
            "id": "INVALID_UPPERCASE_ID",
            "name": "Bad Metadata",
            "version": "v1.beta",
            "author": "Developer",
            "category": "Utilities",
            "tools": []
        }

        with open(os.path.join(out_dir, "wincare-plugin.json"), "w", encoding="utf-8") as f:
            json.dump(bad_manifest, f)

        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertNotEqual(val_res.returncode, 0)
        self.assertIn("reverse-domain format", val_res.stderr)
    def test_scaffold_csharp_plugin_and_validate(self):
        plugin_name = "test_assembly_tool"
        out_dir = os.path.join(self.test_dir, plugin_name)
        
        # 1. Create C# plugin scaffold
        res = subprocess.run(["node", self.cli_path, "create", plugin_name, "--template", "csharp-plugin", "--outDir", out_dir], capture_output=True, text=True)
        self.assertEqual(res.returncode, 0, msg=res.stderr)
        self.assertTrue(os.path.exists(os.path.join(out_dir, "wincare-plugin.json")))
        self.assertTrue(os.path.exists(os.path.join(out_dir, "PluginEntryPoint.cs")))

        # 2. Validate plugin scaffold
        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertEqual(val_res.returncode, 0, msg=val_res.stderr)
        self.assertIn("Plugin manifest and security checks passed cleanly", val_res.stdout)

        # 3. Pack plugin package
        pack_res = subprocess.run(["node", self.cli_path, "pack", out_dir], capture_output=True, text=True)
        self.assertEqual(pack_res.returncode, 0, msg=pack_res.stderr)
        
        archive_path = os.path.join(out_dir, "com.community.testassemblytool-1.0.0.wincare-plugin")
        self.assertTrue(os.path.exists(archive_path))
        
        with zipfile.ZipFile(archive_path, 'r') as zf:
            names = zf.namelist()
            self.assertIn("wincare-plugin.json", names)
            self.assertIn("PluginEntryPoint.cs", names)
            with zf.open("wincare-plugin.json") as mf:
                manifest_data = json.loads(mf.read().decode('utf-8'))
                self.assertEqual(manifest_data["id"], "com.community.testassemblytool")
                self.assertEqual(manifest_data["entryType"], "Assembly")

    def test_create_rejects_unsupported_template(self):
        out_dir = os.path.join(self.test_dir, "invalid_template")
        result = subprocess.run(
            ["node", self.cli_path, "create", "bad_template", "--template", "unknown", "--outDir", out_dir],
            capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Unsupported template", result.stderr)
        self.assertFalse(os.path.exists(out_dir))

    def test_repeated_pack_does_not_include_previous_archive(self):
        out_dir = os.path.join(self.test_dir, "repeat_pack")
        result = subprocess.run(
            ["node", self.cli_path, "create", "repeat_pack", "--outDir", out_dir],
            capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, msg=result.stderr)

        archive_path = os.path.join(out_dir, "com.community.repeatpack-1.0.0.wincare-plugin")
        for _ in range(2):
            result = subprocess.run(["node", self.cli_path, "pack", out_dir], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, msg=result.stderr)
            with zipfile.ZipFile(archive_path, "r") as zf:
                self.assertNotIn(os.path.basename(archive_path), zf.namelist())

if __name__ == "__main__":
    unittest.main()
