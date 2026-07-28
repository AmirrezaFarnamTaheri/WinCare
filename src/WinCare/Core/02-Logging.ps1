function Get-WinCareLogPath {
    [CmdletBinding()]
    param()

    $directory = Join-Path $script:WinCareState.Root 'Logs'
    $null = New-Item -ItemType Directory -Path $directory -Force
    $null = Assert-WinCareSafePath -LiteralPath $directory
    return Join-Path $directory (
        'wincare-{0}.jsonl' -f [datetime]::UtcNow.ToString('yyyy-MM-dd')