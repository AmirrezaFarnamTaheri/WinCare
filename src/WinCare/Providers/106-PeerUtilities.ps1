<#
.SYNOPSIS
Reports observed peer-transfer prerequisites without claiming that a listener or transport is active.
#>
function Get-WinCarePeerDiscoveryState {
    [CmdletBinding()]
    param([ValidateRange(1,65535)][int]$ListenerPort = 8765)

    $wifiDirectType = $null
    if ($IsWindows) {
        try { $wifiDirectType = [type]::GetType('Windows.Devices.WiFiDirect.WiFiDirectDevice, Windows, ContentType=WindowsRuntime', $false) } catch { $wifiDirectType = $null }
    }
    $listeners = @()
    if ($IsWindows -and (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
        $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $ListenerPort -ErrorAction SilentlyContinue | Select-Object LocalAddress,LocalPort,OwningProcess,State)
    }
    $processes = foreach ($listener in $listeners) {
        $process = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
        [pscustomobject]@{ProcessId=[int]$listener.OwningProcess;ProcessName=if($process){$process.ProcessName}else{$null}}
    }
    [pscustomobject]@{
        DeviceName = [Environment]::MachineName
        WifiDirectApiAvailable = $null -ne $wifiDirectType
        WifiDirectSessionActive = $false
        WebRtcRuntimeBundled = $false
        ListenerPort = $ListenerPort
        ListenerActive = $listeners.Count -gt 0
        Listeners = $listeners
        ListenerProcesses = @($processes)
        TransferServiceImplemented = $false
        EffectiveState = if ($listeners.Count -gt 0) { 'ExternalListenerObserved' } else { 'NoListenerObserved' }
        EvidenceType = 'RuntimeTypeAndSocketObservation'
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCarePeerDiscoveryState }
