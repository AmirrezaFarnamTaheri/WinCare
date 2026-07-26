<#
.SYNOPSIS
Builds a reversible typed plan for Connected User Experiences and selected diagnostic tasks.
#>
function Get-WinCareTelemetrySuppressionState {
    [CmdletBinding()]
    param()
    if (-not $IsWindows) { return [pscustomobject]@{Supported=$false;Service=$null;Tasks=@();EvidenceType='PlatformCapability'} }
    $service = Get-CimInstance Win32_Service -Filter "Name='DiagTrack'" -ErrorAction SilentlyContinue
    $taskSpecs = @(
        @{Path='\Microsoft\Windows\Application Experience\';Name='Microsoft Compatibility Appraiser'},
        @{Path='\Microsoft\Windows\Customer Experience Improvement Program\';Name='Consolidator'},
        @{Path='\Microsoft\Windows\Customer Experience Improvement Program\';Name='UsbCeip'}
    )
    $tasks = foreach ($spec in $taskSpecs) {
        $task = if (Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue) { Get-ScheduledTask -TaskPath $spec.Path -TaskName $spec.Name -ErrorAction SilentlyContinue } else { $null }
        [pscustomobject]@{TaskPath=$spec.Path;TaskName=$spec.Name;Exists=$null -ne $task;Enabled=if($task){[string]$task.State -ne 'Disabled'}else{$null};State=if($task){[string]$task.State}else{'Absent'}}
    }
    [pscustomobject]@{
        Supported=$true
        Service=if($service){[pscustomobject]@{Exists=$true;Name='DiagTrack';State=[string]$service.State;StartMode=[string]$service.StartMode}}else{[pscustomobject]@{Exists=$false;Name='DiagTrack';State='Absent';StartMode='Absent'}}
        Tasks=@($tasks);EvidenceType='CimServiceAndTaskSchedulerObservation'
    }
}

function Set-WinCareTelemetrySuppression {
    [CmdletBinding()]
    param([bool]$DisableDiagTrack = $true,[switch]$Apply,[switch]$Approved,[switch]$PreviewOnly)
    $state = Get-WinCareTelemetrySuppressionState
    if (-not $state.Supported) { return New-WinCareResult -Success $false -Status Blocked -Code 'TelemetryControlsUnsupported' -Message 'Windows telemetry controls are unavailable.' -ExitCode 78 }
    $actions = [Collections.Generic.List[object]]::new()
    if ($state.Service.Exists) {
        $targetRuntime = if ($DisableDiagTrack) {'Stopped'} else {'Running'}
        $targetStart = if ($DisableDiagTrack) {'Disabled'} else {'Manual'}
        $runtime = New-WinCareAction -Type 'SetServiceRuntimeState' -Label "$targetRuntime DiagTrack" -Risk High -Parameters @{Name='DiagTrack';State=$targetRuntime;Before=$state.Service.State} -RequiresAdmin $true -Reversible $true -Compensator @{Type='SetServiceRuntimeState';Parameters=@{Name='DiagTrack';State=$state.Service.State;Before=$targetRuntime};RequiresAdmin=$true} -Verification 'The service runtime state must equal the requested state.'
        $start = New-WinCareAction -Type 'SetServiceStartMode' -Label "Set DiagTrack start mode to $targetStart" -Risk High -Parameters @{Name='DiagTrack';StartMode=$targetStart;Before=$state.Service.StartMode} -RequiresAdmin $true -Reversible $true -Compensator @{Type='SetServiceStartMode';Parameters=@{Name='DiagTrack';StartMode=$state.Service.StartMode;Before=$targetStart};RequiresAdmin=$true} -Verification 'The service start mode must equal the requested state.'
        if ($DisableDiagTrack) { $actions.Add($runtime);$actions.Add($start) } else { $actions.Add($start);$actions.Add($runtime) }
    }
    foreach ($task in @($state.Tasks | Where-Object Exists)) {
        $enabled = -not $DisableDiagTrack
        $actions.Add((New-WinCareAction -Type 'SetScheduledTaskState' -Label "Set $($task.TaskName) enabled=$enabled" -Risk Moderate -Parameters @{TaskPath=$task.TaskPath;TaskName=$task.TaskName;Enabled=$enabled;Before=$task.Enabled} -Reversible $true -Compensator @{Type='SetScheduledTaskState';Parameters=@{TaskPath=$task.TaskPath;TaskName=$task.TaskName;Enabled=$task.Enabled;Before=$enabled}} -Verification 'Task Scheduler must report the requested enabled state.'))
    }
    $plan = New-WinCarePlan -Title $(if($DisableDiagTrack){'Suppress selected Windows diagnostic telemetry'}else{'Restore selected Windows diagnostic telemetry'}) -Description 'Changes only the observed DiagTrack service and selected built-in diagnostic tasks.' -Actions @($actions) -RollbackOnFailure $true -Metadata @{Before=$state;EvidenceType='ObservedBeforeState'}
    if (-not $Apply -and -not $PreviewOnly) { return $plan }
    Invoke-WinCarePlan -Plan $plan -Approved:$Approved -PreviewOnly:$PreviewOnly
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareTelemetrySuppressionState,Set-WinCareTelemetrySuppression }
