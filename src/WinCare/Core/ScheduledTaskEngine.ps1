#requires -Version 7.2
function Register-WinCareMaintenanceTask {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]{3,80}$')][string]$TaskName,[Parameter(Mandatory)][string]$PlaybookId,[ValidatePattern('^(daily@(?:[01]\d|2[0-3]):[0-5]\d|weekly:(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)@(?:[01]\d|2[0-3]):[0-5]\d)$')][string]$Schedule='daily@03:00',[switch]$Apply,[switch]$Approved,[switch]$PreviewOnly)
    $playbook=Get-WinCarePlaybook -Id $PlaybookId|Select-Object -First 1
    if(-not $playbook){return New-WinCareResult -Success $false -Status Failed -Code 'PlaybookNotFound' -Message "Playbook not found: $PlaybookId" -ExitCode 2}
    $profile=[ordered]@{schemaVersion=1;id=$TaskName;title="Scheduled playbook: $($playbook.Title)";command='playbook';arguments=@{Id=$PlaybookId};apply=$true;schedule=$Schedule;maximumRuntimeMinutes=240;batteryAllowed=$false;networkRequired=$false;notification='EventLog';sourceRecords=@('ScheduledTaskEngineCompatibility')}
    $plan=New-WinCareAutomationTaskPlan -Profile $profile
    if(-not $Apply -and -not $PreviewOnly){return $plan};Invoke-WinCarePlan -Plan $plan -Approved:$Approved -PreviewOnly:$PreviewOnly
}
function Unregister-WinCareMaintenanceTask {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]{3,80}$')][string]$TaskName,[switch]$Apply,[switch]$Approved,[switch]$PreviewOnly)
    $profile=Get-WinCareAutomationProfile -Id $TaskName|Select-Object -First 1
    if(-not $profile){return New-WinCareResult -Success $true -Code 'MaintenanceTaskAlreadyAbsent' -Message 'The maintenance task and profile are already absent.' -Data @{TaskName='WinCare-'+$TaskName;EvidenceType='ProtectedProfileAbsence'}}
    $plan=New-WinCareAutomationTaskPlan -Profile $profile -Remove
    if(-not $Apply -and -not $PreviewOnly){return $plan};Invoke-WinCarePlan -Plan $plan -Approved:$Approved -PreviewOnly:$PreviewOnly
}
