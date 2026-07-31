#!/usr/bin/env python3
"""Harden the transactional remediator's failure path before execution."""
from __future__ import annotations

from pathlib import Path

ROOT = Path.cwd().resolve()
PATH = ROOT / ".wincare-finalize" / "pr5-remediate.ps1"
if not PATH.is_file() or PATH.is_symlink():
    raise RuntimeError("transactional remediation script is missing or unsafe")
text = PATH.read_text(encoding="utf-8-sig")
replacements = (
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
        raise RuntimeError(f"expected one remediator failure-path anchor, found {count}: {old!r}")
    text = text.replace(old, new, 1)
PATH.write_text(text, encoding="utf-8", newline="\n")
compile(Path(__file__).read_text(encoding="utf-8"), __file__, "exec")
print("Hardened PR #5 remediation failure reporting.")
