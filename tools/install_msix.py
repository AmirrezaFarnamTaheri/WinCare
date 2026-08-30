#!/usr/bin/env python3
"""WinCare 1-Click MSIX Sideloading & Installation Helper.

Performs robust host architecture detection, signer pinning, certificate trust
establishment, and secure MSIX package installation without shell interpolation.
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
    system_root = Path(os.environ.get("SystemRoot", r"C:\\Windows"))
    # Do not inherit a PowerShell 7 module path into Windows PowerShell. Its
    # module manifests can shadow the inbox security module and make the
    # Authenticode cmdlet unavailable even when powershell.exe is used.
    run_env["PSModulePath"] = os.pathsep.join(
        [
            str(Path(os.environ.get("USERPROFILE", "")) / "Documents" / "WindowsPowerShell" / "Modules"),
            str(Path(os.environ.get("ProgramFiles", r"C:\\Program Files")) / "WindowsPowerShell" / "Modules"),
            str(system_root / "System32" / "WindowsPowerShell" / "v1.0" / "Modules"),
        ]
    )
    if env_vars:
        run_env.update(env_vars)
    windows_powershell = system_root / "System32" / "WindowsPowerShell" / "v1.0" / "powershell.exe"
    return subprocess.run(
        # Get-AuthenticodeSignature and Add-AppxPackage are Windows PowerShell
        # cmdlets. Use the inbox executable directly so a PATH-preferred
        # PowerShell 7 installation cannot make MSIX verification fail.
        [str(windows_powershell), "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded_b64],
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
    for candidate in sorted(search_dir.glob("*.msix")):
        if arch.lower() in candidate.name.lower():
            return candidate
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

    package_path: Path | None = args.package
    if package_path is None:
        package_path = find_canonical_package(script_dir, arch) or find_canonical_package(script_dir.parent, arch)

    if package_path is None or not package_path.is_file():
        print(f"[-] Error: Could not locate a valid WinCare MSIX package for architecture '{arch}'.")
        print("    Please specify the path using --package <file.msix>.")
        return 1
    package_path = package_path.resolve()

    cert_path: Path | None = args.certificate
    if cert_path is None:
        cert_candidates = sorted(script_dir.glob("*.cer")) + sorted(script_dir.parent.glob("*.cer"))
        if cert_candidates:
            cert_path = cert_candidates[0]
    if cert_path is not None:
        cert_path = cert_path.resolve()

    env_params = {
        "WINCARE_PKG_PATH": str(package_path),
        "WINCARE_CERT_PATH": str(cert_path) if cert_path and cert_path.is_file() else "",
    }

    print(f"[*] Validating signer and trust for WinCare MSIX package: {package_path.name} ({arch})...")

    verify_and_trust_script = """
$ErrorActionPreference = 'Stop'
$pkgPath = $env:WINCARE_PKG_PATH
$certPath = $env:WINCARE_CERT_PATH

if (-not (Test-Path -LiteralPath $pkgPath -PathType Leaf)) {
    throw "Package file not found: $pkgPath"
}

# Authenticode exposes the signer certificate even when a self-signed signer is
# not trusted yet. Pin the signer before importing any trust material.
$initialSig = Get-AuthenticodeSignature -LiteralPath $pkgPath
if (-not $initialSig.SignerCertificate) {
    throw "MSIX package does not expose a signer certificate (Status: $($initialSig.Status), Detail: $($initialSig.StatusMessage))."
}
if ($initialSig.SignerCertificate.Subject -ne 'CN=WinCare Development') {
    throw "Untrusted signer subject '$($initialSig.SignerCertificate.Subject)'. Expected exactly 'CN=WinCare Development'."
}

if ($certPath) {
    if (-not (Test-Path -LiteralPath $certPath -PathType Leaf)) {
        throw "Certificate file not found: $certPath"
    }
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certPath)
    if ($cert.Subject -ne 'CN=WinCare Development') {
        throw "Provided certificate subject '$($cert.Subject)' does not match expected publisher."
    }
    if ($cert.Thumbprint -ne $initialSig.SignerCertificate.Thumbprint) {
        throw "Provided certificate thumbprint ($($cert.Thumbprint)) does not match package signer thumbprint ($($initialSig.SignerCertificate.Thumbprint))."
    }

    $trustedPeoplePath = "Cert:\\LocalMachine\\TrustedPeople\\$($cert.Thumbprint)"
    if (-not (Test-Path -LiteralPath $trustedPeoplePath)) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Trusting a self-signed MSIX certificate requires an elevated Administrator terminal. Re-run this installer as Administrator.'
        }
        $imported = Import-Certificate -FilePath $certPath -CertStoreLocation 'Cert:\\LocalMachine\\TrustedPeople'
        if (-not $imported) {
            throw 'Certificate import into LocalMachine\\TrustedPeople did not return an imported certificate.'
        }
        Write-Output "Imported pinned signer certificate into LocalMachine\\TrustedPeople: $($cert.Thumbprint)"
    }
    else {
        Write-Output "Pinned signer certificate is already trusted in LocalMachine\\TrustedPeople: $($cert.Thumbprint)"
    }
}
elif ($initialSig.Status -ne 'Valid') {
    throw "Package signer is not already trusted and no matching --certificate was provided (Status: $($initialSig.Status), Detail: $($initialSig.StatusMessage))."
}

# Re-run Authenticode after establishing MSIX trust in Trusted People. No
# Trusted Root Authorities mutation is performed.
$finalSig = Get-AuthenticodeSignature -LiteralPath $pkgPath
if ($finalSig.Status -ne 'Valid') {
    throw "MSIX signature validation failed after trust setup (Status: $($finalSig.Status), Detail: $($finalSig.StatusMessage))."
}
if (-not $finalSig.SignerCertificate -or $finalSig.SignerCertificate.Subject -ne 'CN=WinCare Development') {
    throw 'MSIX signer changed or no longer matches the expected publisher after trust setup.'
}
if ($certPath -and $finalSig.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
    throw 'MSIX signer thumbprint changed after trust setup.'
}

Write-Output "Signature Verified: Valid ($($finalSig.SignerCertificate.Subject))"
Write-Output "Signer Thumbprint: $($finalSig.SignerCertificate.Thumbprint)"
"""

    # PowerShell's keyword is "elseif"; keep the script text readable above and
    # avoid interpolating any user-controlled path into it.
    verify_and_trust_script = verify_and_trust_script.replace("\nelif (", "\nelseif (")

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
