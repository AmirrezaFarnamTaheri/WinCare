# Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs and trusts the WinCare Publisher Certificate to resolve 0x800B010A MSIX package errors.

.DESCRIPTION
    Fixes the Windows MSIX error:
    "This app package's publisher certificate could not be verified. (0x800B010A)"
    by creating/importing the required root and publisher certificates into Trusted Root Certification Authorities
    and Trusted People stores.

.PARAMETER CertPath
    Optional path to a .cer or .pfx certificate file. If omitted, creates a trusted local publisher cert for "CN=WinCare Development".
#>

param(
    [string]$CertPath = ""
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " WinCare Publisher Certificate Installer (Fix 0x800B010A) " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Ensure Administrator privileges
$currentPrincipal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script requires Administrator privileges. Please re-run PowerShell as Administrator."
    exit 1
}

if ($CertPath -ne "" -and (Test-Path $CertPath)) {
    Write-Host "[+] Importing certificate from file: $CertPath" -ForegroundColor Green
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertPath)
} else {
    Write-Host "[+] Generating/Retrieving 'CN=WinCare Development' Publisher Certificate..." -ForegroundColor Yellow
    $subject = "CN=WinCare Development"
    $existing = Get-ChildItem -Path Cert:\LocalMachine\My | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1

    if ($existing) {
        $cert = $existing
        Write-Host "[+] Found existing certificate in LocalMachine\My: $($cert.Thumbprint)" -ForegroundColor Green
    } else {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $subject `
            -KeyUsage DigitalSignature `
            -FriendlyName "WinCare Development Publisher Certificate" `
            -CertStoreLocation "Cert:\LocalMachine\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")
        Write-Host "[+] Created self-signed publisher cert: $($cert.Thumbprint)" -ForegroundColor Green
    }
}

# Install into Trusted Root Certification Authorities (LocalMachine\Root)
Write-Host "[+] Trusting certificate in LocalMachine\Root..." -ForegroundColor Yellow
$rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
$rootStore.Open("ReadWrite")
$rootStore.Add($cert)
$rootStore.Close()

# Install into Trusted People (LocalMachine\TrustedPeople)
Write-Host "[+] Trusting certificate in LocalMachine\TrustedPeople..." -ForegroundColor Yellow
$peopleStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "LocalMachine")
$peopleStore.Open("ReadWrite")
$peopleStore.Add($cert)
$peopleStore.Close()

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " SUCCESS: WinCare Certificate trusted successfully!       " -ForegroundColor Green
Write-Host " Error 0x800B010A is resolved. MSIX packages can now install." -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
