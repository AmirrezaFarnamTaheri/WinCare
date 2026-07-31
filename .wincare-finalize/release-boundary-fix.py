#!/usr/bin/env python3
"""Close WinCare release-brand, archive-name, fixture, package, and clone boundaries."""
from __future__ import annotations

import runpy
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

builder = ROOT / "tools" / "build_release.py"
replace_exact(
    builder,
    '''PRODUCTION_DOC_FILES = {
    "docs/Architecture.md", "docs/CAPABILITY-MATRIX.md", "docs/Compatibility.md",
    "docs/GUI.md", "docs/Safety.md", "docs/Testing.md", "docs/Advanced-Capabilities.md",
    "docs/Source-Reference-Model.md",
}
MAX_SOURCE_MEMBER_BYTES = 64 * 1024 * 1024
''',
    '''PRODUCTION_DOC_FILES = {
    "docs/Architecture.md", "docs/CAPABILITY-MATRIX.md", "docs/Compatibility.md",
    "docs/GUI.md", "docs/Safety.md", "docs/Testing.md", "docs/Advanced-Capabilities.md",
    "docs/Source-Reference-Model.md",
}
PRODUCTION_BRAND_FILES = {
    "design/BRAND.md",
    "design/WinCare-Logo.svg",
    "design/WinCare-Wordmark.svg",
    "design/WinCare-Logo-512.png",
    "design/WinCare.ico",
    "design/WinCare-Brand.manifest.json",
}
MAX_SOURCE_MEMBER_BYTES = 64 * 1024 * 1024
''',
)
replace_exact(
    builder,
    '''    if relative in PRODUCTION_ROOT_FILES or relative in PRODUCTION_CONFIG_FILES or relative in PRODUCTION_DOC_FILES:
''',
    '''    if relative in PRODUCTION_ROOT_FILES or relative in PRODUCTION_CONFIG_FILES or relative in PRODUCTION_DOC_FILES or relative in PRODUCTION_BRAND_FILES:
''',
)
replace_exact(
    builder,
    '''        required = PRODUCTION_ROOT_FILES | PRODUCTION_CONFIG_FILES | {
            "src/WinCare/WinCare.psd1", "src/WinCare/WinCare.psm1",
        }
''',
    '''        required = PRODUCTION_ROOT_FILES | PRODUCTION_CONFIG_FILES | PRODUCTION_BRAND_FILES | {
            "src/WinCare/WinCare.psd1", "src/WinCare/WinCare.psm1",
        }
''',
)

attributes = ROOT / ".gitattributes"
replace_exact(
    attributes,
    "*.json text eol=lf\n*.py text eol=lf\n",
    "*.json text eol=lf\n*.svg text eol=lf\n*.py text eol=lf\n",
)

test_path = ROOT / "tools" / "test_windows_release_pipeline.py"
replace_exact(
    test_path,
    '''BUILD_RELEASE = ROOT / "tools" / "Build-Release.ps1"
PREVIOUS_FIXTURE = ROOT / "tools" / "Build-PreviousReleaseFixture.ps1"
''',
    '''BUILD_RELEASE = ROOT / "tools" / "Build-Release.ps1"
BUILD_RELEASE_PY = ROOT / "tools" / "build_release.py"
GIT_ATTRIBUTES = ROOT / ".gitattributes"
PREVIOUS_FIXTURE = ROOT / "tools" / "Build-PreviousReleaseFixture.ps1"
''',
)
replace_exact(
    test_path,
    '''    def test_release_workflow_preserves_a_clean_tree_until_validation_finishes(self) -> None:
''',
    '''    def test_production_package_contains_closed_canonical_brand_assets(self) -> None:
        text = BUILD_RELEASE_PY.read_text(encoding="utf-8")
        required = {
            "design/BRAND.md",
            "design/WinCare-Logo.svg",
            "design/WinCare-Wordmark.svg",
            "design/WinCare-Logo-512.png",
            "design/WinCare.ico",
            "design/WinCare-Brand.manifest.json",
        }
        self.assertIn("PRODUCTION_BRAND_FILES", text)
        self.assertIn("required = PRODUCTION_ROOT_FILES | PRODUCTION_CONFIG_FILES | PRODUCTION_BRAND_FILES", text)
        for asset in required:
            self.assertIn(f'"{asset}"', text)

    def test_brand_vector_assets_are_lf_normalized_across_windows_clones(self) -> None:
        text = GIT_ATTRIBUTES.read_text(encoding="utf-8")
        self.assertEqual(text.count("*.svg text eol=lf"), 1)

    def test_release_workflow_preserves_a_clean_tree_until_validation_finishes(self) -> None:
''',
)

for path in (verifier, payload, builder, test_path):
    compile(path.read_text(encoding="utf-8-sig"), str(path), "exec")

metadata_fix = ROOT / ".wincare-finalize" / "previous-release-metadata-fix.py"
compile(metadata_fix.read_text(encoding="utf-8-sig"), str(metadata_fix), "exec")
runpy.run_path(str(metadata_fix), run_name="__main__")

print(
    "Closed release brand, raw ZIP filename, previous-release metadata, "
    "production brand package, and Windows clone line-ending boundaries."
)
