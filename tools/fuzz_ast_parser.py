"""PowerShell AST parser fuzz harness.

Security: uses -EncodedCommand (Base64-UTF-16-LE) with shell=False to avoid
command injection. ParseInput captures parse errors in [ref] params, so a
process exit-code of 0 means no runtime crash occurred.
"""
import base64
import subprocess
import unittest


def _ps_b64(script: str) -> str:
    """Encode a PowerShell script as Base64-UTF-16-LE for -EncodedCommand."""
    return base64.b64encode(script.encode("utf-16-le")).decode("ascii")


class TestAstParserFuzzer(unittest.TestCase):
    MALFORMED_SNIPPETS = [
        "function test {",
        "$a = ",
        "param([string]$a",
        "if ($true) { write-host 'hi'",
        "process { return $null }",
    ]

    def test_fuzz_powershell_ast_parser(self):
        """Verify PowerShell AST parser does not crash on malformed input.

        ParseInput places parse errors into the [ref] error array; the process
        exit code is 0 when the call itself succeeds (no runtime exception).
        Uses shell=False + -EncodedCommand to prevent command injection.
        """
        for snippet in self.MALFORMED_SNIPPETS:
            ps_script = (
                "$errors = $null; "
                "$ast = [System.Management.Automation.Language.Parser]::ParseInput("
                f"@'\n{snippet}\n'@, [ref]$null, [ref]$errors); "
                "exit 0"
            )
            encoded = _ps_b64(ps_script)
            res = subprocess.run(
                ["pwsh", "-NonInteractive", "-NoProfile", "-EncodedCommand", encoded],
                shell=False,
                capture_output=True,
                timeout=15,
            )
            self.assertEqual(
                res.returncode,
                0,
                f"AST parser process crashed (exit {res.returncode}) on: {snippet!r}\n"
                f"stderr: {res.stderr.decode(errors='replace')}",
            )


if __name__ == "__main__":
    unittest.main()
