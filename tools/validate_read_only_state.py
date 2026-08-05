#!/usr/bin/env python3
"""Enforce non-persistent state discovery while WinCare is read-only."""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

SCHEMA = "wincare.read-only-state-validation/v1"
SOURCE_ROOTS = (
    "src/WinCare/Core",
    "src/WinCare/Providers",
    "src/WinCare/UI",
)
ROOT_FUNCTION = re.compile(r"(?m)^function\s+(Get-WinCare[A-Za-z0-9_-]*Root)\s*\{")
ANY_FUNCTION = re.compile(r"(?m)^function\s+[A-Za-z][A-Za-z0-9_-]*\s*\{")


def source_files(root: Path) -> list[Path]:
    files: list[Path] = []
    for relative in SOURCE_ROOTS:
        directory = root / relative
        if directory.is_dir():
            files.extend(p for p in directory.rglob("*.ps1") if not p.name.startswith(".wincare-"))
    return sorted(set(files), key=lambda item: item.as_posix().casefold())



def function_body(text: str, start: int) -> str:
    next_match = ANY_FUNCTION.search(text, start + 1)
    return text[start : next_match.start() if next_match else len(text)]


def validate(root: Path) -> dict[str, object]:
    errors: list[dict[str, object]] = []
    roots_checked = 0
    bridge_body = ""
    for path in source_files(root):
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        relative = path.relative_to(root).as_posix()
        for match in ROOT_FUNCTION.finditer(text):
            roots_checked += 1
            name = match.group(1)
            body = function_body(text, match.start())
            if name == "Get-WinCareBridgeStateRoot":
                bridge_body = body
                continue
            if re.search(r"\bNew-Item\b", body, re.I):
                errors.append({
                    "file": relative,
                    "line": text[: match.start()].count("\n") + 1,
                    "code": "StateRootGetterCreatesDirectory",
                    "function": name,
                })
    required_bridge_fragments = (
        "ReadOnlyLocked",
        "if(-not $readOnly)",
        "Test-WinCarePathWithin",
        "ReparsePoint",
    )
    if not bridge_body:
        errors.append({"code": "MissingStateRootBoundary"})
    else:
        for fragment in required_bridge_fragments:
            if fragment not in bridge_body:
                errors.append({
                    "code": "IncompleteStateRootBoundary",
                    "missing": fragment,
                })
    return {
        "schema": SCHEMA,
        "root": str(root.resolve()),
        "rootFunctionsChecked": roots_checked,
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
