#!/usr/bin/env python3
"""Fail-closed byte-for-byte audit of an existing GitHub release asset set.

A CI retry may re-sign the same source with a different temporary identity.
Never append fresh assets to a partial previous release or silently keep bytes
from a different signing run under the same tag and filenames.
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path


def _digest(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            hasher.update(chunk)
    return hasher.hexdigest()


def validate_release_asset_set(expected: Path, published: Path) -> None:
    expected = Path(expected)
    published = Path(published)
    if not expected.is_dir() or not published.is_dir():
        raise ValueError('release asset set directories must exist')
    expected_names = {p.name for p in expected.iterdir() if p.is_file() and not p.is_symlink()}
    published_names = {p.name for p in published.iterdir() if p.is_file() and not p.is_symlink()}
    if not expected_names or expected_names != published_names:
        raise ValueError(
            f'release asset set differs (expected={sorted(expected_names)}, published={sorted(published_names)}); '
            'refusing to mix partial or mismatched signing runs'
        )
    for name in sorted(expected_names):
        if _digest(expected / name) != _digest(published / name):
            raise ValueError(f'release digest mismatch for {name}; refusing to mix signed assets')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--expected', type=Path, required=True)
    parser.add_argument('--published', type=Path, required=True)
    args = parser.parse_args()
    validate_release_asset_set(args.expected, args.published)
    print('Existing GitHub release asset set is byte-for-byte identical to validated staged assets.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
