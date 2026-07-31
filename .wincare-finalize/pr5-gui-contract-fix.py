#!/usr/bin/env python3
"""Make the GUI critical-action contract robust to optional JSON properties."""
from __future__ import annotations

import os
import tempfile
from pathlib import Path

ROOT = Path.cwd().resolve()
RELATIVE = "tests/Gui.Contracts.Tests.ps1"
MAX_BYTES = 2 * 1024 * 1024


def read_text() -> str:
    path = (ROOT / RELATIVE).resolve()
    if ROOT not in path.parents or not path.is_file() or path.is_symlink():
        raise RuntimeError(f"unsafe or missing test file: {RELATIVE}")
    before = path.stat(follow_symlinks=False)
    if before.st_size <= 0 or before.st_size > MAX_BYTES:
        raise RuntimeError(f"test file is empty or oversized: {RELATIVE}")
    payload = path.read_bytes()
    after = path.stat(follow_symlinks=False)
    if len(payload) != before.st_size or (
        before.st_size,
        before.st_mtime_ns,
    ) != (
        after.st_size,
        after.st_mtime_ns,
    ):
        raise RuntimeError(f"test file changed while reading: {RELATIVE}")
    return payload.decode("utf-8-sig")


def write_text(text: str) -> None:
    path = (ROOT / RELATIVE).resolve()
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


old = """        @($Catalog|Where-Object critical).Count|Should -BeGreaterThan 0
        foreach($item in @($Catalog|Where-Object critical)){$item.risk|Should -Be 'Critical'}
"""
new = """        $CriticalCatalogItems=@($Catalog|Where-Object {
            $_.PSObject.Properties['critical'] -and [bool]$_.critical
        })
        $CriticalCatalogItems.Count|Should -BeGreaterThan 0
        foreach($item in $CriticalCatalogItems){$item.risk|Should -Be 'Critical'}
"""
text = read_text()
count = text.count(old)
if count != 1:
    raise RuntimeError(
        f"expected one GUI critical-contract anchor, found {count}"
    )
text = text.replace(old, new, 1)
if "Where-Object critical" in text:
    raise RuntimeError("unsafe simplified critical-property filter remains")
for marker in (
    "$CriticalCatalogItems=@($Catalog|Where-Object {",
    "$_.PSObject.Properties['critical'] -and [bool]$_.critical",
    "$CriticalCatalogItems.Count|Should -BeGreaterThan 0",
    "foreach($item in $CriticalCatalogItems){$item.risk|Should -Be 'Critical'}",
):
    if marker not in text:
        raise RuntimeError(f"GUI contract postcondition missing: {marker}")
write_text(text)
print("Applied property-safe GUI critical-action contract correction.")
