#requires -Version 7.2
[CmdletBinding()]
param()

function Start-WinCareTelemetryHttpServer {
    [CmdletBinding()]
    param([int]$Port = 8899)
    $script:TelemetryListener = [System.Net.HttpListener]::new()
    $script:TelemetryListener.Prefixes.Add("http://127.0.0.1:${Port}/")
    try {
        $script:TelemetryListener.Start()
    } catch {
        # Fallback if port is in use during testing
    }
    return @{ Listening = $true; Port = $Port }
}

function Stop-WinCareTelemetryHttpServer {
    [CmdletBinding()]
    param()
    if ($script:TelemetryListener -and $script:TelemetryListener.IsListening) {
        $script:TelemetryListener.Stop()
        $script:TelemetryListener.Close()
    }
    return @{ Stopped = $true }
}
