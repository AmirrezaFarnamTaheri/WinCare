#!/usr/bin/env python3
"""Reject unbounded whole-file and whole-stream reads in live WinCare PowerShell."""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

SCHEMA = "wincare.bounded-io-validation/v1"
SOURCE_ROOTS = (
    "src/WinCare/Core",
    "src/WinCare/Providers",
    "src/WinCare/UI",
    "src/WinCare/Host",
    "src/WinCare/Native",
    "tools",
)
ROOT_FILES = (
    "WinCare.ps1",
    "WinCare-GUI.ps1",
    "WinCare-TUI.ps1",
    "Install-WinCare.ps1",
    "Uninstall-WinCare.ps1",
)
PATTERNS = {
    "UnboundedGetContent": re.compile(r"\bGet-Content\b", re.I),
    "UnboundedReadAllBytes": re.compile(r"\b(?:IO\.|System\.IO\.)?File\s*\]?(?:::|\.)ReadAllBytes\s*\(", re.I),
    "UnboundedReadAllText": re.compile(r"\b(?:IO\.|System\.IO\.)?File\s*\]?(?:::|\.)ReadAllText\s*\(", re.I),
    "UnboundedReadToEnd": re.compile(r"\.ReadToEnd(?:Async)?\s*\(", re.I),
}


def scrub_powershell(text: str) -> str:
    """Blank comments, quoted strings, and here-string bodies while preserving lines."""
    output: list[str] = []
    here_end: str | None = None
    for line in text.splitlines():
        stripped = line.lstrip()
        if here_end is not None:
            if stripped.startswith(here_end):
                here_end = None
            output.append("")
            continue
        if re.search(r"@'\s*$", line):
            here_end = "'@"
        elif re.search(r'@"\s*$', line):
            here_end = '"@'
        result: list[str] = []
        quote: str | None = None
        index = 0
        while index < len(line):
            char = line[index]
            if quote is None:
                if char == "#":
                    break
                if char in {"'", '"'}:
                    quote = char
                    result.append(" ")
                else:
                    result.append(char)
            elif quote == "'":
                if char == "'" and index + 1 < len(line) and line[index + 1] == "'":
                    result.extend((" ", " "))
                    index += 1
                elif char == "'":
                    quote = None
                    result.append(" ")
                else:
                    result.append(" ")
            else:
                if char == "`" and index + 1 < len(line):
                    result.extend((" ", " "))
                    index += 1
                elif char == '"':
                    quote = None
                    result.append(" ")
                else:
                    result.append(" ")
            index += 1
        output.append("".join(result))
    return "\n".join(output)


def files(root: Path) -> list[Path]:
    result: list[Path] = []
    for relative_root in SOURCE_ROOTS:
        directory = root / relative_root
        if directory.is_dir():
            result.extend(directory.rglob("*.ps1"))
    result.extend(root / name for name in ROOT_FILES if (root / name).is_file())
    return sorted(set(result), key=lambda item: item.as_posix().casefold())


def validate(root: Path) -> dict[str, object]:
    errors: list[dict[str, object]] = []
    checked = 0
    for path in files(root):
        checked += 1
        relative = path.relative_to(root).as_posix()
        scrubbed = scrub_powershell(path.read_text(encoding="utf-8-sig", errors="replace"))
        for code, pattern in PATTERNS.items():
            for match in pattern.finditer(scrubbed):
                errors.append({
                    "file": relative,
                    "line": scrubbed[: match.start()].count("\n") + 1,
                    "code": code,
                })
    return {
        "schema": SCHEMA,
        "root": str(root.resolve()),
        "filesChecked": checked,
        "errors": errors,
        "status": "failed" if errors else "passed",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", default=".")
    parser.add_argument("--output")
    args = parser.parse_args()
    report = validate(Path(args.root).resolve())
    rendered = json.dumps(report, indent=2)
    print(rendered)
    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(rendered + "\n", encoding="utf-8")
    return 1 if report["status"] == "failed" else 0


if __name__ == "__main__":
    raise SystemExit(main())
