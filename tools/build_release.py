#!/usr/bin/env python3
"""Deterministically package and receipt a WinCare release."""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import hashlib
import json
import os
import re
import subprocess
import sys
import zipfile
from pathlib import Path, PurePosixPath
from typing import Iterable

EXCLUDED_PARTS = {".git", "artifacts", "__pycache__", ".pytest_cache", ".mypy_cache"}
EXCLUDED_NAMES = {
    "RELEASE-MANIFEST.sha256", "SBOM.spdx.json", "BUILD-RECEIPT.json",
    ".DS_Store", "Thumbs.db",
}
TEXT_EXECUTABLE_SUFFIXES = {".ps1", ".psm1", ".py", ".cmd", ".bat"}


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_json(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")


def pretty_json(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=False) + "\n").encode("utf-8")


def get_version(root: Path) -> str:
    text = (root / "src/WinCare/WinCare.psd1").read_text(encoding="utf-8")
    match = re.search(r"ModuleVersion\s*=\s*'([^']+)'", text)
    if not match:
        raise RuntimeError("ModuleVersion was not found.")
    return match.group(1)


def get_git(root: Path) -> tuple[str, bool, int]:
    def command(*args: str) -> str:
        return subprocess.check_output(["git", "-C", str(root), *args], text=True).strip()
    try:
        commit = command("rev-parse", "HEAD")
        dirty = bool(command("status", "--porcelain"))
        epoch = int(command("show", "-s", "--format=%ct", "HEAD"))
        return commit, dirty, epoch
    except Exception:
        epoch = int(os.environ.get("SOURCE_DATE_EPOCH", "315532800"))
        return "unavailable", True, epoch


def build_time(epoch: int) -> tuple[str, tuple[int, int, int, int, int, int]]:
    epoch = max(epoch, 315532800)  # ZIP minimum: 1980-01-01 UTC
    value = dt.datetime.fromtimestamp(epoch, dt.timezone.utc).replace(microsecond=0)
    # ZIP stores local-naive fields; UTC tuple is used consistently.
    return value.isoformat().replace("+00:00", "Z"), (value.year, value.month, value.day, value.hour, value.minute, value.second)


def iter_source_files(root: Path) -> Iterable[Path]:
    for path in sorted(root.rglob("*"), key=lambda item: item.as_posix().casefold()):
        if not path.is_file() or any(part in EXCLUDED_PARTS for part in path.relative_to(root).parts):
            continue
        relative = path.relative_to(root)
        if path.name in EXCLUDED_NAMES:
            continue
        if any(part.startswith("WinCare-release-") for part in relative.parts):
            continue
        yield path


def source_files(root: Path) -> dict[str, bytes]:
    result: dict[str, bytes] = {}
    casefolded: set[str] = set()
    for path in iter_source_files(root):
        relative = path.relative_to(root).as_posix()
        pure = PurePosixPath(relative)
        if pure.is_absolute() or ".." in pure.parts:
            raise RuntimeError(f"Unsafe source path: {relative}")
        folded = relative.casefold()
        if folded in casefolded:
            raise RuntimeError(f"Case-colliding source path: {relative}")
        casefolded.add(folded)
        if path.is_symlink():
            raise RuntimeError(f"Symbolic links are not package members: {relative}")
        result[relative] = path.read_bytes()
    return result


def tree_hash(files: dict[str, bytes]) -> str:
    digest = hashlib.sha256()
    for name in sorted(files, key=str.casefold):
        digest.update(name.encode("utf-8")); digest.update(b"\0")
        digest.update(bytes.fromhex(sha256_bytes(files[name]))); digest.update(b"\0")
    return digest.hexdigest()


def load_json(path: Path, default: object) -> object:
    return json.loads(path.read_text(encoding="utf-8")) if path.is_file() else default


def spdx_id(value: str) -> str:
    clean = re.sub(r"[^A-Za-z0-9.-]", "-", value)
    return clean if clean and clean[0].isalnum() else "Item-" + clean


def normalized_license(text: str) -> str:
    if text == "MIT": return "MIT"
    if text == "Apache-2.0": return "Apache-2.0"
    if text.startswith("BSD-3-Clause"): return "BSD-3-Clause"
    if text.startswith("GPL-family"): return "NOASSERTION"
    return "NOASSERTION"


def make_sbom(version: str, created: str, files: dict[str, bytes], inventory: list[dict], ledger: list[dict]) -> dict:
    win_package = "SPDXRef-Package-WinCare"
    doc_namespace = f"https://wincare.invalid/spdx/{version}/{tree_hash(files)}"
    file_entries = []
    relationships = [{"spdxElementId": "SPDXRef-DOCUMENT", "relationshipType": "DESCRIBES", "relatedSpdxElement": win_package}]
    for index, name in enumerate(sorted(files, key=str.casefold), 1):
        file_id = f"SPDXRef-File-{index:05d}"
        file_entries.append({
            "SPDXID": file_id,
            "fileName": "./" + name,
            "checksums": [{"algorithm": "SHA256", "checksumValue": sha256_bytes(files[name])}],
            "licenseConcluded": "NOASSERTION",
            "copyrightText": "NOASSERTION",
        })
        relationships.append({"spdxElementId": win_package, "relationshipType": "CONTAINS", "relatedSpdxElement": file_id})
    license_by_donor: dict[str, str] = {}
    for row in ledger:
        license_by_donor.setdefault(row["donor"], row["license_note"])
    packages = [{
        "SPDXID": win_package,
        "name": "WinCare",
        "versionInfo": version,
        "downloadLocation": "NOASSERTION",
        "filesAnalyzed": True,
        "licenseConcluded": "MIT",
        "licenseDeclared": "MIT",
        "copyrightText": "Copyright 2026 WinCare Project",
    }]
    for item in inventory:
        donor = item["donor_id"]
        package_id = "SPDXRef-Evidence-" + spdx_id(donor)
        packages.append({
            "SPDXID": package_id,
            "name": item["archive"],
            "versionInfo": "evidence-snapshot",
            "downloadLocation": "NOASSERTION",
            "filesAnalyzed": False,
            "licenseConcluded": "NOASSERTION",
            "licenseDeclared": normalized_license(license_by_donor.get(donor, "")),
            "copyrightText": "NOASSERTION",
            "checksums": [{"algorithm": "SHA256", "checksumValue": item["archive_sha256"]}],
            "comment": "Evidence-only convergence source. Donor source and binaries are not bundled in WinCare.",
        })
        relationships.append({
            "spdxElementId": win_package,
            "relationshipType": "OTHER",
            "relatedSpdxElement": package_id,
            "comment": "WinCare contains independently selected, adapted, rederived, guardrail, or reference decisions documented in the adoption matrix; this is not a bundled dependency.",
        })
    return {
        "spdxVersion": "SPDX-2.3",
        "dataLicense": "CC0-1.0",
        "SPDXID": "SPDXRef-DOCUMENT",
        "name": f"WinCare-{version}",
        "documentNamespace": doc_namespace,
        "creationInfo": {"created": created, "creators": ["Tool: WinCare deterministic release builder"]},
        "packages": packages,
        "files": file_entries,
        "relationships": relationships,
    }


def manifest(files: dict[str, bytes]) -> bytes:
    lines = [f"{sha256_bytes(files[name])}  {name}" for name in sorted(files, key=str.casefold)]
    return ("\n".join(lines) + "\n").encode("ascii")


def zip_mode(name: str) -> int:
    return 0o755 if Path(name).suffix.lower() in TEXT_EXECUTABLE_SUFFIXES else 0o644


def write_zip(path: Path, top: str, files: dict[str, bytes], timestamp: tuple[int, int, int, int, int, int]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.unlink(missing_ok=True)
    with zipfile.ZipFile(temp, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9, allowZip64=True) as archive:
        for name in sorted(files, key=str.casefold):
            info = zipfile.ZipInfo(f"{top}/{name}", date_time=timestamp)
            info.create_system = 3
            info.external_attr = (zip_mode(name) & 0xFFFF) << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            info.flag_bits |= 0x800
            archive.writestr(info, files[name], compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    os.replace(temp, path)


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", type=Path, default=Path.cwd())
    parser.add_argument("--output-directory", type=Path, default=Path("artifacts"))
    parser.add_argument("--allow-dirty", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()
    output = args.output_directory.resolve() if args.output_directory.is_absolute() else (root / args.output_directory).resolve()
    version = get_version(root)
    commit, dirty, epoch = get_git(root)
    if dirty and not args.allow_dirty:
        raise RuntimeError("Refusing a release from a dirty Git working tree. Commit or use --allow-dirty for a non-promotable development artifact.")
    created, zip_timestamp = build_time(int(os.environ.get("SOURCE_DATE_EPOCH", epoch)))

    base = source_files(root)
    source_hash = tree_hash(base)
    convergence = load_json(root / "docs/convergence/convergence-validation.json", {})
    source_validation = load_json(root / "docs/convergence/source-validation.json", {})
    v5_validation = load_json(root / "docs/convergence/v5-completeness-validation.json", {})
    inventory = load_json(root / "docs/convergence/donor-inventory.json", [])
    ledger = read_csv(root / "docs/convergence/adoption-matrix.csv")

    sbom = make_sbom(version, created, base, inventory, ledger)
    packaged = dict(base)
    packaged["SBOM.spdx.json"] = pretty_json(sbom)
    build_receipt = {
        "schema": "wincare.build.receipt/v1",
        "product": "WinCare",
        "version": version,
        "source": {"gitCommit": commit, "dirty": dirty, "treeSha256": source_hash},
        "generatedAt": created,
        "evidence": {
            "donors": len(inventory),
            "semanticRecords": len(ledger),
            "classifiedSurfaces": convergence.get("counts", {}).get("classifiedSurfaces", 0),
            "adoptionMatrixSha256": convergence.get("evidence", {}).get("adoptionMatrixSha256"),
            "surfaceMatrixSha256": convergence.get("evidence", {}).get("surfaceMatrixSha256"),
            "donorInventorySha256": convergence.get("evidence", {}).get("donorInventorySha256"),
        },
        "validation": {
            "source": source_validation.get("status", "not-recorded"),
            "convergence": convergence.get("status", "not-recorded"),
            "v5Completeness": v5_validation.get("status", "not-recorded"),
            "windowsNative": "pending-separate-windows-evidence",
        },
        "reproducibility": {
            "ordering": "case-insensitive relative POSIX path",
            "timestampEpoch": int(os.environ.get("SOURCE_DATE_EPOCH", epoch)),
            "zipTimestamp": created,
            "compression": "deflate-9",
            "normalizedModes": "0755 scripts; 0644 other files",
        },
    }
    packaged["BUILD-RECEIPT.json"] = pretty_json(build_receipt)
    packaged["RELEASE-MANIFEST.sha256"] = manifest(packaged)

    top = f"WinCare-{version}"
    archive_path = output / f"{top}.zip"
    write_zip(archive_path, top, packaged, zip_timestamp)
    archive_hash = sha256_file(archive_path)
    checksum_path = archive_path.with_suffix(archive_path.suffix + ".sha256")
    checksum_path.write_text(f"{archive_hash}  {archive_path.name}\n", encoding="ascii")

    receipt = {
        "schema": "wincare.release.receipt/v1",
        "release": version,
        "source": {"gitCommit": commit, "dirty": dirty, "treeSha256": source_hash},
        "archive": {
            "path": archive_path.name,
            "sha256": archive_hash,
            "members": len(packaged),
            "uncompressedBytes": sum(len(value) for value in packaged.values()),
        },
        "donors": [{
            "id": item["donor_id"], "name": item["archive"], "sha256": item["archive_sha256"],
            "members": item["member_count"], "bundled": False,
        } for item in inventory],
        "coverage": {
            "classifiedFiles": convergence.get("counts", {}).get("classifiedSurfaces", 0),
            "semanticContracts": convergence.get("counts", {}).get("semanticRecords", 0),
            "compositionGroups": convergence.get("counts", {}).get("compositionGroups", 0),
            "targetNodes": convergence.get("counts", {}).get("targetNodes", 0),
            "testNodes": convergence.get("counts", {}).get("testNodes", 0),
            "unresolvedHighRiskGaps": 0 if convergence.get("status") == "passed" else None,
        },
        "evidenceSnapshot": convergence.get("evidence", {}),
        "validation": [
            {"name": "source-cross-file", "status": source_validation.get("status", "not-recorded")},
            {"name": "peer-convergence", "status": convergence.get("status", "not-recorded")},
            {"name": "v5-gui-completeness", "status": v5_validation.get("status", "not-recorded")},
            {"name": "archive-integrity", "status": "pending-independent-verifier"},
            {"name": "windows-native", "status": "pending"},
        ],
        "limitations": [
            "PowerShell AST, Pester, PSScriptAnalyzer and Windows-native provider execution require the included Windows validation gate.",
            "The build environment does not establish WPF rendering, UAC, reboot, WUA, WDAC, AppX, Registry, Defender, servicing, or interactive GUI/TUI behavior.",
        ],
        "generatedAt": created,
    }
    receipt_path = output / f"{top}-release-receipt.json"
    receipt_path.write_bytes(pretty_json(receipt))

    provenance = {
        "_type": "https://in-toto.io/Statement/v1",
        "subject": [{"name": archive_path.name, "digest": {"sha256": archive_hash}}],
        "predicateType": "https://slsa.dev/provenance/v1",
        "predicate": {
            "buildDefinition": {
                "buildType": "https://wincare.invalid/build/deterministic-zip/v1",
                "externalParameters": {"version": version},
                "internalParameters": {"sourceDateEpoch": int(os.environ.get("SOURCE_DATE_EPOCH", epoch))},
                "resolvedDependencies": [{"uri": f"git+local@{commit}", "digest": {"sha256": source_hash}}],
            },
            "runDetails": {
                "builder": {"id": "https://wincare.invalid/builder/python-standard-library/v1"},
                "metadata": {"invocationId": f"wincare-{version}-{source_hash[:16]}", "startedOn": created, "finishedOn": created},
                "byproducts": [
                    {"name": receipt_path.name, "digest": {"sha256": sha256_file(receipt_path)}},
                    {"name": checksum_path.name, "digest": {"sha256": sha256_file(checksum_path)}},
                ],
            },
        },
    }
    provenance_path = output / f"{archive_path.name}.intoto.jsonl"
    provenance_path.write_bytes(canonical_json(provenance))

    summary = {
        "schema": "wincare.release.build-result/v1",
        "status": "passed",
        "version": version,
        "archive": str(archive_path),
        "archiveSha256": archive_hash,
        "checksum": str(checksum_path),
        "receipt": str(receipt_path),
        "provenance": str(provenance_path),
        "members": len(packaged),
        "sourceTreeSha256": source_hash,
        "gitCommit": commit,
        "dirty": dirty,
    }
    summary_path = output / f"{top}-build-result.json"
    summary_path.write_bytes(pretty_json(summary))
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(json.dumps({"status": "failed", "error": str(error)}, indent=2), file=sys.stderr)
        raise SystemExit(1)
