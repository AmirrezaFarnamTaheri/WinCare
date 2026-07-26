#!/usr/bin/env python3
"""
Chocolatey Package Generator for WinCare
Generates wincare.nuspec and chocolateyInstall.ps1 for Chocolatey community repository publishing.
"""

import sys
import os

def generate_chocolatey_package(version="1.1.0", installer_url="https://github.com/WinCare/WinCare/releases/download/v1.1.0/WinCare-v1.1.0-x64.msi", sha256_hash="0" * 64, output_dir="release/chocolatey"):
    os.makedirs(os.path.join(output_dir, "tools"), exist_ok=True)
    
    nuspec = f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd">
  <metadata>
    <id>wincare</id>
    <version>{version}</version>
    <title>WinCare Enterprise Windows Maintenance</title>
    <authors>WinCare Contributors</authors>
    <projectUrl>https://github.com/WinCare/WinCare</projectUrl>
    <licenseUrl>https://github.com/WinCare/WinCare/blob/main/LICENSE</licenseUrl>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <tags>wincare maintenance windows optimization security enterprise</tags>
    <summary>Utilitarian Enterprise Windows Maintenance, Security & Storage Intelligence Engine.</summary>
    <description>WinCare is a high-density enterprise Windows optimization, security baseline compliance, and storage intelligence platform built with native PowerShell and OKLCH UI.</description>
  </metadata>
  <files>
    <file src="tools\\**" target="tools" />
  </files>
</package>
"""

    install_ps1 = f"""$ErrorActionPreference = 'Stop';
$packageName= 'wincare'
$toolsDir   = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$url64      = '{installer_url}'
$checksum64 = '{sha256_hash}'

$packageArgs = @{{
  packageName   = $packageName
  unzipLocation = $toolsDir
  fileType      = 'msi'
  url64Bit      = $url64
  checksum64    = $checksum64
  checksumType64= 'sha256'
  silentArgs    = '/qn /norestart'
  validExitCodes= @(0, 3010, 1641)
}}

Install-ChocolateyPackage @packageArgs
"""

    uninstall_ps1 = """$ErrorActionPreference = 'Stop';
$packageName = 'wincare'
$fileType = 'msi'
$silentArgs = '/qn /norestart'
Uninstall-ChocolateyPackage -PackageName $packageName -FileType $fileType -SilentArgs $silentArgs
"""

    with open(os.path.join(output_dir, "wincare.nuspec"), "w", encoding="utf-8") as f:
        f.write(nuspec)
    with open(os.path.join(output_dir, "tools", "chocolateyInstall.ps1"), "w", encoding="utf-8") as f:
        f.write(install_ps1)
    with open(os.path.join(output_dir, "tools", "chocolateyUninstall.ps1"), "w", encoding="utf-8") as f:
        f.write(uninstall_ps1)
        
    print(f"[+] Generated Chocolatey package v{version} in {output_dir}")
    return True

if __name__ == "__main__":
    ver = sys.argv[1] if len(sys.argv) > 1 else "1.1.0"
    url = sys.argv[2] if len(sys.argv) > 2 else "https://github.com/WinCare/WinCare/releases/download/v1.1.0/WinCare-v1.1.0-x64.msi"
    hsh = sys.argv[3] if len(sys.argv) > 3 else "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    generate_chocolatey_package(ver, url, hsh)
