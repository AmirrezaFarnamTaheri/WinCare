#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$PayloadPath,
    [Parameter(Mandatory)][string]$PayloadSha256,
    [Parameter(Mandatory)][string]$PayloadManifestSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WinCare.Tooling.ps1')

$diagnosticsDirectory = if ([string]::IsNullOrWhiteSpace($env:WINCARE_DIAGNOSTICS_DIRECTORY)) { $null } else { [IO.Path]::GetFullPath($env:WINCARE_DIAGNOSTICS_DIRECTORY) }
$tracePath = if ($diagnosticsDirectory) { New-Item -ItemType Directory -Path $diagnosticsDirectory -Force | Out-Null; Join-Path $diagnosticsDirectory 'standalone-publish-trace.jsonl' } else { $null }
function Write-WinCareStandaloneTrace {
    param([Parameter(Mandatory)][string]$Phase,[Parameter(Mandatory)][ValidateSet('started','passed','failed','info')][string]$Status,[hashtable]$Details=@{})
    $record=[ordered]@{schema='wincare.standalone.trace/v1';timestamp=[datetime]::UtcNow.ToString('o');phase=$Phase;status=$Status;details=$Details}
    $text=$record|ConvertTo-Json -Compress -Depth 12
    Write-Host "[standalone][$Status][$Phase] $($Details.message)"
    if($tracePath){[IO.File]::AppendAllText($tracePath,$text+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))}
}

function Test-WinCareStandalonePe {
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [Parameter(Mandatory)][ValidateSet(2,3)][int]$ExpectedSubsystem
    )
    $item = Get-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Standalone output is not a regular file: $LiteralPath" }
    if ([long]$item.Length -lt 20971520L) { throw "Standalone executable is unexpectedly small: $LiteralPath" }
    $stream = [IO.File]::Open($item.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Standalone executable is missing an MZ header: $LiteralPath" }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0x40 -or $peOffset + 94 -gt $stream.Length) { throw "Standalone executable has an invalid PE offset: $LiteralPath" }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Standalone executable is missing a PE signature: $LiteralPath" }
        if ($reader.ReadUInt16() -ne 0x8664) { throw "Standalone executable is not AMD64: $LiteralPath" }
        $stream.Position = $peOffset + 20
        $optionalSize = $reader.ReadUInt16()
        if ($optionalSize -lt 70) { throw "Standalone executable has an invalid optional header: $LiteralPath" }
        $stream.Position = $peOffset + 24
        if ($reader.ReadUInt16() -ne 0x020B) { throw "Standalone executable is not PE32+: $LiteralPath" }
        $stream.Position = $peOffset + 24 + 68
        $subsystem = $reader.ReadUInt16()
        if ($subsystem -ne $ExpectedSubsystem) { throw "Standalone executable subsystem mismatch. Expected=$ExpectedSubsystem Actual=$subsystem Path=$LiteralPath" }
    } finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

Write-WinCareStandaloneTrace -Phase 'initialize' -Status started -Details @{message='validating payload and output paths'}
$rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$payload = (Resolve-Path -LiteralPath $PayloadPath -ErrorAction Stop).Path
$payloadEvidence = Get-WinCareToolingFileSha256 -LiteralPath $payload -MaximumBytes 1073741824L -Purpose 'Standalone payload ZIP'
if ($payloadEvidence.Sha256 -ne $PayloadSha256.ToLowerInvariant()) { throw 'Standalone payload hash does not match the supplied payload identity.' }
if ($PayloadManifestSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'PayloadManifestSha256 must be a SHA-256 value.' }
if (-not (Test-Path -LiteralPath $outputPath)) { New-Item -ItemType Directory -Path $outputPath -ErrorAction Stop | Out-Null }
$outputItem = Get-Item -LiteralPath $outputPath -Force -ErrorAction Stop
if (-not $outputItem.PSIsContainer -or ($outputItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Standalone output directory is unsafe: $outputPath" }
if (@(Get-ChildItem -LiteralPath $outputPath -Force).Count) { throw "Standalone output directory must be empty: $outputPath" }

$dotnet = Get-Command dotnet -ErrorAction Stop | Select-Object -ExpandProperty Source -First 1
$version = [string](Import-PowerShellDataFile (Join-Path $rootPath 'src\WinCare\WinCare.psd1')).ModuleVersion
$projects = @(
    [pscustomobject]@{ Project='WinCare.csproj'; Name='WinCare.exe'; Subsystem=3 },
    [pscustomobject]@{ Project='WinCare.Gui.csproj'; Name='WinCare-GUI.exe'; Subsystem=2 },
    [pscustomobject]@{ Project='WinCare.Tui.csproj'; Name='WinCare-TUI.exe'; Subsystem=3 }
)
$workRoot = Join-Path $env:TEMP ('WinCare-standalone-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workRoot -ErrorAction Stop | Out-Null
$records = [Collections.Generic.List[object]]::new()
Write-WinCareStandaloneTrace -Phase 'initialize' -Status passed -Details @{message="version=$version payloadSha256=$($payloadEvidence.Sha256) payloadManifestSha256=$($PayloadManifestSha256.ToLowerInvariant())"}
try {
    foreach ($project in $projects) {
        $phase='publish-'+[IO.Path]::GetFileNameWithoutExtension($project.Project)
        if($env:GITHUB_ACTIONS -eq 'true'){Write-Host "::group::standalone/$phase"}
        Write-WinCareStandaloneTrace -Phase $phase -Status started -Details @{message="project=$($project.Project) output=$($project.Name) subsystem=$($project.Subsystem)"}
        $publish = Join-Path $workRoot ([IO.Path]::GetFileNameWithoutExtension($project.Project))
        New-Item -ItemType Directory -Path $publish -ErrorAction Stop | Out-Null
        $projectPath = Join-Path $rootPath ('src\WinCare\Standalone\' + $project.Project)
        $arguments = @(
            'publish',$projectPath,
            '--configuration','Release',
            '--runtime','win-x64',
            '--self-contained','true',
            '--output',$publish,
            '--nologo',
            '-p:WinCarePayloadPath=' + $payload,
            '-p:WinCarePayloadSha256=' + $payloadEvidence.Sha256.ToUpperInvariant(),
            '-p:WinCarePayloadManifestSha256=' + $PayloadManifestSha256.ToUpperInvariant(),
            '-p:Version=' + $version,
            '-p:FileVersion=' + $version,
            '-p:InformationalVersion=' + $version
        )
        $result = Invoke-WinCareToolingProcess -Executable $dotnet -Arguments $arguments -TimeoutSeconds 1800 -MaximumCapturedOutputBytes 67108864 -WorkingDirectory $rootPath -WriteCapturedOutput
        if ($result.ExitCode -ne 0) { throw "dotnet publish failed for $($project.Project). ExitCode=$($result.ExitCode)" }
        $files = @(Get-ChildItem -LiteralPath $publish -File -Force -ErrorAction Stop)
        $candidate = $files | Where-Object Name -eq $project.Name
        if (@($candidate).Count -ne 1) { throw "Expected exactly one $($project.Name) output from $($project.Project)." }
        $unexpected = @($files | Where-Object Name -ne $project.Name)
        if ($unexpected.Count) { throw "Standalone publish emitted unexpected loose files for $($project.Project): $($unexpected.Name -join ', ')" }
        Test-WinCareStandalonePe -LiteralPath $candidate.FullName -ExpectedSubsystem $project.Subsystem
        $destination = Join-Path $outputPath $project.Name
        Copy-Item -LiteralPath $candidate.FullName -Destination $destination -ErrorAction Stop
        $selfTest = Invoke-WinCareToolingProcess -Executable $destination -Arguments @('--wincare-self-test') -TimeoutSeconds 180 -MaximumCapturedOutputBytes 16777216 -WorkingDirectory $outputPath -WriteCapturedOutput
        if ($selfTest.ExitCode -ne 0) { throw "Standalone self-test failed for $($project.Name). ExitCode=$($selfTest.ExitCode)" }
        $evidence = Get-WinCareToolingFileSha256 -LiteralPath $destination -MaximumBytes 1073741824L -Purpose 'Standalone executable'
        $records.Add([ordered]@{Name=$project.Name;Sha256=$evidence.Sha256;Bytes=[long]$evidence.Bytes;Subsystem=[int]$project.Subsystem;RuntimeIdentifier='win-x64';SelfTestExitCode=[int]$selfTest.ExitCode})
        Write-WinCareStandaloneTrace -Phase $phase -Status passed -Details @{message="sha256=$($evidence.Sha256) bytes=$($evidence.Bytes) selfTestExit=$($selfTest.ExitCode)"}
        if($env:GITHUB_ACTIONS -eq 'true'){Write-Host '::endgroup::'}
    }
    if (@($records.Sha256 | Select-Object -Unique).Count -ne $records.Count) { throw 'Standalone executables must have independent hashes.' }
    $manifest = [ordered]@{
        SchemaVersion = 1
        Version = $version
        RuntimeIdentifier = 'win-x64'
        Configuration = 'Release'
        SelfContained = $true
        SingleFile = $true
        PayloadSha256 = $payloadEvidence.Sha256
        PayloadManifestSha256 = $PayloadManifestSha256.ToLowerInvariant()
        Artifacts = @($records)
    }
    $manifestPath = Join-Path $outputPath 'WinCare.Standalone.build.json'
    Write-WinCareToolingAtomicJson -LiteralPath $manifestPath -Value $manifest -MaximumBytes 1048576 -Depth 12
    Write-WinCareStandaloneTrace -Phase 'complete' -Status passed -Details @{message="published $($records.Count) independent executables";artifacts=@($records)}
    $records|Format-Table Name,Bytes,Sha256,Subsystem,SelfTestExitCode -AutoSize
    $manifest
} catch {
    Write-WinCareStandaloneTrace -Phase 'publish' -Status failed -Details @{message=$_.Exception.Message;type=$_.Exception.GetType().FullName;scriptStackTrace=$_.ScriptStackTrace}
    if($env:GITHUB_ACTIONS -eq 'true'){Write-Host '::endgroup::'}
    throw
} finally {
    Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
