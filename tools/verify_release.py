#!/usr/bin/env python3
"""Stream-validate and optionally extract WinCare v2/v3 release ZIPs safely."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import stat
import sys
import uuid
import zipfile
from pathlib import Path, PurePosixPath

MAX_MEMBERS = 25_000
MAX_MEMBER_BYTES = 1024 * 1024 * 1024
MAX_TOTAL_BYTES = 4 * 1024 * 1024 * 1024
MAX_RATIO = 250
MAX_MANIFEST_BYTES = 16 * 1024 * 1024
MAX_SBOM_BYTES = 64 * 1024 * 1024
MAX_RECEIPT_BYTES = 4 * 1024 * 1024
COPY_CHUNK_BYTES = 1024 * 1024
MANIFEST_RE = re.compile(r"^([0-9a-f]{64})  (.+)$")
ROOT_RE = re.compile(r"WinCare-\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?")
V3_SCHEMA = "wincare.build.receipt/v3"
V3_EXECUTABLES = {"WinCare.exe", "WinCare-GUI.exe", "WinCare-TUI.exe"}
WINDOWS_RESERVED = {
    "con", "prn", "aux", "nul",
    *(f"com{i}" for i in range(1, 10)),
    *(f"lpt{i}" for i in range(1, 10)),
}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb", buffering=0) as stream:
        for chunk in iter(lambda: stream.read(COPY_CHUNK_BYTES), b""):
            digest.update(chunk)
    return digest.hexdigest()


def tree_hash(files: dict[str, bytes]) -> str:
    return tree_hash_digests({name: hashlib.sha256(data).hexdigest() for name, data in files.items()})


def tree_hash_digests(digests: dict[str, str]) -> str:
    result = hashlib.sha256()
    for name in sorted(digests, key=str.casefold):
        result.update(name.encode("utf-8"))
        result.update(b"\0")
        result.update(bytes.fromhex(digests[name]))
        result.update(b"\0")
    return result.hexdigest()


def _safe_segment(segment: str) -> None:
    if not segment or segment in {".", ".."}:
        raise ValueError(f"Unsafe member path segment: {segment!r}")
    if segment[-1] in {" ", "."}:
        raise ValueError(f"Windows-ambiguous member path segment: {segment!r}")
    if any(ord(char) < 32 or char in '<>:"|?*' for char in segment):
        raise ValueError(f"Unsupported member path character in: {segment!r}")
    if segment.split(".", 1)[0].casefold() in WINDOWS_RESERVED:
        raise ValueError(f"Windows-reserved member path segment: {segment!r}")


def normalize_member(name: str, *, allow_directory: bool = True) -> tuple[str, bool]:
    if not name or "\x00" in name or "\\" in name:
        raise ValueError(f"Unsafe member path: {name}")
    is_directory = name.endswith("/")
    if is_directory and not allow_directory:
        raise ValueError(f"Explicit directory member is not expected: {name}")
    candidate = name[:-1] if is_directory else name
    pure = PurePosixPath(candidate)
    if pure.is_absolute() or not pure.parts or any(part in {"", ".", ".."} for part in pure.parts):
        raise ValueError(f"Unsafe member path: {name}")
    for part in pure.parts:
        _safe_segment(part)
    normalized = "/".join(pure.parts)
    return normalized + ("/" if is_directory else ""), is_directory


def read_member_bounded(archive: zipfile.ZipFile, info: zipfile.ZipInfo, maximum_bytes: int) -> bytes:
    if info.file_size < 0 or info.file_size > maximum_bytes:
        raise ValueError(f"Archive metadata member exceeds {maximum_bytes} bytes: {info.filename}")
    data = bytearray()
    with archive.open(info, "r") as stream:
        while True:
            chunk = stream.read(min(COPY_CHUNK_BYTES, maximum_bytes - len(data) + 1))
            if not chunk:
                break
            data.extend(chunk)
            if len(data) > maximum_bytes:
                raise ValueError(f"Archive metadata member exceeds {maximum_bytes} bytes: {info.filename}")
    if len(data) != info.file_size:
        raise ValueError(
            f"Archive member length changed while reading {info.filename}: "
            f"expected {info.file_size}, observed {len(data)}"
        )
    return bytes(data)


def hash_member(archive: zipfile.ZipFile, info: zipfile.ZipInfo) -> str:
    digest = hashlib.sha256()
    observed = 0
    with archive.open(info, "r") as stream:
        while True:
            chunk = stream.read(COPY_CHUNK_BYTES)
            if not chunk:
                break
            observed += len(chunk)
            if observed > info.file_size or observed > MAX_MEMBER_BYTES:
                raise ValueError(f"Archive member exceeded its declared or permitted size: {info.filename}")
            digest.update(chunk)
    if observed != info.file_size:
        raise ValueError(
            f"Archive member length mismatch for {info.filename}: "
            f"expected {info.file_size}, observed {observed}"
        )
    return digest.hexdigest()


def parse_manifest(data: bytes) -> dict[str, str]:
    try:
        lines = data.decode("ascii").splitlines()
    except UnicodeDecodeError as error:
        raise ValueError("Release manifest is not ASCII.") from error
    expected: dict[str, str] = {}
    folded: set[str] = set()
    for line in lines:
        match = MANIFEST_RE.fullmatch(line)
        if not match:
            raise ValueError(f"Malformed manifest line: {line!r}")
        digest, name = match.groups()
        normalized, is_directory = normalize_member(name)
        if is_directory:
            raise ValueError(f"Manifest identifies a directory: {name}")
        if normalized in expected or normalized.casefold() in folded:
            raise ValueError(f"Duplicate/case-colliding manifest path: {name}")
        expected[normalized] = digest
        folded.add(normalized.casefold())
    if not expected:
        raise ValueError("Release manifest is empty.")
    return expected


def _validate_sbom(archive: zipfile.ZipFile, relative_infos: dict[str, zipfile.ZipInfo], actual_names: set[str], errors: list[str]) -> None:
    try:
        info = relative_infos.get("SBOM.spdx.json")
        if info is None:
            raise ValueError("SBOM member is missing.")
        value = json.loads(read_member_bounded(archive, info, MAX_SBOM_BYTES).decode("utf-8"))
        if value.get("spdxVersion") != "SPDX-2.3":
            errors.append("SBOM is not SPDX-2.3.")
        records = value.get("files", [])
        sbom_files: dict[str, str | None] = {}
        for item in records:
            if not isinstance(item, dict):
                continue
            name = str(item.get("fileName", "")).removeprefix("./")
            if not name:
                continue
            normalized, is_directory = normalize_member(name)
            if is_directory or normalized.casefold() in {key.casefold() for key in sbom_files}:
                raise ValueError(f"Duplicate or invalid SBOM file path: {name}")
            hashes = [
                str(check.get("checksumValue", "")).lower()
                for check in item.get("checksums", [])
                if isinstance(check, dict) and check.get("algorithm") == "SHA256"
            ]
            sbom_files[normalized] = hashes[0] if len(hashes) == 1 and re.fullmatch(r"[0-9a-f]{64}", hashes[0]) else None
        source_names = actual_names - {"SBOM.spdx.json", "BUILD-RECEIPT.json"}
        if not source_names.issubset(sbom_files):
            errors.append(f"SBOM omits source files: {sorted(source_names-set(sbom_files))[:20]}")
    except Exception as error:
        errors.append(f"Invalid SBOM: {error}")


def validate(path: Path) -> dict[str, object]:
    errors: list[str] = []
    warnings: list[str] = []
    member_count = 0
    expanded_bytes = 0
    profile: object = None
    archive_sha256 = ""
    if not path.is_file():
        return {"status": "failed", "errors": [f"Archive does not exist: {path}"]}
    if path.is_symlink():
        return {"status": "failed", "errors": [f"Archive cannot be a symbolic link: {path}"]}
    archive_sha256 = sha256_file(path)
    try:
        with zipfile.ZipFile(path) as archive:
            infos = archive.infolist()
            member_count = len(infos)
            if member_count > MAX_MEMBERS:
                errors.append(f"Member count {member_count} exceeds {MAX_MEMBERS}.")
            names: set[str] = set()
            folded: set[str] = set()
            roots: set[str] = set()
            safe_infos: dict[str, zipfile.ZipInfo] = {}
            for info in infos:
                try:
                    name, is_directory = normalize_member(info.filename)
                except ValueError as error:
                    errors.append(str(error))
                    continue
                if name in names:
                    errors.append(f"Duplicate member: {name}")
                if name.casefold() in folded:
                    errors.append(f"Case-colliding member: {name}")
                names.add(name)
                folded.add(name.casefold())
                roots.add(name.rstrip("/").split("/", 1)[0])
                mode = (info.external_attr >> 16) & 0xFFFF
                file_type = stat.S_IFMT(mode)
                if file_type == stat.S_IFLNK:
                    errors.append(f"Symbolic link member: {name}")
                if is_directory or info.is_dir():
                    errors.append(f"Explicit directory member is not expected: {name}")
                    continue
                if file_type not in {0, stat.S_IFREG}:
                    errors.append(f"Non-regular member: {name}")
                if info.file_size < 0 or info.file_size > MAX_MEMBER_BYTES:
                    errors.append(f"Oversized member: {name}")
                expanded_bytes += max(0, info.file_size)
                ratio = info.file_size / max(1, info.compress_size)
                if info.file_size > 1024 * 1024 and ratio > MAX_RATIO:
                    errors.append(f"Suspicious compression ratio: {name}")
                safe_infos[name] = info
            if expanded_bytes > MAX_TOTAL_BYTES:
                errors.append(f"Expanded size {expanded_bytes} exceeds {MAX_TOTAL_BYTES}.")
            if len(roots) != 1:
                errors.append(f"Archive must have one top-level directory, found {sorted(roots)}")
            root = next(iter(roots), "")
            if not ROOT_RE.fullmatch(root):
                errors.append(f"Unexpected top-level directory: {root}")
            relative_infos = {
                name[len(root) + 1 :]: info
                for name, info in safe_infos.items()
                if root and name.startswith(root + "/")
            }
            required = {
                "RELEASE-MANIFEST.sha256", "SBOM.spdx.json", "BUILD-RECEIPT.json",
                "WinCare.ps1", "src/WinCare/WinCare.psd1",
            }
            missing_required = sorted(required - relative_infos.keys())
            if missing_required:
                errors.append(f"Missing required members: {missing_required}")

            expected: dict[str, str] = {}
            manifest_info = relative_infos.get("RELEASE-MANIFEST.sha256")
            if manifest_info is not None:
                try:
                    expected = parse_manifest(read_member_bounded(archive, manifest_info, MAX_MANIFEST_BYTES))
                except Exception as error:
                    errors.append(f"Invalid release manifest: {error}")
            relative_hashes: dict[str, str] = {}
            if not errors:
                for name in sorted(relative_infos, key=str.casefold):
                    relative_hashes[name] = hash_member(archive, relative_infos[name])
            actual_names = set(relative_infos) - {"RELEASE-MANIFEST.sha256"}
            if expected and set(expected) != actual_names:
                errors.append(
                    "Manifest membership mismatch: "
                    f"missing={sorted(actual_names-set(expected))} extra={sorted(set(expected)-actual_names)}"
                )
            for name, digest in expected.items():
                observed = relative_hashes.get(name)
                if observed is not None and observed != digest:
                    errors.append(f"Manifest hash mismatch: {name}")

            _validate_sbom(archive, relative_infos, actual_names, errors)

            try:
                receipt_info = relative_infos.get("BUILD-RECEIPT.json")
                if receipt_info is None:
                    raise ValueError("Build receipt member is missing.")
                receipt = json.loads(read_member_bounded(archive, receipt_info, MAX_RECEIPT_BYTES).decode("utf-8"))
                version = receipt.get("version")
                if root != f"WinCare-{version}":
                    errors.append("Build receipt version does not match archive root.")
                profile = receipt.get("packageProfile")
                if profile not in {"development", "production"}:
                    errors.append("Build receipt contains an unsupported package profile.")
                source_dirty = receipt.get("source", {}).get("dirty") is True
                if profile == "production":
                    if source_dirty:
                        errors.append("Production archive was built from a dirty source state.")
                    forbidden_prefixes = ("tests/", "tools/", "analysis/", ".github/", "src/WinCare/Native/")
                    forbidden = sorted(name for name in relative_infos if name.startswith(forbidden_prefixes))
                    if forbidden:
                        errors.append(f"Production archive contains development-only members: {forbidden[:20]}")
                    schema = receipt.get("schema")
                    required_native = {"bin/WinCare.Native.dll", "bin/WinCare.Native.build.json"}
                    if schema == V3_SCHEMA:
                        required_native |= V3_EXECUTABLES
                        legacy = sorted(
                            name for name in relative_infos
                            if PurePosixPath(name).name.casefold().startswith(("wincare.launcher.", "wincare.tuilauncher."))
                        )
                        if legacy:
                            errors.append(f"Production v3 archive retains legacy launcher artifacts: {legacy}")
                    else:
                        required_native |= {"bin/WinCare.Launcher.exe", "bin/WinCare.TuiLauncher.exe"}
                    missing_native = sorted(required_native - set(relative_infos))
                    if missing_native:
                        errors.append(f"Production archive omits required native artifacts: {missing_native}")
                    if receipt.get("native", {}).get("status") != "source-built-and-verified":
                        errors.append("Production archive native provenance is not source-built-and-verified.")
                elif source_dirty:
                    warnings.append("Development archive was built from a dirty source state and is non-promotable.")
                package_hashes = {
                    name: digest
                    for name, digest in relative_hashes.items()
                    if name not in {"SBOM.spdx.json", "BUILD-RECEIPT.json", "RELEASE-MANIFEST.sha256"}
                }
                expected_package_hash = receipt.get("packageInputs", {}).get("treeSha256")
                expected_package_members = receipt.get("packageInputs", {}).get("members")
                if not expected_package_hash:
                    errors.append("Build receipt omits package-input tree evidence.")
                elif tree_hash_digests(package_hashes) != expected_package_hash:
                    errors.append("Build receipt package-input tree hash does not match archive content.")
                if expected_package_members is None:
                    errors.append("Build receipt omits package-input member count.")
                elif int(expected_package_members) != len(package_hashes):
                    errors.append("Build receipt package-input member count does not match archive content.")
            except Exception as error:
                errors.append(f"Invalid build receipt: {error}")
    except (OSError, zipfile.BadZipFile, zipfile.LargeZipFile, RuntimeError, ValueError) as error:
        errors.append(f"Archive validation failed: {error}")

    return {
        "schema": "wincare.archive.validation/v3",
        "archive": str(path.resolve()),
        "sha256": archive_sha256,
        "members": member_count,
        "expandedBytes": expanded_bytes,
        "errors": sorted(set(errors)),
        "warnings": sorted(set(warnings)),
        "packageProfile": profile,
        "status": "passed" if not errors else "failed",
    }


def extract_validated(path: Path, destination: Path) -> dict[str, object]:
    report = validate(path)
    if report["status"] != "passed":
        return report
    destination = destination.resolve()
    if destination.exists():
        return {**report, "status": "failed", "errors": [*report["errors"], f"Extraction destination already exists: {destination}"]}
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.parent.is_symlink():
        return {**report, "status": "failed", "errors": [*report["errors"], f"Extraction parent cannot be a symbolic link: {destination.parent}"]}
    staging = destination.parent / f".{destination.name}.{uuid.uuid4().hex}.extracting"
    archive_before = str(report["sha256"])
    extracted = 0
    try:
        staging.mkdir(mode=0o700)
        staging_root = staging.resolve()
        with zipfile.ZipFile(path) as archive:
            infos = archive.infolist()
            if len(infos) > MAX_MEMBERS:
                raise ValueError(f"Member count {len(infos)} exceeds {MAX_MEMBERS} during extraction.")
            normalized_infos: list[tuple[zipfile.ZipInfo, str]] = []
            names: set[str] = set()
            folded: set[str] = set()
            total = 0
            for info in infos:
                name, is_directory = normalize_member(info.filename, allow_directory=False)
                if name in names or name.casefold() in folded:
                    raise ValueError(f"Duplicate or case-colliding member during extraction: {name}")
                names.add(name)
                folded.add(name.casefold())
                mode = (info.external_attr >> 16) & 0xFFFF
                if stat.S_IFMT(mode) not in {0, stat.S_IFREG} or is_directory or info.is_dir():
                    raise ValueError(f"Non-regular member during extraction: {name}")
                if info.file_size < 0 or info.file_size > MAX_MEMBER_BYTES:
                    raise ValueError(f"Oversized member during extraction: {name}")
                total += info.file_size
                if total > MAX_TOTAL_BYTES:
                    raise ValueError(f"Expanded size exceeds {MAX_TOTAL_BYTES} during extraction.")
                ratio = info.file_size / max(1, info.compress_size)
                if info.file_size > 1024 * 1024 and ratio > MAX_RATIO:
                    raise ValueError(f"Suspicious compression ratio during extraction: {name}")
                normalized_infos.append((info, name))
            manifest_pair = next((pair for pair in normalized_infos if pair[1].endswith("/RELEASE-MANIFEST.sha256")), None)
            if manifest_pair is None:
                raise ValueError("Release manifest is missing during extraction.")
            expected = parse_manifest(read_member_bounded(archive, manifest_pair[0], MAX_MANIFEST_BYTES))
            root_name = PurePosixPath(manifest_pair[1]).parts[0]
            observed_relative: set[str] = set()
            for info, name in normalized_infos:
                pure = PurePosixPath(name)
                target = staging.joinpath(*pure.parts)
                target_parent = target.parent
                target_parent.mkdir(parents=True, exist_ok=True)
                if not target_parent.resolve().is_relative_to(staging_root):
                    raise ValueError(f"Extraction path escaped staging root: {name}")
                flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
                if hasattr(os, "O_BINARY"):
                    flags |= os.O_BINARY
                descriptor = os.open(target, flags, 0o600)
                digest = hashlib.sha256()
                observed = 0
                try:
                    with os.fdopen(descriptor, "wb", closefd=True) as output, archive.open(info, "r") as source:
                        while True:
                            chunk = source.read(COPY_CHUNK_BYTES)
                            if not chunk:
                                break
                            observed += len(chunk)
                            if observed > info.file_size or observed > MAX_MEMBER_BYTES:
                                raise ValueError(f"Archive member exceeded its declared or permitted size: {name}")
                            output.write(chunk)
                            digest.update(chunk)
                        output.flush()
                        os.fsync(output.fileno())
                except Exception:
                    try:
                        os.close(descriptor)
                    except OSError:
                        pass
                    raise
                if observed != info.file_size:
                    raise ValueError(f"Extracted member length mismatch: {name}")
                relative = name[len(root_name) + 1 :] if name.startswith(root_name + "/") else ""
                if relative and relative != "RELEASE-MANIFEST.sha256":
                    observed_relative.add(relative)
                    if expected.get(relative) != digest.hexdigest():
                        raise ValueError(f"Extracted member hash mismatch: {relative}")
                extracted += observed
        if observed_relative != set(expected):
            raise ValueError(
                "Extracted manifest membership mismatch: "
                f"missing={sorted(set(expected)-observed_relative)} extra={sorted(observed_relative-set(expected))}"
            )
        if sha256_file(path) != archive_before:
            raise ValueError("Archive bytes changed during validated extraction.")
        os.replace(staging, destination)
        return {**report, "extraction": {"destination": str(destination), "bytes": extracted, "status": "passed"}}
    except Exception as error:
        shutil.rmtree(staging, ignore_errors=True)
        return {**report, "status": "failed", "errors": sorted(set([*report["errors"], f"Validated extraction failed: {error}"]))}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("archive", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--extract-to", type=Path)
    args = parser.parse_args()
    report = extract_validated(args.archive, args.extract_to) if args.extract_to else validate(args.archive)
    text = json.dumps(report, indent=2)
    print(text)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())
