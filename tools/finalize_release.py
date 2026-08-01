#!/usr/bin/env python3
"""Replace legacy launchers with verified standalone hosts and emit v3 release evidence."""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import stat
import tempfile
import zipfile
from pathlib import Path

try:
    from .verify_release_v3 import EXPECTED_EXES, parse_archive, sha256, validate_pe
except ImportError:
    from verify_release_v3 import EXPECTED_EXES, parse_archive, sha256, validate_pe

BUILD_SCHEMA = "wincare.build.receipt/v3"
RELEASE_SCHEMA = "wincare.release.receipt/v3"
STANDALONE_SCHEMA_VERSIONS = {1, 2}
FIXED_EPOCH = 315532800
GENERATED = {"SBOM.spdx.json", "BUILD-RECEIPT.json", "RELEASE-MANIFEST.sha256"}


def canonical_json(value: object) -> bytes:
    """Execute the canonical json operation with validated inputs."""
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")


def pretty_json(value: object) -> bytes:
    """Execute the pretty json operation with validated inputs."""
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=False) + "\n").encode("utf-8")


def tree_hash(files: dict[str, bytes]) -> str:
    """Execute the tree hash operation with validated inputs."""
    digest = hashlib.sha256()
    for name in sorted(files, key=str.casefold):
        digest.update(name.encode("utf-8"))
        digest.update(b"\0")
        digest.update(bytes.fromhex(sha256(files[name])))
        digest.update(b"\0")
    return digest.hexdigest()


def timestamp() -> tuple[str, tuple[int, int, int, int, int, int], int]:
    """Execute the timestamp operation with validated inputs."""
    raw = max(int(os.environ.get("SOURCE_DATE_EPOCH", FIXED_EPOCH)), FIXED_EPOCH)
    value = dt.datetime.fromtimestamp(raw, dt.timezone.utc).replace(microsecond=0)
    second = value.second - (value.second % 2)
    created = value.isoformat().replace("+00:00", "Z")
    return created, (value.year, value.month, value.day, value.hour, value.minute, second), raw


def make_sbom(version: str, created: str, files: dict[str, bytes]) -> dict[str, object]:
    """Execute the make sbom operation with validated inputs."""
    package_id = "SPDXRef-Package-WinCare"
    entries: list[dict[str, object]] = []
    relationships: list[dict[str, str]] = [
        {"spdxElementId": "SPDXRef-DOCUMENT", "relationshipType": "DESCRIBES", "relatedSpdxElement": package_id}
    ]
    for index, name in enumerate(sorted(files, key=str.casefold), 1):
        identifier = f"SPDXRef-File-{index:05d}"
        entries.append(
            {
                "SPDXID": identifier,
                "fileName": "./" + name,
                "checksums": [{"algorithm": "SHA256", "checksumValue": sha256(files[name])}],
                "licenseConcluded": "NOASSERTION",
                "copyrightText": "NOASSERTION",
            }
        )
        relationships.append(
            {"spdxElementId": package_id, "relationshipType": "CONTAINS", "relatedSpdxElement": identifier}
        )
    content_hash = tree_hash(files)
    return {
        "spdxVersion": "SPDX-2.3",
        "dataLicense": "CC0-1.0",
        "SPDXID": "SPDXRef-DOCUMENT",
        "name": f"WinCare-{version}",
        "documentNamespace": f"urn:wincare:spdx:{version}:{content_hash}",
        "creationInfo": {"created": created, "creators": ["Tool: WinCare standalone release finalizer"]},
        "packages": [
            {
                "SPDXID": package_id,
                "name": "WinCare",
                "versionInfo": version,
                "downloadLocation": "NOASSERTION",
                "filesAnalyzed": True,
                "licenseConcluded": "Apache-2.0",
                "licenseDeclared": "Apache-2.0",
                "copyrightText": "Copyright 2026 WinCare Project",
                "checksums": [{"algorithm": "SHA256", "checksumValue": content_hash}],
            }
        ],
        "files": entries,
        "relationships": relationships,
    }


def release_manifest(files: dict[str, bytes]) -> bytes:
    """Execute the release manifest operation with validated inputs."""
    return ("\n".join(f"{sha256(files[name])}  {name}" for name in sorted(files, key=str.casefold)) + "\n").encode("ascii")


def write_zip(path: Path, root: str, files: dict[str, bytes], zip_time: tuple[int, int, int, int, int, int]) -> None:
    """Execute the write zip operation with validated inputs."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(prefix=path.name + ".", suffix=".tmp", dir=path.parent, delete=False) as handle:
        temporary = Path(handle.name)
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9, allowZip64=True) as archive:
            for name in sorted(files, key=str.casefold):
                info = zipfile.ZipInfo(f"{root}/{name}", date_time=zip_time)
                info.create_system = 3
                executable = Path(name).suffix.casefold() in {".ps1", ".psm1", ".py", ".cmd", ".bat", ".exe"}
                info.external_attr = (stat.S_IFREG | (0o755 if executable else 0o644)) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                info.flag_bits |= 0x800
                archive.writestr(info, files[name], compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def read_json(files: dict[str, bytes], name: str) -> dict[str, object]:
    """Execute the read json operation with validated inputs."""
    try:
        value = json.loads(files[name].decode("utf-8-sig"))
    except (KeyError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{name} is missing or invalid") from error
    if not isinstance(value, dict):
        raise ValueError(f"{name} must contain a JSON object")
    return value


def read_regular_file(path: Path, maximum_bytes: int, label: str) -> bytes:
    """Execute the read regular file operation with validated inputs."""
    if not path.is_file() or path.is_symlink():
        raise ValueError(f"{label} is missing or unsafe: {path.name}")
    before = path.stat(follow_symlinks=False)
    if before.st_size <= 0 or before.st_size > maximum_bytes:
        raise ValueError(f"{label} size is outside the permitted range: {path.name}")
    data = path.read_bytes()
    after = path.stat(follow_symlinks=False)
    if len(data) != before.st_size or (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError(f"{label} changed while reading: {path.name}")
    return data


def finalize(core_archive: Path, executable_directory: Path, output_directory: Path) -> dict[str, object]:
    """Execute the finalize operation with validated inputs."""
    root, core_files = parse_archive(core_archive)
    core_receipt = read_json(core_files, "BUILD-RECEIPT.json")
    if core_receipt.get("schema") not in {"wincare.build.receipt/v2", BUILD_SCHEMA}:
        raise ValueError("unsupported core build receipt schema")
    if core_receipt.get("packageProfile") != "production":
        raise ValueError("core archive is not production profile")
    if bool(core_receipt.get("source", {}).get("dirty", True)):
        raise ValueError("core archive records dirty source state")
    required_core_native = {"bin/WinCare.Native.dll", "bin/WinCare.Native.build.json"}
    missing_core_native = sorted(required_core_native - set(core_files), key=str.casefold)
    if missing_core_native:
        raise ValueError("core archive omits required native provenance: " + ", ".join(missing_core_native))
    if (core_receipt.get("native") or {}).get("status") != "source-built-and-verified":
        raise ValueError("core archive native provenance is not source-built-and-verified")

    package_inputs: dict[str, bytes] = {}
    for name, data in core_files.items():
        folded = name.casefold()
        base = Path(name).name.casefold()
        if folded == "docs/windows-validation-evidence.md":
            continue
        if name in GENERATED or base in {item.casefold() for item in EXPECTED_EXES}:
            continue
        if base.startswith("wincare.launcher.") or base.startswith("wincare.tuilauncher."):
            continue
        package_inputs[name] = data

    # wincare.brand.integration/v1
    brand_asset_names = (
        "design/WinCare-Logo.svg",
        "design/WinCare-Wordmark.svg",
        "design/WinCare-Logo-512.png",
        "design/WinCare.ico",
        "design/BRAND.md",
        "design/WinCare-Brand.manifest.json",
    )
    missing_brand_assets = [name for name in brand_asset_names if name not in package_inputs]
    if missing_brand_assets:
        raise ValueError("release omits canonical brand assets: " + ", ".join(missing_brand_assets))
    brand_manifest_bytes = package_inputs["design/WinCare-Brand.manifest.json"]
    try:
        brand_manifest = json.loads(brand_manifest_bytes.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("WinCare brand manifest is invalid") from error
    if not isinstance(brand_manifest, dict) or brand_manifest.get("schema") != "wincare.brand.identity/v1":
        raise ValueError("unsupported WinCare brand manifest schema")
    if brand_manifest.get("product") != "WinCare":
        raise ValueError("WinCare brand manifest product mismatch")
    icon_sizes = brand_manifest.get("iconSizes")
    if icon_sizes != [16, 20, 24, 32, 40, 48, 64, 128, 256]:
        raise ValueError("WinCare brand manifest icon-size contract mismatch")
    manifest_records = brand_manifest.get("assets")
    if not isinstance(manifest_records, list):
        raise ValueError("WinCare brand manifest asset records are missing")
    record_by_path = {
        str(record.get("path")): record
        for record in manifest_records
        if isinstance(record, dict) and isinstance(record.get("path"), str)
    }
    expected_manifest_assets = set(brand_asset_names) - {"design/WinCare-Brand.manifest.json"}
    if set(record_by_path) != expected_manifest_assets or len(record_by_path) != len(manifest_records):
        raise ValueError("WinCare brand manifest membership mismatch")
    closed_brand_assets = []
    for name in sorted(expected_manifest_assets, key=str.casefold):
        data = package_inputs[name]
        record = record_by_path[name]
        observed_hash = sha256(data)
        if record.get("bytes") != len(data) or str(record.get("sha256", "")).lower() != observed_hash:
            raise ValueError(f"WinCare brand manifest evidence mismatch: {name}")
        closed_brand_assets.append({"path": name, "bytes": len(data), "sha256": observed_hash})
    brand_icon_sha256 = str(record_by_path["design/WinCare.ico"]["sha256"]).lower()
    brand = {
        "schema": "wincare.brand.identity/v1",
        "manifest": {
            "path": "design/WinCare-Brand.manifest.json",
            "sha256": sha256(brand_manifest_bytes),
            "bytes": len(brand_manifest_bytes),
        },
        "iconSizes": icon_sizes,
        "iconSha256": brand_icon_sha256,
        "assets": closed_brand_assets,
    }

    manifest_path = executable_directory / "WinCare.Standalone.build.json"
    manifest_bytes = read_regular_file(manifest_path, 1024 * 1024, "standalone build manifest")
    try:
        standalone_manifest = json.loads(manifest_bytes.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("standalone build manifest is invalid") from error
    manifest_schema_version = standalone_manifest.get("SchemaVersion") if isinstance(standalone_manifest, dict) else None
    if manifest_schema_version not in STANDALONE_SCHEMA_VERSIONS:
        raise ValueError("unsupported standalone build manifest schema")
    if standalone_manifest.get("RuntimeIdentifier") != "win-x64" or standalone_manifest.get("Configuration") != "Release":
        raise ValueError("standalone build manifest configuration mismatch")
    if standalone_manifest.get("SelfContained") is not True or standalone_manifest.get("SingleFile") is not True:
        raise ValueError("standalone build manifest does not attest self-contained single-file outputs")
    manifest_records = standalone_manifest.get("Artifacts")
    if not isinstance(manifest_records, list):
        raise ValueError("standalone build manifest artifacts are missing")
    record_by_name = {
        str(record.get("Name")): record
        for record in manifest_records
        if isinstance(record, dict) and isinstance(record.get("Name"), str)
    }
    if set(record_by_name) != set(EXPECTED_EXES) or len(record_by_name) != len(manifest_records):
        raise ValueError("standalone build manifest artifact membership mismatch")
    if str(standalone_manifest.get("IconSha256", "")).lower() != brand_icon_sha256:
        raise ValueError("standalone build manifest icon identity does not match the canonical brand")
    for name, record in record_by_name.items():
        if record.get("EmbeddedIconVerified") is not True:
            raise ValueError(f"standalone executable icon was not verified: {name}")
        if str(record.get("IconSha256", "")).lower() != brand_icon_sha256:
            raise ValueError(f"standalone executable icon identity mismatch: {name}")
    expected_directory_entries = set(EXPECTED_EXES) | {"WinCare.Standalone.build.json"}
    actual_directory_entries = {entry.name for entry in executable_directory.iterdir()}
    if actual_directory_entries != expected_directory_entries:
        missing = sorted(expected_directory_entries - actual_directory_entries, key=str.casefold)
        extra = sorted(actual_directory_entries - expected_directory_entries, key=str.casefold)
        raise ValueError(f"standalone output membership mismatch: missing={missing} extra={extra}")

    standalone_records: list[dict[str, object]] = []
    for name, subsystem in EXPECTED_EXES.items():
        path = executable_directory / name
        data = read_regular_file(path, 1024 * 1024 * 1024, "standalone executable")
        pe_error = validate_pe(data, subsystem)
        if pe_error:
            raise ValueError(f"invalid standalone executable {name}: {pe_error}")
        record = record_by_name[name]
        observed_hash = sha256(data)
        if (
            str(record.get("Sha256", "")).lower() != observed_hash
            or record.get("Bytes") != len(data)
            or record.get("Subsystem") != subsystem
            or record.get("RuntimeIdentifier") != "win-x64"
            or record.get("SelfTestExitCode") != 0
        ):
            raise ValueError(f"standalone build manifest evidence mismatch: {name}")
        package_inputs[name] = data
        standalone_records.append(
            {"name": name, "sha256": observed_hash, "bytes": len(data), "subsystem": subsystem, "runtimeIdentifier": "win-x64"}
        )
    if len({record["sha256"] for record in standalone_records}) != len(standalone_records):
        raise ValueError("standalone executable hashes must be independent")
    package_inputs["WinCare.Standalone.build.json"] = manifest_bytes

    module_manifest = package_inputs.get("src/WinCare/WinCare.psd1", b"").decode("utf-8", errors="strict")
    match = re.search(r"ModuleVersion\s*=\s*'([^']+)'", module_manifest)
    if not match:
        raise ValueError("module version is missing from the core archive")
    version = match.group(1)
    if standalone_manifest.get("Version") != version:
        raise ValueError("standalone build manifest version does not match the release")
    payload_sha256 = str(standalone_manifest.get("PayloadSha256", "")).lower()
    payload_manifest_sha256 = str(standalone_manifest.get("PayloadManifestSha256", "")).lower()
    if not re.fullmatch(r"[0-9a-f]{64}", payload_sha256) or not re.fullmatch(r"[0-9a-f]{64}", payload_manifest_sha256):
        raise ValueError("standalone build manifest payload identity is invalid")
    created, zip_time, effective_epoch = timestamp()
    package_hash = tree_hash(package_inputs)
    sbom = make_sbom(version, created, package_inputs)
    sbom_bytes = pretty_json(sbom)

    build_receipt = {
        "schema": BUILD_SCHEMA,
        "product": "WinCare",
        "version": version,
        "source": core_receipt.get("source"),
        "packageProfile": "production",
        "packageInputs": {"treeSha256": package_hash, "members": len(package_inputs)},
        "brand": brand,
        "generatedAt": created,
        "native": core_receipt.get("native"),
        "standalone": {
            "schema": f"wincare.standalone.build/v{manifest_schema_version}",
            "runtimeIdentifier": "win-x64",
            "selfContained": True,
            "singleFile": True,
            "manifestSha256": sha256(manifest_bytes),
            "payloadSha256": payload_sha256,
            "payloadManifestSha256": payload_manifest_sha256,
            "artifacts": standalone_records,
        },
        "validation": {
            "sourceAssurance": "verified",
            "archiveIntegrity": "verified-by-v3-verifier",
            "windowsNative": (core_receipt.get("native") or {}).get("status"),
            "standaloneHosts": "source-built-and-verified",
            "brandIdentity": "verified",
        },
        "reproducibility": {
            "ordering": "case-insensitive relative POSIX path",
            "timestampEpoch": effective_epoch,
            "compression": "deflate-9",
            "normalizedModes": "0755 executable content; 0644 other files",
        },
    }
    build_receipt_bytes = pretty_json(build_receipt)
    packaged = dict(package_inputs)
    packaged["SBOM.spdx.json"] = sbom_bytes
    packaged["BUILD-RECEIPT.json"] = build_receipt_bytes
    packaged["RELEASE-MANIFEST.sha256"] = release_manifest(packaged)

    output_directory.mkdir(parents=True, exist_ok=True)
    if any(output_directory.iterdir()):
        raise ValueError("final release output directory must be empty")
    archive_path = output_directory / f"WinCare-{version}.zip"
    write_zip(archive_path, root, packaged, zip_time)
    archive_bytes = archive_path.read_bytes()
    archive_hash = sha256(archive_bytes)

    for name in EXPECTED_EXES:
        (output_directory / name).write_bytes(package_inputs[name])
    (output_directory / "WinCare.Standalone.build.json").write_bytes(manifest_bytes)
    (output_directory / f"WinCare-{version}-BRAND.json").write_bytes(brand_manifest_bytes)
    (output_directory / f"WinCare-{version}-SBOM.spdx.json").write_bytes(sbom_bytes)
    (output_directory / f"WinCare-{version}-BUILD-RECEIPT.json").write_bytes(build_receipt_bytes)
    checksum_path = output_directory / f"WinCare-{version}.zip.sha256"
    checksum_path.write_text(f"{archive_hash}  {archive_path.name}\n", encoding="ascii")

    release_receipt = {
        "schema": RELEASE_SCHEMA,
        "release": version,
        "source": core_receipt.get("source"),
        "packageProfile": "production",
        "brand": brand,
        "archive": {"path": archive_path.name, "sha256": archive_hash, "members": len(packaged), "bytes": len(archive_bytes)},
        "standalone": standalone_records,
        "generatedAt": created,
    }
    release_receipt_path = output_directory / f"WinCare-{version}-release-receipt.json"
    release_receipt_path.write_bytes(pretty_json(release_receipt))

    provenance = {
        "_type": "https://in-toto.io/Statement/v1",
        "subject": [{"name": archive_path.name, "digest": {"sha256": archive_hash}}],
        "predicateType": "https://slsa.dev/provenance/v1",
        "predicate": {
            "buildDefinition": {
                "buildType": "urn:wincare:build:standalone-release:v3",
                "externalParameters": {
                    "version": version,
                    "runtimeIdentifier": "win-x64",
                    "brandManifestSha256": brand["manifest"]["sha256"],
                },
                "resolvedDependencies": [
                    {"uri": f"git+https://github.com/AmirrezaFarnamTaheri/WinCare.git@{(core_receipt.get('source') or {}).get('gitCommit', 'unknown')}", "digest": {"sha256": (core_receipt.get('source') or {}).get('treeSha256', package_hash)}}
                ],
            },
            "runDetails": {
                "builder": {"id": "urn:wincare:builder:standalone-v3"},
                "metadata": {"invocationId": f"wincare-{version}-{archive_hash[:16]}", "startedOn": created, "finishedOn": created},
            },
        },
    }
    provenance_path = output_directory / f"WinCare-{version}.zip.intoto.jsonl"
    provenance_path.write_bytes(canonical_json(provenance))

    result = {
        "schema": "wincare.release.build-result/v3",
        "status": "passed",
        "version": version,
        "brand": brand,
        "archive": str(archive_path),
        "archiveSha256": archive_hash,
        "checksum": str(checksum_path),
        "receipt": str(release_receipt_path),
        "provenance": str(provenance_path),
        "sbom": str(output_directory / f"WinCare-{version}-SBOM.spdx.json"),
        "buildReceipt": str(output_directory / f"WinCare-{version}-BUILD-RECEIPT.json"),
        "standaloneManifest": str(output_directory / "WinCare.Standalone.build.json"),
        "brandManifest": str(output_directory / f"WinCare-{version}-BRAND.json"),
        "standalone": standalone_records,
        "members": len(packaged),
    }
    result_path = output_directory / f"WinCare-{version}-build-result.json"
    result_path.write_bytes(pretty_json(result))
    return result


def main() -> int:
    """Run the command-line entrypoint and return its exit status."""
    parser = argparse.ArgumentParser()
    parser.add_argument("core_archive", type=Path)
    parser.add_argument("executable_directory", type=Path)
    parser.add_argument("output_directory", type=Path)
    args = parser.parse_args()
    result = finalize(args.core_archive, args.executable_directory, args.output_directory)
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(json.dumps({"schema": "wincare.release.build-result/v3", "status": "failed", "error": str(error)}, indent=2))
        raise SystemExit(1)
