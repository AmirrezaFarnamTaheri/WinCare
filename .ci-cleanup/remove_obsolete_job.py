#!/usr/bin/env python3
"""Remove the obsolete PR 4 self-remediation job from the primary CI workflow."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CI_PATH = ROOT / ".github/workflows/ci.yml"
MARKER = "\n  remediate-pr4:\n"


def main() -> None:
    """Delete the final obsolete job and fail closed if the workflow shape drifted."""
    text = CI_PATH.read_text(encoding="utf-8")
    count = text.count(MARKER)
    if count != 1:
        raise RuntimeError(f"Expected exactly one obsolete remediation job, found {count}.")
    prefix, suffix = text.split(MARKER, 1)
    if "Apply verified PR 4 remediation" not in suffix:
        raise RuntimeError("The matched CI tail is not the obsolete PR 4 remediation job.")
    if "evidence:" not in prefix or "windows:" not in prefix:
        raise RuntimeError("Refusing to rewrite CI because required validation jobs are missing.")
    CI_PATH.write_text(prefix.rstrip() + "\n", encoding="utf-8", newline="\n")
    result = CI_PATH.read_text(encoding="utf-8")
    if "remediate-pr4:" in result or ".pr4-remediation/" in result:
        raise RuntimeError("Obsolete remediation references remain in CI.")
    print("Removed obsolete PR 4 remediation job from primary CI.")


if __name__ == "__main__":
    main()
