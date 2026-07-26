"""Unit tests for tools/wincare_ps_source.py.

These prove the concrete blind spots found while auditing the validator
suite's independent, hand-rolled PowerShell scrubbers are closed by the
shared implementation: a single quote inside a here-string used to make
validate_source.py silently lose everything after it (including real
function definitions), and a per-line scrubber used to leave multi-line
block comments and here-strings fully exposed to danger-pattern regexes.
"""
from __future__ import annotations

import unittest

from wincare_ps_source import strip_powershell


class LengthAndLineInvariantTests(unittest.TestCase):
    def test_output_is_always_the_same_length_as_input(self) -> None:
        samples = [
            "",
            "\n",
            "function Test { }\n",
            "$x = 'a''b'\n$y = \"c`\"d\"\n",
            "$x = @'\nbody\n'@\n",
            "<# block\ncomment #>\ncode\n",
            "@' unterminated at eof",
        ]
        for sample in samples:
            with self.subTest(sample=sample):
                self.assertEqual(len(strip_powershell(sample)), len(sample))

    def test_newline_count_and_positions_are_preserved(self) -> None:
        text = "a\nb\r\nc\n\n'd\ne'\nf\n"
        stripped = strip_powershell(text)
        self.assertEqual(text.count("\n"), stripped.count("\n"))
        self.assertEqual(
            [i for i, c in enumerate(text) if c == "\n"],
            [i for i, c in enumerate(stripped) if c == "\n"],
        )


class CommentTests(unittest.TestCase):
    def test_line_comment_is_blanked_to_end_of_line_only(self) -> None:
        text = "code1 # comment text\ncode2\n"
        stripped = strip_powershell(text)
        self.assertIn("code1", stripped)
        self.assertIn("code2", stripped)
        self.assertNotIn("comment", stripped)

    def test_block_comment_spans_multiple_lines(self) -> None:
        text = "before\n<#\nhidden line one\nhidden line two\n#>\nafter\n"
        stripped = strip_powershell(text)
        self.assertIn("before", stripped)
        self.assertIn("after", stripped)
        self.assertNotIn("hidden", stripped)

    def test_block_comment_does_not_nest(self) -> None:
        # PowerShell block comments end at the first '#>' -- matches real
        # PowerShell semantics rather than a naive nesting assumption.
        text = "<# outer <# inner #> still-code #>\n"
        stripped = strip_powershell(text)
        self.assertNotIn("outer", stripped)
        self.assertNotIn("inner", stripped)
        # 'still-code #>' is exposed as real code because the first '#>'
        # already closed the comment.
        self.assertIn("still-code", stripped)


class QuotedStringTests(unittest.TestCase):
    def test_single_quoted_doubled_quote_is_an_escape_not_a_terminator(self) -> None:
        text = "$x = 'it''s fine'\nfunction Real-Function { }\n"
        stripped = strip_powershell(text)
        self.assertNotIn("it", stripped)
        self.assertNotIn("fine", stripped)
        self.assertIn("function Real-Function", stripped)

    def test_double_quoted_backtick_escape(self) -> None:
        text = '$x = "a`"b"\nfunction Real-Function { }\n'
        stripped = strip_powershell(text)
        self.assertNotIn("a", stripped.split("function")[0])
        self.assertIn("function Real-Function", stripped)

    def test_double_quoted_doubled_quote_is_an_escape(self) -> None:
        text = '$x = "say ""hi"" ok"\nfunction Real-Function { }\n'
        stripped = strip_powershell(text)
        self.assertNotIn("say", stripped)
        self.assertNotIn("hi", stripped)
        self.assertNotIn("ok", stripped)
        self.assertIn("function Real-Function", stripped)

    def test_single_quoted_string_can_span_multiple_lines(self) -> None:
        text = "$x = 'line one\nline two'\nfunction Real-Function { }\n"
        stripped = strip_powershell(text)
        self.assertNotIn("line one", stripped)
        self.assertNotIn("line two", stripped)
        self.assertIn("function Real-Function", stripped)


class HereStringRegressionTests(unittest.TestCase):
    """Regression tests for the concrete bug found in validate_source.py's
    prior char-by-char scrubber: it had no here-string awareness at all, so
    a single quote inside an @'...'@ body ended the "single-quoted string"
    state early, and every function definition after the here-string
    silently vanished from the scanned output.
    """

    def test_apostrophe_inside_single_quoted_here_string_does_not_leak(self) -> None:
        text = (
            "$x = @'\n"
            "This isn't escaped by the naive single-quote state machine\n"
            "'@\n"
            "function Test-Should-Be-Found {\n"
            "    if ($true) { Write-Host 'ok' }\n"
            "}\n"
        )
        stripped = strip_powershell(text)
        self.assertNotIn("isn't", stripped)
        self.assertNotIn("escaped", stripped)
        self.assertIn("function Test-Should-Be-Found", stripped)
        self.assertIn("if (", stripped)

    def test_double_quote_inside_expandable_here_string_does_not_leak(self) -> None:
        text = (
            "$x = @\"\n"
            'Some "quoted" example text\n'
            '"@\n'
            "function Test-Should-Be-Found { }\n"
        )
        stripped = strip_powershell(text)
        self.assertNotIn("quoted", stripped)
        self.assertIn("function Test-Should-Be-Found", stripped)

    def test_here_string_terminator_can_be_followed_by_code_on_the_same_line(self) -> None:
        # Matches real usage in src/WinCare/Providers/72-Provisioning.ps1:
        # the closing '@ is immediately followed by a pipeline on the same line.
        text = "$x = @'\nbody text\n'@|Set-Content -LiteralPath 'out.txt'\n"
        stripped = strip_powershell(text)
        self.assertNotIn("body text", stripped)
        self.assertIn("|Set-Content -LiteralPath", stripped)

    def test_indented_apostrophe_at_line_start_does_not_close_here_string(self) -> None:
        # PowerShell requires the closing '@ at column 0; an indented line
        # starting with "'@" is here-string BODY content, not a terminator.
        # The real, column-0 terminator on the following line is what
        # actually closes the here-string.
        text = "$x = @'\n    '@ this looks like a terminator but is indented\n'@\nfunction Test-Real { }\n"
        stripped = strip_powershell(text)
        self.assertNotIn("this looks like a terminator", stripped)
        self.assertIn("function Test-Real", stripped)

    def test_at_sign_before_paren_or_brace_is_not_a_here_string_opener(self) -> None:
        # @( ... ) array literals and @{ ... } hashtables must not be
        # mistaken for @' / @" here-string openers.
        text = "$arr = @(1, 2, 'x')\n$h = @{ Key = 'value' }\nfunction Test-Real { }\n"
        stripped = strip_powershell(text)
        self.assertIn("$arr = @(1, 2,", stripped)
        self.assertIn("$h = @{ Key =", stripped)
        self.assertNotIn("'x'", stripped)
        self.assertNotIn("'value'", stripped)
        self.assertIn("function Test-Real", stripped)

    def test_here_string_opener_requires_nothing_but_whitespace_after_it(self) -> None:
        # `@'literal` is not a valid here-string opener (content follows on
        # the same line): PowerShell treats it as a bare '@' plus an
        # ordinary single-quoted string. The scrubber must fall back the
        # same way so the ordinary string is still correctly blanked.
        text = "$x = @'literal'\nfunction Test-Real { }\n"
        stripped = strip_powershell(text)
        self.assertNotIn("literal", stripped)
        self.assertIn("function Test-Real", stripped)


class DangerPatternExposureTests(unittest.TestCase):
    """Regression tests for the second confirmed blind spot: a purely
    per-line scrubber (no state carried across lines) leaves multi-line
    block comments and here-strings fully exposed, so documentation text
    mentioning a banned token produces a false positive.
    """

    def test_banned_token_inside_block_comment_is_not_exposed(self) -> None:
        text = (
            "<#\n"
            ".EXAMPLE\n"
            "    Start-Process notepad.exe\n"
            "#>\n"
            "function Real-Function { }\n"
        )
        stripped = strip_powershell(text)
        self.assertNotIn("Start-Process", stripped)
        self.assertIn("function Real-Function", stripped)

    def test_banned_token_inside_here_string_is_not_exposed(self) -> None:
        text = "$doc = @'\nExample: Start-Process notepad.exe\n'@\nfunction Real-Function { }\n"
        stripped = strip_powershell(text)
        self.assertNotIn("Start-Process", stripped)
        self.assertIn("function Real-Function", stripped)


if __name__ == "__main__":
    unittest.main()
