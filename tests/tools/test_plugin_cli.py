import os
import shutil
import tempfile
import unittest
import subprocess
import zipfile
import json

class PluginCliTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cli_path = os.path.abspath("tools/wincare-plugin-cli/bin/wincare-plugin.js")

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
        self.assertIn("does not conform to SemVer", val_res.stderr)
        self.assertIn("must contain a non-empty \"tools\" array", val_res.stderr)

if __name__ == "__main__":
    unittest.main()
