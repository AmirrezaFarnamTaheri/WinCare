#!/usr/bin/env python3
"""Apply the reviewed WinCare brand identity to product and release surfaces."""
from __future__ import annotations

import importlib.util
import os
import re
import tempfile
from pathlib import Path

ROOT = Path.cwd().resolve()
MAX_TEXT_BYTES = 8 * 1024 * 1024
MAX_BINARY_BYTES = 32 * 1024 * 1024
MARKER = "wincare.brand.integration/v1"


def read_bounded(path: Path, maximum_bytes: int = MAX_TEXT_BYTES) -> bytes:
    metadata = path.stat(follow_symlinks=False)
    if not path.is_file() or path.is_symlink() or metadata.st_size > maximum_bytes:
        raise ValueError(f"unsafe or oversized file: {path}")
    with path.open("rb") as handle:
        payload = handle.read(maximum_bytes + 1)
    after = path.stat(follow_symlinks=False)
    if len(payload) > maximum_bytes or len(payload) != metadata.st_size:
        raise ValueError(f"file size changed while reading: {path}")
    if (metadata.st_size, metadata.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError(f"file changed while reading: {path}")
    return payload


def read_text(path: Path, maximum_bytes: int = MAX_TEXT_BYTES) -> str:
    return read_bounded(path, maximum_bytes).decode("utf-8-sig")


def write_atomic(path: Path, payload: bytes) -> None:
    destination = path.resolve()
    if ROOT not in destination.parents:
        raise ValueError(f"write escapes repository root: {path}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(prefix=destination.name + ".", suffix=".tmp", dir=destination.parent)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(payload)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary_name, destination)
    finally:
        if os.path.exists(temporary_name):
            os.unlink(temporary_name)


def write_text(path: Path, text: str) -> None:
    write_atomic(path, text.encode("utf-8"))


def load_generator():
    path = ROOT / "tools" / "generate_wincare_brand_assets.py"
    specification = importlib.util.spec_from_file_location("wincare_brand_generator", path)
    if specification is None or specification.loader is None:
        raise RuntimeError("Unable to load WinCare brand generator.")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


def generate_assets() -> bytes:
    generator = load_generator()
    generator.write_assets(ROOT)
    assets = generator.build_assets()
    icon = assets["design/WinCare.ico"]
    if len(icon) < 4096 or len(icon) > MAX_BINARY_BYTES:
        raise ValueError("Generated WinCare icon size is outside the permitted range.")

    existing_icons = [
        path
        for path in ROOT.rglob("WinCare.ico")
        if ".git" not in path.parts and "artifacts" not in path.parts and not path.is_symlink()
    ]
    if not existing_icons:
        existing_icons = [ROOT / "design" / "WinCare.ico"]
    for path in sorted(set(existing_icons)):
        write_atomic(path, icon)
    return icon


def patch_installer() -> None:
    candidates = [ROOT / "Install-WinCare.ps1"]
    candidates.extend(
        path
        for path in ROOT.rglob("Install-WinCare.ps1")
        if path not in candidates and ".git" not in path.parts and "artifacts" not in path.parts
    )
    installers = [path for path in candidates if path.is_file() and not path.is_symlink()]
    if not installers:
        raise ValueError("Install-WinCare.ps1 was not found.")

    for installer in installers:
        text = read_text(installer)
        if "wincare.brand.package-check/v1" not in text:
            anchor = "$ErrorActionPreference = 'Stop'"
            if anchor not in text:
                raise ValueError(f"Installer error-policy anchor is missing: {installer}")
            block = """$ErrorActionPreference = 'Stop'
# wincare.brand.package-check/v1
$brandIconPath = Join-Path $PSScriptRoot 'design\\WinCare.ico'
$brandManifestPath = Join-Path $PSScriptRoot 'design\\WinCare-Brand.manifest.json'
if (-not (Test-Path -LiteralPath $brandIconPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $brandManifestPath -PathType Leaf)) {
    throw 'The WinCare installer package is missing its verified brand identity assets.'
}
"""
            text = text.replace(anchor, block, 1)

        lines = text.splitlines()
        output: list[str] = []
        target_pattern = re.compile(r"^(\s*)(\$[A-Za-z_][A-Za-z0-9_]*)\.TargetPath\s*=\s*(.+?)\s*$")
        inserted = 0
        for index, line in enumerate(lines):
            output.append(line)
            match = target_pattern.match(line)
            if not match:
                continue
            indent, variable, _ = match.groups()
            lookahead = "\n".join(lines[index + 1 : index + 8])
            if re.search(rf"{re.escape(variable)}\.IconLocation\s*=", lookahead):
                continue
            output.append(
                f"{indent}{variable}.IconLocation = [string](Join-Path $Destination 'design\\WinCare.ico') + ',0'"
            )
            inserted += 1

        patched = "\n".join(output) + "\n"
        if ".IconLocation" not in patched:
            raise ValueError(f"Installer shortcut icon integration was not established: {installer}")
        if inserted == 0 and "design\\WinCare.ico" not in patched:
            raise ValueError(f"Installer does not reference the installed WinCare icon: {installer}")
        write_text(installer, patched)


def patch_readme() -> None:
    path = ROOT / "README.md"
    text = read_text(path)
    if "design/WinCare-Wordmark.svg" in text:
        return
    banner = """<p align="center">
  <img src="design/WinCare-Wordmark.svg" width="520" alt="WinCare" />
</p>

"""
    write_text(path, banner + text)


def patch_release_documentation() -> None:
    path = ROOT / "docs" / "RELEASE.md"
    if not path.is_file():
        return
    text = read_text(path)
    if "## Brand identity and executable resources" in text:
        return
    addition = """

## Brand identity and executable resources

The canonical visual identity is generated by `tools/generate_wincare_brand_assets.py`. Release validation must run it in `--check` mode, verify the closed `design/WinCare-Brand.manifest.json`, and confirm every committed `WinCare.ico` is byte-identical to the canonical multi-resolution icon.

All standalone Windows executables inherit the canonical icon through MSBuild and are verified against the same rendered pixel identity after publishing. Installer shortcuts explicitly reference the installed `design/WinCare.ico`; they must never point back to an extraction or build directory. The release receipt and build receipt carry the brand manifest and asset hashes, while the SPDX SBOM covers the actual packaged files.
"""
    write_text(path, text.rstrip() + addition + "\n")


def patch_release_finalizer() -> None:
    path = ROOT / "tools" / "finalize_standalone_release.py"
    text = read_text(path)
    if MARKER not in text:
        anchor = '    manifest_path = executable_directory / "WinCare.Standalone.build.json"\n'
        if anchor not in text:
            raise ValueError("Standalone finalizer brand insertion anchor is missing.")
        block = '''    # wincare.brand.integration/v1
    brand_asset_names = (
        "design/WinCare-Logo.svg",
        "design/WinCare-Wordmark.svg",
        "design/WinCare-Logo-512.png",
        "design/WinCare.ico",
        "design/WinCare-Brand.manifest.json",
        "design/BRAND.md",
    )
    missing_brand_assets = [name for name in brand_asset_names if name not in package_inputs]
    if missing_brand_assets:
        raise ValueError("release omits canonical brand assets: " + ", ".join(missing_brand_assets))
    brand = {
        "schema": "wincare.brand.identity/v1",
        "manifest": {
            "path": "design/WinCare-Brand.manifest.json",
            "sha256": sha256(package_inputs["design/WinCare-Brand.manifest.json"]),
        },
        "logo": {
            "path": "design/WinCare-Logo.svg",
            "sha256": sha256(package_inputs["design/WinCare-Logo.svg"]),
        },
        "wordmark": {
            "path": "design/WinCare-Wordmark.svg",
            "sha256": sha256(package_inputs["design/WinCare-Wordmark.svg"]),
        },
        "icon": {
            "path": "design/WinCare.ico",
            "sha256": sha256(package_inputs["design/WinCare.ico"]),
        },
    }

'''
        text = text.replace(anchor, block + anchor, 1)

    build_anchor = '        "packageInputs": {"treeSha256": package_hash, "members": len(package_inputs)},\n'
    if '        "brand": brand,\n' not in text:
        if build_anchor not in text:
            raise ValueError("Build receipt brand anchor is missing.")
        text = text.replace(build_anchor, build_anchor + '        "brand": brand,\n', 1)

    release_anchor = '        "packageProfile": "production",\n        "archive":'
    release_replacement = '        "packageProfile": "production",\n        "brand": brand,\n        "archive":'
    if release_replacement not in text:
        if release_anchor not in text:
            raise ValueError("Release receipt brand anchor is missing.")
        text = text.replace(release_anchor, release_replacement, 1)

    result_anchor = '        "version": version,\n        "archive": str(archive_path),\n'
    result_replacement = '        "version": version,\n        "brand": brand,\n        "archive": str(archive_path),\n'
    if result_replacement not in text:
        if result_anchor not in text:
            raise ValueError("Build-result brand anchor is missing.")
        text = text.replace(result_anchor, result_replacement, 1)

    output_anchor = '    (output_directory / "WinCare.Standalone.build.json").write_bytes(manifest_bytes)\n'
    output_line = '    (output_directory / f"WinCare-{version}-BRAND.json").write_bytes(package_inputs["design/WinCare-Brand.manifest.json"])\n'
    if output_line not in text:
        if output_anchor not in text:
            raise ValueError("Brand output anchor is missing.")
        text = text.replace(output_anchor, output_anchor + output_line, 1)

    result_path_anchor = '        "standaloneManifest": str(output_directory / "WinCare.Standalone.build.json"),\n'
    result_path_line = '        "brandManifest": str(output_directory / f"WinCare-{version}-BRAND.json"),\n'
    if result_path_line not in text:
        if result_path_anchor not in text:
            raise ValueError("Brand result-path anchor is missing.")
        text = text.replace(result_path_anchor, result_path_anchor + result_path_line, 1)

    write_text(path, text)


def verify_project_icon_contract(icon: bytes) -> None:
    project_files = [
        path
        for path in ROOT.rglob("*.csproj")
        if ".git" not in path.parts and "artifacts" not in path.parts and not path.is_symlink()
    ]
    property_files = [
        path
        for path in ROOT.rglob("Directory.Build.props")
        if ".git" not in path.parts and "artifacts" not in path.parts and not path.is_symlink()
    ]
    icon_references = []
    for path in project_files + property_files:
        text = read_text(path)
        icon_references.extend(re.findall(r"<ApplicationIcon>([^<]+)</ApplicationIcon>", text, flags=re.IGNORECASE))
    if not icon_references:
        raise ValueError("No MSBuild ApplicationIcon contract exists for WinCare executables.")
    if any(not reference.strip().lower().endswith("wincare.ico") for reference in icon_references):
        raise ValueError("An executable project references a non-canonical application icon.")

    icons = [
        path
        for path in ROOT.rglob("WinCare.ico")
        if ".git" not in path.parts and "artifacts" not in path.parts and not path.is_symlink()
    ]
    if not icons:
        raise ValueError("No WinCare.ico files exist after brand generation.")
    for path in icons:
        if read_bounded(path, MAX_BINARY_BYTES) != icon:
            raise ValueError(f"Application icon copy differs from the canonical identity: {path}")


def main() -> int:
    icon = generate_assets()
    patch_installer()
    patch_readme()
    patch_release_documentation()
    patch_release_finalizer()
    verify_project_icon_contract(icon)
    print(f"Applied {MARKER}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
