#!/usr/bin/env python3
"""Stage single binary executables (.exe), MSIX packages, and portable ZIP archives for GitHub release uploads."""

import argparse
import os
import shutil
import sys

def main():
    parser = argparse.ArgumentParser(description="Stage release assets for GitHub releases.")
    parser.add_argument("--version", default=os.getenv("WINCARE_VERSION", "v2.4.0-rc1"), help="Release version string (e.g. v2.4.0-rc1 or v2.4.0)")
    parser.add_argument("--downloads", default="artifacts/downloads", help="Directory containing downloaded workflow artifacts")
    parser.add_argument("--output", default="release_assets", help="Target release assets staging directory")
    args = parser.parse_args()

    version_str = args.version
    if not version_str.startswith("v") and not version_str.startswith("V"):
        version_tag = f"v{version_str}"
    else:
        version_tag = version_str

    dest_dir = args.output
    os.makedirs(dest_dir, exist_ok=True)
    src_dir = args.downloads

    if not os.path.exists(src_dir):
        print(f"Source directory '{src_dir}' not found!")
        sys.exit(1)

    staged = {}

    for root, dirs, files in os.walk(src_dir):
        for f in files:
            full_path = os.path.join(root, f)
            lower_path = full_path.lower()
            lower_name = f.lower()

            # Include .msix, single binary .exe, .zip, .md, .json
            if not any(f.endswith(ext) for ext in [".msix", ".exe", ".zip", ".md", ".json"]):
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

            target_path = os.path.join(dest_dir, dest_name)
            file_size = os.path.getsize(full_path)
            if dest_name not in staged or file_size > staged[dest_name]["size"]:
                staged[dest_name] = {"src": full_path, "dest": target_path, "size": file_size}

    staged_files = []
    for dest_name, item in sorted(staged.items()):
        shutil.copy2(item["src"], item["dest"])
        staged_files.append(item["dest"])
        print(f"Staged release asset: '{dest_name}' ({item['size']} bytes)")

    print(f"\nTotal release assets staged: {len(staged_files)}")
    if not staged_files:
        print("Error: No release assets were found to stage!")
        sys.exit(1)

if __name__ == "__main__":
    main()
