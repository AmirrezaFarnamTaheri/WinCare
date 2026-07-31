#!/usr/bin/env python3
"""Keep synthetic previous-release metadata internally consistent."""
from __future__ import annotations

import os
import tempfile
from pathlib import Path

ROOT = Path.cwd().resolve()
MAX_BYTES = 4 * 1024 * 1024


def read_text(path: Path) -> str:
    if not path.is_file() or path.is_symlink():
        raise RuntimeError(f"unsafe or missing file: {path}")
    before = path.stat(follow_symlinks=False)
    if before.st_size <= 0 or before.st_size > MAX_BYTES:
        raise RuntimeError(f"file size outside bounds: {path}")
    data = path.read_bytes()
    after = path.stat(follow_symlinks=False)
    if len(data) != before.st_size or (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise RuntimeError(f"file changed while reading: {path}")
    return data.decode("utf-8-sig")


def write_text(path: Path, text: str) -> None:
    destination = path.resolve()
    if ROOT not in destination.parents:
        raise RuntimeError(f"write escapes repository root: {path}")
    descriptor, temporary = tempfile.mkstemp(prefix=destination.name + ".", suffix=".tmp", dir=destination.parent)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(text.encode("utf-8"))
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, destination)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def replace_once(path: Path, old: str, new: str) -> None:
    text = read_text(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"expected one exact source anchor in {path}, found {count}")
    write_text(path, text.replace(old, new, 1))


fixture = ROOT / "tools" / "Build-PreviousReleaseFixture.ps1"
fixture_anchor = """        [IO.File]::WriteAllText($manifestPath, $updated, [Text.UTF8Encoding]::new($false))

        & python tools/validate_source_references.py . --write
"""
fixture_replacement = """        [IO.File]::WriteAllText($manifestPath, $updated, [Text.UTF8Encoding]::new($false))

        $changeLogPath = Join-Path $cloneRoot 'CHANGELOG.md'
        $changeLogText = Read-WinCareToolingBoundedUtf8Text `
            -LiteralPath $changeLogPath `
            -MaximumBytes 4194304 `
            -Purpose 'Previous-release changelog'
        $currentHeadingPattern = [regex]::new(
            ('(?m)^##\\s+{0}\\s*$' -f [regex]::Escape([string]$current))
        )
        $headingMatches = $currentHeadingPattern.Matches($changeLogText)
        if ($headingMatches.Count -ne 1) {
            throw "Expected exactly one changelog heading for current version $current; found $($headingMatches.Count)."
        }
        $updatedChangeLog = $currentHeadingPattern.Replace($changeLogText, "## $previous", 1)
        if ($updatedChangeLog -eq $changeLogText) {
            throw 'The previous-release changelog metadata was not updated.'
        }
        [IO.File]::WriteAllText(
            $changeLogPath,
            $updatedChangeLog,
            [Text.UTF8Encoding]::new($false)
        )

        & python tools/validate_source_references.py . --write
"""
replace_once(fixture, fixture_anchor, fixture_replacement)

test_path = ROOT / "tools" / "test_windows_release_pipeline.py"
test_anchor = """        self.assertIn(\"Build-Release.ps1\", text)
        self.assertIn(\"previous-release-fixture.json\", text)
"""
test_replacement = """        self.assertIn(\"Build-Release.ps1\", text)
        self.assertIn(\"previous-release-fixture.json\", text)
        self.assertIn(\"CHANGELOG.md\", text)
        self.assertIn(\"Previous-release changelog\", text)
        self.assertIn('\"## $previous\"', text)
"""
replace_once(test_path, test_anchor, test_replacement)

print("Synchronized previous-release module and changelog metadata contracts.")
