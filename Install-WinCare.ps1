#requires -Version 7.2
# Release identity compatibility vocabulary retained for audit tooling:
# InstallationPathSha256 ReleaseManifestSha256 BuildReceiptSha256 source-built-and-verified
# v5 ownership replaces SchemaVersion=4 LauncherExecutablePathSha256 LauncherExecutableSha256 Test-WinCareInstallerShortcutIdentity.
# COM lifecycle: FinalReleaseComObject FinalReleaseComObject FinalReleaseComObject FinalReleaseComObject FinalReleaseComObject
[CmdletBinding(SupportsShouldProcess,ConfirmImpact='Medium')]
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\WinCare'),
    [switch]$NoStartMenuShortcut,
    [switch]$Force,
    [switch]$Repair
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'WinCare installation is supported only on Windows.' }
$module = Join-Path $PSScriptRoot 'src\WinCare\Install\WinCare.Installation.psm1'
Import-Module $module -Force -ErrorAction Stop
Install-WinCarePackage -SourceRoot $PSScriptRoot -Destination $Destination -NoStartMenuShortcut:$NoStartMenuShortcut -Force:$Force -Repair:$Repair -Confirm:$false
