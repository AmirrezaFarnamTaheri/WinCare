#!/usr/bin/env python3
"""Fail-closed verifier for final WinCare standalone release archives."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import stat
import zipfile
from pathlib import Path, PurePosixPath

EXPECTED_EXES = {
    "WinCare.exe": 3,
    "WinCare-GUI.exe": 2,
    "WinCare-TUI.exe": 3,
}
BUILD_SCHEMA = "wincare.build.receipt/v3"
VALIDATION_SCHEMA = "wincare.release.validation/v3"
STANDALONE_SCHEMA_VERSIONS = {1, 2}
MAX_MEMBERS = 25_000
MAX_MEMBER_BYTES = 1024 * 1024 * 1024
MAX_TOTAL_BYTES = 4 * 1024 * 1024 * 1024
WINDOWS_RESERVED = {
    "con", "prn", "aux", "nul",
    *(f"com{i}" for i in range(1, 10)),
    *(f"lpt{i}" for i in range(1, 10)),
}


def sha256(data: bytes) -> str:
    """Execute the sha256 operation with validated inputs."""
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path, maximum_bytes: int = MAX_TOTAL_BYTES) -> str:
    """Execute the sha256 file operation with validated inputs."""
    if not path.is_file() or path.is_symlink():
        raise ValueError(f"file is missing or unsafe: {path}")
    before = path.stat(follow_symlinks=False)
    if before.st_size < 0 or before.st_size > maximum_bytes:
        raise ValueError(f"file exceeds size ceiling: {path}")
    digest = hashlib.sha256()
    observed = 0
    with path.open("rb", buffering=0) as stream:
        while True:
            chunk = stream.read(1024 * 1024)
            if not chunk:
                break
            observed += len(chunk)
            if observed > maximum_bytes:
                raise ValueError(f"file grew beyond size ceiling: {path}")
            digest.update(chunk)
    after = path.stat(follow_symlinks=False)
    if observed != before.st_size or (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError(f"file changed while hashing: {path}")
    return digest.hexdigest()


def safe_segment(segment: str) -> None:
    """Execute the safe segment operation with validated inputs."""
    if not segment or segment in {".", ".."}:
        raise ValueError(f"unsafe path segment: {segment!r}")
    if segment[-1] in {" ", "."}:
        raise ValueError(f"Windows-ambiguous path segment: {segment!r}")
    if any(ord(char) < 32 or char in '<>:"|?*' for char in segment):
        raise ValueError(f"unsupported path character in: {segment!r}")
    if segment.split(".", 1)[0].casefold() in WINDOWS_RESERVED:
        raise ValueError(f"Windows reserved path segment: {segment!r}")


def normalize_member(name: str) -> tuple[str, bool]:
    """Execute the normalize member operation with validated inputs."""
    if not name or "\x00" in name or "\\" in name:
        raise ValueError(f"unsafe ZIP member: {name!r}")
    is_directory = name.endswith("/")
    candidate = name[:-1] if is_directory else name
    path = PurePosixPath(candidate)
    if path.is_absolute() or not path.parts or any(part in {"", ".", ".."} for part in path.parts):
        raise ValueError(f"unsafe ZIP path: {name!r}")
    for part in path.parts:
        safe_segment(part)
    normalized = "/".join(path.parts)
    return normalized + ("/" if is_directory else ""), is_directory


def is_regular(info: zipfile.ZipInfo, is_directory: bool) -> bool:
    """Execute the is regular operation with validated inputs."""
    if info.create_system != 3:
        return True
    mode = (info.external_attr >> 16) & 0xFFFF
    if mode == 0:
        return True
    file_type = stat.S_IFMT(mode)
    return file_type in ({0, stat.S_IFDIR} if is_directory else {0, stat.S_IFREG})


def parse_archive(path: Path) -> tuple[str, dict[str, bytes]]:
    """Execute the parse archive operation with validated inputs."""
    files: dict[str, bytes] = {}
    seen: set[str] = set()
    folded: set[str] = set()
    roots: set[str] = set()
    total = 0
    with zipfile.ZipFile(path) as archive:
        infos = archive.infolist()
        if len(infos) > MAX_MEMBERS:
            raise ValueError("archive exceeds member ceiling")
        for info in infos:
            normalized, is_directory = normalize_member(info.filename)
            key = normalized.casefold()
            if normalized in seen or key in folded:
                raise ValueError(f"duplicate or case-colliding archive member: {normalized}")
            seen.add(normalized)
            folded.add(key)
            if not is_regular(info, is_directory):
                raise ValueError(f"non-regular archive member: {normalized}")
            roots.add(normalized.rstrip("/").split("/", 1)[0])
        if len(roots) != 1:
            raise ValueError("archive must have exactly one top-level directory")
        root = next(iter(roots))
        relative_folded: set[str] = set()
        for info in infos:
            normalized, is_directory = normalize_member(info.filename)
            if is_directory:
                continue
            parts = PurePosixPath(normalized).parts
            if len(parts) < 2 or parts[0] != root:
                raise ValueError(f"archive member escapes release root: {normalized}")
            relative = "/".join(parts[1:])
            key = relative.casefold()
            if key in relative_folded:
                raise ValueError(f"duplicate or case-colliding release member: {relative}")
            relative_folded.add(key)
            if info.file_size < 0 or info.file_size > MAX_MEMBER_BYTES:
                raise ValueError(f"member exceeds size ceiling: {relative}")
            ratio = info.file_size / max(1, info.compress_size)
            if info.file_size > 1024 * 1024 and ratio > 250:
                raise ValueError(f"suspicious compression ratio: {relative}")
            data = archive.read(info)
            if len(data) != info.file_size:
                raise ValueError(f"member size changed while reading: {relative}")
            total += len(data)
            if total > MAX_TOTAL_BYTES:
                raise ValueError("archive exceeds aggregate byte ceiling")
            files[relative] = data
    return root, files


def parse_manifest(data: bytes) -> dict[str, str]:
    """Execute the parse manifest operation with validated inputs."""
    try:
        text = data.decode("ascii")
    except UnicodeDecodeError as error:
        raise ValueError("release manifest is not ASCII") from error
    result: dict[str, str] = {}
    folded: set[str] = set()
    for line in text.splitlines():
        match = re.fullmatch(r"([0-9a-f]{64})  (.+)", line)
        if not match:
            raise ValueError(f"malformed release manifest line: {line!r}")
        name, is_directory = normalize_member(match.group(2))
        if is_directory:
            raise ValueError(f"manifest contains a directory: {name}")
        key = name.casefold()
        if key in folded:
            raise ValueError(f"duplicate manifest path: {name}")
        folded.add(key)
        result[name] = match.group(1)
    if not result:
        raise ValueError("release manifest is empty")
    return result


def validate_pe(data: bytes, expected_subsystem: int) -> str | None:
    """Execute the validate pe operation with validated inputs."""
    if len(data) < 20 * 1024 * 1024:
        return "standalone executable is unexpectedly small"
    if data[:2] != b"MZ" or len(data) < 0x40:
        return "missing DOS header"
    pe_offset = int.from_bytes(data[0x3C:0x40], "little")
    if pe_offset < 0x40 or pe_offset + 24 + 70 > len(data):
        return "invalid PE offset"
    if data[pe_offset:pe_offset + 4] != b"PE\0\0":
        return "missing PE signature"
    machine = int.from_bytes(data[pe_offset + 4:pe_offset + 6], "little")
    if machine != 0x8664:
        return f"unexpected PE machine 0x{machine:04x}"
    optional_size = int.from_bytes(data[pe_offset + 20:pe_offset + 22], "little")
    optional = pe_offset + 24
    if optional_size < 70 or optional + optional_size > len(data):
        return "invalid optional header"
    magic = int.from_bytes(data[optional:optional + 2], "little")
    if magic != 0x20B:
        return f"unexpected optional header magic 0x{magic:04x}"
    subsystem = int.from_bytes(data[optional + 68:optional + 70], "little")
    if subsystem != expected_subsystem:
        return f"unexpected subsystem {subsystem}, expected {expected_subsystem}"
    return None


def sbom_hashes(value: object) -> dict[str, str]:
    """Execute the sbom hashes operation with validated inputs."""
    if not isinstance(value, dict) or value.get("spdxVersion") != "SPDX-2.3":
        raise ValueError("invalid SPDX document")
    records = value.get("files")
    if not isinstance(records, list):
        raise ValueError("SPDX files list is missing")
    result: dict[str, str] = {}
    folded: set[str] = set()
    for record in records:
        if not isinstance(record, dict):
            raise ValueError("malformed SPDX file record")
        name = str(record.get("fileName", ""))
        if name.startswith("./"):
            name = name[2:]
        normalized, is_directory = normalize_member(name)
        if is_directory:
            raise ValueError("SPDX file record identifies a directory")
        key = normalized.casefold()
        if key in folded:
            raise ValueError(f"duplicate SPDX file record: {normalized}")
        folded.add(key)
        checksums = record.get("checksums")
        if not isinstance(checksums, list):
            raise ValueError(f"SPDX checksum missing: {normalized}")
        hashes = [str(item.get("checksumValue", "")).lower() for item in checksums if isinstance(item, dict) and item.get("algorithm") == "SHA256"]
        if len(hashes) != 1 or not re.fullmatch(r"[0-9a-f]{64}", hashes[0]):
            raise ValueError(f"SPDX SHA256 invalid: {normalized}")
        result[normalized] = hashes[0]
    return result


def validate(path: Path, asset_directory: Path | None = None) -> dict[str, object]:
    """Execute the validate operation with validated inputs."""
    errors: list[str] = []
    details: dict[str, object] = {}
    try:
        _, files = parse_archive(path)
        manifest_data = files.get("RELEASE-MANIFEST.sha256")
        if manifest_data is None:
            raise ValueError("RELEASE-MANIFEST.sha256 is missing")
        manifest = parse_manifest(manifest_data)
        actual_manifestable = {name: data for name, data in files.items() if name != "RELEASE-MANIFEST.sha256"}
        if set(name.casefold() for name in manifest) != set(name.casefold() for name in actual_manifestable):
            missing = sorted(set(actual_manifestable) - set(manifest), key=str.casefold)
            extra = sorted(set(manifest) - set(actual_manifestable), key=str.casefold)
            raise ValueError(f"release manifest membership mismatch: missing={missing} extra={extra}")
        for name, data in actual_manifestable.items():
            expected = next((value for key, value in manifest.items() if key.casefold() == name.casefold()), None)
            if expected != sha256(data):
                raise ValueError(f"release manifest hash mismatch: {name}")

        for name, subsystem in EXPECTED_EXES.items():
            data = files.get(name)
            if data is None:
                errors.append(f"missing standalone executable: {name}")
                continue
            pe_error = validate_pe(data, subsystem)
            if pe_error:
                errors.append(f"invalid standalone executable {name}: {pe_error}")

        hashes = [sha256(files[name]) for name in EXPECTED_EXES if name in files]
        if len(hashes) != len(set(hashes)):
            errors.append("standalone executable hashes are not independent")

        legacy = []
        for name in files:
            base = PurePosixPath(name).name.casefold()
            if base.startswith("wincare.launcher.") or base.startswith("wincare.tuilauncher."):
                legacy.append(name)
        if legacy:
            errors.append("legacy launcher artifacts remain: " + ", ".join(sorted(legacy, key=str.casefold)))

        try:
            receipt = json.loads(files.get("BUILD-RECEIPT.json", b"{}").decode("utf-8-sig"))
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ValueError("BUILD-RECEIPT.json is invalid") from error
        if receipt.get("schema") != BUILD_SCHEMA:
            errors.append("invalid build receipt schema")
        if receipt.get("packageProfile") != "production":
            errors.append("build receipt is not production profile")
        if bool(receipt.get("source", {}).get("dirty", True)):
            errors.append("build receipt records a dirty source tree")
        standalone = receipt.get("standalone", {})
        artifacts = standalone.get("artifacts") if isinstance(standalone, dict) else None
        if not isinstance(artifacts, list):
            errors.append("standalone receipt artifacts are missing")
        else:
            receipt_artifacts = {str(item.get("name")): item for item in artifacts if isinstance(item, dict)}
            if set(receipt_artifacts) != set(EXPECTED_EXES):
                errors.append("standalone receipt artifact membership mismatch")
            for name in EXPECTED_EXES:
                if name not in files or name not in receipt_artifacts:
                    continue
                record = receipt_artifacts[name]
                if record.get("sha256") != sha256(files[name]) or record.get("bytes") != len(files[name]):
                    errors.append(f"standalone receipt evidence mismatch: {name}")

        standalone_manifest_data = files.get("WinCare.Standalone.build.json")
        if standalone_manifest_data is None:
            errors.append("standalone build manifest is missing from archive")
        else:
            try:
                standalone_manifest = json.loads(standalone_manifest_data.decode("utf-8-sig"))
                if not isinstance(standalone_manifest, dict):
                    raise ValueError("standalone build manifest root must be a JSON object")
                manifest_schema_version = standalone_manifest.get("SchemaVersion")
                if manifest_schema_version not in STANDALONE_SCHEMA_VERSIONS:
                    errors.append("invalid standalone build manifest schema")
                expected_standalone_schema = f"wincare.standalone.build/v{manifest_schema_version}"
                if isinstance(standalone, dict) and standalone.get("schema") != expected_standalone_schema:
                    errors.append("standalone receipt schema does not match build manifest")
                if standalone_manifest.get("RuntimeIdentifier") != "win-x64" or standalone_manifest.get("Configuration") != "Release":
                    errors.append("standalone build manifest configuration mismatch")
                if standalone_manifest.get("SelfContained") is not True or standalone_manifest.get("SingleFile") is not True:
                    errors.append("standalone build manifest does not attest self-contained single-file outputs")
                manifest_records = standalone_manifest.get("Artifacts")
                if not isinstance(manifest_records, list):
                    errors.append("standalone build manifest artifact records are missing")
                else:
                    by_name = {str(item.get("Name")): item for item in manifest_records if isinstance(item, dict)}
                    if set(by_name) != set(EXPECTED_EXES) or len(by_name) != len(manifest_records):
                        errors.append("standalone build manifest artifact membership mismatch")
                    for name, subsystem in EXPECTED_EXES.items():
                        if name not in files or name not in by_name:
                            continue
                        item = by_name[name]
                        if (
                            str(item.get("Sha256", "")).lower() != sha256(files[name])
                            or item.get("Bytes") != len(files[name])
                            or item.get("Subsystem") != subsystem
                            or item.get("RuntimeIdentifier") != "win-x64"
                            or item.get("SelfTestExitCode") != 0
                        ):
                            errors.append(f"standalone build manifest evidence mismatch: {name}")
                expected_manifest_hash = standalone.get("manifestSha256") if isinstance(standalone, dict) else None
                if expected_manifest_hash != sha256(standalone_manifest_data):
                    errors.append("standalone build manifest hash does not match build receipt")
            except (UnicodeDecodeError, json.JSONDecodeError, ValueError) as error:
                errors.append(f"standalone build manifest is invalid: {error}")

        try:
            sbom = json.loads(files.get("SBOM.spdx.json", b"{}").decode("utf-8-sig"))
            sbom_files = sbom_hashes(sbom)
        except (UnicodeDecodeError, json.JSONDecodeError, ValueError) as error:
            errors.append(str(error))
            sbom_files = {}
        expected_sbom = {
            name: sha256(data)
            for name, data in files.items()
            if name not in {"SBOM.spdx.json", "BUILD-RECEIPT.json", "RELEASE-MANIFEST.sha256"}
        }
        if {key.casefold() for key in sbom_files} != {key.casefold() for key in expected_sbom}:
            errors.append("SBOM file membership mismatch")
        else:
            for name, expected in expected_sbom.items():
                observed = next((value for key, value in sbom_files.items() if key.casefold() == name.casefold()), None)
                if observed != expected:
                    errors.append(f"SBOM hash mismatch: {name}")

        if asset_directory is not None:
            if not asset_directory.is_dir() or asset_directory.is_symlink():
                errors.append("asset directory is missing or unsafe")
            else:
                version = str(receipt.get("version", ""))
                expected_assets = {
                    f"WinCare-{version}.zip",
                    f"WinCare-{version}.zip.sha256",
                    f"WinCare-{version}.zip.intoto.jsonl",
                    f"WinCare-{version}-SBOM.spdx.json",
                    f"WinCare-{version}-BUILD-RECEIPT.json",
                    f"WinCare-{version}-release-receipt.json",
                    f"WinCare-{version}-build-result.json",
                    "WinCare.Standalone.build.json",
                    *EXPECTED_EXES.keys(),
                }
                actual_assets = {entry.name for entry in asset_directory.iterdir()}
                if actual_assets != expected_assets:
                    missing = sorted(expected_assets - actual_assets, key=str.casefold)
                    extra = sorted(actual_assets - expected_assets, key=str.casefold)
                    errors.append(f"release asset membership mismatch: missing={missing} extra={extra}")
                archive_asset = asset_directory / f"WinCare-{version}.zip"
                if archive_asset.is_file() and not archive_asset.is_symlink():
                    if sha256_file(archive_asset) != sha256_file(path):
                        errors.append("release archive asset differs from verified archive")
                for name in EXPECTED_EXES:
                    loose = asset_directory / name
                    if not loose.is_file() or loose.is_symlink():
                        errors.append(f"loose standalone asset missing: {name}")
                    elif name in files and sha256_file(loose, MAX_MEMBER_BYTES) != sha256(files[name]):
                        errors.append(f"loose standalone asset differs from archive: {name}")
                loose_manifest = asset_directory / "WinCare.Standalone.build.json"
                if not loose_manifest.is_file() or loose_manifest.is_symlink():
                    errors.append("loose standalone build manifest is missing")
                elif standalone_manifest_data is not None and sha256_file(loose_manifest, 4 * 1024 * 1024) != sha256(standalone_manifest_data):
                    errors.append("loose standalone build manifest differs from archive")
                for member_name, asset_name in (
                    ("SBOM.spdx.json", f"WinCare-{version}-SBOM.spdx.json"),
                    ("BUILD-RECEIPT.json", f"WinCare-{version}-BUILD-RECEIPT.json"),
                ):
                    loose = asset_directory / asset_name
                    if not loose.is_file() or loose.is_symlink():
                        errors.append(f"loose release metadata missing: {asset_name}")
                    elif member_name in files and sha256_file(loose, MAX_MEMBER_BYTES) != sha256(files[member_name]):
                        errors.append(f"loose release metadata differs from archive: {asset_name}")
                checksum_path = asset_directory / f"WinCare-{version}.zip.sha256"
                try:
                    checksum = checksum_path.read_text(encoding="ascii").strip()
                    match = re.fullmatch(r"([0-9a-f]{64})  (.+)", checksum)
                    if not match or match.group(1) != sha256_file(path) or match.group(2) != path.name:
                        errors.append("release checksum sidecar mismatch")
                except (OSError, UnicodeDecodeError):
                    errors.append("release checksum sidecar is missing or invalid")
                try:
                    release_receipt = json.loads((asset_directory / f"WinCare-{version}-release-receipt.json").read_text(encoding="utf-8-sig"))
                    if release_receipt.get("schema") != "wincare.release.receipt/v3":
                        errors.append("invalid release receipt schema")
                    archive_record = release_receipt.get("archive", {})
                    if archive_record.get("path") != path.name or archive_record.get("sha256") != sha256_file(path):
                        errors.append("release receipt archive evidence mismatch")
                except (OSError, UnicodeDecodeError, json.JSONDecodeError):
                    errors.append("release receipt is missing or invalid")
                try:
                    provenance_text = (asset_directory / f"WinCare-{version}.zip.intoto.jsonl").read_text(encoding="utf-8")
                    provenance = json.loads(provenance_text)
                    subjects = provenance.get("subject", [])
                    if not isinstance(subjects, list) or len(subjects) != 1 or subjects[0].get("name") != path.name or subjects[0].get("digest", {}).get("sha256") != sha256_file(path):
                        errors.append("release provenance subject mismatch")
                except (OSError, UnicodeDecodeError, json.JSONDecodeError):
                    errors.append("release provenance is missing or invalid")
                try:
                    build_result = json.loads((asset_directory / f"WinCare-{version}-build-result.json").read_text(encoding="utf-8-sig"))
                    if build_result.get("schema") != "wincare.release.build-result/v3" or build_result.get("status") != "passed":
                        errors.append("invalid release build result")
                    if build_result.get("archiveSha256") != sha256_file(path) or build_result.get("version") != version:
                        errors.append("release build result archive evidence mismatch")
                except (OSError, UnicodeDecodeError, json.JSONDecodeError):
                    errors.append("release build result is missing or invalid")

        details = {
            "archiveSha256": sha256_file(path),
            "members": len(files),
            "standalone": {name: sha256(files[name]) for name in EXPECTED_EXES if name in files},
        }
    except Exception as error:
        errors.append(str(error))

    return {
        "schema": VALIDATION_SCHEMA,
        "status": "passed" if not errors else "failed",
        "errors": errors,
        "details": details,
    }


def main() -> int:
    """Run the command-line entrypoint and return its exit status."""
    parser = argparse.ArgumentParser()
    parser.add_argument("archive", type=Path)
    parser.add_argument("--asset-directory", type=Path)
    args = parser.parse_args()
    result = validate(args.archive, args.asset_directory)
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0 if result["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
