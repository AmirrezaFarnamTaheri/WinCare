#!/usr/bin/env python3
"""Generate Chocolatey package metadata from verified MSI release evidence."""
from __future__ import annotations

import argparse
import hashlib
import re
from pathlib import Path
from urllib.parse import urlsplit

EMPTY_SHA256 = hashlib.sha256(b"").hexdigest()
SHA256_PATTERN = re.compile(r"^[0-9a-fA-F]{64}$")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_hash(value: str) -> str:
    value = value.strip().lower()
    if not SHA256_PATTERN.fullmatch(value) or value in {"0" * 64, EMPTY_SHA256}:
        raise ValueError("installer checksum is missing, placeholder, or invalid")
    return value


def resolve_hash(value: str | None, installer: Path | None) -> str:
    if (value is None) == (installer is None):
        raise ValueError("provide exactly one checksum source")
    if installer:
        if not installer.is_file() or installer.suffix.lower() != ".msi":
            raise ValueError("installer must be an MSI file")
        value = sha256_file(installer)
    assert value
    return validate_hash(value)


def generate_chocolatey_package(version: str, installer_url: str, checksum: str, output_dir: Path):
    parsed = urlsplit(installer_url)
    if parsed.scheme != "https" or not parsed.path.lower().endswith(".msi"):
        raise ValueError("installer URL must be an HTTPS MSI URL")

    output_dir.mkdir(parents=True, exist_ok=True)
    tools = output_dir / "tools"
    tools.mkdir(exist_ok=True)

    nuspec = f'''<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd">
  <metadata>
    <id>wincare</id>
    <version>{version}</version>
    <title>WinCare Enterprise Windows Maintenance</title>
    <authors>WinCare Contributors</authors>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
  </metadata>
</package>
'''

    install = f'''$ErrorActionPreference = 'Stop'
$packageName = 'wincare'
$toolsDir = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$url64 = '{installer_url}'
$checksum64 = '{checksum}'

Install-ChocolateyPackage @{{
  packageName = $packageName
  fileType = 'msi'
  url64Bit = $url64
  checksum64 = $checksum64
  checksumType64 = 'sha256'
  silentArgs = '/qn /norestart'
}}
'''

    (output_dir / "wincare.nuspec").write_text(nuspec, encoding="utf-8")
    (tools / "chocolateyInstall.ps1").write_text(install, encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True)
    parser.add_argument("--installer-url", required=True)
    parser.add_argument("--sha256")
    parser.add_argument("--installer-path", type=Path)
    parser.add_argument("--output-dir", type=Path, default=Path("release/chocolatey"))
    args = parser.parse_args()
    generate_chocolatey_package(
        args.version,
        args.installer_url,
        resolve_hash(args.sha256, args.installer_path),
        args.output_dir,
    )
