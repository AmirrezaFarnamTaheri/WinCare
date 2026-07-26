#!/usr/bin/env python3
"""
WinGet Package Manifest Generator for WinCare
Generates valid WinGet v1.6 manifest YAMLs (version, installer, locale) for community publishing.
"""

import sys
import os
import json
import hashlib

def generate_winget_manifests(version="1.1.0", installer_url="https://github.com/WinCare/WinCare/releases/download/v1.1.0/WinCare-v1.1.0-x64.msi", sha256_hash="0" * 64, output_dir="release/winget"):
    os.makedirs(output_dir, exist_ok=True)
    
    version_yaml = f"""# Created with WinCare Publishing Engine
PackageIdentifier: WinCare.WinCare
PackageVersion: {version}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"""
    
    installer_yaml = f"""# Created with WinCare Publishing Engine
PackageIdentifier: WinCare.WinCare
PackageVersion: {version}
MinimumOSVersion: 10.0.19041.0
Installers:
  - Architecture: x64
    InstallerType: msi
    InstallerUrl: {installer_url}
    InstallerSha256: {sha256_hash.upper()}
    ProductCode: '{{A1234567-89AB-CDEF-0123-456789ABCDEF}}'
ManifestType: installer
ManifestVersion: 1.6.0
"""
    
    locale_yaml = f"""# Created with WinCare Publishing Engine
PackageIdentifier: WinCare.WinCare
PackageVersion: {version}
PackageLocale: en-US
Publisher: WinCare Open Source Project
PublisherUrl: https://github.com/WinCare/WinCare
PublisherSupportUrl: https://github.com/WinCare/WinCare/issues
Author: WinCare Contributors
PackageName: WinCare Enterprise System Maintenance
PackageUrl: https://github.com/WinCare/WinCare
License: Apache-2.0
LicenseUrl: https://github.com/WinCare/WinCare/blob/main/LICENSE
Copyright: Copyright (c) WinCare Contributors
ShortDescription: Utilitarian Enterprise Windows Maintenance, Security & Storage Intelligence Engine.
Description: WinCare is a high-density enterprise Windows optimization, security baseline compliance, and storage intelligence platform built with native PowerShell and OKLCH UI.
Tags:
  - windows
  - maintenance
  - optimization
  - telemetry
  - security
  - dism
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"""

    with open(os.path.join(output_dir, "WinCare.WinCare.yaml"), "w", encoding="utf-8") as f:
        f.write(version_yaml)
    with open(os.path.join(output_dir, "WinCare.WinCare.installer.yaml"), "w", encoding="utf-8") as f:
        f.write(installer_yaml)
    with open(os.path.join(output_dir, "WinCare.WinCare.locale.en-US.yaml"), "w", encoding="utf-8") as f:
        f.write(locale_yaml)
        
    print(f"[+] Generated WinGet manifests v{version} in {output_dir}")
    return True

if __name__ == "__main__":
    ver = sys.argv[1] if len(sys.argv) > 1 else "1.1.0"
    url = sys.argv[2] if len(sys.argv) > 2 else "https://github.com/WinCare/WinCare/releases/download/v1.1.0/WinCare-v1.1.0-x64.msi"
    hsh = sys.argv[3] if len(sys.argv) > 3 else "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    generate_winget_manifests(ver, url, hsh)
