#!/usr/bin/env python3
"""WinCare 1-Click MSIX Sideloading & Installation Helper.

Performs robust host architecture detection, signing certificate trust verification,
and secure MSIX package installation without shell string interpolation.
"""

from __future__ import annotations

import argparse
import base64
import os
import platform
import subprocess
import sys
from pathlib import Path


def _run_powershell_script(script_text: str, env_vars: dict[str, str] | None = None) -> subprocess.CompletedProcess[str]:
    """Execute a PowerShell script via UTF-16LE Base64 EncodedCommand with environment variables."""
    encoded_bytes = script_text.encode("utf-16le")
    encoded_b64 = base64.b64encode(encoded_bytes).decode("ascii")
    run_env = os.environ.copy()
    if env_vars:
        run_env.update(env_vars)
    return subprocess.run(
        ["powershell", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded_b64],
        capture_output=True,
        text=True,
        env=run_env,
    )


def detect_architecture() -> str:
    """Detect canonical architecture tag ('x64' or 'ARM64')."""
    machine = platform.machine().lower()
    if machine in ("arm64", "aarch64"):
        return "ARM64"
    return "x64"


def find_canonical_package(search_dir: Path, arch: str) -> Path | None:
    """Find the best matching .msix package for the given architecture."""
    # First check exact arch-matching files
    for candidate in sorted(search_dir.glob("*.msix")):
        name_lower = candidate.name.lower()
        if arch.lower() in name_lower:
            return candidate
    # Fallback to any single msix
    all_msix = list(search_dir.glob("*.msix"))
    if len(all_msix) == 1:
        return all_msix[0]
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description="WinCare MSIX Sideloading & Installation Helper")
    parser.add_argument("--package", type=Path, help="Path to the WinCare .msix package to install")
    parser.add_argument("--certificate", type=Path, help="Path to the WinCare public .cer certificate to trust")
    args = parser.parse_args()

    script_dir = Path(__file__).resolve().parent
    arch = detect_architecture()

    # Resolve package
    package_path: Path | None = args.package
    if package_path is None:
        package_path = find_canonical_package(script_dir, arch) or find_canonical_package(script_dir.parent, arch)

    if package_path is None or not package_path.is_file():
        print(f"[-] Error: Could not locate a valid WinCare MSIX package for architecture '{arch}'.")
        print(f"    Please specify the path using --package <file.msix>.")
        return 1

    package_path = package_path.resolve()

    # Resolve certificate
    cert_path: Path | None = args.certificate
    if cert_path is None:
        cert_candidates = list(script_dir.glob("*.cer")) + list(script_dir.parent.glob("*.cer"))
        if cert_candidates:
            cert_path = cert_candidates[0]

    if cert_path is not None:
        cert_path = cert_path.resolve()

    env_params = {
        "WINCARE_PKG_PATH": str(package_path),
        "WINCARE_CERT_PATH": str(cert_path) if cert_path and cert_path.is_file() else "",
    }

    print(f"[*] Validating signature and trust for WinCare MSIX package: {package_path.name} ({arch})...")

    # PowerShell validation and trust establishment script (strictly uses env vars, zero string interpolation)
    verify_and_trust_script = """
$ErrorActionPreference = 'Stop'
$pkgPath = $env:WINCARE_PKG_PATH
$certPath = $env:WINCARE_CERT_PATH

if (-not (Test-Path -Path $pkgPath -PathType Leaf)) {
    Write-Error "Package file not found: $pkgPath"
    exit 1
}

# Verify Authenticode signature on MSIX
$sig = Get-AuthenticodeSignature -FilePath $pkgPath
if ($sig.Status -ne 'Valid') {
    Write-Error "MSIX signature verification failed (Status: $($sig.Status), Detail: $($sig.StatusMessage)). Package is not signed or signature is invalid."
    exit 2
}

$signerSubject = $sig.SignerCertificate.Subject
if ($signerSubject -notmatch 'CN=WinCare Development') {
    Write-Error "Untrusted signer subject '$signerSubject'. Expected publisher 'CN=WinCare Development'."
    exit 3
}

Write-Output "Signature Verified: Valid ($signerSubject)"
Write-Output "Signer Thumbprint: $($sig.SignerCertificate.Thumbprint)"

# If certificate file is provided, verify thumbprint match and import into TrustedPeople
if ($certPath -and (Test-Path -Path $certPath -PathType Leaf)) {
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certPath)
    if ($cert.Thumbprint -ne $sig.SignerCertificate.Thumbprint) {
        Write-Error "Provided certificate thumbprint ($($cert.Thumbprint)) does not match package signer thumbprint ($($sig.SignerCertificate.Thumbprint))."
        exit 4
    }
    $imported = Import-Certificate -FilePath $certPath -CertStoreLocation 'Cert:\\CurrentUser\\TrustedPeople'
    Write-Output "Imported Certificate: $($imported.Thumbprint) into CurrentUser\\TrustedPeople"
}
"""

    verify_res = _run_powershell_script(verify_and_trust_script, env_params)
    if verify_res.returncode != 0:
        print(f"[-] Error: Pre-installation signature / trust verification failed (exit code {verify_res.returncode}):")
        print(verify_res.stderr.strip() or verify_res.stdout.strip())
        return verify_res.returncode

    print(f"[+] Verification passed:\n    {verify_res.stdout.strip()}")
    print(f"[*] Installing WinCare MSIX package: {package_path.name}...")

    install_script = """
$ErrorActionPreference = 'Stop'
$pkgPath = $env:WINCARE_PKG_PATH
Add-AppxPackage -Path $pkgPath
"""
    install_res = _run_powershell_script(install_script, env_params)
    if install_res.returncode == 0:
        print("\n" + "=" * 60)
        print("[SUCCESS] WinCare installed successfully!")
        print("You can launch WinCare directly from your Windows Start Menu.")
        print("=" * 60)
        return 0

    print(f"[-] Error: MSIX installation failed (exit code {install_res.returncode}):")
    print(install_res.stderr.strip() or install_res.stdout.strip())
    return install_res.returncode


if __name__ == "__main__":
    raise SystemExit(main())
