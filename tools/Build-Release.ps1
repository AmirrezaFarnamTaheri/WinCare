#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'),
    [switch]$SkipTests,
    [string]$PreviousReleaseArchivePath,
    [switch]$AllowDirty
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WinCare.Tooling.ps1')

$diagnosticsDirectory = if ([string]::IsNullOrWhiteSpace($env:WINCARE_DIAGNOSTICS_DIRECTORY)) { $null } else { [IO.Path]::GetFullPath($env:WINCARE_DIAGNOSTICS_DIRECTORY) }
$tracePath = if ($diagnosticsDirectory) { New-Item -ItemType Directory -Path $diagnosticsDirectory -Force | Out-Null; Join-Path $diagnosticsDirectory 'release-build-trace.jsonl' } else { $null }
function Write-WinCareReleaseTrace {
    param([Parameter(Mandatory)][string]$Phase,[Parameter(Mandatory)][ValidateSet('started','passed','failed','info')][string]$Status,[hashtable]$Details=@{})
    $record=[ordered]@{schema='wincare.release.trace/v1';timestamp=[datetime]::UtcNow.ToString('o');phase=$Phase;status=$Status;details=$Details}
    $text=$record|ConvertTo-Json -Compress -Depth 12
    Write-Host "[release][$Status][$Phase] $($Details.message)"
    if($tracePath){[IO.File]::AppendAllText($tracePath,$text+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))}
}
function Enter-WinCareReleaseGroup { param([string]$Name) if($env:GITHUB_ACTIONS -eq 'true'){Write-Host "::group::release/$Name"};Write-WinCareReleaseTrace -Phase $Name -Status started -Details @{message='phase started'} }
function Exit-WinCareReleaseGroup { param([string]$Name,[string]$Status='passed',[hashtable]$Details=@{message='phase completed'}) Write-WinCareReleaseTrace -Phase $Name -Status $Status -Details $Details;if($env:GITHUB_ACTIONS -eq 'true'){Write-Host '::endgroup::'} }

function Assert-WinCareReleaseDirectory {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$RepositoryRoot)
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    $drive = [IO.Path]::GetPathRoot($full).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    $trimmed = $full.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    if ($trimmed -eq $root -or $trimmed -eq $drive) { throw "Unsafe release output directory: $full" }
    $cursor = $full
    while (-not (Test-Path -LiteralPath $cursor)) {
        $parent = [IO.Directory]::GetParent($cursor)
        if ($null -eq $parent) { break }
        $cursor = $parent.FullName
    }
    if (Test-Path -LiteralPath $cursor) {
        $existing = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
        if ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Release output ancestry contains a reparse point: $cursor" }
    }
    if (Test-Path -LiteralPath $full) {
        $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Release output is unsafe: $full" }
        if (@(Get-ChildItem -LiteralPath $full -Force).Count) { throw "Release output must be absent or empty: $full" }
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
    Enter-WinCareReleaseGroup 'static-checks'
    $hasPssa = [bool](Get-Module -ListAvailable PSScriptAnalyzer -ErrorAction SilentlyContinue)
    $pssaArgs = @{}
    if ($hasPssa) { $pssaArgs.RequirePSScriptAnalyzer = $true }
    & (Join-Path $PSScriptRoot 'Invoke-StaticChecks.ps1') -Root $rootPath @pssaArgs -OutputPath (Join-Path $workRoot 'powershell-static-validation.json')
    Exit-WinCareReleaseGroup 'static-checks'

    if (-not $SkipTests) {
        Enter-WinCareReleaseGroup 'pester'
        Import-Module Pester -MinimumVersion 5.5.0 -ErrorAction Stop
        $pester = Invoke-Pester -Path (Join-Path $rootPath 'tests') -PassThru
        $failedTests = if ($null -ne $pester.FailedCount) { [int]$pester.FailedCount } else { @($pester.TestResult | Where-Object Passed -eq $false).Count }
        $failedBlocks = if ($null -ne $pester.FailedBlocksCount) { [int]$pester.FailedBlocksCount } else { 0 }
        $failedContainers = if ($null -ne $pester.FailedContainersCount) { [int]$pester.FailedContainersCount } else { 0 }
        if ($failedTests -gt 0 -or $failedBlocks -gt 0 -or $failedContainers -gt 0) {
            Exit-WinCareReleaseGroup 'pester' 'failed' @{message="Pester failures: tests=$failedTests blocks=$failedBlocks containers=$failedContainers"}
            throw "Pester gate failed: tests=$failedTests blocks=$failedBlocks containers=$failedContainers."
        }
        Exit-WinCareReleaseGroup 'pester' 'passed' @{message="Pester passed: tests=$($pester.PassedCount)"}
    }

    $python = Get-Command python,python3,'C:\Program Files\Python311\python.exe' -ErrorAction SilentlyContinue |
        Where-Object Source -NotMatch 'WindowsApps' | Select-Object -ExpandProperty Source -First 1
    if (-not $python) { throw 'Python 3 is required by the deterministic release builder.' }

    if (-not $SkipTests) {
        Enter-WinCareReleaseGroup 'standalone-adversarial-tests'
        $testResult = Invoke-WinCareToolingProcess -Executable $python -Arguments @('-m','unittest','tools.test_standalone_release','-v') -TimeoutSeconds 300 -MaximumCapturedOutputBytes 16777216 -WorkingDirectory $rootPath -WriteCapturedOutput
        if ($testResult.ExitCode -ne 0) { Exit-WinCareReleaseGroup 'standalone-adversarial-tests' 'failed' @{message="exit=$($testResult.ExitCode)"}; throw 'Standalone release adversarial tests failed.' }
        Exit-WinCareReleaseGroup 'standalone-adversarial-tests'
    }

    Enter-WinCareReleaseGroup 'native-build'
    $nativeDirectory = Join-Path $workRoot 'native'
    & (Join-Path $rootPath 'src\WinCare\Native\Build-WinCareNativePolyglot.ps1') -OutputDirectory $nativeDirectory -Configuration Release
    Exit-WinCareReleaseGroup 'native-build'

    Enter-WinCareReleaseGroup 'core-production-package'
    $coreDirectory = Join-Path $workRoot 'core'
    New-Item -ItemType Directory -Path $coreDirectory -ErrorAction Stop | Out-Null
    $coreArguments = @((Join-Path $PSScriptRoot 'build_release.py'),$rootPath,'--output-directory',$coreDirectory,'--native-directory',$nativeDirectory,'--profile','production')
    if ($AllowDirty) { $coreArguments += '--allow-dirty' }
    $coreBuild = Invoke-WinCareToolingProcess -Executable $python -Arguments $coreArguments -TimeoutSeconds 3600 -MaximumCapturedOutputBytes 67108864 -WorkingDirectory $rootPath -WriteCapturedOutput
    if ($coreBuild.ExitCode -ne 0) { Exit-WinCareReleaseGroup 'core-production-package' 'failed' @{message="exit=$($coreBuild.ExitCode)"}; throw "Core deterministic release build failed. ExitCode=$($coreBuild.ExitCode)" }
    Exit-WinCareReleaseGroup 'core-production-package'

    $version = [string](Import-PowerShellDataFile (Join-Path $rootPath 'src\WinCare\WinCare.psd1')).ModuleVersion
    $coreArchive = Join-Path $coreDirectory "WinCare-$version.zip"
    $null = Get-WinCareToolingRegularFile -LiteralPath $coreArchive -MaximumBytes 2147483648L -Purpose 'Core release archive'

    Enter-WinCareReleaseGroup 'standalone-payload'
    $payloadPath = Join-Path $workRoot 'WinCare.Payload.zip'
    $payloadBuild = Invoke-WinCareToolingProcess -Executable $python -Arguments @((Join-Path $PSScriptRoot 'prepare_standalone_payload.py'),$coreArchive,$payloadPath) -TimeoutSeconds 600 -MaximumCapturedOutputBytes 4194304 -WorkingDirectory $rootPath -WriteCapturedOutput
    if ($payloadBuild.ExitCode -ne 0) { throw 'Standalone payload generation failed.' }
    $payloadReport = [string]$payloadBuild.StandardOutput | ConvertFrom-Json -Depth 20 -ErrorAction Stop
    if ($payloadReport.status -ne 'passed' -or [string]$payloadReport.sha256 -notmatch '^[0-9a-f]{64}$' -or [string]$payloadReport.manifestSha256 -notmatch '^[0-9a-f]{64}$') {
        Exit-WinCareReleaseGroup 'standalone-payload' 'failed' @{message='invalid payload report'}
        throw 'Standalone payload report is invalid.'
    }
    Exit-WinCareReleaseGroup 'standalone-payload' 'passed' @{message="sha256=$($payloadReport.sha256) members=$($payloadReport.members) bytes=$($payloadReport.bytes)"}

    Enter-WinCareReleaseGroup 'standalone-publish'
    $standaloneDirectory = Join-Path $workRoot 'standalone'
    New-Item -ItemType Directory -Path $standaloneDirectory -ErrorAction Stop | Out-Null
    & (Join-Path $PSScriptRoot 'Build-Exe.ps1') -Root $rootPath -OutputDirectory $standaloneDirectory -PayloadPath $payloadPath -PayloadSha256 $payloadReport.sha256 -PayloadManifestSha256 $payloadReport.manifestSha256
    Exit-WinCareReleaseGroup 'standalone-publish'

    Enter-WinCareReleaseGroup 'finalize-release'
    $finalize = Invoke-WinCareToolingProcess -Executable $python -Arguments @((Join-Path $PSScriptRoot 'finalize_release.py'),$coreArchive,$standaloneDirectory,$outputPath) -TimeoutSeconds 1200 -MaximumCapturedOutputBytes 16777216 -WorkingDirectory $rootPath -WriteCapturedOutput
    if ($finalize.ExitCode -ne 0) { Exit-WinCareReleaseGroup 'finalize-release' 'failed' @{message="exit=$($finalize.ExitCode)"}; throw 'Final standalone release assembly failed.' }
    Exit-WinCareReleaseGroup 'finalize-release'

    Enter-WinCareReleaseGroup 'verify-v3'
    $archive = Join-Path $outputPath "WinCare-$version.zip"
    $validationPath = Join-Path $workRoot "WinCare-$version-validation-v3.json"
    $verify = Invoke-WinCareToolingProcess -Executable $python -Arguments @((Join-Path $PSScriptRoot 'verify_release_v3.py'),$archive,'--asset-directory',$outputPath) -TimeoutSeconds 600 -MaximumCapturedOutputBytes 16777216 -WorkingDirectory $rootPath -WriteCapturedOutput
    [IO.File]::WriteAllText($validationPath,[string]$verify.StandardOutput,[Text.UTF8Encoding]::new($false))
    if ($verify.ExitCode -ne 0) { throw 'Final v3 release verification failed.' }
    $validation = [string]$verify.StandardOutput | ConvertFrom-Json -Depth 50 -ErrorAction Stop
    if ($validation.status -ne 'passed') { Exit-WinCareReleaseGroup 'verify-v3' 'failed' @{message='verifier status was not passed'}; throw 'Final v3 release verifier did not report passed.' }
    $archiveSha256 = [string]$validation.details.archiveSha256
    $verifiedMembers = [int]$validation.details.members
    if ($archiveSha256 -notmatch '^[0-9a-f]{64}$' -or $verifiedMembers -lt 1) {
        Exit-WinCareReleaseGroup 'verify-v3' 'failed' @{message='verifier details contract is missing or invalid'}
        throw 'Final v3 release verifier omitted required archive evidence.'
    }
    Exit-WinCareReleaseGroup 'verify-v3' 'passed' @{message="archiveSha256=$archiveSha256 members=$verifiedMembers"}

    Enter-WinCareReleaseGroup 'installation-lifecycle'
    $lifecycleParameters = @{
        ArchivePath = $archive
        WorkRoot = (Join-Path $workRoot 'install-lifecycle')
    }
    if (-not [string]::IsNullOrWhiteSpace($PreviousReleaseArchivePath)) {
        $lifecycleParameters.PreviousArchivePath = (Resolve-Path -LiteralPath $PreviousReleaseArchivePath -ErrorAction Stop).Path
    }
    & (Join-Path $PSScriptRoot 'Test-InstallationLifecycle.ps1') @lifecycleParameters
    Exit-WinCareReleaseGroup 'installation-lifecycle'

    $unexpectedDirectories = @(Get-ChildItem -LiteralPath $outputPath -Directory -Force -ErrorAction Stop)
    if ($unexpectedDirectories.Count) { throw "Final release output contains unexpected directories: $($unexpectedDirectories.Name -join ', ')" }
    $assetInventory=@(Get-ChildItem -LiteralPath $outputPath -File -Force|Sort-Object Name|ForEach-Object{[ordered]@{name=$_.Name;bytes=[long]$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
    Write-WinCareReleaseTrace -Phase 'complete' -Status passed -Details @{message="release complete; assets=$($assetInventory.Count) archive=$archive";assets=$assetInventory}
    $assetInventory|Format-Table name,bytes,sha256 -AutoSize
    Write-Host "Standalone WinCare release built and independently verified: $archive" -ForegroundColor Green
} catch {
    Write-WinCareReleaseTrace -Phase 'release' -Status failed -Details @{message=$_.Exception.Message;type=$_.Exception.GetType().FullName;scriptStackTrace=$_.ScriptStackTrace}
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host '::endgroup::' }
    if (Test-Path -LiteralPath $outputPath) {
        Get-ChildItem -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
    throw
} finally {
    Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
