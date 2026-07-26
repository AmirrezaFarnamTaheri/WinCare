#!/usr/bin/env python3
"""Deterministically package and receipt a WinCare release."""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import stat
import subprocess
import threading
import sys
import zipfile
from pathlib import Path, PurePosixPath
from typing import Iterable

try:
    from .validate_source_references import validate as validate_source_references
except ImportError:
    from validate_source_references import validate as validate_source_references

EXCLUDED_PARTS = {".git", "artifacts", "scratch", "bin", "evidence", ".code-review-graph", "__pycache__", ".pytest_cache", ".mypy_cache"}
EXCLUDED_NAMES = {
    "RELEASE-MANIFEST.sha256", "SBOM.spdx.json", "BUILD-RECEIPT.json",
    "WinCare.exe", "WinCare-GUI.exe", "WinCare-TUI.exe",
    ".DS_Store", "Thumbs.db",
}
TEXT_EXECUTABLE_SUFFIXES = {".ps1", ".psm1", ".py", ".cmd", ".bat"}
PRODUCTION_ROOT_FILES = {
    "CHANGELOG.md", "Install-WinCare.ps1", "LICENSE", "THIRD-PARTY-NOTICES.md",
    "README.md", "SECURITY.md", "Uninstall-WinCare.ps1", "VALIDATION.md",
    "WinCare-GUI.ps1", "WinCare-TUI.ps1", "WinCare.ps1",
    "run-wincare-gui.cmd", "run-wincare.cmd",
}
PRODUCTION_CONFIG_FILES = {
    "config/multicloud-targets.json",
    "config/remediation-catalog.json",
    "config/wasi-marketplace.json",
}
PRODUCTION_DOC_FILES = {
    "docs/Architecture.md", "docs/CAPABILITY-MATRIX.md", "docs/Compatibility.md",
    "docs/GUI.md", "docs/Safety.md", "docs/Testing.md", "docs/Advanced-Capabilities.md",
    "docs/Source-Reference-Model.md",
}
MAX_SOURCE_MEMBER_BYTES = 64 * 1024 * 1024
MAX_SOURCE_TOTAL_BYTES = 256 * 1024 * 1024
MAX_GIT_OUTPUT_BYTES = 8 * 1024 * 1024
GIT_TIMEOUT_SECONDS = 60


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path, maximum_bytes: int = 4 * 1024 * 1024 * 1024) -> str:
    before = path.stat(follow_symlinks=False)
    if not stat.S_ISREG(before.st_mode) or path.is_symlink():
        raise RuntimeError(f"Hash input is not a regular file: {path}")
    if before.st_size < 0 or before.st_size > maximum_bytes:
        raise RuntimeError(f"Hash input size is outside 0..{maximum_bytes} bytes: {path}")
    digest = hashlib.sha256()
    observed = 0
    with path.open("rb", buffering=0) as stream:
        opened = os.fstat(stream.fileno())
        if not stat.S_ISREG(opened.st_mode) or (opened.st_dev, opened.st_ino, opened.st_size) != (before.st_dev, before.st_ino, before.st_size):
            raise RuntimeError(f"Hash input changed before reading: {path}")
        while True:
            chunk = stream.read(1024 * 1024)
            if not chunk:
                break
            observed += len(chunk)
            if observed > maximum_bytes:
                raise RuntimeError(f"Hash input exceeded {maximum_bytes} bytes while reading: {path}")
            digest.update(chunk)
        finished = os.fstat(stream.fileno())
    after = path.stat(follow_symlinks=False)
    identity = lambda item: (item.st_mode, item.st_dev, item.st_ino, item.st_size, item.st_mtime_ns)
    if path.is_symlink() or observed != before.st_size or identity(opened) != identity(finished) or identity(before) != identity(after):
        raise RuntimeError(f"Hash input changed while reading: {path}")
    return digest.hexdigest()


def canonical_json(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")


def pretty_json(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=False) + "\n").encode("utf-8")


def read_bounded_file(path: Path, maximum_bytes: int) -> bytes:
    before = path.stat(follow_symlinks=False)
    if not stat.S_ISREG(before.st_mode) or path.is_symlink():
        raise RuntimeError(f"Release input is not a regular file: {path}")
    if before.st_size < 0 or before.st_size > maximum_bytes:
        raise RuntimeError(f"Release input exceeds {maximum_bytes} bytes: {path}")
    with path.open("rb", buffering=0) as stream:
        opened = os.fstat(stream.fileno())
        if not stat.S_ISREG(opened.st_mode) or (opened.st_dev, opened.st_ino, opened.st_size) != (before.st_dev, before.st_ino, before.st_size):
            raise RuntimeError(f"Release input changed before reading: {path}")
        data = bytearray()
        while len(data) < before.st_size:
            chunk = stream.read(min(1024 * 1024, before.st_size - len(data)))
            if not chunk:
                raise RuntimeError(f"Release input ended before its declared length: {path}")
            data.extend(chunk)
        if stream.read(1):
            raise RuntimeError(f"Release input grew while reading: {path}")
        finished = os.fstat(stream.fileno())
    after = path.stat(follow_symlinks=False)
    identity = lambda item: (item.st_mode, item.st_dev, item.st_ino, item.st_size, item.st_mtime_ns)
    if path.is_symlink() or identity(opened) != identity(finished) or identity(before) != identity(after):
        raise RuntimeError(f"Release input changed while reading: {path}")
    return bytes(data)


def get_version(root: Path) -> str:
    text = read_bounded_file(root / "src/WinCare/WinCare.psd1", 1024 * 1024).decode("utf-8")
    match = re.search(r"ModuleVersion\s*=\s*'([^']+)'", text)
    if not match:
        raise RuntimeError("ModuleVersion was not found.")
    return match.group(1)


def run_bounded_command(command: list[str], maximum_bytes: int, timeout_seconds: int) -> str:
    process = subprocess.Popen(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        stdin=subprocess.DEVNULL,
        env={**os.environ, "GIT_PAGER": "cat", "PAGER": "cat"},
    )
    output = bytearray()
    overflow = threading.Event()

    def collect() -> None:
        assert process.stdout is not None
        while True:
            chunk = process.stdout.read(65536)
            if not chunk:
                break
            if len(output) + len(chunk) > maximum_bytes:
                overflow.set()
                process.kill()
                break
            output.extend(chunk)

    reader = threading.Thread(target=collect, name="wincare-release-command-output", daemon=True)
    reader.start()
    try:
        return_code = process.wait(timeout=timeout_seconds)
    except subprocess.TimeoutExpired as error:
        process.kill()
        process.wait()
        reader.join(timeout=5)
        raise RuntimeError(f"Command timed out after {timeout_seconds} seconds: {command[0]}") from error
    reader.join(timeout=5)
    if reader.is_alive():
        process.kill()
        raise RuntimeError(f"Command output reader did not terminate: {command[0]}")
    if overflow.is_set():
        raise RuntimeError(f"Command output exceeded {maximum_bytes} bytes: {command[0]}")
    rendered = bytes(output).decode("utf-8", errors="strict").strip()
    if return_code != 0:
        raise RuntimeError(f"Command failed with exit code {return_code}: {rendered[:2000]}")
    return rendered


def get_git(root: Path) -> tuple[str, bool, int]:
    def command(*args: str) -> str:
        return run_bounded_command(
            ["git", "-C", str(root), *args],
            maximum_bytes=MAX_GIT_OUTPUT_BYTES,
            timeout_seconds=GIT_TIMEOUT_SECONDS,
        )
    try:
        commit = command("rev-parse", "HEAD")
        dirty = bool(command("status", "--porcelain", "--untracked-files=all"))
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


def is_production_member(relative: str) -> bool:
    if relative in PRODUCTION_ROOT_FILES or relative in PRODUCTION_CONFIG_FILES or relative in PRODUCTION_DOC_FILES:
        return True
    if relative.startswith("src/WinCare/") and not relative.startswith("src/WinCare/Native/"):
        return True
    return False


def source_files(root: Path, profile: str = "development") -> dict[str, bytes]:
    if profile not in {"development", "production"}:
        raise RuntimeError(f"Unsupported package profile: {profile}")
    result: dict[str, bytes] = {}
    casefolded: set[str] = set()
    total_bytes = 0
    for path in iter_source_files(root):
        relative = path.relative_to(root).as_posix()
        if profile == "production" and not is_production_member(relative):
            continue
        pure = PurePosixPath(relative)
        if pure.is_absolute() or ".." in pure.parts:
            raise RuntimeError(f"Unsafe source path: {relative}")
        folded = relative.casefold()
        if folded in casefolded:
            raise RuntimeError(f"Case-colliding source path: {relative}")
        casefolded.add(folded)
        data = read_bounded_file(path, MAX_SOURCE_MEMBER_BYTES)
        total_bytes += len(data)
        if total_bytes > MAX_SOURCE_TOTAL_BYTES:
            raise RuntimeError(
                f"Release source payload exceeds the {MAX_SOURCE_TOTAL_BYTES}-byte ceiling."
            )
        result[relative] = data
    if profile == "production":
        required = PRODUCTION_ROOT_FILES | PRODUCTION_CONFIG_FILES | {
            "src/WinCare/WinCare.psd1", "src/WinCare/WinCare.psm1",
        }
        missing = sorted(required - set(result), key=str.casefold)
        if missing:
            raise RuntimeError("Production package inputs are missing: " + ", ".join(missing))
        forbidden_prefixes = ("tests/", "tools/", "analysis/", ".github/", "src/WinCare/Native/")
        forbidden = sorted(name for name in result if name.startswith(forbidden_prefixes))
        if forbidden:
            raise RuntimeError("Production package contains development-only members: " + ", ".join(forbidden[:20]))
    return result


def tree_hash(files: dict[str, bytes]) -> str:
    digest = hashlib.sha256()
    for name in sorted(files, key=str.casefold):
        digest.update(name.encode("utf-8")); digest.update(b"\0")
        digest.update(bytes.fromhex(sha256_bytes(files[name]))); digest.update(b"\0")
    return digest.hexdigest()



def make_sbom(version: str, created: str, files: dict[str, bytes]) -> dict:
    win_package = "SPDXRef-Package-WinCare"
    content_hash = tree_hash(files)
    doc_namespace = f"urn:wincare:spdx:{version}:{content_hash}"
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
    packages = [{
        "SPDXID": win_package,
        "name": "WinCare",
        "versionInfo": version,
        "primaryPackagePurpose": "APPLICATION",
        "downloadLocation": "NOASSERTION",
        "filesAnalyzed": True,
        "licenseConcluded": "Apache-2.0",
        "licenseDeclared": "Apache-2.0",
        "copyrightText": "Copyright 2026 WinCare Project",
        "externalRefs": [{
            "referenceCategory": "PACKAGE-MANAGER",
            "referenceType": "purl",
            "referenceLocator": f"pkg:generic/wincare@{version}",
        }],
        "checksums": [{"algorithm": "SHA256", "checksumValue": content_hash}],
        "comment": "This SBOM describes shipped WinCare source, configuration, documentation, and source-built native artifacts only. Historical research inputs are not shipped as runtime dependencies.",
    }]
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



NATIVE_ARTIFACT_NAMES = {
    "WinCare.Native.dll",
    "WinCare.Launcher.exe", "WinCare.Launcher.dll",
    "WinCare.Launcher.deps.json", "WinCare.Launcher.runtimeconfig.json",
    "WinCare.TuiLauncher.exe", "WinCare.TuiLauncher.dll",
    "WinCare.TuiLauncher.deps.json", "WinCare.TuiLauncher.runtimeconfig.json",
}


def expected_native_source_names(root: Path) -> set[str]:
    native_root = root / "src/WinCare/Native"
    if not native_root.is_dir() or native_root.is_symlink():
        raise RuntimeError("Native source directory is missing or unsafe.")
    entries = list(native_root.iterdir())
    if len(entries) > 63:
        raise RuntimeError("Native source directory exceeds 63 entries plus global.json.")
    expected = {"global.json"}
    for path in entries:
        if path.is_symlink() or not path.is_file() or path.suffix.lower() not in {".cs", ".csproj", ".ps1"}:
            raise RuntimeError(f"Unsupported native source entry: {path.name}")
        expected.add(path.relative_to(root).as_posix())
    return expected


def checked_regular_file(path: Path, root: Path, maximum_bytes: int, label: str) -> bytes:
    try:
        resolved_root = root.resolve(strict=True)
        resolved = path.resolve(strict=True)
    except OSError as error:
        raise RuntimeError(f"{label} is missing or inaccessible: {path}") from error
    try:
        resolved.relative_to(resolved_root)
    except ValueError as error:
        raise RuntimeError(f"{label} escapes its trusted root: {path}") from error
    current = path
    while current != root and current != current.parent:
        if current.is_symlink():
            raise RuntimeError(f"{label} traverses a symbolic link: {path}")
        current = current.parent
    if not path.is_file() or path.is_symlink():
        raise RuntimeError(f"{label} is not a regular file: {path}")
    size = path.stat(follow_symlinks=False).st_size
    if size <= 0 or size > maximum_bytes:
        raise RuntimeError(f"{label} size is outside the permitted range: {path}")
    return read_bounded_file(path, maximum_bytes)


def load_native_artifacts(directory: Path | None, root: Path, require_provenance: bool) -> tuple[dict[str, bytes], dict]:
    if directory is None:
        if require_provenance:
            raise RuntimeError("Production packages require source-built native artifacts.")
        return {}, {"status": "not-included", "reason": "No native artifact directory was supplied."}
    root = root.resolve()
    directory = directory.resolve()
    if not directory.is_dir() or directory.is_symlink():
        raise RuntimeError(f"Native artifact directory is missing or unsafe: {directory}")
    manifest_path = directory / "WinCare.Native.build.json"
    manifest_bytes = checked_regular_file(manifest_path, directory, 1024 * 1024, "Native build manifest")
    try:
        manifest_value = json.loads(manifest_bytes.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise RuntimeError("Native build manifest is not valid UTF-8 JSON.") from error
    if not isinstance(manifest_value, dict) or manifest_value.get("SchemaVersion") != 2:
        raise RuntimeError("Unsupported native build manifest schema; schema 2 provenance is required.")
    if manifest_value.get("Configuration") != "Release":
        raise RuntimeError("Native build manifest must record Release configuration.")
    if manifest_value.get("TargetFramework") != "net8.0-windows":
        raise RuntimeError("Native build manifest target framework is not net8.0-windows.")
    dotnet_version = str(manifest_value.get("DotNetVersion", ""))
    if not re.fullmatch(r"8\.0\.\d{3}", dotnet_version):
        raise RuntimeError("Native build manifest contains an invalid .NET SDK version.")
    dotnet_host_sha = str(manifest_value.get("DotNetHostSha256", "")).lower()
    if not re.fullmatch(r"[0-9a-f]{64}", dotnet_host_sha):
        raise RuntimeError("Native build manifest contains an invalid dotnet host hash.")

    sources = manifest_value.get("Sources")
    if not isinstance(sources, list) or not sources or len(sources) > 64:
        raise RuntimeError("Native build manifest source provenance count is outside 1..64.")
    if manifest_value.get("SourceCount") != len(sources):
        raise RuntimeError("Native build manifest source count does not match its records.")
    expected_sources = expected_native_source_names(root)
    source_files: dict[str, bytes] = {}
    source_names: set[str] = set()
    total_source_bytes = 0
    source_metadata_bytes = 0
    for record in sources:
        if not isinstance(record, dict):
            raise RuntimeError("Malformed native source record.")
        relative = str(record.get("Path", "")).replace("\\", "/")
        pure = PurePosixPath(relative)
        if pure.is_absolute() or ".." in pure.parts or not relative:
            raise RuntimeError(f"Unsafe native source path: {relative}")
        folded = relative.casefold()
        if folded in source_names:
            raise RuntimeError(f"Duplicate native source path: {relative}")
        source_names.add(folded)
        path = root / relative
        data = checked_regular_file(path, root, 16 * 1024 * 1024, "Native source")
        expected_hash = str(record.get("Sha256", "")).lower()
        expected_bytes = record.get("Bytes")
        if not re.fullmatch(r"[0-9a-f]{64}", expected_hash):
            raise RuntimeError(f"Native source record contains an invalid hash: {relative}")
        try:
            expected_size = int(expected_bytes)
        except (TypeError, ValueError) as error:
            raise RuntimeError(f"Native source record contains an invalid size: {relative}") from error
        if sha256_bytes(data) != expected_hash or len(data) != expected_size:
            raise RuntimeError(f"Native source provenance mismatch: {relative}")
        total_source_bytes += len(data)
        source_metadata_bytes += len(relative.encode("utf-8")) + 32 + 2
        if total_source_bytes > 64 * 1024 * 1024 or source_metadata_bytes > 1024 * 1024:
            raise RuntimeError("Native source provenance exceeds aggregate byte or metadata limits.")
        source_files[relative] = data
    if manifest_value.get("TotalSourceBytes") != total_source_bytes or manifest_value.get("SourceMetadataBytes") != source_metadata_bytes:
        raise RuntimeError("Native source aggregate provenance does not match the build manifest.")
    observed_sources = set(source_files)
    if observed_sources != expected_sources:
        missing = sorted(expected_sources - observed_sources, key=str.casefold)
        extra = sorted(observed_sources - expected_sources, key=str.casefold)
        raise RuntimeError(f"Native source membership mismatch: missing={missing} extra={extra}")
    observed_source_hash = tree_hash(source_files)
    expected_tree_hash = str(manifest_value.get("SourceTreeSha256", "")).lower()
    if not re.fullmatch(r"[0-9a-f]{64}", expected_tree_hash) or observed_source_hash != expected_tree_hash:
        raise RuntimeError("Native source tree hash does not match the build manifest.")

    records = manifest_value.get("Artifacts")
    if not isinstance(records, list) or not records or len(records) > len(NATIVE_ARTIFACT_NAMES):
        raise RuntimeError("Native build manifest artifact count is outside the permitted range.")
    native: dict[str, bytes] = {}
    observed: list[dict] = []
    artifact_names: set[str] = set()
    total_artifact_bytes = 0
    for record in records:
        if not isinstance(record, dict):
            raise RuntimeError("Malformed native artifact record.")
        recorded_path = str(record.get("Path", ""))
        name = Path(recorded_path).name
        if recorded_path != name or name not in NATIVE_ARTIFACT_NAMES:
            raise RuntimeError(f"Unexpected native artifact: {recorded_path}")
        folded = name.casefold()
        if folded in artifact_names:
            raise RuntimeError(f"Duplicate native artifact record: {name}")
        artifact_names.add(folded)
        path = directory / name
        data = checked_regular_file(path, directory, 256 * 1024 * 1024, "Native artifact")
        expected = str(record.get("Sha256", "")).lower()
        try:
            expected_size = int(record.get("Bytes", -1))
        except (TypeError, ValueError) as error:
            raise RuntimeError(f"Native artifact record contains an invalid size: {name}") from error
        actual = sha256_bytes(data)
        if not re.fullmatch(r"[0-9a-f]{64}", expected) or actual != expected or len(data) != expected_size:
            raise RuntimeError(f"Native artifact provenance mismatch: {name}")
        total_artifact_bytes += len(data)
        if total_artifact_bytes > 1024 * 1024 * 1024:
            raise RuntimeError("Native artifacts exceed the 1 GiB aggregate limit.")
        native[f"bin/{name}"] = data
        observed.append({"name": name, "sha256": actual, "bytes": len(data)})

    expected_directory_names = {item["name"] for item in observed} | {"WinCare.Native.build.json"}
    actual_entries = {item.name for item in directory.iterdir()}
    if actual_entries != expected_directory_names:
        missing = sorted(expected_directory_names - actual_entries, key=str.casefold)
        extra = sorted(actual_entries - expected_directory_names, key=str.casefold)
        raise RuntimeError(f"Native artifact directory membership mismatch: missing={missing} extra={extra}")
    required = {"WinCare.Native.dll", "WinCare.Launcher.exe", "WinCare.TuiLauncher.exe"}
    missing = sorted(required - {item["name"] for item in observed})
    if missing:
        raise RuntimeError("Required native artifacts are missing: " + ", ".join(missing))
    canonical_manifest = canonical_json(manifest_value)
    native["bin/WinCare.Native.build.json"] = canonical_manifest
    return native, {
        "status": "source-built-and-verified",
        "manifestSha256": sha256_bytes(canonical_manifest),
        "dotNetVersion": dotnet_version,
        "targetFramework": manifest_value.get("TargetFramework"),
        "sourceTreeSha256": observed_source_hash,
        "sourceMembers": len(source_files),
        "dotNetHostSha256": dotnet_host_sha,
        "artifacts": observed,
    }


def assert_empty_output_directory(output: Path, root: Path) -> None:
    resolved_root = root.resolve(strict=True)
    resolved_output = output.resolve(strict=False)
    if resolved_output == resolved_root or resolved_output == Path(resolved_output.anchor):
        raise RuntimeError(f"Unsafe release output directory: {resolved_output}")
    cursor = resolved_output
    while not cursor.exists() and cursor != cursor.parent:
        cursor = cursor.parent
    if cursor.exists() and cursor.is_symlink():
        raise RuntimeError(f"Release output ancestry is a symbolic link: {cursor}")
    if resolved_output.exists():
        if not resolved_output.is_dir() or resolved_output.is_symlink():
            raise RuntimeError(f"Release output is not a regular directory: {resolved_output}")
        if any(resolved_output.iterdir()):
            raise RuntimeError(f"Release output must be absent or empty: {resolved_output}")
    else:
        resolved_output.mkdir(parents=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", type=Path, default=Path.cwd())
    parser.add_argument("--output-directory", type=Path, default=Path("artifacts"))
    parser.add_argument("--allow-dirty", action="store_true")
    parser.add_argument("--native-directory", type=Path)
    parser.add_argument("--profile", choices=("development", "production"), default="development")
    args = parser.parse_args()

    root = args.root.resolve()
    source_reference_validation = validate_source_references(root)
    if source_reference_validation["status"] != "passed":
        details = "; ".join(source_reference_validation["errors"][:10])
        raise RuntimeError(f"Source-reference validation failed: {details}")
    output = args.output_directory.resolve() if args.output_directory.is_absolute() else (root / args.output_directory).resolve()
    assert_empty_output_directory(output, root)
    version = get_version(root)
    commit, dirty, epoch = get_git(root)
    if dirty and not args.allow_dirty:
        raise RuntimeError(
            "Refusing a release from a dirty Git working tree. Commit or use --allow-dirty "
            "for a non-promotable development artifact."
        )
    created, zip_timestamp = build_time(int(os.environ.get("SOURCE_DATE_EPOCH", epoch)))

    full_source = source_files(root, "development")
    source_hash = tree_hash(full_source)
    base = source_files(root, args.profile)
    native_directory = None
    if args.native_directory is not None:
        native_directory = args.native_directory if args.native_directory.is_absolute() else (root / args.native_directory)
    native_files, native_evidence = load_native_artifacts(
        native_directory,
        root,
        require_provenance=args.profile == "production",
    )

    package_inputs = dict(base)
    for name, data in native_files.items():
        if name in package_inputs:
            raise RuntimeError(f"Native artifact collides with source member: {name}")
        package_inputs[name] = data
    package_input_hash = tree_hash(package_inputs)

    sbom = make_sbom(version, created, package_inputs)
    packaged = dict(package_inputs)
    packaged["SBOM.spdx.json"] = pretty_json(sbom)
    build_receipt = {
        "schema": "wincare.build.receipt/v2",
        "product": "WinCare",
        "version": version,
        "source": {
            "gitCommit": commit,
            "dirty": dirty,
            "treeSha256": source_hash,
            "members": len(full_source),
        },
        "packageProfile": args.profile,
        "packageInputs": {
            "treeSha256": package_input_hash,
            "members": len(package_inputs),
        },
        "generatedAt": created,
        "validation": {
            "sourceAssurance": "required-before-promotion",
            "windowsNative": native_evidence["status"],
            "archiveIntegrity": "pending-independent-verifier",
            "sourceReferences": "verified",
        },
        "native": native_evidence,
        "reproducibility": {
            "ordering": "case-insensitive relative POSIX path",
            "timestampEpoch": int(os.environ.get("SOURCE_DATE_EPOCH", epoch)),
            "zipTimestamp": created,
            "compression": "deflate-9",
            "normalizedModes": "0755 scripts; 0644 other files",
        },
    }
    build_receipt_bytes = pretty_json(build_receipt)
    sbom_bytes = pretty_json(sbom)
    packaged["BUILD-RECEIPT.json"] = build_receipt_bytes
    packaged["RELEASE-MANIFEST.sha256"] = manifest(packaged)

    top = f"WinCare-{version}"
    sbom_path = output / f"{top}-SBOM.spdx.json"
    build_receipt_path = output / f"{top}-BUILD-RECEIPT.json"
    sbom_path.write_bytes(sbom_bytes)
    build_receipt_path.write_bytes(build_receipt_bytes)

    archive_path = output / f"{top}.zip"
    write_zip(archive_path, top, packaged, zip_timestamp)
    archive_hash = sha256_file(archive_path)
    checksum_path = archive_path.with_suffix(archive_path.suffix + ".sha256")
    checksum_path.write_text(f"{archive_hash}  {archive_path.name}\n", encoding="ascii")

    receipt = {
        "schema": "wincare.release.receipt/v2",
        "release": version,
        "source": {
            "gitCommit": commit,
            "dirty": dirty,
            "treeSha256": source_hash,
            "members": len(full_source),
        },
        "packageProfile": args.profile,
        "packageInputs": {
            "treeSha256": package_input_hash,
            "members": len(package_inputs),
        },
        "archive": {
            "path": archive_path.name,
            "sha256": archive_hash,
            "members": len(packaged),
            "uncompressedBytes": sum(len(value) for value in packaged.values()),
        },
        "validation": [
            {"name": "source-assurance", "status": "required-before-promotion"},
            {"name": "archive-integrity", "status": "pending-independent-verifier"},
            {"name": "windows-native", "status": native_evidence["status"]},
            {"name": "source-references", "status": "verified"},
        ],
        "native": native_evidence,
        "limitations": [
            "Windows, WPF, UAC, reboot, WUA, WDAC, AppX, Registry, Defender, servicing, "
            "and native runtime evidence is emitted by the separate Windows validation job."
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
                "buildType": "urn:wincare:build:deterministic-zip:v2",
                "externalParameters": {"version": version, "packageProfile": args.profile},
                "internalParameters": {"sourceDateEpoch": int(os.environ.get("SOURCE_DATE_EPOCH", epoch))},
                "resolvedDependencies": [{
                    "uri": f"git+https://github.com/AmirrezaFarnamTaheri/WinCare.git@{commit}",
                    "digest": {"sha256": source_hash},
                }],
            },
            "runDetails": {
                "builder": {"id": "urn:wincare:builder:python-standard-library:v2"},
                "metadata": {
                    "invocationId": f"wincare-{version}-{source_hash[:16]}",
                    "startedOn": created,
                    "finishedOn": created,
                },
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
        "schema": "wincare.release.build-result/v2",
        "status": "passed",
        "version": version,
        "archive": str(archive_path),
        "archiveSha256": archive_hash,
        "checksum": str(checksum_path),
        "receipt": str(receipt_path),
        "provenance": str(provenance_path),
        "sbom": str(sbom_path),
        "buildReceipt": str(build_receipt_path),
        "native": native_evidence,
        "members": len(packaged),
        "sourceTreeSha256": source_hash,
        "packageInputTreeSha256": package_input_hash,
        "gitCommit": commit,
        "dirty": dirty,
        "packageProfile": args.profile,
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
