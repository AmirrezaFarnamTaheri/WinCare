#!/usr/bin/env python3
"""Generate WinGet manifests from verified MSI release metadata.

The generator intentionally has no publishable placeholder defaults. Callers must
supply a real installer URL and either the MSI itself or its verified SHA-256.
The MSI ProductCode is optional; when supplied it is validated and emitted.
"""
from __future__ import annotations

import argparse
import hashlib
import re
import tempfile
from pathlib import Path
from urllib.parse import urlsplit

EMPTY_SHA256 = hashlib.sha256(b"").hexdigest()
SHA256_PATTERN = re.compile(r"^[0-9a-fA-F]{64}$")
VERSION_PATTERN = re.compile(r"^[0-9]+(?:\.[0-9]+){1,3}(?:[-+][0-9A-Za-z.-]+)?$")
PRODUCT_CODE_PATTERN = re.compile(
    r"^\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-"
    r"[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?$"
)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_version(version: str) -> str:
    value = version.strip()
    if not VERSION_PATTERN.fullmatch(value):
        raise ValueError("version must be a dotted numeric release version")
    return value


def validate_installer_url(installer_url: str) -> str:
    value = installer_url.strip()
    parsed = urlsplit(value)
    if parsed.scheme != "https" or not parsed.netloc or parsed.username or parsed.password:
        raise ValueError("installer URL must be an HTTPS URL without embedded credentials")
    if not parsed.path.lower().endswith(".msi"):
        raise ValueError("installer URL must identify an MSI file")
    return value


def validate_sha256(value: str) -> str:
    digest = value.strip().lower()
    if not SHA256_PATTERN.fullmatch(digest):
        raise ValueError("installer SHA-256 must contain exactly 64 hexadecimal characters")
    if digest == "0" * 64 or digest == EMPTY_SHA256:
        raise ValueError("installer SHA-256 is a placeholder or the digest of an empty file")
    return digest.upper()


def validate_product_code(product_code: str | None) -> str | None:
    if product_code is None:
        return None
    value = product_code.strip()
    if not PRODUCT_CODE_PATTERN.fullmatch(value):
        raise ValueError("MSI ProductCode must be a GUID")
    return "{" + value.strip("{}").upper() + "}"


def resolve_sha256(sha256_hash: str | None, installer_path: Path | None) -> str:
    if (sha256_hash is None) == (installer_path is None):
        raise ValueError("provide exactly one of sha256_hash or installer_path")
    if installer_path is not None:
        path = installer_path.resolve(strict=True)
        if not path.is_file() or path.suffix.lower() != ".msi":
            raise ValueError("installer_path must reference an existing MSI file")
        if path.stat().st_size <= 0:
            raise ValueError("installer MSI must not be empty")
        sha256_hash = sha256_file(path)
    assert sha256_hash is not None
    return validate_sha256(sha256_hash)


def write_atomic(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        mode="w", encoding="utf-8", newline="\n", dir=path.parent, delete=False
    ) as temporary:
        temporary.write(content)
        temporary_path = Path(temporary.name)
    temporary_path.replace(path)


def generate_winget_manifests(
    version: str,
    installer_url: str,
    *,
    sha256_hash: str | None = None,
    installer_path: Path | None = None,
    product_code: str | None = None,
    output_dir: Path = Path("release/winget"),
) -> tuple[Path, Path, Path]:
    version = validate_version(version)
    installer_url = validate_installer_url(installer_url)
    installer_sha256 = resolve_sha256(sha256_hash, installer_path)
    product_code = validate_product_code(product_code)

    version_yaml = f"""# Generated from verified release metadata; do not edit manually.
PackageIdentifier: WinCare.WinCare
PackageVersion: {version}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"""

    product_code_line = f"    ProductCode: '{product_code}'\n" if product_code else ""
    installer_yaml = f"""# Generated from verified release metadata; do not edit manually.
PackageIdentifier: WinCare.WinCare
PackageVersion: {version}
MinimumOSVersion: 10.0.19041.0
Installers:
  - Architecture: x64
    InstallerType: msi
    InstallerUrl: {installer_url}
    InstallerSha256: {installer_sha256}
{product_code_line}ManifestType: installer
ManifestVersion: 1.6.0
"""

    locale_yaml = f"""# Generated from verified release metadata; do not edit manually.
PackageIdentifier: WinCare.WinCare
PackageVersion: {version}
PackageLocale: en-US
Publisher: WinCare Open Source Project
PublisherUrl: https://github.com/AmirrezaFarnamTaheri/WinCare
PublisherSupportUrl: https://github.com/AmirrezaFarnamTaheri/WinCare/issues
Author: WinCare Contributors
PackageName: WinCare Enterprise System Maintenance
PackageUrl: https://github.com/AmirrezaFarnamTaheri/WinCare
License: Apache-2.0
LicenseUrl: https://github.com/AmirrezaFarnamTaheri/WinCare/blob/master/LICENSE
Copyright: Copyright (c) WinCare Contributors
ShortDescription: Enterprise Windows maintenance, security, and storage intelligence engine.
Description: WinCare provides auditable Windows maintenance, security baseline, and storage intelligence workflows.
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

    output_dir = output_dir.resolve()
    version_path = output_dir / "WinCare.WinCare.yaml"
    installer_path_out = output_dir / "WinCare.WinCare.installer.yaml"
    locale_path = output_dir / "WinCare.WinCare.locale.en-US.yaml"
    write_atomic(version_path, version_yaml)
    write_atomic(installer_path_out, installer_yaml)
    write_atomic(locale_path, locale_yaml)
    return version_path, installer_path_out, locale_path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--installer-url", required=True)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--sha256")
    source.add_argument("--installer-path", type=Path)
    parser.add_argument("--product-code")
    parser.add_argument("--output-dir", type=Path, default=Path("release/winget"))
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        paths = generate_winget_manifests(
            args.version,
            args.installer_url,
            sha256_hash=args.sha256,
            installer_path=args.installer_path,
            product_code=args.product_code,
            output_dir=args.output_dir,
        )
    except (OSError, ValueError) as exc:
        raise SystemExit(f"error: {exc}") from exc
    print("Generated verified WinGet manifests:")
    for path in paths:
        print(f"  {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
