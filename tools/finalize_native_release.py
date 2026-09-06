#!/usr/bin/env python3
"""Stage and package auditable WinCare native release sources.

The finalizer deliberately separates executable legacy PowerShell source from
native release source. Release-candidate mode records incomplete migration
state. Production mode fails closed until all 259 commands are behavior-verified.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import sys
import tempfile
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Literal

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from tools.package_portable import create_portable_archive

EXPECTED_COMMAND_COUNT = 259
VERSION_LABEL_PATTERN = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$")
POWERSHELL_SUFFIXES = {".ps1", ".psm1", ".psd1"}
SECRET_KEY_SUFFIXES = {".pfx", ".p12", ".key", ".pem", ".snk", ".secret", ".token"}
IGNORED_DIRECTORY_NAMES = {".git", ".vs", ".pytest_cache", "__pycache__", "artifacts", "bin", "obj", "target", "AppPackages"}
IGNORED_FILE_SUFFIXES = {".pyc", ".pyo"}
ReleaseMode = Literal["rc", "production"]

NATIVE_TOP_LEVEL_FILES = (
    ".gitattributes",
    ".gitignore",
    "CHANGELOG.md",
    "CONTRIBUTING.md",
    "DESIGN.md",
    "Directory.Build.props",
    "Directory.Packages.props",
    "LICENSE",
    "NuGet.Config",
    "README.md",
    "SECURITY.md",
    "THIRD-PARTY-NOTICES.md",
    "VALIDATION.md",
    "WinCare.Native.sln",
    "global.json",
    "rust-toolchain.toml",
)

NATIVE_DIRECTORIES = (
    "docs",
    "migration/oracle",
    "native",
    "policy",
    "src/WinCare.App",
    "src/WinCare.Application",
    "src/WinCare.CommandCatalog",
    "src/WinCare.Domain",
    "src/WinCare.Infrastructure",
    "tests/WinCare.Application.Tests",
    "tests/WinCare.CommandCatalog.Tests",
    "tests/WinCare.Infrastructure.Tests",
    "tests/native",
    "tests/tools",
    "tools/wincare-plugin-cli",
)

NATIVE_DESIGN_FILES = (
    "design/BRAND.md",
    "design/README.md",
    "design/WinCare-Brand.manifest.json",
    "design/WinCare-Logo-512.png",
    "design/WinCare-Logo.png",
    "design/WinCare-Logo.svg",
    "design/WinCare-Wordmark.svg",
    "design/WinCare.ico",
)

NATIVE_DOCUMENT_FILES = (
    "docs/Architecture.md",
    "docs/README.md",
    "docs/User-Guide.md",
    "docs/migration/command-parity-ledger.md",
    "docs/migration/finalization-status.md",
    "docs/migration/windows-validation.md",
    "docs/plans/2026-08-14-ai-system-doctor-plan.md",
    "docs/plans/2026-08-14-ai-system-doctor-requirements.md",
    "docs/plans/2026-08-14-community-plugin-sdk-plan.md",
    "docs/plans/2026-08-14-community-plugin-sdk-requirements.md",
    "docs/plans/2026-08-14-plugin-store-and-modular-architecture-plan.md",
    "docs/plans/2026-08-14-plugin-store-and-modular-architecture-requirements.md",
    "docs/plans/2026-08-14-plugin-store-components-upgrade-plan.md",
    "docs/plans/2026-08-14-rust-background-guard-service-plan.md",
    "docs/plans/2026-08-14-rust-background-guard-service-requirements.md",
)

NATIVE_TOOL_FILES = (
    "tools/__init__.py",
    "tools/wincare-plugin-cli/bin/wincare-plugin.js",
    "tools/capture_screenshots.py",
    "tools/finalize_native_release.py",
    "tools/install_msix.py",
    "tools/package_portable.py",
    "tools/release_checklist.py",
    "tools/stage_release_assets.py",
    "tools/verify_native_foundation.py",
    "tools/verify_visual_tokens.py",
    "tools/verify_pill_contrast.py",
)

NATIVE_WORKFLOW_FILES = (
    ".github/CODEOWNERS",
    ".github/dependabot.yml",
    ".github/pull_request_template.md",
    ".github/workflows/native-winui.yml",
    ".github/workflows/native-release-candidate.yml",
)

ORACLE_TOP_LEVEL_FILES = (
    "Install-WinCare.ps1",
    "PSScriptAnalyzerSettings.psd1",
    "Uninstall-WinCare.ps1",
    "WinCare-GUI.ps1",
    "WinCare-TUI.ps1",
    "WinCare.ps1",
    "run-wincare-gui.cmd",
    "run-wincare.cmd",
)

ORACLE_DIRECTORIES = (
    "config",
    "migration/oracle",
    "policy",
    "src/WinCare",
)


class ProductionBlockedError(RuntimeError):
    """Raised when production promotion is attempted before parity is complete."""


@dataclass(frozen=True)
class Readiness:
    cataloged: int
    contract_verified: int
    implemented: int
    behavior_verified: int
    implementation_blockers: int
    production_blockers: int
    production_ready: bool


@dataclass(frozen=True)
class FinalizationResult:
    native_archive: Path
    oracle_archive: Path
    report_path: Path
    manifest_path: Path
    readiness: Readiness


def _read_catalog(catalog_path: Path) -> list[dict[str, object]]:
    document = json.loads(catalog_path.read_text(encoding="utf-8"))
    if document.get("schemaVersion") != 1:
        raise ValueError("unsupported command catalog schema")
    commands = document.get("commands")
    if not isinstance(commands, list):
        raise ValueError("command catalog must contain a commands array")
    ids = [command.get("id") for command in commands if isinstance(command, dict)]
    if len(commands) != EXPECTED_COMMAND_COUNT or len(ids) != EXPECTED_COMMAND_COUNT:
        raise ValueError(f"command catalog must contain exactly {EXPECTED_COMMAND_COUNT} commands")
    if len(set(ids)) != EXPECTED_COMMAND_COUNT or any(not isinstance(value, str) or not value for value in ids):
        raise ValueError("command catalog IDs must be 259 unique non-empty strings")
    return commands


def evaluate_readiness(catalog_path: Path, mode: ReleaseMode = "rc") -> Readiness:
    if mode not in {"rc", "production"}:
        raise ValueError(f"unsupported finalization mode: {mode}")

    commands = _read_catalog(catalog_path)
    statuses = [str(command.get("migrationStatus", "")) for command in commands]
    allowed = {"Cataloged", "ContractVerified", "Implemented", "BehaviorVerified"}
    invalid = sorted(set(statuses) - allowed)
    if invalid:
        raise ValueError(f"unsupported migration status values: {invalid}")

    readiness = Readiness(
        cataloged=len(commands),
        contract_verified=sum(status in {"ContractVerified", "Implemented", "BehaviorVerified"} for status in statuses),
        implemented=sum(status in {"Implemented", "BehaviorVerified"} for status in statuses),
        behavior_verified=sum(status == "BehaviorVerified" for status in statuses),
        implementation_blockers=sum(status not in {"Implemented", "BehaviorVerified"} for status in statuses),
        production_blockers=sum(status != "BehaviorVerified" for status in statuses),
        production_ready=all(status == "BehaviorVerified" for status in statuses),
    )

    if mode == "production" and not readiness.production_ready:
        raise ProductionBlockedError(
            f"production release blocked by {readiness.production_blockers} command(s) without behavior verification"
        )
    return readiness


def _reject_symlink(path: Path) -> None:
    if path.is_symlink():
        raise ValueError(f"symbolic link is not allowed in release staging: {path}")


def _copy_file(root: Path, destination: Path, relative: str) -> None:
    source = root / relative
    _reject_symlink(source)
    if not source.is_file():
        raise FileNotFoundError(f"required release source file is missing: {relative}")
    target = destination / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)


def _is_ignored(root: Path, path: Path) -> bool:
    relative = path.relative_to(root)
    return (
        any(part in IGNORED_DIRECTORY_NAMES for part in relative.parts)
        or path.suffix.lower() in IGNORED_FILE_SUFFIXES
    )


def _copy_directory(root: Path, destination: Path, relative: str) -> None:
    source = root / relative
    _reject_symlink(source)
    if not source.is_dir():
        raise FileNotFoundError(f"required release source directory is missing: {relative}")
    for path in sorted(source.rglob("*"), key=lambda item: item.relative_to(root).as_posix()):
        _reject_symlink(path)
        if _is_ignored(root, path) or not path.is_file():
            continue
        child_relative = path.relative_to(root)
        target = destination / child_relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)


def _copy_matching_files(root: Path, destination: Path, relative: str, suffixes: set[str]) -> None:
    source = root / relative
    if not source.is_dir():
        return
    for path in sorted(source.rglob("*"), key=lambda item: item.relative_to(root).as_posix()):
        _reject_symlink(path)
        if _is_ignored(root, path):
            continue
        if path.is_file() and path.suffix.lower() in suffixes:
            child_relative = path.relative_to(root)
            target = destination / child_relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)


def stage_native_source(root: Path, destination: Path) -> Path:
    root = root.resolve()
    destination = destination.resolve()

    # Pre-staging scan across native source directories to ensure source repo contains no secret signing keys
    for relative in (
        NATIVE_TOP_LEVEL_FILES
        + NATIVE_DESIGN_FILES
        + NATIVE_DOCUMENT_FILES
        + NATIVE_TOOL_FILES
        + NATIVE_WORKFLOW_FILES
    ):
        file_path = root / relative
        if file_path.is_file() and file_path.suffix.lower() in SECRET_KEY_SUFFIXES:
            raise ValueError(f"native source contains secret/signing key file: {relative}")

    for relative in NATIVE_DIRECTORIES:
        source_dir = root / relative
        if source_dir.is_dir():
            for path in source_dir.rglob("*"):
                if path.is_file() and path.suffix.lower() in SECRET_KEY_SUFFIXES:
                    raise ValueError(f"native source tree contains secret/signing key file: {path.relative_to(root).as_posix()}")

    destination.mkdir(parents=True, exist_ok=False)

    for relative in (
        NATIVE_TOP_LEVEL_FILES
        + NATIVE_DESIGN_FILES
        + NATIVE_DOCUMENT_FILES
        + NATIVE_TOOL_FILES
        + NATIVE_WORKFLOW_FILES
    ):
        _copy_file(root, destination, relative)
    for relative in NATIVE_DIRECTORIES:
        _copy_directory(root, destination, relative)

    powershell = [
        path.relative_to(destination).as_posix()
        for path in destination.rglob("*")
        if path.is_file() and path.suffix.lower() in POWERSHELL_SUFFIXES
    ]
    if powershell:
        raise ValueError(f"native source staging contains PowerShell files: {powershell}")

    secrets = [
        path.relative_to(destination).as_posix()
        for path in destination.rglob("*")
        if path.is_file() and path.suffix.lower() in SECRET_KEY_SUFFIXES
    ]
    if secrets:
        raise ValueError(f"native source staging contains secret/signing key files: {secrets}")
    return destination


def stage_legacy_oracle(root: Path, destination: Path) -> Path:
    root = root.resolve()
    destination = destination.resolve()
    destination.mkdir(parents=True, exist_ok=False)

    for relative in ORACLE_TOP_LEVEL_FILES:
        source = root / relative
        if source.is_file():
            _copy_file(root, destination, relative)
    for relative in ORACLE_DIRECTORIES:
        source = root / relative
        if source.is_dir():
            _copy_directory(root, destination, relative)
    _copy_matching_files(root, destination, "tools", POWERSHELL_SUFFIXES)
    _copy_matching_files(root, destination, "tests", POWERSHELL_SUFFIXES | {".json", ".xml", ".xaml"})

    if not (destination / "migration/oracle/provenance.json").is_file():
        raise ValueError("legacy oracle staging requires migration/oracle/provenance.json")
    return destination


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _file_count(root: Path) -> int:
    return sum(path.is_file() for path in root.rglob("*"))


def _write_report(path: Path, version: str, mode: ReleaseMode, readiness: Readiness) -> None:
    status = "Production ready" if readiness.production_ready else "Release candidate only"
    lines = [
        f"# WinCare {version} finalization report",
        "",
        f"**Mode:** {mode}",
        f"**Status:** {status}",
        "",
        "## Command migration",
        "",
        "| State | Count |",
        "|---|---:|",
        f"| Cataloged | {readiness.cataloged} |",
        f"| Contract verified or later | {readiness.contract_verified} |",
        f"| Implemented or later | {readiness.implemented} |",
        f"| Behavior verified | {readiness.behavior_verified} |",
        f"| Native implementation blockers | {readiness.implementation_blockers} |",
        f"| Production behavior-verification blockers | {readiness.production_blockers} |",
        "",
        "## Artifact separation",
        "",
        "- The native source archive contains no `.ps1`, `.psm1`, or `.psd1` files.",
        "- The legacy implementation is preserved only in the separate oracle archive.",
        "- Native runtime projects do not invoke or embed PowerShell.",
        "",
        "## Verification boundary",
        "",
        "This report records deterministic source finalization and Linux-capable structural checks. "
        "C#, Rust, WinUI, UI Automation, MSIX, and Windows command behavior require the Windows CI gates.",
    ]
    if not readiness.production_ready:
        lines.extend(
            [
                "",
                "## Promotion blocked",
                "",
                f"Production promotion is blocked until all {EXPECTED_COMMAND_COUNT} commands are behavior-verified. "
                f"The current catalog has {readiness.production_blockers} behavior-verification blocker(s) "
                f"and {readiness.implementation_blockers} native implementation blocker(s).",
            ]
        )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def _validate_version_label(version: str) -> str:
    if not VERSION_LABEL_PATTERN.fullmatch(version):
        raise ValueError(f"unsafe or unsupported version label: {version!r}")
    return version


def finalize_release(
    root: Path,
    output_dir: Path,
    *,
    version: str,
    mode: ReleaseMode = "rc",
) -> FinalizationResult:
    root = root.resolve()
    output_dir = output_dir.resolve()
    version = _validate_version_label(version)
    output_dir.mkdir(parents=True, exist_ok=True)

    readiness = evaluate_readiness(
        root / "src/WinCare.CommandCatalog/Data/commands.json",
        mode=mode,
    )

    native_archive = output_dir / f"WinCare-{version}-native-source.zip"
    oracle_archive = output_dir / f"WinCare-{version}-legacy-oracle.zip"
    report_path = output_dir / f"WinCare-{version}-finalization-report.md"
    manifest_path = output_dir / f"WinCare-{version}-finalization-manifest.json"

    with tempfile.TemporaryDirectory(prefix="wincare-finalize-") as directory:
        staging_root = Path(directory)
        native_stage = stage_native_source(root, staging_root / "native")
        oracle_stage = stage_legacy_oracle(root, staging_root / "oracle")
        create_portable_archive(native_stage, native_archive)
        create_portable_archive(oracle_stage, oracle_archive)
        native_file_count = _file_count(native_stage)
        oracle_file_count = _file_count(oracle_stage)

    _write_report(report_path, version, mode, readiness)
    oracle_provenance = json.loads(
        (root / "migration/oracle/provenance.json").read_text(encoding="utf-8")
    )
    manifest = {
        "schemaVersion": 1,
        "version": version,
        "mode": mode,
        "oracleProvenance": {
            "repository": oracle_provenance["sourceRepository"],
            "commit": oracle_provenance["sourceCommit"],
            "capturedAt": oracle_provenance["capturedAt"],
        },
        "readiness": asdict(readiness),
        "artifacts": {
            "nativeSource": {
                "path": native_archive.name,
                "sha256": _sha256(native_archive),
                "files": native_file_count,
                "powerShellFiles": 0,
            },
            "legacyOracle": {
                "path": oracle_archive.name,
                "sha256": _sha256(oracle_archive),
                "files": oracle_file_count,
            },
            "report": {
                "path": report_path.name,
                "sha256": _sha256(report_path),
            },
        },
    }
    manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    return FinalizationResult(
        native_archive=native_archive,
        oracle_archive=oracle_archive,
        report_path=report_path,
        manifest_path=manifest_path,
        readiness=readiness,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Finalize WinCare native source and legacy oracle artifacts.")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--mode", choices=("rc", "production"), default="rc")
    args = parser.parse_args()

    try:
        result = finalize_release(args.root, args.output, version=args.version, mode=args.mode)
    except (FileNotFoundError, ValueError, OSError, ProductionBlockedError) as error:
        print(f"finalization failed: {error}")
        return 1

    print(result.native_archive)
    print(result.oracle_archive)
    print(result.report_path)
    print(result.manifest_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
