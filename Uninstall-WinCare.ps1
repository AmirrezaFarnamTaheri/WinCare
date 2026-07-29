#requires -Version 7.2
# Compatibility contract retained at the public entrypoint while implementation lives in WinCare.Installation.psm1.
# Accepted receipts: 'wincare.build.receipt/v2' 'wincare.build.receipt/v3'
# InstallationPathSha256 ReleaseManifestSha256 BuildReceiptSha256 source-built-and-verified
# Test-WinCareShortcutIdentity ExpectedArguments ExpectedWorkingDirectory Test-WinCareUninstallerDataTree Assert-DataRootIdentity
# Tombstone validation contract: $safeTombstone = $tombstone; Test-InstalledReleaseTree -Root $safeTombstone
# Transactional shortcut rollback contract: shortcutBackup consoleShortcutBackup restore shortcut restore console shortcut
# COM lifecycle: FinalReleaseComObject FinalReleaseComObject
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
