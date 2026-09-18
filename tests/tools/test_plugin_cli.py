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

        # 2. The scaffold declares an assembly that has not been built yet. The linter now
        #    requires that declared assembly to exist -- a DLL-less package is exactly what
        #    leaves the installer with no assembly digest to bind -- so the scaffold alone
        #    must fail validation with an explicit error rather than packing.
        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertNotEqual(val_res.returncode, 0)
        self.assertIn("assembly file does not exist", val_res.stderr)

        # 3. With the declared assembly present the scaffold validates and packs.
        with open(os.path.join(out_dir, "PluginAssembly.dll"), "wb") as assembly:
            assembly.write(b"MZ\x90\x00 placeholder assembly bytes")
        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertEqual(val_res.returncode, 0, msg=val_res.stderr)
        self.assertIn("Plugin manifest and security checks passed cleanly", val_res.stdout)

        # 4. Pack plugin package
        pack_res = subprocess.run(["node", self.cli_path, "pack", out_dir], capture_output=True, text=True)
        self.assertEqual(pack_res.returncode, 0, msg=pack_res.stderr)

        archive_path = os.path.join(out_dir, "com.community.testassemblytool-1.0.0.wincare-plugin")
        self.assertTrue(os.path.exists(archive_path))

        with zipfile.ZipFile(archive_path, 'r') as zf:
            names = zf.namelist()
            self.assertIn("wincare-plugin.json", names)
            self.assertIn("PluginEntryPoint.cs", names)
            self.assertIn("PluginAssembly.dll", names)
            with zf.open("wincare-plugin.json") as mf:
                manifest_data = json.loads(mf.read().decode('utf-8'))
                self.assertEqual(manifest_data["id"], "com.community.testassemblytool")
                self.assertEqual(manifest_data["entryType"], "Assembly")

    def test_assembly_plugin_packs_only_when_the_declared_assembly_exists(self):
        out_dir = os.path.join(self.test_dir, "assembly_gate")
        subprocess.run(["node", self.cli_path, "create", "assembly_gate", "--template", "csharp-plugin", "--outDir", out_dir],
                       capture_output=True, text=True)
        archive_path = os.path.join(out_dir, "com.community.assemblygate-1.0.0.wincare-plugin")

        # No built assembly: both validate and pack must refuse, and no archive may appear.
        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertNotEqual(val_res.returncode, 0)
        self.assertIn("assembly file does not exist", val_res.stderr)
        pack_res = subprocess.run(["node", self.cli_path, "pack", out_dir], capture_output=True, text=True)
        self.assertNotEqual(pack_res.returncode, 0)
        self.assertIn("assembly file does not exist", pack_res.stderr)
        self.assertFalse(os.path.exists(archive_path))

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

    def test_canonical_risk_mutating_is_rejected_but_legacy_risklevel_is_accepted(self):
        out_dir = os.path.join(self.test_dir, "risk_alias")
        os.makedirs(os.path.join(out_dir, "scripts"), exist_ok=True)
        with open(os.path.join(out_dir, "scripts", "run.cmd"), "w", encoding="utf-8") as script:
            script.write("@echo off\r\n")

        manifest = {
            "id": "com.example.alias", "name": "Alias", "version": "1.0.0",
            "author": "Developer", "category": "Utilities",
            "tools": [{"id": "com.example.alias.run", "title": "Run",
                       "risk": "Mutating", "scriptPath": "scripts/run.cmd"}]
        }
        with open(os.path.join(out_dir, "wincare-plugin.json"), "w", encoding="utf-8") as f:
            json.dump(manifest, f)
        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertNotEqual(val_res.returncode, 0)
        self.assertIn('invalid risk "Mutating"', val_res.stderr)

        # The same spelling is legal on the legacy riskLevel alias.
        manifest["tools"][0]["risk"] = "Moderate"
        manifest["tools"][0]["riskLevel"] = "Mutating"
        with open(os.path.join(out_dir, "wincare-plugin.json"), "w", encoding="utf-8") as f:
            json.dump(manifest, f)
        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertEqual(val_res.returncode, 0, msg=val_res.stderr)

    def test_plugin_id_longer_than_the_installer_limit_is_rejected(self):
        out_dir = os.path.join(self.test_dir, "long_id")
        os.makedirs(out_dir, exist_ok=True)
        manifest = {
            "id": "com.example." + "a" * 125, "name": "Long", "version": "1.0.0",
            "author": "Developer", "category": "Utilities",
            "tools": [{"id": "com.example.long.run", "title": "Run"}]
        }
        with open(os.path.join(out_dir, "wincare-plugin.json"), "w", encoding="utf-8") as f:
            json.dump(manifest, f)
        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertNotEqual(val_res.returncode, 0)
        self.assertIn("3 to 128 characters", val_res.stderr)

    def test_legacy_plugin_json_manifest_is_accepted_with_a_warning(self):
        out_dir = os.path.join(self.test_dir, "legacy_name")
        os.makedirs(os.path.join(out_dir, "scripts"), exist_ok=True)
        with open(os.path.join(out_dir, "scripts", "run.cmd"), "w", encoding="utf-8") as script:
            script.write("@echo off\r\n")
        manifest = {
            "id": "com.example.legacy", "name": "Legacy", "version": "1.0.0",
            "author": "Developer", "category": "Utilities",
            "tools": [{"id": "com.example.legacy.run", "title": "Run",
                       "risk": "ReadOnly", "scriptPath": "scripts/run.cmd"}]
        }
        with open(os.path.join(out_dir, "plugin.json"), "w", encoding="utf-8") as f:
            json.dump(manifest, f)

        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertEqual(val_res.returncode, 0, msg=val_res.stderr)
        self.assertIn("canonical", val_res.stderr)
        pack_res = subprocess.run(["node", self.cli_path, "pack", out_dir], capture_output=True, text=True)
        self.assertEqual(pack_res.returncode, 0, msg=pack_res.stderr)

    def test_oversized_manifest_is_rejected(self):
        out_dir = os.path.join(self.test_dir, "oversized")
        os.makedirs(out_dir, exist_ok=True)
        manifest = {
            "id": "com.example.big", "name": "Big", "version": "1.0.0",
            "author": "Developer", "category": "Utilities",
            "description": "x" * (2 * 1024 * 1024),
            "tools": [{"id": "com.example.big.run", "title": "Run"}]
        }
        with open(os.path.join(out_dir, "wincare-plugin.json"), "w", encoding="utf-8") as f:
            json.dump(manifest, f)
        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertNotEqual(val_res.returncode, 0)
        self.assertIn("size limit", val_res.stderr)

    def test_a_bundled_signature_file_warns(self):
        out_dir = os.path.join(self.test_dir, "stray_sig")
        os.makedirs(os.path.join(out_dir, "scripts"), exist_ok=True)
        with open(os.path.join(out_dir, "scripts", "run.cmd"), "w", encoding="utf-8") as script:
            script.write("@echo off\r\n")
        manifest = {
            "id": "com.example.sig", "name": "Sig", "version": "1.0.0",
            "author": "Developer", "category": "Utilities",
            "tools": [{"id": "com.example.sig.run", "title": "Run",
                       "risk": "ReadOnly", "scriptPath": "scripts/run.cmd"}]
        }
        with open(os.path.join(out_dir, "wincare-plugin.json"), "w", encoding="utf-8") as f:
            json.dump(manifest, f)
        with open(os.path.join(out_dir, "wincare-plugin.sig"), "w", encoding="utf-8") as sig:
            sig.write("not-an-independent-trust-assertion")
        val_res = subprocess.run(["node", self.cli_path, "validate", out_dir], capture_output=True, text=True)
        self.assertEqual(val_res.returncode, 0, msg=val_res.stderr)
        self.assertIn("not an independent trust assertion", val_res.stderr)

    def test_zip_writer_rejects_traversal_entry_names(self):
        builder = str(ROOT / "tools/wincare-plugin-cli/src/packager/zipBuilder.js")
        result = subprocess.run(
            ["node", "-e", "const w = require(process.argv[1]); new w.DeterministicZipWriter().addFile(process.argv[2], 'data')",
             builder, "../../evil.txt"], capture_output=True, text=True, timeout=15)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("unsafe archive entry name", result.stderr)

    def test_pack_accepts_the_out_flag_and_rejects_a_dash_value(self):
        out_dir = os.path.join(self.test_dir, "out_flag")
        subprocess.run(["node", self.cli_path, "create", "out_flag", "--outDir", out_dir],
                       capture_output=True, text=True)
        archive_path = os.path.join(self.test_dir, "flagged.wincare-plugin")

        res = subprocess.run(["node", self.cli_path, "pack", out_dir, "--out", archive_path],
                             capture_output=True, text=True)
        self.assertEqual(res.returncode, 0, msg=res.stderr)
        self.assertTrue(os.path.exists(archive_path))
        self.assertNotIn("--out", os.listdir(out_dir))

        res = subprocess.run(["node", self.cli_path, "pack", out_dir, "--out", "--template"],
                             capture_output=True, text=True)
        self.assertNotEqual(res.returncode, 0)
        self.assertIn('begins with "-"', res.stderr)

    def generate_publisher_key(self):
        """Emits a fresh RSA private key as PEM using Node's built-in crypto only."""
        key_dir = os.path.join(self.test_dir, "keys")
        os.makedirs(key_dir, exist_ok=True)
        key_path = os.path.join(key_dir, "publisher.pem")
        res = subprocess.run(
            ["node", "-e",
             "const c = require('crypto');"
             "const k = c.generateKeyPairSync('rsa', {modulusLength: 2048});"
             "require('fs').writeFileSync(process.argv[1], k.privateKey.export({type: 'pkcs8', format: 'pem'}));",
             key_path], capture_output=True, text=True, timeout=30)
        self.assertEqual(res.returncode, 0, msg=res.stderr)
        return key_path

    def test_unsigned_pack_warns_and_emits_no_trust_metadata(self):
        out_dir = os.path.join(self.test_dir, "unsigned_pack")
        subprocess.run(["node", self.cli_path, "create", "unsigned_pack", "--outDir", out_dir],
                       capture_output=True, text=True)
        res = subprocess.run(["node", self.cli_path, "pack", out_dir], capture_output=True, text=True)
        self.assertEqual(res.returncode, 0, msg=res.stderr)
        self.assertIn("unsigned", res.stderr)
        self.assertFalse(os.path.exists(os.path.join(out_dir, "com.community.unsignedpack-1.0.0.wincare-plugin.sig.json")))
        self.assertFalse(os.path.exists(os.path.join(out_dir, "com.community.unsignedpack-1.0.0.wincare-plugin.sha256")))

    def test_pack_with_key_signs_the_raw_manifest_bytes(self):
        out_dir = os.path.join(self.test_dir, "signed_pack")
        subprocess.run(["node", self.cli_path, "create", "signed_pack", "--outDir", out_dir],
                       capture_output=True, text=True)
        key_path = self.generate_publisher_key()
        archive_path = os.path.join(out_dir, "com.community.signedpack-1.0.0.wincare-plugin")

        res = subprocess.run(["node", self.cli_path, "pack", out_dir, "--key", key_path],
                             capture_output=True, text=True)
        self.assertEqual(res.returncode, 0, msg=res.stderr)

        with open(archive_path + ".sig.json", encoding="utf-8") as f:
            trust = json.load(f)
        self.assertEqual(trust["pluginId"], "com.community.signedpack")
        for field in ("publisherId", "publisherPublicKeyPem", "publisherSignature", "archiveSha256", "manifestSha256"):
            self.assertTrue(trust[field], f"missing {field}")

        # The digest file must match an independent SHA-256 of the archive bytes.
        with open(archive_path + ".sha256", encoding="utf-8") as f:
            self.assertEqual(f.read().split()[0], trust["archiveSha256"])

        # The signature must verify over the exact manifest bytes on disk and nowhere else.
        with open(os.path.join(out_dir, "wincare-plugin.json"), "rb") as f:
            raw_manifest = f.read()
        verify = (
            "const c = require('crypto');"
            "const sig = Buffer.from(process.argv[1], 'base64');"
            "const key = c.createPublicKey(process.argv[2]);"
            "process.stdout.write(String(c.verify('sha256', Buffer.from(process.argv[3], 'hex'), key, sig)));"
        )
        encoded = subprocess.run(
            ["node", "-e", verify, trust["publisherSignature"], trust["publisherPublicKeyPem"],
             raw_manifest.hex()], capture_output=True, text=True, timeout=15)
        self.assertEqual(encoded.stdout, "true", encoded.stderr)

        # Re-serialising the same document with different whitespace must invalidate it.
        reformatted = json.dumps(json.loads(raw_manifest.decode("utf-8")), indent=4)
        encoded = subprocess.run(
            ["node", "-e", verify, trust["publisherSignature"], trust["publisherPublicKeyPem"],
             reformatted.encode("utf-8").hex()], capture_output=True, text=True, timeout=15)
        self.assertEqual(encoded.stdout, "false")

if __name__ == "__main__":
    unittest.main()
