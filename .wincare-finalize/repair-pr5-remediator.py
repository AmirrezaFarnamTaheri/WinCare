#!/usr/bin/env python3
"""Harden the transactional remediator before execution."""
from __future__ import annotations

from pathlib import Path

ROOT = Path.cwd().resolve()
PATH = ROOT / ".wincare-finalize" / "pr5-remediate.ps1"
if not PATH.is_file() or PATH.is_symlink():
    raise RuntimeError("transactional remediation script is missing or unsafe")
text = PATH.read_text(encoding="utf-8-sig")
replacements = (
    (
        "    Import-Module Pester -RequiredVersion 5.9.0 -Force -ErrorAction Stop\n"
        "    Import-Module PSScriptAnalyzer -RequiredVersion 1.24.0 -Force -ErrorAction Stop\n\n",
        "    # Keep the pinned modules installed but unloaded in this orchestration process.\n"
        "    # The Pester and release/static gates import them inside isolated child processes.\n\n",
    ),
    (
        "    Write-Error $failure\n    try {\n",
        "    Write-Host (\"PR #5 remediation failed: {0}\" -f $failure.Exception.Message) -ForegroundColor Red\n    try {\n",
    ),
    (
        "        Write-Error \"Failure receipt publication also failed: $_\"\n",
        "        Write-Warning \"Failure receipt publication also failed: $_\"\n",
    ),
)
for old, new in replacements:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"expected one remediator repair anchor, found {count}: {old!r}")
    text = text.replace(old, new, 1)
for forbidden in (
    "Import-Module Pester -RequiredVersion 5.9.0 -Force -ErrorAction Stop",
    "Import-Module PSScriptAnalyzer -RequiredVersion 1.24.0 -Force -ErrorAction Stop",
):
    if forbidden in text:
        raise RuntimeError(f"parent-process module import remains after repair: {forbidden}")
PATH.write_text(text, encoding="utf-8", newline="\n")
compile(Path(__file__).read_text(encoding="utf-8"), __file__, "exec")
print("Hardened PR #5 remediation failure reporting and isolated module loading.")
