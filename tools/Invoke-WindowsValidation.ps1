#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\windows-validation'),
    [ValidateRange(60,7200)][int]$GateTimeoutSeconds = 1800,
    [switch]$SkipReleaseBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WinCare.Tooling.ps1')
if (-not $IsWindows) { throw 'Windows validation must run on Windows.' }

$rootPath = (Resolve-Path -LiteralPath $Root).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$tempRoot = Join-Path $env:TEMP ("WinCare-validation-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
$pwsh = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$null = Get-WinCareToolingRegularFile -LiteralPath $pwsh -MaximumBytes 1073741824 -Purpose 'Windows validation PowerShell host'
$gates = [Collections.Generic.List[object]]::new()


function Invoke-WinCareValidationCapturedProcess {
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$Process,
        [ValidateRange(1,3600)][int]$TimeoutSeconds,
        [ValidateRange(1024,16777216)][int]$MaximumBytes=4194304
    )
    $stdout=[IO.MemoryStream]::new($MaximumBytes);$stderr=[IO.MemoryStream]::new($MaximumBytes)
    $stdoutBuffer=[byte[]]::new(65536);$stderrBuffer=[byte[]]::new(65536)
    $cancellation=[Threading.CancellationTokenSource]::new();$cancellation.CancelAfter($TimeoutSeconds*1000)
    $stdoutOpen=$true;$stderrOpen=$true;$stdoutTruncated=$false;$stderrTruncated=$false;$timedOut=$false
    try {
        if(-not $Process.Start()){throw 'The process did not start.'}
        $stdoutStream=$Process.StandardOutput.BaseStream;$stderrStream=$Process.StandardError.BaseStream
        $stdoutRead=$stdoutStream.ReadAsync($stdoutBuffer,0,$stdoutBuffer.Length,$cancellation.Token)
        $stderrRead=$stderrStream.ReadAsync($stderrBuffer,0,$stderrBuffer.Length,$cancellation.Token)
        while($stdoutOpen -or $stderrOpen) {
            $active=[Collections.Generic.List[Threading.Tasks.Task]]::new()
            if($stdoutOpen){$active.Add($stdoutRead)};if($stderrOpen){$active.Add($stderrRead)}
            try{$null=[Threading.Tasks.Task]::WhenAny($active.ToArray()).GetAwaiter().GetResult()}catch [OperationCanceledException]{$timedOut=$true;break}
            foreach($name in @('stdout','stderr')) {
                if($name -eq 'stdout'){$open=$stdoutOpen;$task=$stdoutRead;$buffer=$stdoutBuffer;$memory=$stdout}
                else{$open=$stderrOpen;$task=$stderrRead;$buffer=$stderrBuffer;$memory=$stderr}
                if(-not $open -or -not $task.IsCompleted){continue}
                try{$count=$task.GetAwaiter().GetResult()}catch [OperationCanceledException]{$timedOut=$true;break}
                if($count -eq 0){if($name -eq 'stdout'){$stdoutOpen=$false}else{$stderrOpen=$false};continue}
                $remaining=$MaximumBytes-$memory.Length;$accepted=[int][Math]::Min([long]$count,[Math]::Max(0L,$remaining))
                if($accepted -gt 0){$memory.Write($buffer,0,$accepted)}
                if($accepted -lt $count){if($name -eq 'stdout'){$stdoutTruncated=$true}else{$stderrTruncated=$true}}
                $next=if($name -eq 'stdout'){$stdoutStream.ReadAsync($stdoutBuffer,0,$stdoutBuffer.Length,$cancellation.Token)}else{$stderrStream.ReadAsync($stderrBuffer,0,$stderrBuffer.Length,$cancellation.Token)}
                if($name -eq 'stdout'){$stdoutRead=$next}else{$stderrRead=$next}
            }
            if($timedOut){break}
        }
        if($timedOut -or $cancellation.IsCancellationRequested){$timedOut=$true;try{$Process.Kill($true)}catch{} }
        if(-not $Process.HasExited){$Process.WaitForExit()}
        $stdoutBytes=$stdout.ToArray();$stderrBytes=$stderr.ToArray()
        try {
            [pscustomobject]@{
                TimedOut=$timedOut;ExitCode=if($timedOut){124}else{$Process.ExitCode}
                StdOut=[Text.UTF8Encoding]::new($false,$false).GetString($stdoutBytes)
                StdErr=[Text.UTF8Encoding]::new($false,$false).GetString($stderrBytes)
                StdoutTruncated=$stdoutTruncated;StderrTruncated=$stderrTruncated
            }
        } finally {
            if($stdoutBytes.Length){[Array]::Clear($stdoutBytes,0,$stdoutBytes.Length)}
            if($stderrBytes.Length){[Array]::Clear($stderrBytes,0,$stderrBytes.Length)}
        }
    } finally {
        $cancellation.Dispose();[Array]::Clear($stdoutBuffer,0,$stdoutBuffer.Length);[Array]::Clear($stderrBuffer,0,$stderrBuffer.Length)
        $stdout.Dispose();$stderr.Dispose()
    }
}

function Invoke-ValidationGate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Script,
        [int]$TimeoutSeconds = $GateTimeoutSeconds
    )
    $scriptPath = Join-Path $tempRoot ($Name + '.ps1')
    $stdout = Join-Path $outputPath ($Name + '.out.txt')
    $stderr = Join-Path $outputPath ($Name + '.err.txt')
    Set-Content -LiteralPath $scriptPath -Value $Script -Encoding utf8NoBOM

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $pwsh
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    foreach ($argument in @('-NoLogo','-NoProfile','-NonInteractive','-File',$scriptPath)) {
        $null = $psi.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    $started = [datetime]::UtcNow
    $capture = Invoke-WinCareValidationCapturedProcess -Process $process -TimeoutSeconds $TimeoutSeconds -MaximumBytes 16777216
    $timedOut = [bool]$capture.TimedOut
    $outText = [string]$capture.StdOut
    $errText = [string]$capture.StdErr
    if($capture.StdoutTruncated){$outText += "`n[WinCare validation truncated stdout at 16 MiB.]"}
    if($capture.StderrTruncated){$errText += "`n[WinCare validation truncated stderr at 16 MiB.]"}
    Set-Content -LiteralPath $stdout -Value $outText -Encoding utf8NoBOM
    Set-Content -LiteralPath $stderr -Value $errText -Encoding utf8NoBOM

    $record = [pscustomobject]@{
        Name = $Name
        Status = if (-not $timedOut -and $capture.ExitCode -eq 0) { 'passed' } elseif ($timedOut) { 'timed-out' } else { 'failed' }
        ExitCode = if ($timedOut) { -1 } else { $capture.ExitCode }
        StartedAt = $started.ToString('o')
        DurationMilliseconds = [math]::Round(([datetime]::UtcNow - $started).TotalMilliseconds)
        StdOut = $stdout
        StdErr = $stderr
    }
    $gates.Add($record)
    $process.Dispose()
    $record
}

try {
    $quotedRoot = $rootPath.Replace("'", "''")
    $quotedOutput = $outputPath.Replace("'", "''")
    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $dotnetResult = Invoke-WinCareToolingProcess -Executable $dotnetCommand.Source -Arguments @('--version') -TimeoutSeconds 30 -MaximumCapturedOutputBytes 4096
    if ($dotnetResult.ExitCode -ne 0) { throw 'dotnet --version failed during environment capture.' }
    $dotnetVersion = $dotnetResult.StandardOutput.Trim()
    $environment = [ordered]@{
        schema = 'wincare.windows.environment/v2'
        capturedAt = [datetime]::UtcNow.ToString('o')
        computerName = $env:COMPUTERNAME
        user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        isAdministrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        os = Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,OSArchitecture
        powershell = $PSVersionTable.PSVersion.ToString()
        edition = $PSVersionTable.PSEdition
        dotnet = $dotnetVersion
        culture = [Globalization.CultureInfo]::CurrentCulture.Name
        uiCulture = [Globalization.CultureInfo]::CurrentUICulture.Name
        capabilities = [ordered]@{
            winget = [bool](Get-Command winget.exe -ErrorAction SilentlyContinue)
            defender = [bool](Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue)
            citool = [bool](Get-Command CiTool.exe -ErrorAction SilentlyContinue)
            appx = [bool](Get-Command Get-AppxPackage -ErrorAction SilentlyContinue)
            wuaCom = try { $null = New-Object -ComObject Microsoft.Update.Session; $true } catch { $false }
        }
    }
    $environment | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputPath 'environment.json') -Encoding utf8NoBOM

    $static = Invoke-ValidationGate -Name '01-static' -Script @"
`$ErrorActionPreference = 'Stop'
& '$quotedRoot\tools\Invoke-StaticChecks.ps1' -Root '$quotedRoot' -RequirePSScriptAnalyzer -OutputPath '$quotedOutput\powershell-static-validation.json'
python '$quotedRoot\tools\validate_action_bindings.py' '$quotedRoot' --output '$quotedOutput\action-binding-validation.json'
if (`$LASTEXITCODE -ne 0) { throw 'Action binding validation failed.' }
python '$quotedRoot\tools\validate_test_source_assertions.py' '$quotedRoot' --output '$quotedOutput\test-source-validation.json'
if (`$LASTEXITCODE -ne 0) { throw 'Test/source assertion validation failed.' }
python '$quotedRoot\tools\validate_powershell_calls.py' '$quotedRoot' --output '$quotedOutput\public-call-bindings.json'
if (`$LASTEXITCODE -ne 0) { throw 'Public call binding validation failed.' }
python '$quotedRoot\tools\validate_module_manifest.py' '$quotedRoot' --output '$quotedOutput\module-source-manifest.json'
python '$quotedRoot\tools\validate_source_references.py' '$quotedRoot' --output '$quotedOutput\source-reference-validation.json'
if (`$LASTEXITCODE -ne 0) { throw 'Module source manifest validation failed.' }
python '$quotedRoot\tools\audit_stub_inventory.py' '$quotedRoot' --output '$quotedOutput\stub-inventory.json'
if (`$LASTEXITCODE -ne 0) { throw 'Anti-stub validation failed.' }
python '$quotedRoot\tools\validate_maintainability.py' '$quotedRoot' --output '$quotedOutput\maintainability-validation.json'
if (`$LASTEXITCODE -ne 0) { throw 'Maintainability validation failed.' }
python '$quotedRoot\tools\validate_network_egress.py' '$quotedRoot' --output '$quotedOutput\network-egress-validation.json'
if (`$LASTEXITCODE -ne 0) { throw 'Network egress validation failed.' }
python '$quotedRoot\tools\validate_external_processes.py' '$quotedRoot' --output '$quotedOutput\external-process-validation.json'
if (`$LASTEXITCODE -ne 0) { throw 'External process boundary validation failed.' }
python '$quotedRoot\tools\validate_bounded_io.py' '$quotedRoot' --output '$quotedOutput\bounded-io-validation.json'
if (`$LASTEXITCODE -ne 0) { throw 'Bounded I/O validation failed.' }
python '$quotedRoot\tools\validate_read_only_state.py' '$quotedRoot' --output '$quotedOutput\read-only-state-validation.json'
if (`$LASTEXITCODE -ne 0) { throw 'Read-only state validation failed.' }
python '$quotedRoot\tools\validate_context_menu_catalog.py' '$quotedRoot' --output '$quotedOutput\context-menu-validation.json'
if (`$LASTEXITCODE -ne 0) { throw 'Context-menu validation failed.' }
python '$quotedRoot\tools\validate_test_fixtures.py' '$quotedRoot' --output '$quotedOutput\test-fixture-validation.json'
if (`$LASTEXITCODE -ne 0) { throw 'Test fixture validation failed.' }
python -m unittest tools.test_security_invariants tools.test_source_references tools.test_wincare_ps_source -v
if (`$LASTEXITCODE -ne 0) { throw 'Security invariant tests failed.' }
"@

    $pester = Invoke-ValidationGate -Name '02-pester' -Script @"
`$ErrorActionPreference = 'Stop'
Import-Module Pester -RequiredVersion 5.5.0 -ErrorAction Stop
`$configuration = New-PesterConfiguration
`$configuration.Run.Path = '$quotedRoot\tests'
`$configuration.Run.PassThru = `$true
`$configuration.Output.Verbosity = 'Detailed'
`$result = Invoke-Pester -Configuration `$configuration
`$failedCount = if (`$null -ne `$result.FailedCount) { [int]`$result.FailedCount } else { @(`$result.TestResult | Where-Object Passed -eq `$false).Count }
# A file that fails during Discovery (for example InModuleScope before the module
# is loaded) books no failed tests at all -- Pester records it under
# FailedContainersCount/FailedBlocksCount. Counting only FailedCount therefore
# reports a green gate while whole test files never ran.
`$failedContainers = if (`$null -ne `$result.FailedContainersCount) { [int]`$result.FailedContainersCount } else { 0 }
`$failedBlocks = if (`$null -ne `$result.FailedBlocksCount) { [int]`$result.FailedBlocksCount } else { 0 }
`$failedCount = `$failedCount + `$failedContainers + `$failedBlocks
`$passedCount = if (`$null -ne `$result.PassedCount) { [int]`$result.PassedCount } else { @(`$result.TestResult | Where-Object Passed -eq `$true).Count }
[ordered]@{ passed=`$passedCount; failed=`$failedCount; skipped=[int]`$result.SkippedCount; duration=[string]`$result.Duration } | ConvertTo-Json | Set-Content -LiteralPath '$quotedOutput\pester-summary.json' -Encoding utf8NoBOM
if (`$failedCount -gt 0) { throw "Pester reported `$failedCount failed test(s)." }
"@

    $module = Invoke-ValidationGate -Name '03-module-smoke' -Script @"
`$ErrorActionPreference = 'Stop'
`$manifestPath = '$quotedRoot\src\WinCare\WinCare.psd1'
`$manifest = Test-ModuleManifest -Path `$manifestPath -ErrorAction Stop
Import-Module `$manifestPath -Force -ErrorAction Stop
`$expected = @(`$manifest.ExportedFunctions.Keys | Sort-Object)
`$actual = @((Get-Command -Module WinCare -CommandType Function).Name | Sort-Object)
`$differences = @(Compare-Object `$expected `$actual)
if (`$differences.Count) { throw "Module export mismatch: `$(`$differences | Out-String)" }
if (Get-Command Get-WinCareOpenSslPqcCapability -ErrorAction SilentlyContinue) { throw 'Private capability helper leaked into the public command surface.' }
`$capabilities = Get-WinCareCapabilityRegistry
if (`$capabilities.Capabilities.Count -lt 8) { throw 'Capability registry is incomplete.' }
if (@(`$capabilities.Capabilities | Where-Object { `$null -eq `$_.Available -or [string]::IsNullOrWhiteSpace([string]`$_.Mode) }).Count) { throw 'Capability registry contains malformed records.' }
Initialize-WinCareState -ReadOnly -SkipConfigSave
`$gui = Test-WinCareGuiCatalog
if (-not `$gui.Success -or `$gui.Actions -lt 1 -or `$gui.Commands -lt 1) { throw "GUI catalog validation failed: `$(`$gui.Errors -join '; ')" }
foreach (`$command in @('system','catalog','presets','knowledge')) {
    `$result = Invoke-WinCareHeadlessCommand -Command `$command -JsonObject
    if (`$null -eq `$result) { throw "Headless command returned no result: `$command" }
}
[ordered]@{ exports=`$actual.Count; capabilities=`$capabilities.Capabilities.Count; guiActions=`$gui.Actions; headlessCommands=4 } | ConvertTo-Json | Set-Content -LiteralPath '$quotedOutput\module-smoke.json' -Encoding utf8NoBOM
"@

    $native = Invoke-ValidationGate -Name '04-native-build' -TimeoutSeconds ([math]::Max($GateTimeoutSeconds, 1800)) -Script @"
`$ErrorActionPreference = 'Stop'
. '$quotedRoot\tools\WinCare.Tooling.ps1'
`$nativeOutput = '$quotedOutput\native'
& '$quotedRoot\src\WinCare\Native\Build-WinCareNativePolyglot.ps1' -OutputDirectory `$nativeOutput -Configuration Release
`$manifestPath = Join-Path `$nativeOutput 'WinCare.Native.build.json'
`$manifest = Read-WinCareToolingJson -LiteralPath `$manifestPath -MaximumBytes 1048576 -MaximumDepth 32
if ([int]`$manifest.SchemaVersion -ne 2) { throw 'Native build manifest schema 2 is required.' }
if ([string]`$manifest.Configuration -ne 'Release' -or [string]`$manifest.TargetFramework -ne 'net8.0-windows') { throw 'Native build configuration or target framework mismatch.' }
`$globalJsonPath = Join-Path '$quotedRoot' 'global.json'
`$globalJson = Read-WinCareToolingJson -LiteralPath `$globalJsonPath -MaximumBytes 65536 -MaximumDepth 8
`$expectedSdk = [string]`$globalJson.sdk.version
if ([string]`$manifest.DotNetVersion -ne `$expectedSdk) { throw 'Native build SDK provenance does not match global.json.' }
`$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
`$dotnetEvidence = Get-WinCareToolingFileSha256 -LiteralPath `$dotnetPath -MaximumBytes 1073741824L -Purpose '.NET host'
if (`$dotnetEvidence.Sha256 -ne [string]`$manifest.DotNetHostSha256) { throw 'Native build host hash mismatch.' }

`$nativeEntries = @(Get-ChildItem -LiteralPath '$quotedRoot\src\WinCare\Native' -Force -ErrorAction Stop)
`$unsupportedSources = @(`$nativeEntries | Where-Object { `$_.PSIsContainer -or (`$_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or `$_.Extension -notin @('.cs','.csproj','.ps1') })
if (`$unsupportedSources.Count) { throw "Unsupported native source entries: `$(`$unsupportedSources.Name -join ', ')" }
if (`$nativeEntries.Count -gt 63) { throw 'Native source count exceeds 63 files plus global.json.' }
`$sourceNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
`$expectedSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
`$null = `$expectedSources.Add('global.json')
foreach (`$source in `$nativeEntries) {
    `$relativeSource = [IO.Path]::GetRelativePath('$quotedRoot',`$source.FullName).Replace('/','\')
    if (-not `$expectedSources.Add(`$relativeSource)) { throw "Duplicate expected native source path: `$relativeSource" }
}
`$records = @(`$manifest.Sources)
if (`$records.Count -ne [int]`$manifest.SourceCount -or `$records.Count -ne `$expectedSources.Count -or `$records.Count -gt 64) { throw 'Native source count provenance mismatch.' }
`$sourceHash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
`$sourceTotal = 0L
`$metadataTotal = 0L
try {
    foreach (`$record in @(`$records | Sort-Object { [string]`$_.Path } -CaseSensitive)) {
        `$canonical = ([string]`$record.Path).Replace('\','/')
        `$relative = `$canonical.Replace('/','\')
        if ([string]::IsNullOrWhiteSpace(`$relative) -or [IO.Path]::IsPathRooted(`$relative) -or `$relative.Split('\') -contains '..' -or -not `$sourceNames.Add(`$relative)) { throw "Unsafe or duplicate native source path: `$relative" }
        `$path = [IO.Path]::GetFullPath((Join-Path '$quotedRoot' `$relative))
        `$prefix = [IO.Path]::GetFullPath('$quotedRoot').TrimEnd('\') + '\'
        if (-not `$path.StartsWith(`$prefix,[StringComparison]::OrdinalIgnoreCase)) { throw "Native source escapes repository root: `$relative" }
        `$evidence = Get-WinCareToolingFileSha256 -LiteralPath `$path -MaximumBytes 16777216L -Purpose 'Native source'
        if ([long]`$evidence.Bytes -ne [long]`$record.Bytes -or `$evidence.Sha256 -ne [string]`$record.Sha256) { throw "Native source provenance mismatch: `$relative" }
        `$sourceTotal += [long]`$evidence.Bytes
        if (`$sourceTotal -gt 67108864L) { throw 'Native source aggregate exceeds 64 MiB.' }
        `$nameBytes = [Text.Encoding]::UTF8.GetBytes(`$canonical)
        `$hashBytes = [Convert]::FromHexString(`$evidence.Sha256)
        try {
            `$metadataTotal += `$nameBytes.Length + `$hashBytes.Length + 2
            if (`$metadataTotal -gt 1048576L) { throw 'Native source metadata exceeds 1 MiB.' }
            `$sourceHash.AppendData(`$nameBytes); `$sourceHash.AppendData([byte[]]@(0))
            `$sourceHash.AppendData(`$hashBytes); `$sourceHash.AppendData([byte[]]@(0))
        } finally {
            [Array]::Clear(`$nameBytes,0,`$nameBytes.Length)
            [Array]::Clear(`$hashBytes,0,`$hashBytes.Length)
        }
    }
    `$missingSources = @(`$expectedSources | Where-Object { -not `$sourceNames.Contains(`$_) })
    `$extraSources = @(`$sourceNames | Where-Object { -not `$expectedSources.Contains(`$_) })
    if (`$missingSources.Count -or `$extraSources.Count) { throw "Native source membership mismatch: missing=`$(`$missingSources -join ','); extra=`$(`$extraSources -join ',')" }
    if (`$sourceTotal -ne [long]`$manifest.TotalSourceBytes -or `$metadataTotal -ne [long]`$manifest.SourceMetadataBytes) { throw 'Native source aggregate provenance mismatch.' }
    `$treeDigest = `$sourceHash.GetHashAndReset()
    try { `$treeSha = [Convert]::ToHexString(`$treeDigest).ToLowerInvariant() }
    finally { [Array]::Clear(`$treeDigest,0,`$treeDigest.Length) }
    if (`$treeSha -ne [string]`$manifest.SourceTreeSha256) { throw 'Native source tree hash mismatch.' }
} finally { `$sourceHash.Dispose() }

`$artifactNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
`$artifactBytes = 0L
foreach (`$artifact in @(`$manifest.Artifacts)) {
    `$name = [IO.Path]::GetFileName([string]`$artifact.Path)
    if (`$name -ne [string]`$artifact.Path -or -not `$artifactNames.Add(`$name)) { throw "Unsafe or duplicate native artifact path: `$(`$artifact.Path)" }
    `$path = Join-Path `$nativeOutput `$name
    `$evidence = Get-WinCareToolingFileSha256 -LiteralPath `$path -MaximumBytes 268435456L -Purpose 'Native artifact'
    if ([long]`$evidence.Bytes -ne [long]`$artifact.Bytes -or `$evidence.Sha256 -ne [string]`$artifact.Sha256) { throw "Native artifact provenance mismatch: `$name" }
    `$artifactBytes += [long]`$evidence.Bytes
    if (`$artifactBytes -gt 1073741824L) { throw 'Native artifact aggregate exceeds 1 GiB.' }
}
foreach (`$required in @('WinCare.Native.dll','WinCare.Launcher.exe','WinCare.TuiLauncher.exe')) { if (-not `$artifactNames.Contains(`$required)) { throw "Required native artifact missing: `$required" } }
`$allowed = [Collections.Generic.HashSet[string]]::new(`$artifactNames,[StringComparer]::OrdinalIgnoreCase)
`$null = `$allowed.Add('WinCare.Native.build.json')
`$unexpected = @(Get-ChildItem -LiteralPath `$nativeOutput -Force | Where-Object { `$_.PSIsContainer -or (`$_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or -not `$allowed.Contains(`$_.Name) })
if (`$unexpected.Count) { throw "Native output contains unexpected entries: `$(`$unexpected.Name -join ', ')" }
[ordered]@{ schema='wincare.native.validation/v3'; sources=`$sourceNames.Count; sourceBytes=`$sourceTotal; artifacts=`$artifactNames.Count; artifactBytes=`$artifactBytes; sourceTreeSha256=`$treeSha; status='passed' } | ConvertTo-Json | Set-Content -LiteralPath '$quotedOutput\native-validation.json' -Encoding utf8NoBOM

"@

    if (-not $SkipReleaseBuild) {
        $release = Invoke-ValidationGate -Name '05-release' -TimeoutSeconds ([math]::Max($GateTimeoutSeconds, 3600)) -Script "& '$quotedRoot\tools\Build-Release.ps1' -Root '$quotedRoot' -OutputDirectory '$quotedOutput\release' -SkipTests"
        $cleanInstall = Invoke-ValidationGate -Name '06-clean-install' -TimeoutSeconds ([math]::Max($GateTimeoutSeconds, 1800)) -Script @"
`$ErrorActionPreference = 'Stop'
`$manifest = Import-PowerShellDataFile '$quotedRoot\src\WinCare\WinCare.psd1'
`$archive = Join-Path '$quotedOutput\release' ("WinCare-{0}.zip" -f `$manifest.ModuleVersion)
if (-not (Test-Path -LiteralPath `$archive -PathType Leaf)) { throw "Release archive is missing: `$archive" }
`$sandbox = Join-Path `$env:TEMP ('WinCare-clean-install-' + [guid]::NewGuid().ToString('N'))
`$extract = Join-Path `$sandbox 'extract'
`$destination = Join-Path `$sandbox 'installed'
try {
    python '$quotedRoot\tools\verify_release.py' `$archive --extract-to `$extract --output '$quotedOutput\clean-install-archive-validation.json'
    if (`$LASTEXITCODE -ne 0) { throw 'Validated release extraction failed.' }
    `$releaseRoot = Join-Path `$extract ("WinCare-{0}" -f `$manifest.ModuleVersion)
    & (Join-Path `$releaseRoot 'Install-WinCare.ps1') -Destination `$destination -NoStartMenuShortcut
    `$installedManifest = Join-Path `$destination 'src\WinCare\WinCare.psd1'
    Import-Module `$installedManifest -Force -ErrorAction Stop
    Initialize-WinCareState -ReadOnly -SkipConfigSave
    `$nativeManifest = Join-Path `$destination 'bin\WinCare.Native.build.json'
    if (-not (Test-Path -LiteralPath `$nativeManifest -PathType Leaf)) { throw 'Source-built native manifest was not installed.' }
    foreach (`$command in @('system','catalog','presets','knowledge')) {
        `$result = Invoke-WinCareHeadlessCommand -Command `$command -JsonObject
        if (`$null -eq `$result) { throw "Installed headless command returned no result: `$command" }
    }
    Remove-Module WinCare -Force
    & (Join-Path `$destination 'Uninstall-WinCare.ps1') -Destination `$destination -Confirm:`$false
    if (Test-Path -LiteralPath `$destination) { throw 'Clean-install destination remains after uninstall.' }
} finally {
    Remove-Module WinCare -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath `$sandbox -Recurse -Force -ErrorAction SilentlyContinue
}
"@
    }

    $failed = @($gates | Where-Object Status -ne 'passed')
    $report = [ordered]@{
        schema = 'wincare.windows.validation/v2'
        generatedAt = [datetime]::UtcNow.ToString('o')
        sourceRoot = $rootPath
        environment = $environment
        gates = @($gates)
        status = if ($failed.Count) { 'failed' } else { 'passed' }
    }
    $reportPath = Join-Path $outputPath 'windows-validation-report.json'
    $report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    if ($failed.Count) {
        # A failing gate previously reported only its name here: the actual
        # output lived in artifacts that a reader has to download separately,
        # which makes a red build undiagnosable from the log alone. Echo a
        # bounded tail of each failing gate's streams so the cause is visible
        # in the job output itself.
        foreach ($record in $failed) {
            Write-Host ''
            Write-Host "===== $($record.Name): $($record.Status) (exit $($record.ExitCode)) =====" -ForegroundColor Red
            foreach ($stream in @(
                @{ Label = 'stdout'; Path = $record.StdOut },
                @{ Label = 'stderr'; Path = $record.StdErr }
            )) {
                if (-not (Test-Path -LiteralPath $stream.Path -PathType Leaf)) { continue }
                # Bounded read: the gate streams are already capped at 16 MiB when
                # captured, and Get-Content is banned tree-wide as an unbounded read.
                $text = try {
                    Read-WinCareToolingBoundedUtf8Text -LiteralPath $stream.Path `
                        -MaximumBytes 4194304 -Purpose "Failed gate $($record.Name) $($stream.Label)"
                } catch { '' }
                $lines = @($text -split "`r?`n" | Where-Object { $_ -ne '' })
                if (-not $lines.Count) { continue }
                $tail = @($lines | Select-Object -Last 120)
                Write-Host "--- $($record.Name) $($stream.Label) (last $($tail.Count) of $($lines.Count) line(s)) ---" -ForegroundColor Yellow
                foreach ($line in $tail) { Write-Host $line }
            }
        }
        Write-Host ''
        throw "Windows validation failed in gate(s): $($failed.Name -join ', ')"
    }
    Write-Host "Windows validation passed: $reportPath" -ForegroundColor Green
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
