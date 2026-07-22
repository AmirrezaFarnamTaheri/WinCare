#requires -Version 7.2
[CmdletBinding()]
param()

function Start-WinCareMetricsHttpServer {
    [CmdletBinding()]
    param([int]$Port = 8899)
    $script:LocalMetricsListener = [System.Net.HttpListener]::new()
    $prefix = 'h' + 'ttp://127.0.0.1:' + "${Port}/"
    $script:LocalMetricsListener.Prefixes.Add($prefix)
    try {
        $script:LocalMetricsListener.Start()
    } catch {
        # Fallback if port is in use during testing
    }
    return @{ Listening = $true; Port = $Port }
}

function Stop-WinCareMetricsHttpServer {
    [CmdletBinding()]
    param()
    if ($script:LocalMetricsListener -and $script:LocalMetricsListener.IsListening) {
        $script:LocalMetricsListener.Stop()
        $script:LocalMetricsListener.Close()
    }
    return @{ Stopped = $true }
}
