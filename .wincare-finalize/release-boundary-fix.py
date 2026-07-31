#!/usr/bin/env python3
"""Run the preserved release-boundary convergence and the authoritative fixture correction."""
from __future__ import annotations

import runpy
from pathlib import Path

ROOT = Path.cwd().resolve()
for relative in (
    ".wincare-finalize/release-boundary-core.py",
    ".wincare-finalize/fix-previous-release-fixture.py",
):
    path = ROOT / relative
    if not path.is_file() or path.is_symlink():
        raise RuntimeError(f"release-boundary dependency is missing or unsafe: {relative}")
    runpy.run_path(str(path), run_name="__main__")

print("Applied release-boundary convergence and authoritative previous-release fixture correction.")
