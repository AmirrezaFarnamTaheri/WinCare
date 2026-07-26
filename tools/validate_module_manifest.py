#!/usr/bin/env python3
"""Validate WinCare's closed, grouped, deterministic PowerShell source manifests."""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

GROUPS = (
    ("Core", "SourceManifests/Core.psd1", ("Core",)),
    ("Providers", "SourceManifests/Providers.psd1", ("Providers",)),
    ("UI", "SourceManifests/UI.psd1", ("UI", "UI/Gui", "UI/Screens")),
)


def quoted_values(block: str, suffix: str) -> list[str]:
    return re.findall(rf"'([^']+{re.escape(suffix)})'", block)


def validate(root: Path) -> dict[str, object]:
    module_root = root / "src" / "WinCare"
    module_path = module_root / "WinCare.psm1"
    errors: list[str] = []
    text = module_path.read_text(encoding="utf-8-sig")
    declaration = re.search(r"\$script:WinCareSourceManifestFiles\s*=\s*@\((.*?)\)\s*\n\$script:WinCareSourceGroups", text, re.S)
    expected_manifests = [item[1] for item in GROUPS]
    declared_manifests = quoted_values(declaration.group(1), ".psd1") if declaration else []
    if not declaration:
        errors.append("Explicit $script:WinCareSourceManifestFiles declaration was not found.")
    elif declared_manifests != expected_manifests:
        errors.append("Source manifest declaration differs from the required Core/Providers/UI order.")

    all_declared: list[str] = []
    group_reports: list[dict[str, object]] = []
    for group, manifest_relative, folders in GROUPS:
        manifest_path = module_root / manifest_relative
        declared: list[str] = []
        if not manifest_path.is_file():
            errors.append(f"Required source manifest is missing: {manifest_relative}")
        else:
            manifest_text = manifest_path.read_text(encoding="utf-8-sig")
            schema = re.search(r"SchemaVersion\s*=\s*(\d+)", manifest_text)
            identity = re.search(r"Group\s*=\s*'([^']+)'", manifest_text)
            files_block = re.search(r"Files\s*=\s*@\((.*?)\)\s*\n\}", manifest_text, re.S)
            if not schema or int(schema.group(1)) != 1:
                errors.append(f"Unsupported source manifest schema: {manifest_relative}")
            if not identity or identity.group(1) != group:
                errors.append(f"Source manifest group identity mismatch: {manifest_relative}")
            if not files_block:
                errors.append(f"Files array is missing from source manifest: {manifest_relative}")
            else:
                declared = quoted_values(files_block.group(1), ".ps1")
        actual: list[str] = []
        for folder in folders:
            actual.extend(
                p.relative_to(module_root).as_posix()
                for p in sorted((module_root / folder).glob("*.ps1"), key=lambda item: item.name.casefold())
            )
        duplicates = sorted({item for item in declared if declared.count(item) > 1}, key=str.casefold)
        missing = sorted(set(actual) - set(declared), key=str.casefold)
        unexpected = sorted(set(declared) - set(actual), key=str.casefold)
        if duplicates:
            errors.append(f"Duplicate entries in {manifest_relative}: {', '.join(duplicates)}")
        if missing:
            errors.append(f"Undeclared {group} files: {', '.join(missing)}")
        if unexpected:
            errors.append(f"Declared {group} files do not exist: {', '.join(unexpected)}")
        if declared != actual:
            errors.append(f"{group} source order differs from canonical grouped case-insensitive order.")
        all_declared.extend(declared)
        group_reports.append({"group": group, "manifest": manifest_relative, "declaredFiles": len(declared), "actualFiles": len(actual)})

    cross_duplicates = sorted({item for item in all_declared if all_declared.count(item) > 1}, key=str.casefold)
    if cross_duplicates:
        errors.append("Source files assigned to multiple groups: " + ", ".join(cross_duplicates))
    return {
        "schema": "wincare.module-source-manifest/v2",
        "module": str(module_path),
        "sourceManifests": expected_manifests,
        "groups": group_reports,
        "declaredFiles": len(all_declared),
        "errors": errors,
        "status": "passed" if not errors else "failed",
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
    sys.exit(main())
