"""Release publication must bind both architectures to a stable signer."""
import tempfile
import unittest
from pathlib import Path
from tools.verify_release_signer import assert_shared_release_signer

ROOT = Path(__file__).resolve().parents[2]

class ReleaseSignerTests(unittest.TestCase):
    def test_matching_architectures_are_accepted(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / "WinCare-v4.0.0-rc2-x64.cer").write_bytes(b"a" * 256)
            (root / "WinCare-v4.0.0-rc2-ARM64.cer").write_bytes(b"a" * 256)
            self.assertEqual(64, len(assert_shared_release_signer(root, "v4.0.0-rc2")))

    def test_missing_or_distinct_architecture_certificate_is_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / "WinCare-v4.0.0-rc2-x64.cer").write_bytes(b"a" * 256)
            with self.assertRaisesRegex(ValueError, "missing"):
                assert_shared_release_signer(root, "v4.0.0-rc2")
            (root / "WinCare-v4.0.0-rc2-ARM64.cer").write_bytes(b"b" * 256)
            with self.assertRaisesRegex(ValueError, "different"):
                assert_shared_release_signer(root, "v4.0.0-rc2")

    def test_release_build_refuses_ephemeral_signer_when_publishing(self):
        source = (ROOT / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        self.assertIn("WINCARE_SIGN_FOR_RELEASE", source)
        self.assertIn("WINCARE_RELEASE_PFX_BASE64", source)
        self.assertIn("WINCARE_RELEASE_CERT_THUMBPRINT", source)
        self.assertIn("Refusing ephemeral release signing", source)
        self.assertIn("python tools/verify_release_signer.py", source)

if __name__ == "__main__":
    unittest.main()
