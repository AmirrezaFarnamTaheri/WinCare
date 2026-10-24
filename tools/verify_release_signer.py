#!/usr/bin/env python3
"""Validate that all published architectures use the same release signing identity."""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path


def assert_shared_release_signer(assets: Path, version: str) -> str:
    tag = version if version.startswith("v") else "v" + version
    paths = [
        assets / f"WinCare-{tag}-x64.cer",
        assets / f"WinCare-{tag}-ARM64.cer",
    ]
    for path in paths:
        if not path.is_file():
            raise ValueError(f"missing architecture-specific release signer: {path.name}")
        if path.stat().st_size < 64 or path.stat().st_size > 65536:
            raise ValueError(f"invalid release signer certificate size: {path.name}")

    digests = [hashlib.sha256(path.read_bytes()).hexdigest() for path in paths]
    if digests[0] != digests[1]:
        raise ValueError("x64 and ARM64 signed artifacts use different release certificates")
    return digests[0]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    try:
        print("Verified shared release signer SHA-256:", assert_shared_release_signer(args.assets, args.version))
        return 0
    except ValueError as exc:
        parser.exit(status=1, message=f"Release signer verification failed: {exc}\n")


if __name__ == "__main__":
    raise SystemExit(main())
