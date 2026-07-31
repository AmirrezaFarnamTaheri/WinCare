#!/usr/bin/env python3
"""Ignore and remove deterministic .NET build outputs before candidate publication."""
from __future__ import annotations

import os
import tempfile
from pathlib import Path

ROOT = Path.cwd().resolve()
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
    ".gitignore",
    """artifacts/
*.zip
""",
    """artifacts/
**/bin/
**/obj/
*.zip
""",
)

replace_exact(
    "tools/test_windows_release_pipeline.py",
    """    def test_native_build_treats_nullable_warnings_as_errors(self) -> None:
""",
    """    def test_dotnet_build_outputs_are_ignored(self) -> None:
        ignores = (ROOT / ".gitignore").read_text(encoding="utf-8").splitlines()
        self.assertEqual(ignores.count("**/bin/"), 1)
        self.assertEqual(ignores.count("**/obj/"), 1)

    def test_native_build_treats_nullable_warnings_as_errors(self) -> None:
""",
)

replace_exact(
    ".wincare-finalize/pr5-remediate.ps1",
    """    Copy-Item -LiteralPath $fixtureResultPath `
        -Destination (Join-Path $outputPath 'previous-release-fixture.json') -Force
    Assert-CleanTree -Purpose 'complete Windows validation'
""",
    """    Copy-Item -LiteralPath $fixtureResultPath `
        -Destination (Join-Path $outputPath 'previous-release-fixture.json') -Force

    # Remove only deterministic compiler outputs created by validation. They are
    # ignored in the published repository, but the transactional runner also
    # leaves its workspace physically clean before branch publication.
    foreach ($relative in @(
        'src\\WinCare\\Standalone\\bin',
        'src\\WinCare\\Standalone\\obj'
    )) {
        $generatedPath = Join-Path $rootPath $relative
        if (Test-Path -LiteralPath $generatedPath) {
            Remove-Item -LiteralPath $generatedPath -Recurse -Force -ErrorAction Stop
        }
    }
    Assert-CleanTree -Purpose 'complete Windows validation'
""",
)

ignore = read_text(".gitignore")
test = read_text("tools/test_windows_release_pipeline.py")
remediator = read_text(".wincare-finalize/pr5-remediate.ps1")
for marker in ("**/bin/", "**/obj/"):
    if ignore.splitlines().count(marker) != 1:
        raise RuntimeError(f"generated-artifact ignore rule is not unique: {marker}")
for marker in (
    "test_dotnet_build_outputs_are_ignored",
    'ignores.count("**/bin/")',
    'ignores.count("**/obj/")',
):
    if marker not in test:
        raise RuntimeError(f"generated-artifact regression test missing: {marker}")
for marker in (
    "Remove only deterministic compiler outputs created by validation",
    "src\\\\WinCare\\\\Standalone\\\\bin",
    "src\\\\WinCare\\\\Standalone\\\\obj",
    "Remove-Item -LiteralPath $generatedPath -Recurse -Force -ErrorAction Stop",
):
    if marker not in remediator:
        raise RuntimeError(f"transactional cleanup postcondition missing: {marker}")

print("Applied deterministic .NET build-artifact hygiene correction.")
