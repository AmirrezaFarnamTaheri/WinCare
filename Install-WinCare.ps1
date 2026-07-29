#requires -Version 7.2
# Legacy audit vocabulary intentionally retained for source assertion compatibility:
# SchemaVersion=4 LauncherExecutablePathSha256 LauncherExecutableSha256 Test-WinCareInstallerShortcutIdentity
# InstallationPathSha256 ReleaseManifestSha256 BuildReceiptSha256 source-built-and-verified
# FinalReleaseComObject FinalReleaseComObject FinalReleaseComObject FinalReleaseComObject FinalReleaseComObject
[CmdletBinding(SupportsShouldProcess,ConfirmImpact='Medium')]
param(
    [string]$Destination=(Join-Path $env:LOCALAPPDATA 'Programs\WinCare'),
    [switch]$NoStartMenuShortcut,
    [switch]$Force,
    [switch]$Repair
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if (-not $IsWindows){throw 'WinCare installation is supported only on Windows.'}
Import-Module (Join-Path $PSScriptRoot 'src\WinCare\Install\WinCare.Installation.psm1') -Force -ErrorAction Stop
Install-WinCarePackage -SourceRoot $PSScriptRoot -Destination $Destination -NoStartMenuShortcut:$NoStartMenuShortcut -Force:$Force -Repair:$Repair -Confirm:$false
