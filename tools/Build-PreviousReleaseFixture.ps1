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

function Get-WinCareFixtureArchiveManifestVersion {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ArchivePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entries = @(
            $archive.Entries | Where-Object {
                $_.FullName -match '^[^/]+/src/WinCare/WinCare\.psd1$'
            }
        )
        if ($entries.Count -ne 1) {
            throw "Expected one module manifest in the previous-release archive; found $($entries.Count)."
        }
        $entry = $entries[0]
        if ($entry.Length -lt 1 -or $entry.Length -gt 1048576) {
            throw 'The archived module manifest size is outside the permitted range.'
        }
        $stream = $entry.Open()
        try {
            $reader = [IO.StreamReader]::new($stream, [Text.UTF8Encoding]::new($false, $true), $true, 4096, $true)
            try {
                $builder = [Text.StringBuilder]::new(
                    [int][Math]::Min([long]$entry.Length, 1048576L)
                )
                $buffer = [char[]]::new(4096)
                try {
                    $totalCharacters = 0
                    while (($count = $reader.Read($buffer, 0, $buffer.Length)) -gt 0) {
                        $totalCharacters += $count
                        if ($totalCharacters -gt 1048576) {
                            throw 'The archived module manifest exceeds the permitted decoded length.'
                        }
                        $null = $builder.Append($buffer, 0, $count)
                    }
                    $text = $builder.ToString()
                } finally {
                    [Array]::Clear($buffer, 0, $buffer.Length)
                }
            } finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
        $match = [regex]::Match($text, "(?m)^\s*ModuleVersion\s*=\s*'([^']+)'\s*$")
        if (-not $match.Success) { throw 'The archived module manifest does not declare ModuleVersion.' }
        [version]$match.Groups[1].Value
    } finally {
        $archive.Dispose()
    }
}

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
        if ($previous -ge $current) { throw "Derived previous version is not lower: $previous >= $current" }

        $text = Read-WinCareToolingBoundedUtf8Text -LiteralPath $manifestPath -MaximumBytes 1048576 -Purpose 'Previous-release module manifest'
        $pattern = [regex]::new("(?m)^(\s*ModuleVersion\s*=\s*)'[^']+'")
        $evaluator = [Text.RegularExpressions.MatchEvaluator]{
            param([Text.RegularExpressions.Match]$match)
            return "$($match.Groups[1].Value)'$previous'"
        }
        $updated = $pattern.Replace($text, $evaluator, 1)
        if ($updated -eq $text) { throw 'The fixture module version was not updated.' }
        [IO.File]::WriteAllText($manifestPath, $updated, [Text.UTF8Encoding]::new($false))
        $rewrittenVersion = [version][string](Import-PowerShellDataFile -LiteralPath $manifestPath).ModuleVersion
        if ($rewrittenVersion -ne $previous) {
            throw "Rewritten fixture manifest version mismatch: expected $previous, observed $rewrittenVersion."
        }

        $changeLogPath = Join-Path $cloneRoot 'CHANGELOG.md'
        $changeLogText = Read-WinCareToolingBoundedUtf8Text -LiteralPath $changeLogPath -MaximumBytes 4194304 -Purpose 'Previous-release changelog'
        $headingPattern = [regex]::new(('(?m)^##\s+{0}\s*$' -f [regex]::Escape([string]$current)))
        if ($headingPattern.Matches($changeLogText).Count -ne 1) {
            throw "Expected exactly one changelog heading for current version $current."
        }
        $updatedChangeLog = $headingPattern.Replace($changeLogText, "## $previous", 1)
        [IO.File]::WriteAllText($changeLogPath, $updatedChangeLog, [Text.UTF8Encoding]::new($false))

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
        $fixtureTree = (git rev-parse 'HEAD^{tree}').Trim()
        $committedManifestText = ((& git show 'HEAD:src/WinCare/WinCare.psd1') -join [Environment]::NewLine)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the committed fixture manifest.' }
        $committedMatch = [regex]::Match($committedManifestText, "(?m)^\s*ModuleVersion\s*=\s*'([^']+)'\s*$")
        if (-not $committedMatch.Success) { throw 'Committed fixture manifest version is missing.' }
        $committedVersion = [version]$committedMatch.Groups[1].Value
        if ($committedVersion -ne $previous) {
            throw "Committed fixture manifest version mismatch: expected $previous, observed $committedVersion."
        }

        & (Join-Path $cloneRoot 'tools\Build-Release.ps1') -Root $cloneRoot -OutputDirectory $outputPath -SkipTests
        if ($LASTEXITCODE -ne 0) { throw 'Previous-release fixture build failed.' }

        $archives = @(Get-ChildItem -LiteralPath $outputPath -Filter 'WinCare-*.zip' -File -Force -ErrorAction Stop)
        if ($archives.Count -ne 1) {
            throw "Previous-release fixture must produce exactly one archive; found $($archives.Count): $($archives.Name -join ', ')."
        }
        $archiveItem = $archives[0]
        $expectedName = "WinCare-$previous.zip"
        if ($archiveItem.Name -cne $expectedName) {
            throw "Previous-release fixture archive name mismatch: expected $expectedName, observed $($archiveItem.Name)."
        }
        $archive = $archiveItem.FullName
        $archiveVersion = Get-WinCareFixtureArchiveManifestVersion -ArchivePath $archive
        if ($archiveVersion -ne $previous) {
            throw "Previous-release archive manifest mismatch: expected $previous, observed $archiveVersion."
        }
        $verificationPath = Join-Path $outputPath 'previous-release-verification.json'
        & python (Join-Path $cloneRoot 'tools\verify_release.py') $archive --output $verificationPath
        if ($LASTEXITCODE -ne 0) { throw 'Independent previous-release archive verification failed.' }

        $archiveEvidence = Get-WinCareToolingFileSha256 -LiteralPath $archive -MaximumBytes 2147483648L -Purpose 'Previous-release archive'
        $result = [ordered]@{
            schema = 'wincare.previous-release.fixture/v2'
            status = 'passed'
            currentVersion = [string]$current
            previousVersion = [string]$previous
            archiveVersion = [string]$archiveVersion
            fixtureCommit = $fixtureCommit
            fixtureTree = $fixtureTree
            archive = $archive
            archiveSha256 = [string]$archiveEvidence.Sha256
            bytes = [long]$archiveEvidence.Bytes
            verification = $verificationPath
        }
        [IO.File]::WriteAllText($resultFullPath, (($result | ConvertTo-Json -Depth 8) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
        [pscustomobject]$result
    } finally {
        Pop-Location
    }
} finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
