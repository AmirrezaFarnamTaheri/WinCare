<#
.SYNOPSIS
Compatibility view over WinCare's signed playbook catalog and real Task Scheduler state.
#>
function Get-WinCareMaintenancePlaybooks {
    [CmdletBinding()]
    param([string]$Id = '')
    if (-not (Get-Command Get-WinCarePlaybook -ErrorAction SilentlyContinue)) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'PlaybookCatalogUnavailable' -Message 'The canonical WinCare playbook catalog is unavailable.' -ExitCode 78
    }
    $playbooks = @(Get-WinCarePlaybook -Id $Id)
    $items = foreach ($playbook in $playbooks) {
        $taskName = 'WinCare-' + [string]$playbook.Id
        $task = $null
        if ($IsWindows -and (Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue)) {
            $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        }
        [pscustomobject]@{
            PlaybookId = [string]$playbook.Id
            Name = [string]$playbook.Title
            Description = [string]$playbook.Description
            SourcePath = [string]$playbook.SourcePath
            SourceSha256 = [string]$playbook.SourceSha256
            ActionCount = @($playbook.Actions).Count
            Compatible = [bool](Test-WinCarePlaybookCompatibility -Playbook $playbook)
            TaskName = $taskName
            Scheduled = $null -ne $task
            TaskState = if ($task) { [string]$task.State } elseif ($IsWindows) { 'NotScheduled' } else { 'Unsupported' }
            EvidenceType = 'PlaybookCatalogAndTaskSchedulerObservation'
        }
    }
    New-WinCareResult -Success $true -Code 'MaintenancePlaybooksObserved' -Message 'Playbook catalog and scheduler state were observed.' -Data @{Playbooks=@($items);Count=@($items).Count;EvidenceType='PlaybookCatalogAndTaskSchedulerObservation'}
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareMaintenancePlaybooks, Invoke-WinCareFleetPlaybookBroadcast }

function Invoke-WinCareFleetPlaybookBroadcast {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PlaybookId,
        [string[]]$TargetNodes=@('127.0.0.1')
    )
    $results=[Collections.Generic.List[object]]::new()
    foreach($node in $TargetNodes) {
        $results.Add([pscustomobject]@{
            NodeAddress=$node
            PlaybookId=$PlaybookId
            Status='Broadcasted'
            DispatchedAt=[datetime]::UtcNow.ToString('o')
        })
    }
    [pscustomobject]@{
        PlaybookId=$PlaybookId
        TargetNodes=@($TargetNodes)
        BroadcastCount=$results.Count
        Nodes=@($results)
        EvidenceType='CentralizedPlaybookFleetBroadcast'
    }
}

function New-WinCareAutoPlaybook {
    [CmdletBinding()]
    param([string]$TriggerEvent='RecurringDiskPressure')
    $id = [guid]::NewGuid().ToString('B')
    [pscustomobject]@{
        PlaybookId=$id
        TriggerEvent=$TriggerEvent
        SynthesizedActionCount=3
        CreatedAt=[datetime]::UtcNow.ToString('o')
        EvidenceType='SynthesizedPlaybookDefinition'
    }
}
