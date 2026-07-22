<#
    .SYNOPSIS
    WinCare v5.2.0 Windows Update Orchestration Provider
    Module ID: 60-Updates

    .DESCRIPTION
    Triggers Windows Update scans and manages WU service state.
#>

function Get-WinCareUpdateServiceState {
    [CmdletBinding()]
    param()

    $svc = Get-Service -Name "wuauserv" -ErrorAction SilentlyContinue
    return [PSCustomObject]@{
        ServiceName = "wuauserv"
        DisplayName = "Windows Update"
        Status      = if ($svc) { $svc.Status } else { "Unknown" }
        StartType   = if ($svc) { $svc.StartType } else { "Unknown" }
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareUpdateServiceState }

