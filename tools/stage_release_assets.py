#!/usr/bin/env python3
"""Stage single binary executables (.exe), MSIX packages, and portable ZIP archives for GitHub release uploads."""

from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import sys
from pathlib import Path


def _compute_sha256(file_path: Path) -> str:
    hasher = hashlib.sha256()
    with open(file_path, "rb") as f:
        while chunk := f.read(65536):
            hasher.update(chunk)
    return hasher.hexdigest()


def _get_default_version() -> str:
    props_path = Path(__file__).resolve().parents[1] / "Directory.Build.props"
    if props_path.is_file():
        import xml.etree.ElementTree as ET
        tree = ET.parse(props_path)
        prefix = tree.findtext(".//VersionPrefix", "2.5.0")
        suffix = tree.findtext(".//VersionSuffix", "")
        return f"v{prefix}-{suffix}" if suffix else f"v{prefix}"
    return "v2.5.0-rc1"


def stage_assets(src_dir: Path, dest_dir: Path, version: str) -> list[Path]:
    """Discover, validate, and stage release assets, failing closed on conflicting duplicates."""
    src_dir = Path(src_dir).resolve()
    dest_dir = Path(dest_dir).resolve()

    if not src_dir.exists():
        raise FileNotFoundError(f"Source directory '{src_dir}' not found!")

    dest_dir.mkdir(parents=True, exist_ok=True)
    version_tag = version if version.startswith(("v", "V")) else f"v{version}"

    staged: dict[str, dict[str, str | int]] = {}

    for root, _, files in os.walk(src_dir):
        for f in files:
            full_path = Path(root) / f
            lower_path = full_path.as_posix().lower()
            lower_name = f.lower()

            # Include .msix, single binary .exe, .zip, .md, .json, .cer, .py
            if not any(f.endswith(ext) for ext in [".msix", ".exe", ".zip", ".md", ".json", ".cer", ".py"]):
                continue

            # Skip intermediate build executables
            if f.endswith(".exe") and ("wincare" not in lower_name and "release" not in lower_path and "portable" not in lower_path):
                continue

            # For MSIX, only accept the final packaged MSIX
            if f.endswith(".msix") and ("wincare.app" not in lower_name and "apppackages" not in lower_path):
                continue

            # Identify platform from path or filename
            if "x64" in lower_path or "x86_64" in lower_path or "win-x64" in lower_path:
                platform_tag = "x64"
            elif "arm64" in lower_path or "aarch64" in lower_path or "win-arm64" in lower_path:
                platform_tag = "ARM64"
            else:
                platform_tag = ""

            # Standardize destination filenames
            if f.endswith(".msix"):
                dest_name = f"WinCare-{version_tag}-{platform_tag}.msix" if platform_tag else f
            elif f.endswith(".exe"):
                dest_name = f"WinCare-{version_tag}-{platform_tag}.exe" if platform_tag else f
            elif f.endswith(".zip") and "portable" in lower_name:
                dest_name = f"WinCare-{version_tag}-{platform_tag}-portable.zip" if platform_tag else f
            else:
                dest_name = f

            file_size = full_path.stat().st_size
            file_sha = _compute_sha256(full_path)

            if dest_name in staged:
                existing = staged[dest_name]
                if existing["sha256"] != file_sha:
                    raise ValueError(
                        f"Conflicting release assets found for destination '{dest_name}': "
                        f"SHA-256 mismatch ({existing['sha256']} from '{existing['src']}' vs "
                        f"{file_sha} from '{full_path}'). Multiple jobs produced divergent artifacts under the same name."
                    )
            else:
                staged[dest_name] = {
                    "src": str(full_path),
                    "dest": str(dest_dir / dest_name),
                    "size": file_size,
                    "sha256": file_sha,
                }

    staged_files: list[Path] = []
    for dest_name, item in sorted(staged.items()):
        dest_path = Path(item["dest"])
        shutil.copy2(item["src"], dest_path)
        staged_files.append(dest_path)
        print(f"Staged release asset: '{dest_name}' ({item['size']} bytes, sha256: {item['sha256'][:12]}...)")

    if not staged_files:
        raise ValueError("No release assets were found to stage!")

    return staged_files


def main() -> int:
    parser = argparse.ArgumentParser(description="Stage release assets for GitHub releases.")
    parser.add_argument("--version", default=os.getenv("WINCARE_VERSION", _get_default_version()), help="Release version string (e.g. v2.5.0-rc1)")
    parser.add_argument("--downloads", default="artifacts/downloads", help="Directory containing downloaded workflow artifacts")
    parser.add_argument("--output", default="release_assets", help="Target release assets staging directory")
    args = parser.parse_args()

    try:
        staged = stage_assets(Path(args.downloads), Path(args.output), args.version)
        print(f"\nTotal release assets staged: {len(staged)}")
        return 0
    except Exception as exc:
        print(f"Error staging release assets: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
