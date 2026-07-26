#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\..\bin'),
    [ValidateSet('Release','Debug')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:WinCareNativeSourceNames = @(
    'Build-WinCareNativePolyglot.ps1',
    'WinCare.Audio.cs',
    'WinCare.CommandLine.cs',
    'WinCare.ProcessMemory.cs',
    'WinCare.DpiHelper.cs',
    'WinCare.FileIntelligence.cs',
    'WinCare.Launcher.csproj',
    'WinCare.Native.csproj',
    'WinCare.PolicyEngine.cs',
    'WinCare.Productivity.cs',
    'WinCare.SecurityAuditor.cs',
    'WinCare.ShellHardware.cs',
    'WinCare.TuiLauncher.csproj',
    'WinCareLauncher.cs',
    'WinCareTuiLauncher.cs'
)

function Get-WinCareNativeRegularFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [ValidateRange(1,1073741824)][long]$MaximumBytes,
        [Parameter(Mandatory)][string]$Purpose
    )
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "$Purpose is not a regular non-reparse file: $Path"
    }
    if ([long]$item.Length -lt 0 -or [long]$item.Length -gt $MaximumBytes) {
        throw "$Purpose exceeds the $MaximumBytes-byte limit: $Path"
    }
    $item
}

function Read-WinCareNativeFileBytes {
    param(
        [Parameter(Mandatory)][string]$Path,
        [ValidateRange(1,1048576)][int]$MaximumBytes,
        [Parameter(Mandatory)][string]$Purpose
    )
    $before = Get-WinCareNativeRegularFile -Path $Path -MaximumBytes $MaximumBytes -Purpose $Purpose
    $expectedLength = [long]$before.Length
    $expectedWriteTicks = $before.LastWriteTimeUtc.Ticks
    $stream = [IO.FileStream]::new(
        $before.FullName,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read,
        65536,
        [IO.FileOptions]::SequentialScan
    )
    try {
        if ($stream.Length -ne $expectedLength) { throw "$Purpose changed before it could be read: $Path" }
        $bytes = [byte[]]::new([int]$expectedLength)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes,$offset,$bytes.Length-$offset)
            if ($read -le 0) { throw "$Purpose ended before its declared length: $Path" }
            $offset += $read
        }
        if ($stream.ReadByte() -ne -1) { throw "$Purpose grew while it was read: $Path" }
    } finally {
        $stream.Dispose()
    }
    $after = Get-WinCareNativeRegularFile -Path $Path -MaximumBytes $MaximumBytes -Purpose $Purpose
    if ([long]$after.Length -ne $expectedLength -or $after.LastWriteTimeUtc.Ticks -ne $expectedWriteTicks) {
        if ($bytes.Length) { [Array]::Clear($bytes,0,$bytes.Length) }
        throw "$Purpose changed while it was read: $Path"
    }
    $bytes
}

function Get-WinCareNativeFileSha256 {
    param(
        [Parameter(Mandatory)][string]$Path,
        [ValidateRange(1,1073741824)][long]$MaximumBytes,
        [Parameter(Mandatory)][string]$Purpose
    )
    $before = Get-WinCareNativeRegularFile -Path $Path -MaximumBytes $MaximumBytes -Purpose $Purpose
    $expectedLength = [long]$before.Length
    $expectedWriteTicks = $before.LastWriteTimeUtc.Ticks
    $stream = [IO.FileStream]::new(
        $before.FullName,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read,
        131072,
        [IO.FileOptions]::SequentialScan
    )
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $buffer = [byte[]]::new(131072)
    $observed = 0L
    try {
        if ($stream.Length -ne $expectedLength) { throw "$Purpose changed before it could be hashed: $Path" }
        while (($read = $stream.Read($buffer,0,$buffer.Length)) -gt 0) {
            $observed += $read
            if ($observed -gt $MaximumBytes) { throw "$Purpose exceeded its byte limit while hashing: $Path" }
            $hash.AppendData($buffer,0,$read)
        }
        if ($observed -ne $expectedLength) { throw "$Purpose length changed while hashing: $Path" }
        $digest = $hash.GetHashAndReset()
        try {
            $hex = [Convert]::ToHexString($digest).ToLowerInvariant()
        } finally {
            [Array]::Clear($digest,0,$digest.Length)
        }
    } finally {
        [Array]::Clear($buffer,0,$buffer.Length)
        $hash.Dispose()
        $stream.Dispose()
    }
    $after = Get-WinCareNativeRegularFile -Path $Path -MaximumBytes $MaximumBytes -Purpose $Purpose
    if ([long]$after.Length -ne $expectedLength -or $after.LastWriteTimeUtc.Ticks -ne $expectedWriteTicks) {
        throw "$Purpose changed while it was hashed: $Path"
    }
    [pscustomobject]@{ Sha256=$hex; Bytes=$expectedLength }
}

function Read-WinCareNativeJson {
    param(
        [Parameter(Mandatory)][string]$Path,
        [ValidateRange(1,1048576)][int]$MaximumBytes = 65536
    )
    $bytes = Read-WinCareNativeFileBytes -Path $Path -MaximumBytes $MaximumBytes -Purpose 'Native JSON input'
    try {
        $encoding = [Text.UTF8Encoding]::new($false,$true)
        $text = $encoding.GetString($bytes)
        try {
            $text | ConvertFrom-Json -Depth 32 -ErrorAction Stop
        } finally {
            $text = $null
        }
    } finally {
        if ($bytes.Length) { [Array]::Clear($bytes,0,$bytes.Length) }
    }
}

function Stop-WinCareNativeProcess {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process)
    if (-not $Process.HasExited) {
        try { $Process.Kill($true) } catch { try { $Process.Kill() } catch { } }
        try { $Process.WaitForExit(5000) | Out-Null } catch { }
    }
}

function Invoke-WinCareNativeProcess {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [ValidateRange(1,3600)][int]$TimeoutSeconds,
        [ValidateRange(1024,67108864)][int]$MaximumCapturedOutputBytes,
        [string]$WorkingDirectory
    )
    $executableItem = Get-WinCareNativeRegularFile -Path $Executable -MaximumBytes 536870912L -Purpose 'Native build executable'
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $executableItem.FullName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    if ($WorkingDirectory) { $startInfo.WorkingDirectory = $WorkingDirectory }
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add([string]$argument) }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stdout = [IO.MemoryStream]::new()
    $stderr = [IO.MemoryStream]::new()
    $stdoutBuffer = [byte[]]::new(16384)
    $stderrBuffer = [byte[]]::new(16384)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) { throw "Failed to start native build process: $Executable" }
        $stdoutTask = $process.StandardOutput.BaseStream.ReadAsync($stdoutBuffer,0,$stdoutBuffer.Length)
        $stderrTask = $process.StandardError.BaseStream.ReadAsync($stderrBuffer,0,$stderrBuffer.Length)
        $stdoutDone = $false
        $stderrDone = $false
        $captured = 0L
        while (-not ($stdoutDone -and $stderrDone -and $process.HasExited)) {
            if ($clock.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
                Stop-WinCareNativeProcess -Process $process
                throw "Native build process exceeded the $TimeoutSeconds-second timeout: $Executable"
            }
            $progress = $false
            if (-not $stdoutDone -and $stdoutTask.IsCompleted) {
                $read = $stdoutTask.GetAwaiter().GetResult()
                if ($read -eq 0) {
                    $stdoutDone = $true
                } else {
                    $captured += $read
                    if ($captured -gt $MaximumCapturedOutputBytes) {
                        Stop-WinCareNativeProcess -Process $process
                        throw "Native build process exceeded the $MaximumCapturedOutputBytes-byte output limit: $Executable"
                    }
                    $stdout.Write($stdoutBuffer,0,$read)
                    $stdoutTask = $process.StandardOutput.BaseStream.ReadAsync($stdoutBuffer,0,$stdoutBuffer.Length)
                }
                $progress = $true
            }
            if (-not $stderrDone -and $stderrTask.IsCompleted) {
                $read = $stderrTask.GetAwaiter().GetResult()
                if ($read -eq 0) {
                    $stderrDone = $true
                } else {
                    $captured += $read
                    if ($captured -gt $MaximumCapturedOutputBytes) {
                        Stop-WinCareNativeProcess -Process $process
                        throw "Native build process exceeded the $MaximumCapturedOutputBytes-byte output limit: $Executable"
                    }
                    $stderr.Write($stderrBuffer,0,$read)
                    $stderrTask = $process.StandardError.BaseStream.ReadAsync($stderrBuffer,0,$stderrBuffer.Length)
                }
                $progress = $true
            }
            if (-not $progress) { Start-Sleep -Milliseconds 10 }
        }
        $process.WaitForExit()
        $encoding = [Text.UTF8Encoding]::new($false,$false)
        $stdoutBytes = $stdout.ToArray()
        $stderrBytes = $stderr.ToArray()
        try {
            [pscustomobject]@{
                ExitCode=$process.ExitCode
                StandardOutput=$encoding.GetString($stdoutBytes)
                StandardError=$encoding.GetString($stderrBytes)
                CapturedBytes=$captured
            }
        } finally {
            if ($stdoutBytes.Length) { [Array]::Clear($stdoutBytes,0,$stdoutBytes.Length) }
            if ($stderrBytes.Length) { [Array]::Clear($stderrBytes,0,$stderrBytes.Length) }
        }
    } finally {
        Stop-WinCareNativeProcess -Process $process
        $clock.Stop()
        [Array]::Clear($stdoutBuffer,0,$stdoutBuffer.Length)
        [Array]::Clear($stderrBuffer,0,$stderrBuffer.Length)
        $stdout.Dispose()
        $stderr.Dispose()
        $process.Dispose()
    }
}

function Invoke-WinCareDotNet {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$FailureMessage
    )
    $result = Invoke-WinCareNativeProcess -Executable $Executable -Arguments $Arguments -TimeoutSeconds 1800 -MaximumCapturedOutputBytes 33554432
    if ($result.StandardOutput) { [Console]::Out.Write($result.StandardOutput) }
    if ($result.StandardError) { [Console]::Error.Write($result.StandardError) }
    if ($result.ExitCode -ne 0) { throw "$FailureMessage ExitCode=$($result.ExitCode)" }
}

function Get-WinCareNativeTimestamp {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$GitExecutable
    )
    $epoch = 0L
    if (-not $env:SOURCE_DATE_EPOCH -or -not [long]::TryParse($env:SOURCE_DATE_EPOCH,[ref]$epoch) -or $epoch -lt 315532800L) {
        $result = Invoke-WinCareNativeProcess -Executable $GitExecutable -Arguments @('-C',$RepositoryRoot,'show','-s','--format=%ct','HEAD') -TimeoutSeconds 30 -MaximumCapturedOutputBytes 4096
        if ($result.ExitCode -ne 0 -or -not [long]::TryParse($result.StandardOutput.Trim(),[ref]$epoch) -or $epoch -lt 315532800L) {
            throw 'SOURCE_DATE_EPOCH is invalid and the Git commit timestamp could not be resolved.'
        }
        $env:SOURCE_DATE_EPOCH = [string]$epoch
    }
    [DateTimeOffset]::FromUnixTimeSeconds($epoch).UtcDateTime.ToString('o')
}

function Get-WinCareNativeSourceEvidence {
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $maximumSources = 64
    $maximumSourceBytes = 16777216L
    $maximumTotalSourceBytes = 67108864L
    $maximumMetadataBytes = 1048576L
    $nativeEntries = @(Get-ChildItem -LiteralPath $PSScriptRoot -Force -ErrorAction Stop)
    $unexpected = @($nativeEntries | Where-Object { $_.PSIsContainer -or $_.Name -notin $script:WinCareNativeSourceNames })
    $missing = @($script:WinCareNativeSourceNames | Where-Object { -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $_) -PathType Leaf) })
    if ($unexpected.Count -or $missing.Count) {
        throw "Native source membership mismatch. Missing=$($missing -join ',') Unexpected=$($unexpected.Name -join ',')"
    }

    $paths = [Collections.Generic.List[string]]::new()
    $paths.Add('global.json')
    foreach ($name in $script:WinCareNativeSourceNames) {
        $paths.Add([IO.Path]::GetRelativePath($RepositoryRoot,(Join-Path $PSScriptRoot $name)).Replace('\','/'))
    }
    if ($paths.Count -gt $maximumSources) { throw "Native source count exceeds $maximumSources." }
    $sorted = [string[]]$paths.ToArray()
    [Array]::Sort($sorted,[StringComparer]::Ordinal)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $records = [Collections.Generic.List[object]]::new()
    $treeHash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $totalBytes = 0L
    $metadataBytes = 0L
    try {
        foreach ($relative in $sorted) {
            if (-not $seen.Add($relative)) { throw "Duplicate native source path: $relative" }
            $path = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $relative))
            $rootPrefix = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not $path.StartsWith($rootPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw "Native source escapes repository root: $relative" }
            $evidence = Get-WinCareNativeFileSha256 -Path $path -MaximumBytes $maximumSourceBytes -Purpose 'Native source'
            $totalBytes += [long]$evidence.Bytes
            if ($totalBytes -gt $maximumTotalSourceBytes) { throw "Native sources exceed the $maximumTotalSourceBytes-byte aggregate limit." }
            $records.Add([ordered]@{ Path=$relative; Sha256=$evidence.Sha256; Bytes=[long]$evidence.Bytes })
            $nameBytes = [Text.Encoding]::UTF8.GetBytes($relative)
            $hashBytes = [Convert]::FromHexString($evidence.Sha256)
            try {
                $metadataBytes += $nameBytes.Length + $hashBytes.Length + 2
                if ($metadataBytes -gt $maximumMetadataBytes) { throw "Native source metadata exceeds $maximumMetadataBytes bytes." }
                $treeHash.AppendData($nameBytes); $treeHash.AppendData([byte[]]@(0))
                $treeHash.AppendData($hashBytes); $treeHash.AppendData([byte[]]@(0))
            } finally {
                [Array]::Clear($nameBytes,0,$nameBytes.Length)
                [Array]::Clear($hashBytes,0,$hashBytes.Length)
            }
        }
        $treeDigest = $treeHash.GetHashAndReset()
        try {
            $treeSha256 = [Convert]::ToHexString($treeDigest).ToLowerInvariant()
        } finally {
            [Array]::Clear($treeDigest,0,$treeDigest.Length)
        }
        [pscustomobject]@{
            Records=@($records)
            TreeSha256=$treeSha256
            SourceCount=$records.Count
            TotalSourceBytes=$totalBytes
            MetadataBytes=$metadataBytes
        }
    } finally {
        $treeHash.Dispose()
    }
}

function Write-WinCareNativeAtomicJson {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Value,
        [ValidateRange(1,1048576)][int]$MaximumBytes = 1048576
    )
    $text = $Value | ConvertTo-Json -Depth 10
    $bytes = [Text.UTF8Encoding]::new($false,$true).GetBytes($text + [Environment]::NewLine)
    if ($bytes.Length -gt $MaximumBytes) {
        [Array]::Clear($bytes,0,$bytes.Length)
        throw "Native JSON output exceeds $MaximumBytes bytes: $Path"
    }
    $temporary = "$Path.tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        $stream = [IO.FileStream]::new($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,65536,[IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($bytes,0,$bytes.Length)
            $stream.Flush($true)
        } finally {
            $stream.Dispose()
        }
        Move-Item -LiteralPath $temporary -Destination $Path -Force -ErrorAction Stop
    } finally {
        [Array]::Clear($bytes,0,$bytes.Length)
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}

$dotnet = Get-Command dotnet -ErrorAction Stop
$git = Get-Command git -ErrorAction Stop
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..') -ErrorAction Stop).Path
$globalJsonPath = Join-Path $repositoryRoot 'global.json'
$globalJson = Read-WinCareNativeJson -Path $globalJsonPath -MaximumBytes 65536
$expectedSdk = [string]$globalJson.sdk.version
if ($expectedSdk -notmatch '^8\.0\.\d{3}$') { throw "Unsupported .NET SDK version in global.json: $expectedSdk" }
$versionResult = Invoke-WinCareNativeProcess -Executable $dotnet.Source -Arguments @('--version') -TimeoutSeconds 30 -MaximumCapturedOutputBytes 4096
$observedSdk = $versionResult.StandardOutput.Trim()
if ($versionResult.ExitCode -ne 0 -or $observedSdk -ne $expectedSdk) { throw "The native build requires .NET SDK $expectedSdk; observed $observedSdk." }
$builtAt = Get-WinCareNativeTimestamp -RepositoryRoot $repositoryRoot -GitExecutable $git.Source

$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    $outputItem = Get-Item -LiteralPath $outputPath -Force -ErrorAction Stop
    if (-not $outputItem.PSIsContainer -or ($outputItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Native output is unsafe: $outputPath" }
    $existingEntries = @(Get-ChildItem -LiteralPath $outputPath -Force -ErrorAction Stop)
    if ($existingEntries.Count) { throw "Native output must be absent or empty: $outputPath" }
} else {
    New-Item -ItemType Directory -Path $outputPath -ErrorAction Stop | Out-Null
}

$knownOutputs = @(
    'WinCare.Native.dll','WinCare.Launcher.exe','WinCare.Launcher.dll',
    'WinCare.Launcher.deps.json','WinCare.Launcher.runtimeconfig.json',
    'WinCare.TuiLauncher.exe','WinCare.TuiLauncher.dll',
    'WinCare.TuiLauncher.deps.json','WinCare.TuiLauncher.runtimeconfig.json',
    'WinCare.Native.build.json'
)
$buildRoot = Join-Path $env:TEMP ('WinCare-native-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildRoot -ErrorAction Stop | Out-Null
try {
    $sourceEvidence = Get-WinCareNativeSourceEvidence -RepositoryRoot $repositoryRoot
    $projects = @(
        [pscustomobject]@{ Project=(Join-Path $PSScriptRoot 'WinCare.Native.csproj'); Name='WinCare.Native'; Publish=$false },
        [pscustomobject]@{ Project=(Join-Path $PSScriptRoot 'WinCare.Launcher.csproj'); Name='WinCare.Launcher'; Publish=$true },
        [pscustomobject]@{ Project=(Join-Path $PSScriptRoot 'WinCare.TuiLauncher.csproj'); Name='WinCare.TuiLauncher'; Publish=$true }
    )
    $commonProperties = @(
        '--property:ContinuousIntegrationBuild=true',
        '--property:Deterministic=true',
        "--property:PathMap=$repositoryRoot=/_/"
    )
    foreach ($project in $projects) {
        $obj = Join-Path $buildRoot ("obj\{0}\" -f $project.Name)
        $bin = Join-Path $buildRoot ("bin\{0}\" -f $project.Name)
        $properties = $commonProperties + @("--property:BaseIntermediateOutputPath=$obj","--property:BaseOutputPath=$bin")
        Invoke-WinCareDotNet -Executable $dotnet.Source -Arguments (@('restore',$project.Project,'--nologo','--disable-parallel') + $properties) -FailureMessage "dotnet restore failed for $($project.Name)."
        if (-not $project.Publish) {
            Invoke-WinCareDotNet -Executable $dotnet.Source -Arguments (@('build',$project.Project,'--configuration',$Configuration,'--no-restore','--nologo') + $properties) -FailureMessage "dotnet build failed for $($project.Name)."
        } else {
            $publishDirectory = Join-Path $buildRoot ("publish\{0}" -f $project.Name)
            Invoke-WinCareDotNet -Executable $dotnet.Source -Arguments (@('publish',$project.Project,'--configuration',$Configuration,'--no-restore','--nologo','--self-contained','false','--output',$publishDirectory) + $properties) -FailureMessage "dotnet publish failed for $($project.Name)."
        }
    }

    $copyPlan = [Collections.Generic.List[object]]::new()
    $nativeSource = Join-Path $buildRoot "bin\WinCare.Native\$Configuration\net8.0-windows\WinCare.Native.dll"
    $copyPlan.Add([pscustomobject]@{ Source=$nativeSource; Name='WinCare.Native.dll' })
    foreach ($launcherName in @('WinCare.Launcher','WinCare.TuiLauncher')) {
        $publishDirectory = Join-Path $buildRoot "publish\$launcherName"
        foreach ($fileName in @("$launcherName.exe","$launcherName.dll","$launcherName.deps.json","$launcherName.runtimeconfig.json")) {
            $copyPlan.Add([pscustomobject]@{ Source=(Join-Path $publishDirectory $fileName); Name=$fileName })
        }
    }

    $artifacts = [Collections.Generic.List[object]]::new()
    foreach ($entry in $copyPlan) {
        $source = Get-WinCareNativeRegularFile -Path $entry.Source -MaximumBytes 268435456L -Purpose 'Native build artifact'
        if ([long]$source.Length -le 0) { throw "Native build artifact is empty: $($entry.Name)" }
        $destination = Join-Path $outputPath $entry.Name
        Copy-Item -LiteralPath $source.FullName -Destination $destination -ErrorAction Stop
        $evidence = Get-WinCareNativeFileSha256 -Path $destination -MaximumBytes 268435456L -Purpose 'Copied native artifact'
        $artifacts.Add([ordered]@{ Path=$entry.Name; Sha256=$evidence.Sha256; Bytes=[long]$evidence.Bytes })
    }

    $dotnetEvidence = Get-WinCareNativeFileSha256 -Path $dotnet.Source -MaximumBytes 536870912L -Purpose '.NET host'
    $manifestValue = [ordered]@{
        SchemaVersion=2
        Configuration=$Configuration
        BuiltAt=$builtAt
        DotNetVersion=$observedSdk
        DotNetHostSha256=$dotnetEvidence.Sha256
        TargetFramework='net8.0-windows'
        SourceTreeSha256=$sourceEvidence.TreeSha256
        SourceCount=$sourceEvidence.SourceCount
        TotalSourceBytes=$sourceEvidence.TotalSourceBytes
        SourceMetadataBytes=$sourceEvidence.MetadataBytes
        Sources=@($sourceEvidence.Records)
        Artifacts=@($artifacts)
    }
    Write-WinCareNativeAtomicJson -Path (Join-Path $outputPath 'WinCare.Native.build.json') -Value $manifestValue

    $actualEntries = @(Get-ChildItem -LiteralPath $outputPath -Force -ErrorAction Stop)
    $unexpected = @($actualEntries | Where-Object { $_.PSIsContainer -or $_.Name -notin $knownOutputs })
    $missingOutputs = @($knownOutputs | Where-Object { -not (Test-Path -LiteralPath (Join-Path $outputPath $_) -PathType Leaf) })
    if ($unexpected.Count -or $missingOutputs.Count) {
        throw "Native output membership mismatch. Missing=$($missingOutputs -join ',') Unexpected=$($unexpected.Name -join ',')"
    }
    $manifestValue
} finally {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force -ErrorAction SilentlyContinue
}
