#!/usr/bin/env python3
"""Validate current-source ownership references used by WinCare records."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import sys
from pathlib import Path, PurePosixPath
from typing import Any

SCHEMA = "wincare.source-reference-validation/v1"
INDEX_SCHEMA = "wincare.source-reference-index/v1"
REFERENCE_PATTERN = re.compile(r"(?P<quote>['\"])(?P<id>SRC:[0-9a-f]{10,64})(?P=quote)")
LEGACY_PATTERN = re.compile(r"(?P<quote>['\"])(?:D\d{1,3}|W\d+|CG)-[A-Z0-9-]+(?P=quote)")
SOURCE_SUFFIXES = {".ps1", ".psd1", ".psm1", ".json"}
MAX_INDEX_BYTES = 4 * 1024 * 1024
MAX_SOURCE_BYTES = 32 * 1024 * 1024


def sha256_file(path: Path, maximum_bytes: int = MAX_SOURCE_BYTES) -> str:
    before = path.stat(follow_symlinks=False)
    if not stat.S_ISREG(before.st_mode):
        raise ValueError(f"Not a regular file: {path}")
    if before.st_size > maximum_bytes:
        raise ValueError(f"File exceeds {maximum_bytes} bytes: {path}")
    digest = hashlib.sha256()
    observed = 0
    with path.open("rb", buffering=0) as stream:
        while True:
            chunk = stream.read(131072)
            if not chunk:
                break
            observed += len(chunk)
            if observed > maximum_bytes:
                raise ValueError(f"File exceeded {maximum_bytes} bytes while reading: {path}")
            digest.update(chunk)
    after = path.stat(follow_symlinks=False)
    if (
        before.st_size != observed
        or before.st_size != after.st_size
        or before.st_mtime_ns != after.st_mtime_ns
        or before.st_ino != after.st_ino
    ):
        raise ValueError(f"File changed while hashing: {path}")
    return digest.hexdigest()


def read_text(path: Path, maximum_bytes: int) -> str:
    data = path.read_bytes()
    if len(data) > maximum_bytes:
        raise ValueError(f"File exceeds {maximum_bytes} bytes: {path}")
    return data.decode("utf-8")


def expected_reference_id(relative_path: str) -> str:
    path = PurePosixPath(relative_path)
    without_suffix = str(path.with_suffix(""))
    return "SRC:" + hashlib.sha256(without_suffix.encode("utf-8")).hexdigest()[:10]


def validate_relative_path(value: str) -> str:
    path = PurePosixPath(value)
    if not value or path.is_absolute() or ".." in path.parts or "\\" in value:
        raise ValueError(f"Unsafe source-reference path: {value!r}")
    if path.suffix.lower() not in SOURCE_SUFFIXES:
        raise ValueError(f"Unsupported source-reference suffix: {value!r}")
    return path.as_posix()


def scan_references(source_root: Path) -> tuple[dict[str, list[str]], list[str]]:
    uses: dict[str, list[str]] = {}
    legacy: list[str] = []
    for path in sorted(source_root.rglob("*")):
        if not path.is_file() or path.suffix.lower() not in SOURCE_SUFFIXES:
            continue
        if path.is_symlink():
            legacy.append(f"Unsafe source symlink: {path}")
            continue
        text = read_text(path, MAX_SOURCE_BYTES)
        relative = path.relative_to(source_root).as_posix()
        for match in REFERENCE_PATTERN.finditer(text):
            uses.setdefault(match.group("id"), []).append(relative)
        for match in LEGACY_PATTERN.finditer(text):
            legacy.append(f"Legacy source reference in {relative}: {match.group(0)}")
    return uses, legacy


def load_index(index_path: Path) -> dict[str, Any]:
    text = read_text(index_path, MAX_INDEX_BYTES)
    value = json.loads(text)
    if not isinstance(value, dict):
        raise ValueError("Source-reference index must be a JSON object.")
    return value


def write_index(index_path: Path, source_root: Path, records: list[dict[str, Any]]) -> None:
    updated = []
    for record in records:
        relative = validate_relative_path(str(record["path"]))
        path = source_root / relative
        updated.append(
            {
                "id": str(record["id"]),
                "path": relative,
                "sha256": sha256_file(path),
            }
        )
    updated.sort(key=lambda item: item["path"])
    payload = {"schema": INDEX_SCHEMA, "references": updated}
    temporary = index_path.with_suffix(index_path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8", newline="\n")
    os.replace(temporary, index_path)


def validate(root: Path, write: bool = False) -> dict[str, Any]:
    source_root = root / "src" / "WinCare"
    index_path = source_root / "Data" / "SourceReferenceIndex.json"
    errors: list[str] = []
    warnings: list[str] = []

    try:
        index = load_index(index_path)
    except Exception as exc:  # noqa: BLE001
        return {
            "schema": SCHEMA,
            "root": str(root),
            "status": "failed",
            "errors": [f"Index load failed: {exc}"],
            "warnings": [],
        }

    if index.get("schema") != INDEX_SCHEMA:
        errors.append(f"Unexpected index schema: {index.get('schema')!r}")
    records = index.get("references")
    if not isinstance(records, list):
        errors.append("Index references must be an array.")
        records = []

    if write and not errors:
        try:
            write_index(index_path, source_root, records)
            index = load_index(index_path)
            records = index["references"]
        except Exception as exc:  # noqa: BLE001
            errors.append(f"Index refresh failed: {exc}")

    indexed: dict[str, str] = {}
    indexed_paths: set[str] = set()
    for number, record in enumerate(records, 1):
        if not isinstance(record, dict):
            errors.append(f"Index record {number} is not an object.")
            continue
        identifier = str(record.get("id", ""))
        relative_raw = str(record.get("path", ""))
        expected_hash = str(record.get("sha256", ""))
        if not re.fullmatch(r"SRC:[0-9a-f]{10,64}", identifier):
            errors.append(f"Invalid source-reference ID in record {number}: {identifier!r}")
            continue
        try:
            relative = validate_relative_path(relative_raw)
        except ValueError as exc:
            errors.append(str(exc))
            continue
        if identifier in indexed:
            errors.append(f"Duplicate source-reference ID: {identifier}")
        if relative in indexed_paths:
            errors.append(f"Duplicate source-reference path: {relative}")
        indexed[identifier] = relative
        indexed_paths.add(relative)
        expected_identifier = expected_reference_id(relative)
        if identifier != expected_identifier:
            errors.append(
                f"Source-reference ID/path mismatch: {identifier} != {expected_identifier} for {relative}"
            )
        path = source_root / relative
        try:
            resolved_parent = path.parent.resolve(strict=True)
            source_resolved = source_root.resolve(strict=True)
            if source_resolved not in (resolved_parent, *resolved_parent.parents):
                raise ValueError(f"Source-reference path escapes the source root: {relative}")
            if path.is_symlink():
                raise ValueError(f"Source-reference target is a symlink: {relative}")
            actual_hash = sha256_file(path)
            if not re.fullmatch(r"[0-9a-f]{64}", expected_hash):
                errors.append(f"Invalid SHA-256 for {identifier}: {expected_hash!r}")
            elif actual_hash != expected_hash:
                errors.append(
                    f"Source-reference hash drift for {identifier} ({relative}): {expected_hash} != {actual_hash}"
                )
        except Exception as exc:  # noqa: BLE001
            errors.append(f"Source-reference target validation failed for {identifier}: {exc}")

    try:
        uses, legacy = scan_references(source_root)
        errors.extend(legacy)
    except Exception as exc:  # noqa: BLE001
        uses = {}
        errors.append(f"Source-reference scan failed: {exc}")

    for identifier, locations in sorted(uses.items()):
        if identifier not in indexed:
            errors.append(f"Unindexed source reference {identifier} used by: {', '.join(sorted(set(locations)))}")
    for identifier, relative in sorted(indexed.items()):
        if identifier not in uses:
            warnings.append(f"Unused source-reference index record: {identifier} -> {relative}")

    return {
        "schema": SCHEMA,
        "root": str(root),
        "index": str(index_path.relative_to(root)),
        "indexedReferences": len(indexed),
        "usedReferences": len(uses),
        "usageSites": sum(len(items) for items in uses.values()),
        "status": "failed" if errors else "passed",
        "errors": errors,
        "warnings": warnings,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", default=".")
    parser.add_argument("--write", action="store_true", help="Refresh indexed source hashes before validating.")
    parser.add_argument("--output")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    report = validate(root, write=args.write)
    rendered = json.dumps(report, indent=2)
    print(rendered)
    if args.output:
        output = Path(args.output)
        if not output.is_absolute():
            output = root / output
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(rendered + "\n", encoding="utf-8")
    return 1 if report["status"] != "passed" else 0


if __name__ == "__main__":
    sys.exit(main())
