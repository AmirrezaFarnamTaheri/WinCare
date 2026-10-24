param(
    [Parameter(Mandatory = $true)][string] $PackagePath,
    [Parameter(Mandatory = $true)][string] $CertificatePath,
    [string] $PreviousPackagePath
)
$ErrorActionPreference = 'Stop'
if ($env:WINCARE_DISPOSABLE_TEST_HOST -ne '1') {
    throw 'This script changes MSIX registration and certificate trust. Run only on a disposable test VM.'
}
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf) -or -not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) { throw 'Package and certificate required.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator permissions required.' }
$name = 'WinCare.Native'
if (@(Get-AppxPackage -Name $name).Count -gt 0) { throw 'WinCare already exists; refusing to change user data.' }
$cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $CertificatePath).Path)
$sig = Get-AuthenticodeSignature -LiteralPath $PackagePath
if (-not $sig.SignerCertificate -or $sig.SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw 'MSIX signer mismatch.' }
$trustPath = "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)"
$wasTrusted = Test-Path -LiteralPath $trustPath
try {
    if (-not $wasTrusted) {
        $imported = Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
        if (-not $imported) { throw 'Signer import failed.' }
    }
    $previous = $null
    if ($PreviousPackagePath) {
        if (-not (Test-Path -LiteralPath $PreviousPackagePath -PathType Leaf)) { throw 'Previous package missing.' }
        Add-AppxPackage -Path $PreviousPackagePath -ForceApplicationShutdown
        $previous = @(Get-AppxPackage -Name $name)
        if ($previous.Count -ne 1) { throw 'Previous version did not install.' }
    }
    Add-AppxPackage -Path $PackagePath -ForceApplicationShutdown
    $installed = @(Get-AppxPackage -Name $name)
    if ($installed.Count -ne 1) { throw 'Expected one installed WinCare registration.' }
    if (-not $installed[0].Publisher.Contains('CN=WinCare Development')) { throw 'Wrong installed publisher.' }
    if ($previous -and [Version]$installed[0].Version -le [Version]$previous[0].Version) { throw 'Upgrade version did not increase.' }
    Write-Output "Installed version $($installed[0].Version)"
    Remove-AppxPackage -Package $installed[0].PackageFullName
    if (@(Get-AppxPackage -Name $name).Count -gt 0) { throw 'Uninstall failed to clear package registration.' }
    Write-Output 'Installed lifecycle accepted.'
}
finally {
    foreach ($entry in @(Get-AppxPackage -Name $name)) {
        try { Remove-AppxPackage -Package $entry.PackageFullName -ErrorAction Stop }
        catch { Write-Warning "Cleanup failed: $_" }
    }
    if (-not $wasTrusted) {
        try { Remove-Item -LiteralPath $trustPath -Force -ErrorAction Stop }
        catch { Write-Warning "Signer cleanup failed: $_" }
    }
}
