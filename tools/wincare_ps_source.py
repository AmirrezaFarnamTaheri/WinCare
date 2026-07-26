#!/usr/bin/env python3
"""Shared PowerShell source scrubber for WinCare's Python validators.

Several validators need to run regex patterns over PowerShell source while
ignoring anything inside a comment or string literal (so a banned token
mentioned only in documentation text, or a stray delimiter inside a string,
does not produce a false positive or a false negative). Prior to this module,
four validators (validate_source.py, validate_network_egress.py,
validate_bounded_io.py, validate_external_processes.py) each carried their
own hand-rolled scrubber, and they had drifted apart:

  - validate_source.py's char-by-char state machine had no here-string
    handling at all: a single quote anywhere inside an @'...'@ block (a very
    common thing to have in example/help text) would prematurely end the
    "single-quoted string" state, and everything after it -- potentially
    including real function definitions -- would be silently dropped from
    the scanned/parsed output.
  - validate_external_processes.py's scrubber ran per line with no state
    carried across lines at all, so it did not blank multi-line block
    comments, multi-line quoted strings, or here-strings of any kind --
    leaving their contents exposed to the danger-pattern regexes (a false
    positive risk: e.g. documentation that mentions `Start-Process` as an
    example inside a `<# ... #>` block would trip the gate).
  - validate_network_egress.py and validate_bounded_io.py each had their own,
    independently written, line-based here-string-aware scrubber that mostly
    agreed with each other but were two copies of the same logic to keep in
    sync by hand.

This module is the single, tested implementation all four now delegate to.

Supported constructs (this is a scrubber, not a full PowerShell parser --
it targets exactly what the validators need blanked):
  - line comments:      # ... to end of line
  - block comments:     <# ... #>  (PowerShell block comments do not nest)
  - single-quoted:       '...'   with '' as an escaped literal quote
  - double-quoted:       "..."   with `x as a backtick escape and "" as an
                          escaped literal quote (both forms are valid PowerShell)
  - here-strings:        @'...'@ and @"..."@

Here-string rules, matched to real PowerShell syntax and to how they are
actually written in this repository (see src/WinCare/Providers/72-Provisioning.ps1
for terminators immediately followed by a pipeline on the same line):
  - The opener (@' or @") is only recognized when it is the last thing on its
    line before the newline (optionally preceded by trailing whitespace is
    NOT permitted by PowerShell either -- the @' / @" must be immediately
    followed by the line terminator). Anywhere else, the '@' is left as an
    ordinary character and the quote that follows starts an ordinary
    single/double-quoted string.
  - The terminator ('@ or "@) is only recognized at column 0 of a line (no
    leading whitespace), and may be followed immediately by further code on
    the same line (e.g. `'@|Set-Content ...`).

All matched comment/string spans are replaced character-for-character with
spaces (newlines are preserved verbatim), so the output is exactly the same
length as the input and `text[:i].count("\\n") + 1` continues to compute the
correct 1-based line number for any offset `i` into the scrubbed text.
"""
from __future__ import annotations

_CODE = "code"
_LINE_COMMENT = "line-comment"
_BLOCK_COMMENT = "block-comment"
_SINGLE = "single"
_DOUBLE = "double"
_HERE_SINGLE = "here-single"
_HERE_DOUBLE = "here-double"


def strip_powershell(text: str) -> str:
    """Return `text` with comments and string-literal bodies blanked to spaces.

    Newlines are preserved so line numbers computed against the result stay
    aligned with the original source. The output is always the same length
    as the input.
    """
    n = len(text)
    out: list[str] = [" "] * n
    i = 0
    state = _CODE
    at_line_start = True
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "\n":
            out[i] = "\n"
            if state == _LINE_COMMENT:
                state = _CODE
            i += 1
            at_line_start = True
            continue

        if state == _CODE:
            if c == "<" and nxt == "#":
                state = _BLOCK_COMMENT
                i += 2
                at_line_start = False
                continue
            if c == "#":
                state = _LINE_COMMENT
                i += 1
                at_line_start = False
                continue
            if c == "@" and nxt == "'" and _rest_of_line_is_blank(text, i + 2):
                state = _HERE_SINGLE
                i += 2
                at_line_start = False
                continue
            if c == "@" and nxt == '"' and _rest_of_line_is_blank(text, i + 2):
                state = _HERE_DOUBLE
                i += 2
                at_line_start = False
                continue
            if c == "'":
                state = _SINGLE
                i += 1
                at_line_start = False
                continue
            if c == '"':
                state = _DOUBLE
                i += 1
                at_line_start = False
                continue
            out[i] = c
            i += 1
            at_line_start = False
            continue

        if state == _LINE_COMMENT:
            i += 1
            continue

        if state == _BLOCK_COMMENT:
            if c == "#" and nxt == ">":
                state = _CODE
                i += 2
            else:
                i += 1
            continue

        if state == _SINGLE:
            if c == "'" and nxt == "'":
                i += 2
            elif c == "'":
                state = _CODE
                i += 1
            else:
                i += 1
            continue

        if state == _DOUBLE:
            if c == "`" and i + 1 < n:
                i += 2
            elif c == '"' and nxt == '"':
                i += 2
            elif c == '"':
                state = _CODE
                i += 1
            else:
                i += 1
            continue

        if state == _HERE_SINGLE:
            if at_line_start and text[i : i + 2] == "'@":
                state = _CODE
                out[i] = "'"
                out[i + 1] = "@"
                i += 2
                at_line_start = False
            else:
                i += 1
                at_line_start = False
            continue

        if state == _HERE_DOUBLE:
            if at_line_start and text[i : i + 2] == '"@':
                state = _CODE
                out[i] = '"'
                out[i + 1] = "@"
                i += 2
                at_line_start = False
            else:
                i += 1
                at_line_start = False
            continue

        # Unreachable: every state above is handled explicitly.
        raise AssertionError(f"unhandled scrubber state: {state!r}")  # pragma: no cover

    return "".join(out)


def _rest_of_line_is_blank(text: str, start: int) -> bool:
    """True if `text[start:]` up to the next newline (or EOF) is all whitespace.

    Used to recognize a here-string opener: PowerShell requires `@'`/`@"` to
    be immediately followed by the line terminator, with nothing else
    (not even trailing spaces) in between.
    """
    end = text.find("\n", start)
    segment = text[start:] if end == -1 else text[start:end]
    return segment.strip(" \t\r") == ""
