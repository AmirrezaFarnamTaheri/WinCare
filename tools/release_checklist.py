#!/usr/bin/env python3
"""WinCare native source and portable-artifact checklist runner.

Runs self-contained source checks by default. Passing ``--portable-artifact`` validates the
canonical single-file portable executable produced by the Windows packaging workflow.
Windows build/runtime gates remain owned by the Windows workflows.
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PYTHON = sys.executable
PORTABLE_EXECUTABLE_MAX_BYTES = 70_000_000


def _get_product_version() -> str:
    props_path = ROOT / "Directory.Build.props"
    if props_path.is_file():
        import xml.etree.ElementTree as ET
        tree = ET.parse(props_path)
        prefix = tree.findtext(".//VersionPrefix", "2.5.0")
        suffix = tree.findtext(".//VersionSuffix", "")
        return f"{prefix}-{suffix}" if suffix else prefix
    # Keep the fallback aligned with Directory.Build.props (VersionPrefix/VersionSuffix).
    return "2.5.0-rc5"


CHECKS = [
    ("Visual tokens check", [PYTHON, "tools/verify_visual_tokens.py"], 120),
    ("Pill contrast check", [PYTHON, "tools/verify_pill_contrast.py"], 120),
    ("Native foundation gate", [PYTHON, "tools/verify_native_foundation.py"], 120),
    ("Rust format check", ["cargo", "fmt", "--manifest-path", "native/Cargo.toml", "--all", "--", "--check"], 300),
    ("Rust unit tests", ["cargo", "test", "--manifest-path", "native/Cargo.toml"], 900),
    ("Rust clippy lints", ["cargo", "clippy", "--manifest-path", "native/Cargo.toml", "--all-targets", "--all-features", "--", "-D", "warnings"], 900),
    ("Python repository tests", [PYTHON, "-m", "unittest", "discover", "-s", "tests", "-t", ".", "-v"], 300),
]


def validate_portable_artifact(path: Path) -> int:
    """Validate the canonical portable executable and return its size in bytes."""
    artifact = path.resolve()
    if not artifact.is_file():
        raise FileNotFoundError(f"portable executable is missing: {path}")

    size = artifact.stat().st_size
    if size > PORTABLE_EXECUTABLE_MAX_BYTES:
        raise ValueError(
            f"portable executable is {size:,} bytes; limit is {PORTABLE_EXECUTABLE_MAX_BYTES:,} bytes"
        )
    return size


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--portable-artifact",
        type=Path,
        help=(
            "Validate one published WinCare.App.exe against the canonical "
            f"{PORTABLE_EXECUTABLE_MAX_BYTES:,}-byte ceiling and exit."
        ),
    )
    return parser.parse_args()


def main() -> int:
    args = _parse_args()
    if args.portable_artifact is not None:
        try:
            size = validate_portable_artifact(args.portable_artifact)
        except (FileNotFoundError, ValueError) as exc:
            print(f"[FAIL] {exc}")
            return 1
        print(
            f"[PASS] Portable executable: {size:,} bytes "
            f"(limit {PORTABLE_EXECUTABLE_MAX_BYTES:,} bytes)"
        )
        return 0

    if sys.version_info < (3, 11):
        print("[FAIL] WinCare release validation requires Python 3.11 or newer.")
        return 1

    failed = []
    print("=" * 60)
    print("WinCare Release Checklist Validation")
    print("=" * 60)
    for name, cmd, timeout_seconds in CHECKS:
        print(f"\n[RUNNING] {name}...")
        try:
            res = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, timeout=timeout_seconds)
        except subprocess.TimeoutExpired:
            print(f"[FAIL] {name} — timed out after {timeout_seconds} seconds")
            failed.append(name)
            continue
        except OSError as exc:
            print(f"[FAIL] {name} — could not launch process: {exc}")
            failed.append(name)
            continue
        if res.returncode == 0:
            print(f"[PASS] {name}")
        else:
            print(f"[FAIL] {name} (exit code {res.returncode})")
            if res.stdout:
                print("--- stdout ---")
                print(res.stdout.strip())
            if res.stderr:
                print("--- stderr ---")
                print(res.stderr.strip())
            failed.append(name)

    print("\n" + "=" * 60)
    if failed:
        print(f"RELEASE CHECKLIST FAILED ({len(failed)}/{len(CHECKS)} failed):")
        for f in failed:
            print(f"  - {f}")
        return 1

    print(f"ALL {len(CHECKS)} SOURCE CHECKLIST VALIDATIONS PASSED.")
    print("Source checks are green. Windows build/runtime promotion still requires the Windows workflows.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
