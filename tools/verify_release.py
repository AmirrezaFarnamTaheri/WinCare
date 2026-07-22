#!/usr/bin/env python3
"""Independently validate a WinCare release ZIP without extracting it."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import stat
import sys
import zipfile
from pathlib import PurePosixPath, Path

MAX_MEMBERS = 5000
MAX_MEMBER_BYTES = 512 * 1024 * 1024
MAX_TOTAL_BYTES = 2 * 1024 * 1024 * 1024
MAX_RATIO = 250
MANIFEST_RE = re.compile(r"^([0-9a-f]{64})  (.+)$")


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def validate(path: Path) -> dict[str, object]:
    errors: list[str] = []
    warnings: list[str] = []
    if not path.is_file():
        return {"status": "failed", "errors": [f"Archive does not exist: {path}"]}
    with zipfile.ZipFile(path) as archive:
        infos = archive.infolist()
        if len(infos) > MAX_MEMBERS:
            errors.append(f"Member count {len(infos)} exceeds {MAX_MEMBERS}.")
        names: set[str] = set(); folded: set[str] = set(); roots: set[str] = set(); total = 0
        payload: dict[str, bytes] = {}
        for info in infos:
            name = info.filename.replace("\\", "/")
            pure = PurePosixPath(name)
            if pure.is_absolute() or not pure.parts or ".." in pure.parts or any(part in {"", "."} for part in pure.parts):
                errors.append(f"Unsafe member path: {name}"); continue
            if name in names: errors.append(f"Duplicate member: {name}")
            if name.casefold() in folded: errors.append(f"Case-colliding member: {name}")
            names.add(name); folded.add(name.casefold()); roots.add(pure.parts[0])
            mode = (info.external_attr >> 16) & 0xFFFF
            if stat.S_ISLNK(mode): errors.append(f"Symbolic link member: {name}")
            if info.file_size > MAX_MEMBER_BYTES: errors.append(f"Oversized member: {name}")
            total += info.file_size
            ratio = info.file_size / max(1, info.compress_size)
            if info.file_size > 1024 * 1024 and ratio > MAX_RATIO: errors.append(f"Suspicious compression ratio: {name}")
            if name.endswith("/"): errors.append(f"Explicit directory member is not expected: {name}"); continue
            payload[name] = archive.read(info)
        if len(roots) != 1: errors.append(f"Archive must have one top-level directory, found {sorted(roots)}")
        if total > MAX_TOTAL_BYTES: errors.append(f"Expanded size {total} exceeds {MAX_TOTAL_BYTES}.")
        root = next(iter(roots), "")
        if not re.fullmatch(r"WinCare-\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?", root):
            errors.append(f"Unexpected top-level directory: {root}")
        relative = {name[len(root)+1:]: data for name, data in payload.items() if name.startswith(root + "/")}
        required = {"RELEASE-MANIFEST.sha256", "SBOM.spdx.json", "BUILD-RECEIPT.json", "WinCare.ps1", "src/WinCare/WinCare.psd1"}
        missing_required = sorted(required - relative.keys())
        if missing_required: errors.append(f"Missing required members: {missing_required}")

        expected: dict[str, str] = {}
        manifest_data = relative.get("RELEASE-MANIFEST.sha256", b"")
        try:
            for line in manifest_data.decode("ascii").splitlines():
                match = MANIFEST_RE.fullmatch(line)
                if not match: raise ValueError(f"Malformed manifest line: {line!r}")
                digest, name = match.groups()
                if name in expected or name.casefold() in {x.casefold() for x in expected}:
                    raise ValueError(f"Duplicate/case-colliding manifest path: {name}")
                pure = PurePosixPath(name)
                if pure.is_absolute() or ".." in pure.parts: raise ValueError(f"Unsafe manifest path: {name}")
                expected[name] = digest
        except Exception as error:
            errors.append(f"Invalid release manifest: {error}")
        actual_names = set(relative) - {"RELEASE-MANIFEST.sha256"}
        if set(expected) != actual_names:
            errors.append(f"Manifest membership mismatch: missing={sorted(actual_names-set(expected))} extra={sorted(set(expected)-actual_names)}")
        for name, digest in expected.items():
            data = relative.get(name)
            if data is not None and hashlib.sha256(data).hexdigest() != digest:
                errors.append(f"Manifest hash mismatch: {name}")

        try:
            sbom = json.loads(relative.get("SBOM.spdx.json", b"{}").decode("utf-8"))
            if sbom.get("spdxVersion") != "SPDX-2.3": errors.append("SBOM is not SPDX-2.3.")
            sbom_files = {item.get("fileName", "").removeprefix("./") for item in sbom.get("files", [])}
            source_names = actual_names - {"SBOM.spdx.json", "BUILD-RECEIPT.json"}
            # Manifest is intentionally outside SBOM self-hashing; generated receipt may also be omitted.
            source_names.discard("RELEASE-MANIFEST.sha256")
            if not source_names.issubset(sbom_files):
                errors.append(f"SBOM omits source files: {sorted(source_names-sbom_files)[:20]}")
        except Exception as error:
            errors.append(f"Invalid SBOM: {error}")
        try:
            receipt = json.loads(relative.get("BUILD-RECEIPT.json", b"{}").decode("utf-8"))
            version = receipt.get("version")
            if root != f"WinCare-{version}": errors.append("Build receipt version does not match archive root.")
            if receipt.get("source", {}).get("dirty") is True: warnings.append("Archive was built from a dirty source state and is non-promotable.")
        except Exception as error:
            errors.append(f"Invalid build receipt: {error}")

    return {
        "schema": "wincare.archive.validation/v1",
        "archive": str(path.resolve()),
        "sha256": sha256_file(path),
        "members": len(infos),
        "expandedBytes": total,
        "errors": sorted(set(errors)),
        "warnings": sorted(set(warnings)),
        "status": "passed" if not errors else "failed",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("archive", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = validate(args.archive)
    text = json.dumps(report, indent=2)
    print(text)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())
