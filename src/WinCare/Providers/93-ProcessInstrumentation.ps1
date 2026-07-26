<#
.SYNOPSIS
Observed process TCP connection inventory.
#>
function Get-WinCareActiveProcessConnections {
    [CmdletBinding()]
    param([ValidateRange(1,5000)][int]$MaxResults = 250)

    if (-not $IsWindows -or -not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'TcpInventoryUnavailable' -Message 'Get-NetTCPConnection is unavailable on this platform.' -ExitCode 78
    }
    try {
        $connections = @(Get-NetTCPConnection -State Established -ErrorAction Stop | Sort-Object OwningProcess,LocalPort | Select-Object -First $MaxResults)
        $nameCache = @{}
        $items = foreach ($connection in $connections) {
            $id = [int]$connection.OwningProcess
            if (-not $nameCache.ContainsKey($id)) {
                $process = Get-Process -Id $id -ErrorAction SilentlyContinue
                $nameCache[$id] = if ($process) { $process.ProcessName } else { $null }
            }
            [pscustomobject]@{
                ProcessId=$id;ProcessName=$nameCache[$id]
                LocalAddress=[string]$connection.LocalAddress;LocalPort=[int]$connection.LocalPort
                RemoteAddress=[string]$connection.RemoteAddress;RemotePort=[int]$connection.RemotePort
                State=[string]$connection.State;CreationTime=$connection.CreationTime
            }
        }
        New-WinCareResult -Success $true -Code 'ProcessConnectionsObserved' -Message 'Established TCP connections were observed.' -Data @{Connections=@($items);Count=@($items).Count;Truncated=$connections.Count -ge $MaxResults;EvidenceType='GetNetTcpConnectionObservation'}
    } catch {
        New-WinCareResult -Success $false -Status Failed -Code 'TcpInventoryFailed' -Message $_.Exception.Message -ExitCode 1
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareActiveProcessConnections }
