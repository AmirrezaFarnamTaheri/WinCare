import unittest
import os
import glob

class TestMutationCoverage(unittest.TestCase):
    def test_provider_file_coverage(self):
        providers_dir = os.path.join(os.path.dirname(__file__), "..", "src", "WinCare", "Providers")
        ps_files = glob.glob(os.path.join(providers_dir, "*.ps1"))
        self.assertGreater(len(ps_files), 20, "Must have at least 20 provider modules")
        # Ensure enterprise providers (111+) have EvidenceType tags
        for f in ps_files:
            basename = os.path.basename(f)
            num = basename.split("-")[0]
            if num.isdigit() and int(num) >= 111:
                with open(f, "r", encoding="utf-8") as fp:
                    content = fp.read()
                    self.assertIn("EvidenceType", content, f"Provider {basename} missing EvidenceType metadata tag")

if __name__ == "__main__":
    unittest.main()
