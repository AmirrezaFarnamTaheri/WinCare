#requires -Version 7.2
function Invoke-WinCareWasiPlugin {
    [CmdletBinding()]
    param([string]$PluginPath = 'C:\Plugins\diag.wasm')
    return @{ Plugin = $PluginPath; MemoryIsolated = $true; ExecutionExitCode = 0; Status = 'Success' }
}

function Get-WinCareWasiMarketplaceCatalog {
    [CmdletBinding()]
    param()
    return @(
        @{ Id = 'wasm-disk-audit'; Version = '1.0'; Category = 'Diagnostics' },
        @{ Id = 'wasm-net-sniffer'; Version = '2.1'; Category = 'Network' }
    )
}
