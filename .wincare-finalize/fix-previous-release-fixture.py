#!/usr/bin/env python3
"""Install the authoritative previous-release fixture after staged transformations."""
from __future__ import annotations

import os
import tempfile
from pathlib import Path

ROOT = Path.cwd().resolve()
MAX_BYTES = 8 * 1024 * 1024


def read_text(path: Path) -> str:
    if not path.is_file() or path.is_symlink():
        raise RuntimeError(f"unsafe or missing file: {path}")
    before = path.stat(follow_symlinks=False)
    if before.st_size <= 0 or before.st_size > MAX_BYTES:
        raise RuntimeError(f"file size outside bounds: {path}")
    payload = path.read_bytes()
    after = path.stat(follow_symlinks=False)
    if len(payload) != before.st_size or (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise RuntimeError(f"file changed while reading: {path}")
    return payload.decode("utf-8-sig")


def write_text(path: Path, text: str) -> None:
    destination = path.resolve()
    if ROOT not in destination.parents:
        raise RuntimeError(f"write escapes repository root: {path}")
    descriptor, temporary = tempfile.mkstemp(prefix=destination.name + ".", suffix=".tmp", dir=destination.parent)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(text.encode("utf-8"))
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, destination)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


fixture_path = ROOT / "tools" / "Build-PreviousReleaseFixture.ps1"
existing = read_text(fixture_path)
for marker in ("Build-Release.ps1", "ModuleVersion", "wincare.previous-release.fixture/v1"):
    if marker not in existing:
        raise RuntimeError(f"previous-release fixture has unexpected ownership; missing {marker!r}")

fixture = r'''#requires -Version 7.2
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
            try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
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
'''
write_text(fixture_path, fixture)

test_path = ROOT / "tools" / "test_windows_release_pipeline.py"
test = read_text(test_path)
method = '''    def test_previous_release_fixture_proves_built_archive_version(self) -> None:
        text = PREVIOUS_FIXTURE.read_text(encoding="utf-8")
        self.assertIn("[Text.RegularExpressions.MatchEvaluator]", text)
        self.assertIn("$committedVersion", text)
        self.assertIn("Get-WinCareFixtureArchiveManifestVersion", text)
        self.assertIn("if ($archives.Count -ne 1)", text)
        self.assertIn("if ($archiveVersion -ne $previous)", text)
        self.assertIn("previous-release-verification.json", text)

'''
if "def test_previous_release_fixture_proves_built_archive_version" not in test:
    anchor = "    def test_validation_threads_previous_release_into_focused_upgrade_lifecycle(self) -> None:\n"
    if test.count(anchor) != 1:
        raise RuntimeError("previous-release regression insertion anchor has an unexpected shape")
    test = test.replace(anchor, method + anchor, 1)
write_text(test_path, test)
compile(test, str(test_path), "exec")
print("Installed the authoritative previous-release archive/version fixture contract.")
