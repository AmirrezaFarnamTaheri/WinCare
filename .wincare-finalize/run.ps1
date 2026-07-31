#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Get-Location).Path,
    [string]$OutputDirectory = (Join-Path $env:RUNNER_TEMP 'artifacts\windows-validation')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root).Path
Set-Location -LiteralPath $rootPath

function Invoke-NativeChecked {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$FailureMessage
    )
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit $LASTEXITCODE)."
    }
}

function Assert-CleanTree {
    param([Parameter(Mandatory)][string]$Purpose)
    $changes = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect the Git tree for $Purpose." }
    if ($changes.Count) {
        throw "The Git tree is dirty after $Purpose: $($changes -join '; ')"
    }
}

$redPath = Join-Path $env:RUNNER_TEMP 'windows-release-contracts-red.txt'
$originalNativePreference = $PSNativeCommandUseErrorActionPreference
$redExit = 0
$redOutput = ''
try {
    $PSNativeCommandUseErrorActionPreference = $false
    $redOutput = & python -m unittest tools.test_windows_release_pipeline tools.test_brand_identity -v 2>&1 |
        Out-String
    $redExit = $LASTEXITCODE
} finally {
    $PSNativeCommandUseErrorActionPreference = $originalNativePreference
}
[IO.File]::WriteAllText($redPath, $redOutput, [Text.UTF8Encoding]::new($false))
Write-Host $redOutput
if ($redExit -eq 0) {
    throw 'The RED release and brand contracts unexpectedly passed before implementation.'
}
if ($redOutput -notmatch 'FAILED \(failures=') {
    throw 'The RED contract run failed without producing the expected assertion evidence.'
}

Invoke-NativeChecked -Executable 'python' -Arguments @('.wincare-finalize/apply.py') `
    -FailureMessage 'The Windows finalization transformer failed'
Invoke-NativeChecked -Executable 'python' -Arguments @('.wincare-finalize/brand.py') `
    -FailureMessage 'The WinCare brand transformer failed'
Invoke-NativeChecked -Executable 'python' -Arguments @('tools/generate_wincare_brand_assets.py','--root','.','--check') `
    -FailureMessage 'The generated WinCare brand assets are not deterministic'
Invoke-NativeChecked -Executable 'python' -Arguments @('tools/validate_source_references.py','.','--write') `
    -FailureMessage 'Source-reference evidence refresh failed'

$transientPaths = @(
    '.wincare-finalize',
    '.github/workflows/apply-windows-release-finalization.yml',
    '.github/workflows/apply-windows-release-finalization-v2.yml',
    '.github/workflows/windows-release-red.yml'
)
foreach ($relative in $transientPaths) {
    if (Test-Path -LiteralPath $relative) {
        Remove-Item -LiteralPath $relative -Recurse -Force
    }
}

git config user.name 'wincare-release-bot'
git config user.email 'wincare-release-bot@users.noreply.github.com'
git add -A
Invoke-NativeChecked -Executable 'git' -Arguments @('diff','--cached','--check') `
    -FailureMessage 'Candidate whitespace validation failed'
Invoke-NativeChecked -Executable 'git' -Arguments @(
    'commit','-m','fix: complete Windows release finalization and brand identity'
) -FailureMessage 'Candidate commit failed'
$candidate = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $candidate -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to resolve the candidate commit.'
}
Assert-CleanTree -Purpose 'candidate commit'

$validators = @(
    'validate_source.py',
    'validate_module_manifest.py',
    'validate_action_bindings.py',
    'validate_powershell_calls.py',
    'validate_test_source_assertions.py',
    'validate_source_references.py',
    'audit_stub_inventory.py',
    'validate_maintainability.py',
    'validate_network_egress.py',
    'validate_external_processes.py',
    'validate_bounded_io.py',
    'validate_read_only_state.py',
    'validate_context_menu_catalog.py',
    'validate_test_fixtures.py',
    'validate_gui.py'
)
foreach ($validator in $validators) {
    Invoke-NativeChecked -Executable 'python' -Arguments @((Join-Path 'tools' $validator),'.') `
        -FailureMessage "Validator failed: $validator"
}
Invoke-NativeChecked -Executable 'python' -Arguments @(
    'tools/generate_wincare_brand_assets.py','--root','.','--check'
) -FailureMessage 'Brand reproducibility check failed'
Invoke-NativeChecked -Executable 'python' -Arguments @(
    '-m','unittest','discover','-s','tools','-p','test_*.py','-v'
) -FailureMessage 'Python regression suite failed'
Invoke-NativeChecked -Executable 'git' -Arguments @('diff','--check') `
    -FailureMessage 'Candidate diff validation failed'
Assert-CleanTree -Purpose 'GREEN structural and regression validation'

. (Join-Path $rootPath 'tools\WinCare.Tooling.ps1')
$fixtureOutput = Join-Path $env:RUNNER_TEMP 'wincare-previous-release'
$fixtureResultPath = Join-Path $fixtureOutput 'previous-release-fixture.json'
& (Join-Path $rootPath 'tools\Build-PreviousReleaseFixture.ps1') `
    -Root $rootPath `
    -OutputDirectory $fixtureOutput `
    -ResultPath $fixtureResultPath
$fixtureResult = Read-WinCareToolingJson -LiteralPath $fixtureResultPath -MaximumBytes 1048576 -MaximumDepth 32
if ([string]$fixtureResult.status -ne 'passed') {
    throw 'The previous-version release fixture did not pass.'
}
$previousArchive = [string]$fixtureResult.archive
$previousVersion = [string]$fixtureResult.previousVersion
$null = Get-WinCareToolingRegularFile -LiteralPath $previousArchive -MaximumBytes 2147483648L `
    -Purpose 'Previous-version release fixture'
Assert-CleanTree -Purpose 'previous-version fixture construction'

$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
& (Join-Path $rootPath 'tools\Invoke-WindowsValidation.ps1') `
    -Root $rootPath `
    -OutputDirectory $outputPath `
    -PreviousReleaseArchivePath $previousArchive `
    -GateTimeoutSeconds 7200
Assert-CleanTree -Purpose 'complete Windows validation'

Copy-Item -LiteralPath $redPath -Destination (Join-Path $outputPath 'windows-release-contracts-red.txt')
Copy-Item -LiteralPath $fixtureResultPath -Destination (Join-Path $outputPath 'previous-release-fixture.json')
$reportPath = Join-Path $outputPath 'windows-validation-report.json'
$report = Read-WinCareToolingJson -LiteralPath $reportPath -MaximumBytes 16777216 -MaximumDepth 64
if ([string]$report.status -ne 'passed') {
    throw 'The Windows validation report did not pass.'
}

$manifest = Import-PowerShellDataFile (Join-Path $rootPath 'src\WinCare\WinCare.psd1')
$version = [string]$manifest.ModuleVersion
$releaseDirectory = Join-Path $outputPath 'release'
$requiredReleaseAssets = @(
    "WinCare-$version.zip",
    "WinCare-$version.zip.sha256",
    "WinCare-$version-SBOM.spdx.json",
    "WinCare-$version-BUILD-RECEIPT.json",
    "WinCare-$version-release-receipt.json",
    "WinCare-$version.zip.intoto.jsonl",
    "WinCare-$version-build-result.json",
    "WinCare-$version-BRAND.json",
    'WinCare.Standalone.build.json',
    'WinCare.exe',
    'WinCare-GUI.exe',
    'WinCare-TUI.exe'
)
foreach ($name in $requiredReleaseAssets) {
    $path = Join-Path $releaseDirectory $name
    $null = Get-WinCareToolingRegularFile -LiteralPath $path -MaximumBytes 2147483648L `
        -Purpose "Release evidence $name"
}

$archivePath = Join-Path $releaseDirectory "WinCare-$version.zip"
$archiveEvidence = Get-WinCareToolingFileSha256 -LiteralPath $archivePath -MaximumBytes 2147483648L `
    -Purpose 'WinCare release archive'
$checksumText = Read-WinCareToolingBoundedUtf8Text `
    -LiteralPath (Join-Path $releaseDirectory "WinCare-$version.zip.sha256") `
    -MaximumBytes 4096 `
    -Purpose 'Release archive checksum companion'
if (-not $checksumText.Trim().StartsWith($archiveEvidence.Sha256, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The release archive checksum companion does not match the archive.'
}

$assets = @(
    Get-ChildItem -LiteralPath $releaseDirectory -File -Force |
        Sort-Object Name |
        ForEach-Object {
            $evidence = Get-WinCareToolingFileSha256 -LiteralPath $_.FullName -MaximumBytes 2147483648L `
                -Purpose "Release asset $($_.Name)"
            [ordered]@{
                name = $_.Name
                bytes = [long]$evidence.Bytes
                sha256 = [string]$evidence.Sha256
            }
        }
)
$signatures = @(
    foreach ($name in @('WinCare.exe','WinCare-GUI.exe','WinCare-TUI.exe')) {
        $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $releaseDirectory $name)
        [ordered]@{
            name = $name
            status = [string]$signature.Status
            statusMessage = [string]$signature.StatusMessage
            signerSubject = if ($signature.SignerCertificate) {
                [string]$signature.SignerCertificate.Subject
            } else { $null }
            timestampSubject = if ($signature.TimeStamperCertificate) {
                [string]$signature.TimeStamperCertificate.Subject
            } else { $null }
        }
    }
)

$packageReferences = @(
    foreach ($project in @(Get-ChildItem -LiteralPath (Join-Path $rootPath 'src\WinCare') `
        -Filter '*.csproj' -Recurse -File | Sort-Object FullName)) {
        $projectText = Read-WinCareToolingBoundedUtf8Text -LiteralPath $project.FullName `
            -MaximumBytes 1048576 -Purpose "Project file $($project.Name)"
        [xml]$projectXml = $projectText
        foreach ($reference in @($projectXml.Project.ItemGroup.PackageReference)) {
            if ($null -eq $reference) { continue }
            [ordered]@{
                project = [IO.Path]::GetRelativePath($rootPath, $project.FullName).Replace('\','/')
                name = [string]$reference.Include
                version = [string]$reference.Version
            }
        }
    }
)
$brandManifestPath = Join-Path $rootPath 'design\WinCare-Brand.manifest.json'
$brandManifest = Read-WinCareToolingJson -LiteralPath $brandManifestPath -MaximumBytes 1048576 -MaximumDepth 32
$brandManifestEvidence = Get-WinCareToolingFileSha256 -LiteralPath $brandManifestPath `
    -MaximumBytes 1048576 -Purpose 'WinCare brand identity manifest'
if ([string]$brandManifest.schema -ne 'wincare.brand.identity/v1') {
    throw 'The WinCare brand manifest schema is invalid.'
}

$pesterVersion = Get-Module -ListAvailable Pester |
    Where-Object Version -ge ([version]'5.5.0') |
    Sort-Object Version -Descending |
    Select-Object -First 1 -ExpandProperty Version
$analyzerVersion = Get-Module -ListAvailable PSScriptAnalyzer |
    Where-Object Version -ge ([version]'1.24.0') |
    Sort-Object Version -Descending |
    Select-Object -First 1 -ExpandProperty Version
$inventory = [ordered]@{
    schema = 'wincare.supply-chain.inventory/v2'
    status = 'passed'
    version = $version
    sourceCommit = $candidate
    previousFixtureVersion = $previousVersion
    brand = [ordered]@{
        schema = [string]$brandManifest.schema
        manifestSha256 = [string]$brandManifestEvidence.Sha256
        iconSizes = @($brandManifest.iconSizes)
    }
    toolchain = [ordered]@{
        operatingSystem = [Environment]::OSVersion.VersionString
        powershell = $PSVersionTable.PSVersion.ToString()
        dotnet = (& dotnet --version).Trim()
        python = ((& python --version) 2>&1 | Out-String).Trim()
        pester = [string]$pesterVersion
        psscriptAnalyzer = [string]$analyzerVersion
    }
    workflowDependencies = @(
        [ordered]@{ name='actions/checkout'; commit='3d3c42e5aac5ba805825da76410c181273ba90b1' },
        [ordered]@{ name='actions/setup-dotnet'; commit='26b0ec14cb23fa6904739307f278c14f94c95bf1' },
        [ordered]@{ name='actions/upload-artifact'; commit='043fb46d1a93c77aae656e7c1c64a875d1fc6a0a' }
    )
    powershellRequiredModules = @($manifest.RequiredModules | ForEach-Object { [string]$_ })
    dotnetPackageReferences = $packageReferences
    authenticode = $signatures
    assets = $assets
}
$inventoryPath = Join-Path $outputPath 'supply-chain-inventory.json'
[IO.File]::WriteAllText(
    $inventoryPath,
    (($inventory | ConvertTo-Json -Depth 24) + [Environment]::NewLine),
    [Text.UTF8Encoding]::new($false)
)

$metadata = [ordered]@{
    schema = 'wincare.windows.release-candidate/v1'
    status = 'passed'
    candidateCommit = $candidate
    version = $version
    previousVersion = $previousVersion
    outputDirectory = $outputPath
    report = $reportPath
    inventory = $inventoryPath
    archiveSha256 = [string]$archiveEvidence.Sha256
}
$metadataPath = Join-Path $outputPath 'release-candidate.json'
[IO.File]::WriteAllText(
    $metadataPath,
    (($metadata | ConvertTo-Json -Depth 12) + [Environment]::NewLine),
    [Text.UTF8Encoding]::new($false)
)

"WINCARE_OUTPUT_DIR=$outputPath" >> $env:GITHUB_ENV
"WINCARE_CANDIDATE_SHA=$candidate" >> $env:GITHUB_ENV
"WINCARE_VERSION=$version" >> $env:GITHUB_ENV
"WINCARE_PREVIOUS_VERSION=$previousVersion" >> $env:GITHUB_ENV
"WINCARE_REPORT_PATH=$reportPath" >> $env:GITHUB_ENV
"WINCARE_INVENTORY_PATH=$inventoryPath" >> $env:GITHUB_ENV
Write-Host "Five-phase WinCare candidate passed locally on Windows: $candidate" -ForegroundColor Green
