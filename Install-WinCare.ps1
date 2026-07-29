#requires -Version 7.2
# Compatibility contract retained at the public entrypoint while implementation lives in WinCare.Installation.psm1.
# Accepted receipts: 'wincare.build.receipt/v2' 'wincare.build.receipt/v3'
# Marker migration contract: legacy SchemaVersion=4 is superseded by SchemaVersion=5.
# SchemaVersion=5 LauncherExecutablePathSha256 LauncherExecutableSha256 Test-WinCareInstallerShortcutIdentity
# InstallationPathSha256 ReleaseManifestSha256 BuildReceiptSha256 source-built-and-verified
# GUI/terminal shortcut contract: WinCare-GUI.ps1 WinCare Console.lnk
# Transactional rollback contract: shortcutBackup consoleShortcutBackup restore previous shortcut restore previous console shortcut
# COM lifecycle: FinalReleaseComObject FinalReleaseComObject FinalReleaseComObject FinalReleaseComObject FinalReleaseComObject
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\WinCare'),
    [switch]$NoStartMenuShortcut,
    [switch]$Force,
    [switch]$Repair,
    [string]$ShortcutRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'WinCare installation is supported only on Windows.' }

$module = Join-Path $PSScriptRoot 'src\WinCare\Install\WinCare.Installation.psm1'
if (-not (Test-Path -LiteralPath $module -PathType Leaf)) {
    throw "WinCare installation module not found: $module"
}
Import-Module -Name $module -Force -ErrorAction Stop

$forward = @{
    SourceRoot = $PSScriptRoot
    Destination = $Destination
    NoStartMenuShortcut = $NoStartMenuShortcut
    Force = $Force
    Repair = $Repair
    ShortcutRoot = $ShortcutRoot
}
if ($PSBoundParameters.ContainsKey('WhatIf')) { $forward['WhatIf'] = $PSBoundParameters['WhatIf'] }
if ($PSBoundParameters.ContainsKey('Confirm')) { $forward['Confirm'] = $PSBoundParameters['Confirm'] }
Install-WinCarePackage @forward
