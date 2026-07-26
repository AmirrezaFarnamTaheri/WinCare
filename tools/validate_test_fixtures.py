#!/usr/bin/env python3
"""Fail before Pester when tests reference missing literal fixtures or source files."""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

DIRECT_PSSCRIPTROOT = re.compile(
    r'''(?im)^\s*\.\s+["']\$PSScriptRoot(?P<relative>[^"']+)["']'''
)
JOIN_PSSCRIPTROOT = re.compile(
    r'''(?im)(?:Import-Module|Get-Content|Test-Path)\s+\(Join-Path\s+\$PSScriptRoot\s+["'](?P<relative>[^"']+)["']\)'''
)


def resolve_reference(test_file: Path, relative: str) -> Path:
    normalized = relative.replace("\\", "/").lstrip("/")
    return (test_file.parent / normalized).resolve()


def validate(root: Path) -> dict[str, object]:
    errors: list[dict[str, object]] = []
    references: list[dict[str, object]] = []
    tests = sorted((root / "tests").rglob("*.ps1"))
    for test_file in tests:
        text = test_file.read_text(encoding="utf-8-sig", errors="replace")
        matches = list(DIRECT_PSSCRIPTROOT.finditer(text)) + list(JOIN_PSSCRIPTROOT.finditer(text))
        for match in matches:
            relative = match.group("relative")
            target = resolve_reference(test_file, relative)
            record = {
                "test": str(test_file.relative_to(root)),
                "line": text[: match.start()].count("\n") + 1,
                "reference": "$PSScriptRoot" + relative,
                "target": str(target),
                "exists": target.exists(),
            }
            references.append(record)
            if not target.exists():
                errors.append(record)
    return {
        "schema": "wincare.test-fixture-validation/v1",
        "status": "passed" if not errors else "failed",
        "testsChecked": len(tests),
        "literalReferencesChecked": len(references),
        "missingReferences": errors,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    report = validate(root)
    rendered = json.dumps(report, indent=2)
    print(rendered)
    if args.output:
        output = args.output if args.output.is_absolute() else root / args.output
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(rendered + "\n", encoding="utf-8")
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
