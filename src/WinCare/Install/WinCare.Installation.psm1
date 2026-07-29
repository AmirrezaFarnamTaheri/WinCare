Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$script:ProductGuid='f43eb775-7d08-42ec-9888-8b1bd79e90a3'
$script:ReceiptSchemas=@('wincare.build.receipt/v2','wincare.build.receipt/v3')
$script:MarkerSchema=5

foreach($relative in @(
    'Private\10-Filesystem.ps1',
    'Private\20-Integrity.ps1',
    'Private\30-Shortcuts.ps1',
    'Private\40-Lifecycle.ps1'
)){
    $path=Join-Path $PSScriptRoot $relative
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "WinCare installation implementation file is missing: $path"}
    . $path
}
Export-ModuleMember -Function Install-WinCarePackage,Uninstall-WinCarePackage,Test-WinCareManagedReleaseTree,Test-WinCareShortcutIdentity
