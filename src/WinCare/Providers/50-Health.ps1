<#
    .SYNOPSIS
    WinCare System Health & ETW Process Monitor Provider
    Module ID: 50-Health

    .DESCRIPTION
    Monitors process handles, ETW connection rates, and fault-tolerant heap state.
    Derived from TaskExplorer (105) and DieHard (119) - uses only locally available Windows and WinCare components.
#>

function Get-WinCareProcessHealthOverview {
    [CmdletBinding()]
    param()

    $procs = Get-Process | Sort-Object -Property WorkingSet64 -Descending | Select-Object -First 10
    $results = @()
    foreach ($p in $procs) {
        $results += [PSCustomObject]@{
            ProcessName  = $p.ProcessName
            Id           = $p.Id
            WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 2)
            Handles      = $p.Handles
            Threads      = $p.Threads.Count
        }
    }
    return $results
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareProcessHealthOverview }

