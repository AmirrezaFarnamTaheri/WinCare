#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ResultPath = (Join-Path $OutputDirectory 'previous-release-fixture.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The previous-release fixture must be built on Windows.' }

$rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
. (Join-Path $rootPath 'tools\WinCare.Tooling.ps1')
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$resultFullPath = [IO.Path]::GetFullPath($ResultPath)
if (Test-Path -LiteralPath $outputPath) {
    $item = Get-Item -LiteralPath $outputPath -Force -ErrorAction Stop
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Unsafe previous-release output directory: $outputPath"
    }
    if (@(Get-ChildItem -LiteralPath $outputPath -Force -ErrorAction Stop).Count) {
        throw "Previous-release output directory must be empty: $outputPath"
    }
} else {
    [void][IO.Directory]::CreateDirectory($outputPath)
}

$workRoot = Join-Path $env:TEMP ('WinCare-previous-release-' + [guid]::NewGuid().ToString('N'))
$cloneRoot = Join-Path $workRoot 'repository'
try {
    [void][IO.Directory]::CreateDirectory($workRoot)
    & git clone --no-hardlinks --local -- $rootPath $cloneRoot
    if ($LASTEXITCODE -ne 0) { throw 'Local previous-release fixture clone failed.' }

    Push-Location $cloneRoot
    try {
        $manifestPath = Join-Path $cloneRoot 'src\WinCare\WinCare.psd1'
        $manifest = Import-PowerShellDataFile -LiteralPath $manifestPath
        $current = [version][string]$manifest.ModuleVersion
        if ($current.Build -gt 0) {
            $previous = [version]::new($current.Major, $current.Minor, $current.Build - 1)
        } elseif ($current.Minor -gt 0) {
            $previous = [version]::new($current.Major, $current.Minor - 1, 9)
        } elseif ($current.Major -gt 0) {
            $previous = [version]::new($current.Major - 1, 9, 9)
        } else {
            throw "Cannot derive a previous semantic version from $current."
        }

        $text = Read-WinCareToolingBoundedUtf8Text -LiteralPath $manifestPath -MaximumBytes 1048576 -Purpose 'Previous-release module manifest'
        $pattern = [regex]::new("(?m)^(\s*ModuleVersion\s*=\s*)'[^']+'")
        $updated = $pattern.Replace($text, "`$1'$previous'", 1)
        if ($updated -eq $text) { throw 'The fixture module version was not updated.' }
        [IO.File]::WriteAllText($manifestPath, $updated, [Text.UTF8Encoding]::new($false))

        & python tools/validate_source_references.py . --write
        if ($LASTEXITCODE -ne 0) { throw 'Previous-release source-reference refresh failed.' }

        git config user.name 'wincare-fixture-bot'
        git config user.email 'wincare-fixture-bot@users.noreply.github.com'
        git add -A
        git diff --cached --check
        if ($LASTEXITCODE -ne 0) { throw 'Previous-release fixture diff validation failed.' }
        git commit -m "test fixture: build WinCare $previous"
        if ($LASTEXITCODE -ne 0) { throw 'Previous-release fixture commit failed.' }
        $fixtureCommit = (git rev-parse HEAD).Trim()

        & (Join-Path $cloneRoot 'tools\Build-Release.ps1') `
            -Root $cloneRoot `
            -OutputDirectory $outputPath `
            -SkipTests
        if ($LASTEXITCODE -ne 0) { throw 'Previous-release fixture build failed.' }

        $archive = Join-Path $outputPath "WinCare-$previous.zip"
        if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
            throw "Previous-release fixture archive is missing: $archive"
        }
        $archiveItem = Get-Item -LiteralPath $archive -Force -ErrorAction Stop
        if ($archiveItem.Length -lt 1) { throw 'Previous-release fixture archive is empty.' }
        $archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        $result = [ordered]@{
            schema = 'wincare.previous-release.fixture/v1'
            status = 'passed'
            currentVersion = [string]$current
            previousVersion = [string]$previous
            fixtureCommit = $fixtureCommit
            archive = $archive
            archiveSha256 = $archiveHash
            bytes = [long]$archiveItem.Length
        }
        [IO.File]::WriteAllText(
            $resultFullPath,
            (($result | ConvertTo-Json -Depth 8) + [Environment]::NewLine),
            [Text.UTF8Encoding]::new($false)
        )
        [pscustomobject]$result
    } finally {
        Pop-Location
    }
} finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
