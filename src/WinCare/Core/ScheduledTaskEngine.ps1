#requires -Version 7.2
[CmdletBinding()]
param()

function Register-WinCareMaintenanceTask {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$TaskName,
        [Parameter(Mandatory)][string]$PlaybookId,
        [string]$Schedule = 'Daily'
    )
    return @{ TaskName = $TaskName; PlaybookId = $PlaybookId; Status = 'Registered' }
}

function Unregister-WinCareMaintenanceTask {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$TaskName)
    return @{ TaskName = $TaskName; Status = 'Unregistered' }
}
