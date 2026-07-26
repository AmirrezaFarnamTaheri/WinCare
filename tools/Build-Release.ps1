#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'),
    [switch]$SkipTests,
    [switch]$AllowDirty
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WinCare.Tooling.ps1')

function Assert-WinCareReleaseDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RepositoryRoot
    )
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    $driveRoot = [IO.Path]::GetPathRoot($full).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    $trimmed = $full.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    if ($trimmed -eq $root -or $trimmed -eq $driveRoot) { throw "Unsafe release output directory: $full" }

    $cursor = $full
    while (-not (Test-Path -LiteralPath $cursor)) {
        $parent = [IO.Directory]::GetParent($cursor)
        if ($null -eq $parent) { break }
        $cursor = $parent.FullName
    }
    if (Test-Path -LiteralPath $cursor) {
        $existing = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
        if ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Release output ancestry contains a reparse point: $cursor"
        }
    }

    if (Test-Path -LiteralPath $full) {
        $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
        if (-not $item.PSIsContainer) { throw "Release output exists and is not a directory: $full" }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Release output is a reparse point: $full" }
        if (@(Get-ChildItem -LiteralPath $full -Force -ErrorAction Stop).Count) {
            throw "Release output must be absent or empty; refusing to overwrite: $full"
        }
    } else {
        New-Item -ItemType Directory -Path $full -ErrorAction Stop | Out-Null
    }
    $full
}

$rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
$outputPath = Assert-WinCareReleaseDirectory -Path $OutputDirectory -RepositoryRoot $rootPath
$null = Resolve-WinCareToolingSourceDateEpoch -RepositoryRoot $rootPath
$workRoot = Join-Path $env:TEMP ('WinCare-release-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workRoot -ErrorAction Stop | Out-Null

try {
    $hasPssa = [bool](Get-Module -ListAvailable PSScriptAnalyzer -ErrorAction SilentlyContinue)
    $pssaArgs = @{}
    if ($hasPssa) { $pssaArgs.RequirePSScriptAnalyzer = $true }

    & (Join-Path $PSScriptRoot 'Invoke-StaticChecks.ps1') -Root $rootPath @pssaArgs -OutputPath (Join-Path $workRoot 'powershell-static-validation.json')

    if (-not $SkipTests) {
        Import-Module Pester -RequiredVersion 5.5.0 -ErrorAction Stop
        $result = Invoke-Pester -Path (Join-Path $rootPath 'tests') -PassThru
        $failedCount = if ($null -ne $result.FailedCount) { [int]$result.FailedCount } else { @($result.TestResult | Where-Object Passed -eq $false).Count }
        if ($failedCount -gt 0) { throw "Pester gate failed: failed=$failedCount." }
    }

    $python = Get-Command python, python3, 'C:\Program Files\Python311\python.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.Source -notmatch 'WindowsApps' } | Select-Object -ExpandProperty Source -First 1
    if (-not $python) { throw 'Python 3 is required by the deterministic release builder.' }

    $nativeDirectory = Join-Path $workRoot 'native'
    & (Join-Path $rootPath 'src\WinCare\Native\Build-WinCareNativePolyglot.ps1') -OutputDirectory $nativeDirectory -Configuration Release

    $buildScript = Join-Path $PSScriptRoot 'build_release.py'
    $arguments = @($buildScript,$rootPath,'--output-directory',$outputPath,'--native-directory',$nativeDirectory,'--profile','production')
    if ($AllowDirty) { $arguments += '--allow-dirty' }
    $buildResult = Invoke-WinCareToolingProcess -Executable $python -Arguments $arguments -TimeoutSeconds 3600 -MaximumCapturedOutputBytes 67108864 -WorkingDirectory $rootPath -WriteCapturedOutput
    if ($buildResult.ExitCode -ne 0) { throw "Deterministic release build failed. ExitCode=$($buildResult.ExitCode)" }

    $version = (Import-PowerShellDataFile (Join-Path $rootPath 'src\WinCare\WinCare.psd1')).ModuleVersion
    $archive = Join-Path $outputPath "WinCare-$version.zip"
    & (Join-Path $PSScriptRoot 'Build-Exe.ps1') -Root $rootPath -OutputDirectory $outputPath -ZipPackage $archive -NativeDirectory $nativeDirectory
    & (Join-Path $PSScriptRoot 'Test-ReleaseArchive.ps1') -ArchivePath $archive -OutputPath (Join-Path $outputPath "WinCare-$version-archive-validation.json")

    $unexpectedDirectories = @(Get-ChildItem -LiteralPath $outputPath -Directory -Force -ErrorAction Stop)
    if ($unexpectedDirectories.Count) { throw "Release output contains unexpected directories: $($unexpectedDirectories.Name -join ', ')" }
    Write-Host "Release built from source and independently verified: $archive" -ForegroundColor Green
} catch {
    if (Test-Path -LiteralPath $outputPath) {
        Get-ChildItem -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
    throw
} finally {
    Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
