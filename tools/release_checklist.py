#!/usr/bin/env python3
"""WinCare 2.4.0-rc1 Release Checklist Runner.
Executes all 7 validation tools in sequence and reports binary pass/fail status.
"""
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

CHECKS = [
    ("Visual tokens check", ["python", "tools/verify_visual_tokens.py"]),
    ("Pill contrast check", ["python", "tools/verify_pill_contrast.py"]),
    ("Native foundation gate V5", ["python", "tools/verify_native_foundation.py"]),
    ("Rust unit tests", ["cargo", "test", "--manifest-path", "native/wincare-core/Cargo.toml"]),
    ("Rust clippy lints", ["cargo", "clippy", "--manifest-path", "native/wincare-core/Cargo.toml", "--", "-D", "warnings"]),
    ("Python native tests", ["python", "-m", "unittest", "discover", "-s", "tests/native", "-v"]),
    ("RC package finalization", ["python", "tools/finalize_native_release.py", "--version", "2.4.0-rc1", "--output", "artifacts/"]),
]

def main() -> int:
    failed = []
    print("=" * 60)
    print("WinCare Release Checklist Validation")
    print("=" * 60)
    for name, cmd in CHECKS:
        print(f"\n[RUNNING] {name}...")
        try:
            res = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, timeout=120)
        except subprocess.TimeoutExpired:
            print(f"[FAIL] {name} — timed out after 120 seconds")
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

    print("ALL 7 RELEASE CHECKLIST VALIDATIONS PASSED! 🚀")
    print("WinCare 2.4.0-rc1 is READY for release candidate packaging.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
