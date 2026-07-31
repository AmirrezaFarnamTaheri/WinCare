#!/usr/bin/env python3
"""Prepare the authoritative WinCare finalizer without leaving duplicate tooling."""
from __future__ import annotations

import runpy
from pathlib import Path

ROOT = Path.cwd().resolve()
runpy.run_path(str(ROOT / ".wincare-finalize" / "brand-owner-fix.py"), run_name="__main__")

for relative in (
    ".github/workflows/apply-windows-release-brand-finalization.yml",
    "tools/Generate-WinCareBrand.ps1",
    "tools/Test-WinCareBrand.ps1",
    "docs/BRAND.md",
):
    path = ROOT / relative
    if path.exists():
        if not path.is_file() or path.is_symlink():
            raise RuntimeError(f"staging-only brand path is unsafe: {relative}")
        path.unlink()

brand_source = ROOT / ".wincare-finalize" / "brand.py"
compile(brand_source.read_text(encoding="utf-8-sig"), str(brand_source), "exec")
print("Prepared canonical WinCare brand finalization and removed duplicate staging tools.")
