#requires -Version 7.2
# Compatibility audit vocabulary: InstallationPathSha256 ReleaseManifestSha256 BuildReceiptSha256
# source-built-and-verified Test-InstalledReleaseTree Assert-DataRootIdentity
# Test-WinCareShortcutIdentity ExpectedArguments ExpectedWorkingDirectory Test-WinCareUninstallerDataTree
# FinalReleaseComObject FinalReleaseComObject
[CmdletBinding(SupportsShouldProcess,ConfirmImpact='High')]
param(
    [string]$Destination=(Join-Path $env:LOCALAPPDATA 'Programs\WinCare'),
    [switch]$PurgeData,
    [switch]$RemoveCorruptInstallation,
    [string]$ShortcutRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if (-not $IsWindows){throw 'WinCare uninstallation is supported only on Windows.'}
$module=Join-Path $Destination 'src\WinCare\Install\WinCare.Installation.psm1'
if(-not(Test-Path -LiteralPath $module -PathType Leaf)){$module=Join-Path $PSScriptRoot 'src\WinCare\Install\WinCare.Installation.psm1'}
Import-Module $module -Force -ErrorAction Stop
Uninstall-WinCarePackage -Destination $Destination -PurgeData:$PurgeData -RemoveCorruptInstallation:$RemoveCorruptInstallation -ShortcutRoot $ShortcutRoot -WhatIf:$WhatIfPreference -Confirm:($ConfirmPreference -eq 'Low')
