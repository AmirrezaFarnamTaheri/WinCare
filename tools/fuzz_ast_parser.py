import unittest
import subprocess

class TestAstParserFuzzer(unittest.TestCase):
    def test_fuzz_powershell_ast_parser(self):
        malformed_snippets = [
            "function test {",
            "$a = ",
            "param([string]$a",
            "if ($true) { write-host 'hi'",
            "process { return $null }",
        ]
        for snippet in malformed_snippets:
            cmd = f'pwsh -NoProfile -Command "[System.Management.Automation.Language.Parser]::ParseInput(\'{snippet}\', [ref]$null, [ref]$null)"'
            res = subprocess.run(cmd, shell=True, capture_output=True)
            self.assertEqual(res.returncode, 0, f"AST parser crashed on snippet: {snippet}")

if __name__ == "__main__":
    unittest.main()
