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

function Get-WinCareIconFrameSizes {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath)
    $bytes=Read-WinCareToolingBoundedBytes -LiteralPath $LiteralPath -MaximumBytes 1048576 -Purpose 'WinCare application icon'
    try{
        if($bytes.Length -lt 6){throw 'WinCare application icon is truncated.'}
        $reserved=[BitConverter]::ToUInt16($bytes,0)
        $type=[BitConverter]::ToUInt16($bytes,2)
        $count=[BitConverter]::ToUInt16($bytes,4)
        if($reserved -ne 0 -or $type -ne 1 -or $count -lt 1 -or $count -gt 64){
            throw 'WinCare application icon has an invalid ICO header.'
        }
        if($bytes.Length -lt 6+(16*$count)){throw 'WinCare application icon directory is truncated.'}
        $sizes=[Collections.Generic.List[int]]::new()
        for($index=0;$index -lt $count;$index++){
            $offset=6+(16*$index)
            $width=if($bytes[$offset] -eq 0){256}else{[int]$bytes[$offset]}
            $height=if($bytes[$offset+1] -eq 0){256}else{[int]$bytes[$offset+1]}
            if($width -ne $height){throw "WinCare application icon frame is not square: ${width}x${height}."}
            $imageBytes=[BitConverter]::ToUInt32($bytes,$offset+8)
            $imageOffset=[BitConverter]::ToUInt32($bytes,$offset+12)
            if($imageBytes -lt 1 -or [long]$imageOffset+[long]$imageBytes -gt $bytes.Length){
                throw "WinCare application icon frame $width has an invalid byte range."
            }
            $sizes.Add($width)
        }
        return @($sizes|Sort-Object -Unique)
    }finally{
        if($bytes.Length){[Array]::Clear($bytes,0,$bytes.Length)}
    }
}

function Get-WinCareIconPixelSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Drawing.Icon]$Icon)
    $bitmap=$Icon.ToBitmap()
    $bytes=[byte[]]::new($bitmap.Width*$bitmap.Height*4)
    try{
        $offset=0
        for($y=0;$y -lt $bitmap.Height;$y++){
            for($x=0;$x -lt $bitmap.Width;$x++){
                $color=$bitmap.GetPixel($x,$y)
                $bytes[$offset]=$color.A;$offset++
                $bytes[$offset]=$color.R;$offset++
                $bytes[$offset]=$color.G;$offset++
                $bytes[$offset]=$color.B;$offset++
            }
        }
        $digest=[Security.Cryptography.SHA256]::HashData($bytes)
        try{return [Convert]::ToHexString($digest).ToLowerInvariant()}
        finally{[Array]::Clear($digest,0,$digest.Length)}
    }finally{
        if($bytes.Length){[Array]::Clear($bytes,0,$bytes.Length)}
        $bitmap.Dispose()
    }
}

function Test-WinCareStandaloneEmbeddedIcon {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [Parameter(Mandatory)][string]$ExpectedIconPath
    )
    $associated=[Drawing.Icon]::ExtractAssociatedIcon($LiteralPath)
    if($null -eq $associated){throw "Standalone executable has no associated application icon: $LiteralPath"}
    $expected=$null
    try{
        if($associated.Width -lt 16 -or $associated.Height -lt 16){
            throw "Standalone executable returned an invalid associated icon: $LiteralPath"
        }
        $expected=[Drawing.Icon]::new($ExpectedIconPath,$associated.Width,$associated.Height)
        $actualPixelSha256=Get-WinCareIconPixelSha256 -Icon $associated
        $expectedPixelSha256=Get-WinCareIconPixelSha256 -Icon $expected
        if($actualPixelSha256 -ne $expectedPixelSha256){
            throw "Standalone executable icon pixels do not match the canonical WinCare icon: $LiteralPath"
        }
        return [pscustomobject]@{
            Verified=$true
            Width=[int]$associated.Width
            Height=[int]$associated.Height
            PixelSha256=$actualPixelSha256
        }
    }finally{
        if($expected){$expected.Dispose()}
        $associated.Dispose()
    }
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
$iconPath=(Resolve-Path -LiteralPath (Join-Path $rootPath 'src\WinCare\Data\Gui\WinCare.ico') -ErrorAction Stop).Path
$iconEvidence=Get-WinCareToolingFileSha256 -LiteralPath $iconPath -MaximumBytes 1048576 -Purpose 'WinCare application icon'
$iconFrameSizes=@(Get-WinCareIconFrameSizes -LiteralPath $iconPath)
$requiredIconFrameSizes=@(16,24,32,48,64,128,256)
$missingIconFrameSizes=@($requiredIconFrameSizes|Where-Object{$_ -notin $iconFrameSizes})
if($missingIconFrameSizes.Count){throw "WinCare application icon is missing required frame size(s): $($missingIconFrameSizes -join ', ')"}
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
Write-WinCareStandaloneTrace -Phase 'initialize' -Status passed -Details @{message="version=$version payloadSha256=$($payloadEvidence.Sha256) payloadManifestSha256=$($PayloadManifestSha256.ToLowerInvariant()) iconSha256=$($iconEvidence.Sha256) iconFrames=$($iconFrameSizes -join ',')"}
try {
    foreach ($project in $projects) {
        $phase='publish-'+[IO.Path]::GetFileNameWithoutExtension($project.Project)
        if($env:GITHUB_ACTIONS -eq 'true'){Write-Host "::group::standalone/$phase"}
        Write-WinCareStandaloneTrace -Phase $phase -Status started -Details @{message="project=$($project.Project) output=$($project.Name) subsystem=$($project.Subsystem)"}
        $publish = Join-Path $workRoot ([IO.Path]::GetFileNameWithoutExtension($project.Project))
        New-Item -ItemType Directory -Path $publish -ErrorAction Stop | Out-Null
        $projectPath = Join-Path $rootPath ('src\WinCare\Standalone\' + $project.Project)
        $propertyArguments = @(
            ("-p:WinCarePayloadPath={0}" -f $payload),
            ("-p:WinCarePayloadSha256={0}" -f $payloadEvidence.Sha256.ToUpperInvariant()),
            ("-p:WinCarePayloadManifestSha256={0}" -f $PayloadManifestSha256.ToUpperInvariant()),
            ("-p:WinCareApplicationIcon={0}" -f $iconPath),
            ("-p:Version={0}" -f $version),
            ("-p:FileVersion={0}" -f $version),
            ("-p:InformationalVersion={0}" -f $version)
        )
        $invalidPropertyArguments = @($propertyArguments | Where-Object { $_ -notmatch '^-p:[A-Za-z][A-Za-z0-9]*=.+$' })
        if ($invalidPropertyArguments.Count) {
            throw "Invalid standalone MSBuild property argument(s): $($invalidPropertyArguments -join ', ')"
        }
        $arguments = @(
            'publish',$projectPath,
            '--configuration','Release',
            '--runtime','win-x64',
            '--self-contained','true',
            '--output',$publish,
            '--nologo'
        ) + $propertyArguments
        Write-WinCareStandaloneTrace -Phase $phase -Status info -Details @{message="dotnetArguments=$($arguments -join ' ')"}
        $result = Invoke-WinCareToolingProcess -Executable $dotnet -Arguments $arguments -TimeoutSeconds 1800 -MaximumCapturedOutputBytes 67108864 -WorkingDirectory $rootPath -WriteCapturedOutput
        if ($result.ExitCode -ne 0) { throw "dotnet publish failed for $($project.Project). ExitCode=$($result.ExitCode)" }
        $files = @(Get-ChildItem -LiteralPath $publish -File -Force -ErrorAction Stop)
        $candidate = $files | Where-Object Name -eq $project.Name
        if (@($candidate).Count -ne 1) { throw "Expected exactly one $($project.Name) output from $($project.Project)." }
        $unexpected = @($files | Where-Object Name -ne $project.Name)
        if ($unexpected.Count) { throw "Standalone publish emitted unexpected loose files for $($project.Project): $($unexpected.Name -join ', ')" }
        Test-WinCareStandalonePe -LiteralPath $candidate.FullName -ExpectedSubsystem $project.Subsystem
        $publishedIcon=Test-WinCareStandaloneEmbeddedIcon -LiteralPath $candidate.FullName -ExpectedIconPath $iconPath
        $destination = Join-Path $outputPath $project.Name
        Copy-Item -LiteralPath $candidate.FullName -Destination $destination -ErrorAction Stop
        $selfTest = Invoke-WinCareToolingProcess -Executable $destination -Arguments @('--wincare-self-test') -TimeoutSeconds 180 -MaximumCapturedOutputBytes 16777216 -WorkingDirectory $outputPath -WriteCapturedOutput
        if ($selfTest.ExitCode -ne 0) { throw "Standalone self-test failed for $($project.Name). ExitCode=$($selfTest.ExitCode)" }
        $evidence = Get-WinCareToolingFileSha256 -LiteralPath $destination -MaximumBytes 1073741824L -Purpose 'Standalone executable'
        $records.Add([ordered]@{
            Name=$project.Name
            Sha256=$evidence.Sha256
            Bytes=[long]$evidence.Bytes
            Subsystem=[int]$project.Subsystem
            RuntimeIdentifier='win-x64'
            SelfTestExitCode=[int]$selfTest.ExitCode
            EmbeddedIconVerified=[bool]$publishedIcon.Verified
            EmbeddedIconWidth=[int]$publishedIcon.Width
            EmbeddedIconHeight=[int]$publishedIcon.Height
            EmbeddedIconPixelSha256=[string]$publishedIcon.PixelSha256
            IconSha256=$iconEvidence.Sha256
            IconFrameSizes=@($iconFrameSizes)
        })
        Write-WinCareStandaloneTrace -Phase $phase -Status passed -Details @{message="sha256=$($evidence.Sha256) bytes=$($evidence.Bytes) selfTestExit=$($selfTest.ExitCode)"}
        if($env:GITHUB_ACTIONS -eq 'true'){Write-Host '::endgroup::'}
    }
    if (@($records.Sha256 | Select-Object -Unique).Count -ne $records.Count) { throw 'Standalone executables must have independent hashes.' }
    $manifest = [ordered]@{
        SchemaVersion = 2
        Version = $version
        RuntimeIdentifier = 'win-x64'
        Configuration = 'Release'
        SelfContained = $true
        SingleFile = $true
        PayloadSha256 = $payloadEvidence.Sha256
        PayloadManifestSha256 = $PayloadManifestSha256.ToLowerInvariant()
        IconSha256 = $iconEvidence.Sha256
        IconFrameSizes = @($iconFrameSizes)
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
