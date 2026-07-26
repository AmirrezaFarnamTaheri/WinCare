#!/usr/bin/env python3
"""Enforce WinCare source-size, readability, and function-complexity ratchets.

Legacy source remains on a no-regression baseline. Files deliberately modernized are
moved to a strict profile so minified one-line code cannot look more maintainable
than readable bounded functions.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

MANIFESTS = (
    ("Core", "src/WinCare/SourceManifests/Core.psd1"),
    ("Providers", "src/WinCare/SourceManifests/Providers.psd1"),
    ("UI", "src/WinCare/SourceManifests/UI.psd1"),
)
DEFAULT_BASELINE = "config/maintainability-baseline.json"
STRICT_LIMITS = {
    "maxFileLines": 600,
    "maxLineLength": 200,
    "linesOver200": 0,
    "linesOver500": 0,
    "maxFunctionLines": 200,
}


def source_files(root: Path) -> list[tuple[str, str]]:
    result: list[tuple[str, str]] = []
    seen: set[str] = set()
    for group, relative in MANIFESTS:
        path = root / relative
        text = path.read_text(encoding="utf-8-sig")
        block = re.search(r"Files\s*=\s*@\((.*?)\)\s*\n\}", text, re.S)
        if not block:
            raise ValueError(f"Files array missing from {relative}")
        for item in re.findall(r"'([^']+\.ps1)'", block.group(1)):
            folded = item.casefold()
            if folded in seen:
                raise ValueError(f"Source file assigned more than once: {item}")
            seen.add(folded)
            result.append((group, item))
    return result


def file_metrics(path: Path, group: str) -> dict[str, object]:
    text = path.read_text(encoding="utf-8-sig")
    lines = text.splitlines()
    function_starts = [
        text[: match.start()].count("\n") + 1
        for match in re.finditer(r"(?m)^function\s+[A-Za-z][A-Za-z0-9_-]*\s*\{", text)
    ]
    function_lengths: list[int] = []
    for index, start in enumerate(function_starts):
        next_start = function_starts[index + 1] if index + 1 < len(function_starts) else len(lines) + 1
        function_lengths.append(max(1, next_start - start))
    return {
        "group": group,
        "lineCount": len(lines),
        "functionCount": len(function_starts),
        "maxFunctionLines": max(function_lengths, default=0),
        "maxLineLength": max((len(line) for line in lines), default=0),
        "linesOver200": sum(len(line) > 200 for line in lines),
        "linesOver500": sum(len(line) > 500 for line in lines),
    }


def collect(root: Path) -> dict[str, dict[str, object]]:
    metrics: dict[str, dict[str, object]] = {}
    for group, relative in source_files(root):
        path = root / "src/WinCare" / relative
        if not path.is_file():
            raise FileNotFoundError(relative)
        metrics[relative] = file_metrics(path, group)
    return metrics


def totals(files: dict[str, dict[str, object]]) -> dict[str, int]:
    return {
        "fileCount": len(files),
        "lineCount": sum(int(item["lineCount"]) for item in files.values()),
        "functionCount": sum(int(item["functionCount"]) for item in files.values()),
        "linesOver200": sum(int(item["linesOver200"]) for item in files.values()),
        "linesOver500": sum(int(item["linesOver500"]) for item in files.values()),
        "maxFileLines": max((int(item["lineCount"]) for item in files.values()), default=0),
        "maxLineLength": max((int(item["maxLineLength"]) for item in files.values()), default=0),
        "maxFunctionLines": max((int(item["maxFunctionLines"]) for item in files.values()), default=0),
    }


def baseline_document(
    files: dict[str, dict[str, object]],
    strict_files: list[str] | None = None,
) -> dict[str, object]:
    return {
        "schema": "wincare.maintainability-baseline/v2",
        "description": (
            "Legacy files may improve but must not regress. New and explicitly modernized "
            "files must satisfy the strict readability and bounded-function profile."
        ),
        "strictLimits": STRICT_LIMITS,
        "strictFiles": sorted(strict_files or [], key=str.casefold),
        "totals": totals(files),
        "files": dict(sorted(files.items(), key=lambda item: item[0].casefold())),
    }


def validate_limits(
    relative: str,
    item: dict[str, object],
    limits: dict[str, object],
    errors: list[str],
    label: str,
) -> None:
    for metric, limit_name in (
        ("lineCount", "maxFileLines"),
        ("maxLineLength", "maxLineLength"),
        ("linesOver200", "linesOver200"),
        ("linesOver500", "linesOver500"),
        ("maxFunctionLines", "maxFunctionLines"),
    ):
        if int(item[metric]) > int(limits[limit_name]):
            errors.append(
                f"{label} {relative} exceeds {limit_name}: "
                f"{item[metric]} > {limits[limit_name]}"
            )


def validate(root: Path, baseline_path: Path) -> dict[str, object]:
    errors: list[str] = []
    current = collect(root)
    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
    schema = baseline.get("schema")
    if schema not in {
        "wincare.maintainability-baseline/v1",
        "wincare.maintainability-baseline/v2",
    }:
        errors.append("Unsupported maintainability baseline schema.")
    baseline_files = baseline.get("files", {})
    limits = baseline.get("strictLimits", baseline.get("newFileLimits", STRICT_LIMITS))
    strict_files = {str(item) for item in baseline.get("strictFiles", [])}

    unknown_strict = sorted(strict_files - set(current), key=str.casefold)
    for relative in unknown_strict:
        errors.append(f"Strict maintainability file is not present in the module manifest: {relative}")

    for relative, item in current.items():
        previous = baseline_files.get(relative)
        strict = previous is None or relative in strict_files
        if strict:
            validate_limits(
                relative,
                item,
                limits,
                errors,
                "Strict source file" if previous else "New source file",
            )
        else:
            if item["group"] != previous.get("group"):
                errors.append(f"Source file changed ownership group: {relative}")
            for metric in ("maxLineLength", "linesOver200", "linesOver500", "maxFunctionLines"):
                if int(item[metric]) > int(previous.get(metric, 0)):
                    errors.append(
                        f"Maintainability regression in {relative}: {metric} "
                        f"{item[metric]} > baseline {previous.get(metric, 0)}"
                    )
            if int(item["lineCount"]) > max(600, int(previous.get("lineCount", 0))):
                errors.append(
                    f"Source file exceeds 600-line bounded-file limit: "
                    f"{relative} ({item['lineCount']})"
                )

    removed = sorted(set(baseline_files) - set(current), key=str.casefold)
    current_totals = totals(current)
    baseline_totals = baseline.get("totals", {})
    for metric in ("linesOver200", "linesOver500", "maxLineLength"):
        if int(current_totals[metric]) > int(baseline_totals.get(metric, 0)):
            errors.append(
                f"Global maintainability regression: {metric} "
                f"{current_totals[metric]} > baseline {baseline_totals.get(metric, 0)}"
            )
    return {
        "schema": "wincare.maintainability-validation/v2",
        "baseline": str(baseline_path),
        "status": "passed" if not errors else "failed",
        "strictFiles": sorted(strict_files, key=str.casefold),
        "totals": current_totals,
        "baselineTotals": baseline_totals,
        "newFiles": sorted(set(current) - set(baseline_files), key=str.casefold),
        "removedFiles": removed,
        "errors": errors,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", type=Path, default=Path.cwd())
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--write-baseline", action="store_true")
    parser.add_argument("--strict-file", action="append", default=[])
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    baseline_path = (args.baseline or (root / DEFAULT_BASELINE)).resolve()
    if args.write_baseline:
        baseline_path.parent.mkdir(parents=True, exist_ok=True)
        document = baseline_document(collect(root), strict_files=args.strict_file)
        baseline_path.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
        report = {
            "status": "written",
            "baseline": str(baseline_path),
            "totals": document["totals"],
            "strictFiles": document["strictFiles"],
        }
        code = 0
    else:
        report = validate(root, baseline_path)
        code = 0 if report["status"] == "passed" else 1
    rendered = json.dumps(report, indent=2)
    print(rendered)
    if args.output:
        output = args.output if args.output.is_absolute() else root / args.output
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(rendered + "\n", encoding="utf-8")
    return code


if __name__ == "__main__":
    sys.exit(main())
