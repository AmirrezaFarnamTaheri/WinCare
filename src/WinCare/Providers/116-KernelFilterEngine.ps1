#requires -Version 7.2
function Enable-WinCareEbpfFilter {
    [CmdletBinding()]
    param([string]$Mode = 'XDP_DROP')
    return @{ FilterMode = $Mode; ActiveRules = 8; HardwareOffload = $true; Status = 'Active' }
}

function Set-WinCareMicrosegmentationRule {
    [CmdletBinding()]
    param([string]$ProcessName = 'svchost.exe', [string]$Action = 'Block')
    return @{ Process = $ProcessName; Action = $Action; Status = 'Applied' }
}
