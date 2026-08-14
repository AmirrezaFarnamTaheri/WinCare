import os
import shutil
import glob
import sys

def main():
    dest_dir = "release_assets"
    os.makedirs(dest_dir, exist_ok=True)
    src_dir = "artifacts/downloads"

    if not os.path.exists(src_dir):
        print(f"Source directory '{src_dir}' not found!")
        sys.exit(1)

    staged_files = []

    for root, dirs, files in os.walk(src_dir):
        for f in files:
            full_path = os.path.join(root, f)
            lower_name = f.lower()

            # Identify platform from path
            if "x64" in root.lower() or "x86_64" in root.lower() or "win-x64" in root.lower():
                platform_tag = "x64"
            elif "arm64" in root.lower() or "aarch64" in root.lower() or "win-arm64" in root.lower():
                platform_tag = "ARM64"
            else:
                platform_tag = ""

            # Standardize destination filenames
            if f.endswith(".msix"):
                dest_name = f"WinCare-v2.4.0-rc1-{platform_tag}.msix" if platform_tag else f
            elif f.endswith(".zip"):
                if "portable" in lower_name or "win-" in lower_name:
                    dest_name = f"WinCare-v2.4.0-rc1-{platform_tag}-portable.zip" if platform_tag else f
                else:
                    dest_name = f
            elif f.endswith(".md") or f.endswith(".json"):
                dest_name = f
            else:
                continue

            target_path = os.path.join(dest_dir, dest_name)
            shutil.copy2(full_path, target_path)
            staged_files.append(target_path)
            print(f"Staged release asset: '{dest_name}' ({os.path.getsize(target_path)} bytes)")

    print(f"\nTotal release assets staged: {len(staged_files)}")

if __name__ == "__main__":
    main()
