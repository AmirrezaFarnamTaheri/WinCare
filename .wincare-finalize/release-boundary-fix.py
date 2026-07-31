#!/usr/bin/env python3
"""Close WinCare release-brand and raw ZIP-name verification boundaries."""
from __future__ import annotations

from pathlib import Path

ROOT = Path.cwd().resolve()


def replace_exact(path: Path, old: str, new: str, expected: int = 1) -> None:
    text = path.read_text(encoding="utf-8-sig")
    count = text.count(old)
    if count != expected:
        raise RuntimeError(
            f"{path}: expected {expected} occurrence(s), found {count}: {old[:120]!r}"
        )
    path.write_text(text.replace(old, new), encoding="utf-8", newline="\n")


verifier = ROOT / "tools" / "verify_release_v3.py"
replace_exact(
    verifier,
    '''                    f"WinCare-{version}-build-result.json",
                    "WinCare.Standalone.build.json",
''',
    '''                    f"WinCare-{version}-build-result.json",
                    f"WinCare-{version}-BRAND.json",
                    "WinCare.Standalone.build.json",
''',
)
replace_exact(
    verifier,
    '''                elif standalone_manifest_data is not None and sha256_file(loose_manifest, 4 * 1024 * 1024) != sha256(standalone_manifest_data):
                    errors.append("loose standalone build manifest differs from archive")
                for member_name, asset_name in (
''',
    '''                elif standalone_manifest_data is not None and sha256_file(loose_manifest, 4 * 1024 * 1024) != sha256(standalone_manifest_data):
                    errors.append("loose standalone build manifest differs from archive")
                brand_manifest_data = files.get("design/WinCare-Brand.manifest.json")
                loose_brand = asset_directory / f"WinCare-{version}-BRAND.json"
                if not loose_brand.is_file() or loose_brand.is_symlink():
                    errors.append("loose brand identity manifest is missing")
                elif brand_manifest_data is None:
                    errors.append("archive brand identity manifest is missing")
                elif sha256_file(loose_brand, 4 * 1024 * 1024) != sha256(brand_manifest_data):
                    errors.append("loose brand identity manifest differs from archive")
                for member_name, asset_name in (
''',
)

payload = ROOT / "tools" / "prepare_standalone_payload.py"
replace_exact(
    payload,
    '''        for info in infos:
            normalized, is_directory = normalize_member(info.filename)
''',
    '''        for info in infos:
            raw_name = getattr(info, "orig_filename", info.filename)
            normalized, is_directory = normalize_member(raw_name)
''',
    expected=2,
)

for path in (verifier, payload):
    compile(path.read_text(encoding="utf-8-sig"), str(path), "exec")
print("Closed release brand receipt and raw ZIP filename verification boundaries.")
