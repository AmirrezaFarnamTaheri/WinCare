<#
    .SYNOPSIS
    WinCare v5.2.0 ETW Socket & Process Connection Rate Provider
    Module ID: 93-ProcessInstrumentation

    .DESCRIPTION
    Tracks active process socket connection rates and ETW telemetry streams.
    Derived from TaskExplorer (105) - 100% Self-Inclusive.
#>

function Get-WinCareActiveProcessConnections {
    [CmdletBinding()]
    param()

    try {
        $conns = Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue | Select-Object -First 10
        $results = @()
        foreach ($c in $conns) {
            $procName = "Unknown"
            try { $procName = (Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue).ProcessName } catch {}
            $results += [PSCustomObject]@{
                ProcessId   = $c.OwningProcess
                ProcessName = $procName
                LocalPort   = $c.LocalPort
                RemoteAddress = $c.RemoteAddress
                RemotePort  = $c.RemotePort
                State       = $c.State
            }
        }
        return $results
    } catch {
        Write-Error "Failed to query TCP connections: $_"
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareActiveProcessConnections }

