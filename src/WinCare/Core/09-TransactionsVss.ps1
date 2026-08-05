function New-WinCareVssRestorePointAction {
    [CmdletBinding()]
    param([string]$Description='WinCare Pre-Mutation Restore Point')
    New-WinCareAction -Type 'CreateSystemRestorePoint' `
        -Label "Create VSS restore point: $Description" `
        -Risk Moderate `
        -Parameters @{Description=$Description;RestorePointType='MODIFY_SETTINGS'} `
        -RequiresAdmin $true `
        -Reversible $false `
        -TimeoutSeconds 300
}

function Invoke-WinCareVssRestorePointAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $desc  = [string]$Action.Parameters.Description
    $rtype = [string]$Action.Parameters.RestorePointType
    if([string]::IsNullOrWhiteSpace($desc)){$desc='WinCare Pre-Mutation Restore Point'}
    if([string]::IsNullOrWhiteSpace($rtype)){$rtype='MODIFY_SETTINGS'}
    try {
        if (-not $IsWindows) {
            return New-WinCareResult -Success $false -Status Blocked `
                -Code 'VssNotSupportedOnPlatform' `
                -Message 'VSS SystemRestore is only available on Windows.' `
                -ExitCode 1
        }
        $result = Invoke-CimMethod -Namespace 'root\default' -ClassName 'SystemRestore' -MethodName 'CreateRestorePoint' -Arguments @{
            Description = $desc
            RestorePointType = 12 # MODIFY_SETTINGS
            EventType = 100 # BEGIN_SYSTEM_CHANGE
        } -ErrorAction Stop
        if([int]$result.ReturnValue -ne 0) {
            return New-WinCareResult -Success $false -Status Failed `
                -Code 'VssRestorePointFailed' `
                -Message "SystemRestore.CreateRestorePoint returned $($result.ReturnValue)" `
                -ExitCode 1
        }
        New-WinCareResult -Success $true -Status Succeeded `
            -Code 'VssRestorePointCreated' `
            -Message "VSS restore point created: $desc" `
            -Data @{Description=$desc;RestorePointType=$rtype;EvidenceType='VssSystemRestorePointCreated'}
    } catch {
        New-WinCareResult -Success $false -Status Failed `
            -Code 'VssRestorePointException' `
            -Message $_.Exception.Message -ExitCode 1
    }
}

function Invoke-WinCareDisasterRecoverySnapshot {
    [CmdletBinding()]
    param([string]$Volume='C:\')
    $point = if (Get-Command Checkpoint-Computer -ErrorAction SilentlyContinue) {
        try { Checkpoint-Computer -Description 'WinCare One-Click Recovery Snapshot' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop; $true } catch { $false }
    } else { $false }
    [pscustomobject]@{
        Volume=$Volume
        SnapshotCreated=$point
        Timestamp=[datetime]::UtcNow.ToString('o')
        EvidenceType='DisasterRecoveryVssSnapshot'
    }
}
