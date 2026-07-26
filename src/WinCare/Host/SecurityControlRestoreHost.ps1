#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModuleManifest,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$RecordId
)
$ErrorActionPreference='Stop'
try{
    $module=Import-Module -Name $ModuleManifest -Force -PassThru -ErrorAction Stop
    Initialize-WinCareState -SkipConfigSave
    $result=& $module { param($Id) Invoke-WinCareSecurityMaintenanceAutoRestore -RecordId $Id } $RecordId
    if(-not $result.Success){throw $result.Message}
    exit 0
}catch{
    try{Write-EventLog -LogName Application -Source 'Windows PowerShell' -EventId 4100 -Message "WinCare automatic security-control restore failed for ${RecordId}: $($_.Exception.Message)" -ErrorAction SilentlyContinue}catch{Write-Verbose 'Event log reporting was unavailable.'}
    exit 1
}
