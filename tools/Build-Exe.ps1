#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'),
    [string]$ZipPackage = '',
    [string]$NativeDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WinCare.Tooling.ps1')

$rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
$epoch = Resolve-WinCareToolingSourceDateEpoch -RepositoryRoot $rootPath
$nativeBuilder = Join-Path $rootPath 'src\WinCare\Native\Build-WinCareNativePolyglot.ps1'
$null = Get-WinCareToolingRegularFile -LiteralPath $nativeBuilder -MaximumBytes 1048576L -Purpose 'Native build script'

$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    $outputItem = Get-Item -LiteralPath $outputPath -Force -ErrorAction Stop
    if (-not $outputItem.PSIsContainer -or ($outputItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Launcher output is unsafe: $outputPath"
    }
} else {
    New-Item -ItemType Directory -Path $outputPath -ErrorAction Stop | Out-Null
}

$nativeOutput = if ($NativeDirectory) { [IO.Path]::GetFullPath($NativeDirectory) } else { Join-Path $outputPath 'native' }
$nativeManifestPath = Join-Path $nativeOutput 'WinCare.Native.build.json'
if (-not (Test-Path -LiteralPath $nativeManifestPath -PathType Leaf)) {
    & $nativeBuilder -OutputDirectory $nativeOutput -Configuration Release
}
$nativeManifest = Read-WinCareToolingJson -LiteralPath $nativeManifestPath -MaximumBytes 1048576 -MaximumDepth 32
if ([int]$nativeManifest.SchemaVersion -ne 2 -or [string]$nativeManifest.TargetFramework -ne 'net8.0-windows' -or [string]$nativeManifest.Configuration -ne 'Release') {
    throw 'Unsupported native build manifest.'
}
if ([int]$nativeManifest.SourceCount -ne @($nativeManifest.Sources).Count) { throw 'Native source count provenance mismatch.' }
if ([long]$nativeManifest.TotalSourceBytes -ne [long](@($nativeManifest.Sources | Measure-Object Bytes -Sum).Sum)) { throw 'Native source byte provenance mismatch.' }

$artifactByName = @{}
$artifactBytes = 0L
foreach ($artifact in @($nativeManifest.Artifacts)) {
    $name = [IO.Path]::GetFileName([string]$artifact.Path)
    if ($name -ne [string]$artifact.Path -or $artifactByName.ContainsKey($name)) { throw "Unsafe or duplicate native artifact path: $($artifact.Path)" }
    $path = Join-Path $nativeOutput $name
    $evidence = Get-WinCareToolingFileSha256 -LiteralPath $path -MaximumBytes 268435456L -Purpose 'Native launcher artifact'
    if ([long]$evidence.Bytes -ne [long]$artifact.Bytes -or $evidence.Sha256 -ne [string]$artifact.Sha256) { throw "Native artifact provenance mismatch: $name" }
    $artifactBytes += [long]$evidence.Bytes
    if ($artifactBytes -gt 1073741824L) { throw 'Native launcher artifacts exceed the 1 GiB aggregate limit.' }
    $artifactByName[$name] = $path
}

$aliases = @(
    @{ Source = 'WinCare.Launcher.exe'; Destination = 'WinCare-GUI.exe' },
    @{ Source = 'WinCare.Launcher.exe'; Destination = 'WinCare.exe' },
    @{ Source = 'WinCare.TuiLauncher.exe'; Destination = 'WinCare-TUI.exe' }
)
$copyPlan = [Collections.Generic.List[object]]::new()
foreach ($alias in $aliases) {
    if (-not $artifactByName.ContainsKey($alias.Source)) { throw "Expected launcher missing from native manifest: $($alias.Source)" }
    $copyPlan.Add([pscustomobject]@{ Source=$artifactByName[$alias.Source]; Destination=$alias.Destination })
}
foreach ($name in @($artifactByName.Keys | Sort-Object)) {
    if ($name -in @('WinCare.Launcher.exe','WinCare.TuiLauncher.exe')) { continue }
    $copyPlan.Add([pscustomobject]@{ Source=$artifactByName[$name]; Destination=$name })
}
$copyPlan.Add([pscustomobject]@{ Source=$nativeManifestPath; Destination='WinCare.Native.build.json' })
foreach ($entry in $copyPlan) {
    $destination = Join-Path $outputPath $entry.Destination
    if (Test-Path -LiteralPath $destination) { throw "Launcher output already exists; refusing to overwrite: $destination" }
    Copy-Item -LiteralPath $entry.Source -Destination $destination -ErrorAction Stop
}

$sourcePackage = $null
if ($ZipPackage) {
    $packagePath = [IO.Path]::GetFullPath($ZipPackage)
    $packageEvidence = Get-WinCareToolingFileSha256 -LiteralPath $packagePath -MaximumBytes 2147483648L -Purpose 'Source release package'
    $sourcePackage = [ordered]@{
        Name = [IO.Path]::GetFileName($packagePath)
        Sha256 = $packageEvidence.Sha256
        Bytes = [long]$packageEvidence.Bytes
    }
}
$nativeManifestEvidence = Get-WinCareToolingFileSha256 -LiteralPath $nativeManifestPath -MaximumBytes 1048576L -Purpose 'Native build manifest'
$manifest = [ordered]@{
    SchemaVersion = 2
    BuiltAt = [DateTimeOffset]::FromUnixTimeSeconds($epoch).UtcDateTime.ToString('o')
    SourcePackage = $sourcePackage
    NativeManifestSha256 = $nativeManifestEvidence.Sha256
    NativeSourceTreeSha256 = [string]$nativeManifest.SourceTreeSha256
    Launchers = foreach ($alias in $aliases) {
        $path = Join-Path $outputPath $alias.Destination
        $evidence = Get-WinCareToolingFileSha256 -LiteralPath $path -MaximumBytes 268435456L -Purpose 'Launcher alias'
        [ordered]@{ Name=$alias.Destination; Sha256=$evidence.Sha256; Bytes=[long]$evidence.Bytes }
    }
}
$manifestPath = Join-Path $outputPath 'WinCare.Launchers.build.json'
if (Test-Path -LiteralPath $manifestPath) { throw "Launcher manifest already exists; refusing to overwrite: $manifestPath" }
Write-WinCareToolingAtomicJson -LiteralPath $manifestPath -Value $manifest -MaximumBytes 1048576 -Depth 8
$manifest
