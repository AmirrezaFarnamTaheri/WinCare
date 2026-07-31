#!/usr/bin/env python3
"""Prepare the authoritative WinCare finalizer without leaving duplicate tooling."""
from __future__ import annotations

import runpy
from pathlib import Path

ROOT = Path.cwd().resolve()
correction_path = ROOT / ".wincare-finalize" / "brand-owner-fix.py"
correction = correction_path.read_text(encoding="utf-8-sig")
old = "updated, count = re.subn(pattern, replacement, text, count=1, flags=re.DOTALL)"
new = (
    "updated, count = re.subn("
    "pattern, lambda _match: replacement, text, count=1, flags=re.DOTALL)"
)
if correction.count(old) != 1:
    raise RuntimeError("The brand correction replacement helper has an unexpected shape.")
correction_path.write_text(correction.replace(old, new, 1), encoding="utf-8", newline="\n")
runpy.run_path(str(correction_path), run_name="__main__")

brand_source = ROOT / ".wincare-finalize" / "brand.py"
brand = brand_source.read_text(encoding="utf-8-sig")
helper_anchor = "\ndef patch_installer() -> None:\n"
helper = '''

def replace_required(text: str, old: str, new: str, expected: int = 1) -> str:
    """Replace an exact source fragment only when ownership and count match."""
    count = text.count(old)
    if count != expected:
        raise ValueError(
            f"expected {expected} occurrence(s), found {count}: {old[:120]!r}"
        )
    return text.replace(old, new)
'''
if "def replace_required(" not in brand:
    if brand.count(helper_anchor) != 1:
        raise RuntimeError("The WinCare brand installer function has an unexpected shape.")
    brand = brand.replace(helper_anchor, helper + helper_anchor, 1)

whitespace_old = '    write_text(path, text.rstrip() + addition + "\\n")'
whitespace_new = '    write_text(path, text.rstrip() + addition.rstrip() + "\\n")'
if brand.count(whitespace_old) != 1:
    raise RuntimeError("The WinCare release-documentation writer has an unexpected shape.")
brand = brand.replace(whitespace_old, whitespace_new, 1)
brand_source.write_text(brand, encoding="utf-8", newline="\n")

release_fix_path = ROOT / ".wincare-finalize" / "brand-release-fix.py"
release_fix = release_fix_path.read_text(encoding="utf-8-sig")
open_delimiter = "release_function = r'''def patch_release_finalizer() -> None:"
close_delimiter = "    write_text(path, text)\n\n'''\nbrand = regex_once("
if release_fix.count(open_delimiter) != 1 or release_fix.count(close_delimiter) != 1:
    raise RuntimeError("The v3 brand release correction has an unexpected delimiter shape.")
release_fix = release_fix.replace(
    open_delimiter,
    'release_function = r"""def patch_release_finalizer() -> None:',
    1,
).replace(
    close_delimiter,
    '    write_text(path, text)\n\n"""\nbrand = regex_once(',
    1,
)
release_fix_path.write_text(release_fix, encoding="utf-8", newline="\n")
compile(release_fix, str(release_fix_path), "exec")
runpy.run_path(str(release_fix_path), run_name="__main__")

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

compile(brand_source.read_text(encoding="utf-8-sig"), str(brand_source), "exec")
print("Prepared canonical WinCare brand finalization and removed duplicate staging tools.")
