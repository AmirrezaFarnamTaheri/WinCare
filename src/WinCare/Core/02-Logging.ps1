function Get-WinCareLogPath {
    [CmdletBinding()]
    param()
    Join-Path (Join-Path $script:WinCareState.Root 'Logs') ("wincare-{0}.jsonl" -f [datetime]::UtcNow.ToString('yyyy-MM-dd'))
}

function Write-WinCareLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('Debug','Info','Warning','Error','Audit')][string]$Level,
        [Parameter(Mandatory)][string]$Message,
        [hashtable]$Data = @{}
    )
    $record = [ordered]@{
        timestamp = [datetime]::UtcNow.ToString('o')
        level     = $Level
        sessionId = $script:WinCareState.SessionId
        message   = $Message
        data      = $Data
    }
    $record | ConvertTo-Json -Compress -Depth 12 | Add-Content -LiteralPath (Get-WinCareLogPath) -Encoding utf8
}

function Remove-WinCareExpiredLogs {
    [CmdletBinding()]
    param()
    $days = [int](Get-WinCareConfig 'LogRetentionDays')
    $cutoff = (Get-Date).AddDays(-$days)
    Get-ChildItem -LiteralPath (Join-Path $script:WinCareState.Root 'Logs') -File -ErrorAction SilentlyContinue |
        Where-Object LastWriteTime -lt $cutoff |
        Remove-Item -Force -ErrorAction SilentlyContinue
}
