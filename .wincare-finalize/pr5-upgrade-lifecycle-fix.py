#!/usr/bin/env python3
"""Prevent release-verifier diagnostics from contaminating upgrade source paths."""
from __future__ import annotations

import os
import tempfile
from pathlib import Path

ROOT = Path.cwd().resolve()
SOURCE = "tools/Test-UpgradeLifecycle.ps1"
TEST = "tools/test_windows_release_pipeline.py"
MAX_BYTES = 4 * 1024 * 1024


def read_text(relative: str) -> str:
    path = (ROOT / relative).resolve()
    if ROOT not in path.parents or not path.is_file() or path.is_symlink():
        raise RuntimeError(f"unsafe or missing file: {relative}")
    before = path.stat(follow_symlinks=False)
    if before.st_size <= 0 or before.st_size > MAX_BYTES:
        raise RuntimeError(f"empty or oversized file: {relative}")
    payload = path.read_bytes()
    after = path.stat(follow_symlinks=False)
    if len(payload) != before.st_size or (
        before.st_size,
        before.st_mtime_ns,
    ) != (
        after.st_size,
        after.st_mtime_ns,
    ):
        raise RuntimeError(f"file changed while reading: {relative}")
    return payload.decode("utf-8-sig")


def write_text(relative: str, text: str) -> None:
    path = (ROOT / relative).resolve()
    if ROOT not in path.parents or path.is_symlink():
        raise RuntimeError(f"unsafe destination: {relative}")
    descriptor, temporary = tempfile.mkstemp(
        prefix=path.name + ".",
        suffix=".tmp",
        dir=path.parent,
    )
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(text.encode("utf-8"))
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def replace_exact(relative: str, old: str, new: str) -> None:
    text = read_text(relative)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(
            f"{relative}: expected one exact replacement anchor, found {count}: {old!r}"
        )
    write_text(relative, text.replace(old, new, 1))


replace_exact(
    SOURCE,
    """    & $python (Join-Path $PSScriptRoot 'verify_release.py') $Archive --extract-to $Destination
    if ($LASTEXITCODE -ne 0) { throw \"Validated archive extraction failed: $Archive\" }
    $roots = @(Get-ChildItem -LiteralPath $Destination -Directory -Force -ErrorAction Stop)
    if ($roots.Count -ne 1) { throw 'Upgrade archive must contain exactly one root directory.' }
    $roots[0].FullName
""",
    """    $verificationOutput = @(
        & $python (Join-Path $PSScriptRoot 'verify_release.py') `
            $Archive --extract-to $Destination 2>&1
    )
    $verificationExitCode = $LASTEXITCODE
    foreach ($line in $verificationOutput) {
        Write-Host ([string]$line)
    }
    if ($verificationExitCode -ne 0) {
        throw \"Validated archive extraction failed: $Archive\"
    }
    $roots = @(Get-ChildItem -LiteralPath $Destination -Directory -Force -ErrorAction Stop)
    if ($roots.Count -ne 1) { throw 'Upgrade archive must contain exactly one root directory.' }
    return [string]$roots[0].FullName
""",
)

replace_exact(
    TEST,
    """        self.assertIn("UpgradeVerified = $true", upgrade)
        self.assertIn("UpgradeRollbackVerified = $true", upgrade)

    def test_native_build_treats_nullable_warnings_as_errors(self) -> None:
""",
    """        self.assertIn("UpgradeVerified = $true", upgrade)
        self.assertIn("UpgradeRollbackVerified = $true", upgrade)

    def test_upgrade_archive_diagnostics_cannot_contaminate_source_paths(self) -> None:
        upgrade = UPGRADE.read_text(encoding="utf-8")
        self.assertIn("$verificationOutput = @(", upgrade)
        self.assertIn("$verificationExitCode = $LASTEXITCODE", upgrade)
        self.assertIn("Write-Host ([string]$line)", upgrade)
        self.assertIn("return [string]$roots[0].FullName", upgrade)
        self.assertNotIn(
            "& $python (Join-Path $PSScriptRoot 'verify_release.py') $Archive --extract-to $Destination\\n",
            upgrade,
        )

    def test_native_build_treats_nullable_warnings_as_errors(self) -> None:
""",
)

source = read_text(SOURCE)
test = read_text(TEST)
for marker in (
    "$verificationOutput = @(",
    "$verificationExitCode = $LASTEXITCODE",
    "Write-Host ([string]$line)",
    "return [string]$roots[0].FullName",
):
    if marker not in source:
        raise RuntimeError(f"upgrade lifecycle postcondition missing: {marker}")
for marker in (
    "test_upgrade_archive_diagnostics_cannot_contaminate_source_paths",
    "return [string]$roots[0].FullName",
):
    if marker not in test:
        raise RuntimeError(f"upgrade lifecycle regression test missing: {marker}")

print("Applied isolated upgrade archive extraction output correction.")
