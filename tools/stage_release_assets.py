import os
import shutil
import sys

def main():
    dest_dir = "release_assets"
    os.makedirs(dest_dir, exist_ok=True)
    src_dir = "artifacts/downloads"

    if not os.path.exists(src_dir):
        print(f"Source directory '{src_dir}' not found!")
        sys.exit(1)

    staged = {}

    for root, dirs, files in os.walk(src_dir):
        for f in files:
            full_path = os.path.join(root, f)
            lower_path = full_path.lower()
            lower_name = f.lower()

            # Skip unneeded files
            if not any(f.endswith(ext) for ext in [".msix", ".zip", ".md", ".json"]):
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
                dest_name = f"WinCare-v2.4.0-rc1-{platform_tag}.msix" if platform_tag else f
            elif f.endswith(".zip"):
                if "portable" in lower_name or "win-" in lower_name:
                    dest_name = f"WinCare-v2.4.0-rc1-{platform_tag}-portable.zip" if platform_tag else f
                else:
                    dest_name = f
            else:
                dest_name = f

            target_path = os.path.join(dest_dir, dest_name)
            # Prefer larger file if duplicate dest_name exists
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


