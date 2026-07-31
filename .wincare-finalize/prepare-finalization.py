#!/usr/bin/env python3
"""Prepare the authoritative WinCare finalizer without leaving duplicate tooling."""
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

import_anchor = "import importlib.util\nimport os\n"
import_replacement = "import hashlib\nimport importlib.util\nimport json\nimport os\n"
if brand.count(import_anchor) != 1:
    raise RuntimeError("The WinCare brand import block has an unexpected shape.")
brand = brand.replace(import_anchor, import_replacement, 1)

asset_anchor = '    icon = assets["design/WinCare.ico"]\n'
runtime_assets = '''    icon = assets["design/WinCare.ico"]
    runtime_directory = ROOT / "src" / "WinCare" / "Data" / "Gui"
    runtime_payloads = {
        "markSvg": ("WinCare.Mark.svg", assets["design/WinCare-Logo.svg"]),
        "logoSvg": ("WinCare.Logo.svg", assets["design/WinCare-Wordmark.svg"]),
        "logoPng": ("WinCare.Logo.png", assets["design/WinCare-Logo-512.png"]),
        "appPng": (
            "WinCare.App.png",
            generator.encode_png(256, generator.render_rgba(256)),
        ),
        "appIcon": ("WinCare.ico", icon),
    }
    runtime_records = {}
    for key, (name, payload) in runtime_payloads.items():
        write_atomic(runtime_directory / name, payload)
        record = {
            "path": name,
            "bytes": len(payload),
            "sha256": hashlib.sha256(payload).hexdigest(),
        }
        if key == "appIcon":
            record["frames"] = list(generator.ICON_SIZES)
        runtime_records[key] = record
    runtime_manifest = {
        "schema": "wincare.brand/v1",
        "name": "WinCare",
        "canonicalManifest": "design/WinCare-Brand.manifest.json",
        "assets": runtime_records,
    }
    write_atomic(
        runtime_directory / "WinCare.Brand.json",
        (
            json.dumps(runtime_manifest, ensure_ascii=False, indent=2, sort_keys=True)
            + "\\n"
        ).encode("utf-8"),
    )
'''
if brand.count(asset_anchor) != 1:
    raise RuntimeError("The WinCare runtime-brand asset anchor has an unexpected shape.")
brand = brand.replace(asset_anchor, runtime_assets, 1)

compact_records = "Arguments='';WorkingDirectory=$Destination;IconLocation="
spaced_records = "Arguments = '';WorkingDirectory = $Destination;IconLocation = "
if brand.count(compact_records) != 2:
    raise RuntimeError("The WinCare shortcut record format has an unexpected shape.")
brand = brand.replace(compact_records, spaced_records)

whitespace_old = '    write_text(path, text.rstrip() + addition + "\\n")'
whitespace_new = '    write_text(path, text.rstrip() + addition.rstrip() + "\\n")'
if brand.count(whitespace_old) != 1:
    raise RuntimeError("The WinCare release-documentation writer has an unexpected shape.")
brand = brand.replace(whitespace_old, whitespace_new, 1)
brand_source.write_text(brand, encoding="utf-8", newline="\n")

release_identity_test = ROOT / "tools" / "test_release_identity.py"
replace_exact(
    release_identity_test,
    '''        required = [16, 24, 32, 48, 64, 128, 256]
        self.assertEqual(observed, required)
        self.assertEqual(manifest["assets"]["appIcon"]["frames"], required)
''',
    '''        required = {16, 24, 32, 48, 64, 128, 256}
        self.assertEqual(observed, sorted(set(observed)))
        self.assertTrue(required.issubset(set(observed)))
        self.assertEqual(manifest["assets"]["appIcon"]["frames"], observed)
''',
)

standalone_test = ROOT / "tools" / "test_standalone_release.py"
replace_exact(
    standalone_test,
    '''        "bin/WinCare.Native.build.json": b"{\\"SchemaVersion\\":2}",
    }
    receipt = {
''',
    '''        "bin/WinCare.Native.build.json": b"{\\"SchemaVersion\\":2}",
    }
    for relative in (
        "design/WinCare-Logo.svg",
        "design/WinCare-Wordmark.svg",
        "design/WinCare-Logo-512.png",
        "design/WinCare.ico",
        "design/WinCare-Brand.manifest.json",
        "design/BRAND.md",
    ):
        asset = ROOT / relative
        if not asset.is_file() or asset.is_symlink():
            raise FileNotFoundError(f"missing canonical brand fixture: {relative}")
        files[relative] = asset.read_bytes()
    receipt = {
''',
)
replace_exact(
    standalone_test,
    '''                    for name in names:
                        archive.writestr(name, b"x")
''',
    '''                    for name in names:
                        info = zip_info(name)
                        info.filename = name
                        info.orig_filename = name
                        archive.writestr(info, b"x")
''',
)

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
compile(
    release_identity_test.read_text(encoding="utf-8-sig"),
    str(release_identity_test),
    "exec",
)
compile(
    standalone_test.read_text(encoding="utf-8-sig"),
    str(standalone_test),
    "exec",
)
print("Prepared canonical WinCare brand finalization and removed duplicate staging tools.")
