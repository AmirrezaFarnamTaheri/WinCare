function Get-WinCareLogPath {
    [CmdletBinding()]
    param()

    $directory = Join-Path $script:WinCareState.Root 'Logs'
    $null = New-Item -ItemType Directory -Path $directory -Force
    $null = Assert-WinCareSafePath -LiteralPath $directory
    return Join-Path $directory (
        'wincare-{0}.jsonl' -f [datetime]::UtcNow.ToString('yyyy-MM-dd')
    )
}

function Get-WinCareLogMutexName {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath)

    $bytes = [Text.Encoding]::UTF8.GetBytes(
        (Resolve-WinCareCanonicalPath -LiteralPath $LiteralPath -AllowMissing).ToLowerInvariant()
    )
    try {
        $hash = [Security.Cryptography.SHA256]::HashData($bytes)
        try { return 'Local\WinCare.Log.' + [Convert]::ToHexString($hash) }
        finally { [Array]::Clear($hash, 0, $hash.Length) }
    } finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}

function Write-WinCareLogLine {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [Parameter(Mandatory)][string]$Line,
        [ValidateRange(1, 30)][int]$LockTimeoutSeconds = 5,
        [ValidateRange(1024, 1048576)][int]$MaximumLogRecordBytes = 1048576
    )

    $path = Assert-WinCareSafePath -LiteralPath $LiteralPath -AllowMissing
    $payload = [Text.UTF8Encoding]::new($false).GetBytes($Line + "`n")
    if ($payload.Length -gt $MaximumLogRecordBytes) {
        [Array]::Clear($payload, 0, $payload.Length)
        throw 'Log record exceeds the 1 MiB limit.'
    }
    $mutex = [Threading.Mutex]::new($false, (Get-WinCareLogMutexName $path))
    $acquired = $false
    $stream = $null
    try {
        try { $acquired = $mutex.WaitOne($LockTimeoutSeconds * 1000) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'Timed out acquiring the WinCare log lock.' }
        $stream = [IO.FileStream]::new(
            $path,
            [IO.FileMode]::Append,
            [IO.FileAccess]::Write,
            [IO.FileShare]::Read,
            4096,
            [IO.FileOptions]::WriteThrough
        )
        $stream.Write($payload, 0, $payload.Length)
        $stream.Flush($true)
    } finally {
        if ($stream) { $stream.Dispose() }
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
        [Array]::Clear($payload, 0, $payload.Length)
    }
}

function Write-WinCareLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Debug', 'Info', 'Warning', 'Error', 'Audit')]
        [string]$Level,
        [Parameter(Mandatory)][string]$Message,
        [hashtable]$Data = @{}
    )

    if($script:WinCareState -is [Collections.IDictionary] -and
        [bool]$script:WinCareState.ReadOnlyLocked) {
        return
    }
    $safeMessage = ConvertTo-WinCareRedactedScalar -Value $Message
    $safeData = ConvertTo-WinCareRedactedValue -Value $Data
    $record = [ordered]@{
        schemaVersion = 1
        timestamp = [datetime]::UtcNow.ToString('o')
        level = $Level
        sessionId = [string]$script:WinCareState.SessionId
        processId = $PID
        message = $safeMessage
        data = $safeData
    }
    $json = $record | ConvertTo-Json -Compress -Depth 20
    Write-WinCareLogLine -LiteralPath (Get-WinCareLogPath) -Line $json
}

function Remove-WinCareExpiredLogs {
    [CmdletBinding()]
    param()

    if($script:WinCareState -is [Collections.IDictionary] -and
        [bool]$script:WinCareState.ReadOnlyLocked) {
        return
    }
    $days = [int](Get-WinCareConfig 'LogRetentionDays')
    $cutoff = [datetime]::UtcNow.AddDays(-$days)
    $directory = Assert-WinCareSafePath `
        -LiteralPath (Join-Path $script:WinCareState.Root 'Logs')
    Get-ChildItem -LiteralPath $directory -File -ErrorAction SilentlyContinue |
        Where-Object LastWriteTimeUtc -lt $cutoff |
        ForEach-Object {
            $path = Assert-WinCareSafePath -LiteralPath $_.FullName `
                -AllowedRoots @($directory)
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
}
