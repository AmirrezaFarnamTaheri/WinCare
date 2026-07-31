#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Get-Location).Path,
    [string]$OutputDirectory = (Join-Path $env:RUNNER_TEMP 'artifacts\windows-validation')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
Set-Location -LiteralPath $rootPath
$inputSha = [string]$env:GITHUB_SHA
$branchName = [string]$env:GITHUB_REF_NAME
if ($inputSha -notmatch '^[a-f0-9]{40}$') { throw 'GITHUB_SHA is missing or invalid.' }
if ([string]::IsNullOrWhiteSpace($branchName)) { throw 'GITHUB_REF_NAME is missing.' }

function Invoke-NativeChecked {
    [CmdletBinding()]
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
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Purpose)
    $changes = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect the Git tree for $Purpose." }
    if ($changes.Count) { throw "The Git tree is dirty after ${Purpose}: $($changes -join '; ')" }
}

function Get-RemoteBranchSha {
    [CmdletBinding()]
    param()
    $remoteLine = git ls-remote origin "refs/heads/$branchName"
    if ($LASTEXITCODE -ne 0 -or -not $remoteLine) {
        throw 'Unable to resolve the remote PR branch.'
    }
    $sha = ([string]$remoteLine -split '\s+')[0]
    if ($sha -notmatch '^[a-f0-9]{40}$') { throw 'Remote branch SHA is invalid.' }
    $sha
}

function Publish-WinCareFailureReceipt {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Management.Automation.ErrorRecord]$Failure)

    git reset --hard $inputSha
    if ($LASTEXITCODE -ne 0) { throw 'Unable to restore the input tree after failure.' }
    git clean -fdx
    if ($LASTEXITCODE -ne 0) { throw 'Unable to clean unverified remediation files.' }

    $directory = Join-Path $rootPath '.wincare-finalize'
    [void][IO.Directory]::CreateDirectory($directory)
    $message = [string]$Failure.Exception.Message
    if ($message.Length -gt 4096) { $message = $message.Substring(0, 4096) }
    $receipt = [ordered]@{
        schema = 'wincare.pr5-remediation.failure/v1'
        status = 'failed'
        inputSha = $inputSha
        runId = [string]$env:GITHUB_RUN_ID
        runAttempt = [string]$env:GITHUB_RUN_ATTEMPT
        runUrl = "https://github.com/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)"
        workflow = [string]$env:GITHUB_WORKFLOW
        job = [string]$env:GITHUB_JOB
        message = $message
        recordedAtUtc = [datetime]::UtcNow.ToString('o')
    }
    $receiptPath = Join-Path $directory 'last-remediation-failure.json'
    [IO.File]::WriteAllText(
        $receiptPath,
        (($receipt | ConvertTo-Json -Depth 8) + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false)
    )

    git config user.name 'wincare-remediation-bot'
    git config user.email 'wincare-remediation-bot@users.noreply.github.com'
    git add -- '.wincare-finalize/last-remediation-failure.json'
    Invoke-NativeChecked -Executable 'git' -Arguments @('diff','--cached','--check') `
        -FailureMessage 'Failure-receipt whitespace validation failed'
    Invoke-NativeChecked -Executable 'git' -Arguments @(
        'commit','-m',"chore: record failed PR 5 remediation run $($env:GITHUB_RUN_ID)"
    ) -FailureMessage 'Failure-receipt commit failed'

    $remoteSha = Get-RemoteBranchSha
    if ($remoteSha -ne $inputSha) {
        Write-Warning "Branch advanced; failure receipt not published. Expected $inputSha, observed $remoteSha."
        return
    }
    Invoke-NativeChecked -Executable 'git' -Arguments @('push','origin',"HEAD:$branchName") `
        -FailureMessage 'Publishing the failure locator failed'
}

try {
    $observedHead = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $observedHead -ne $inputSha) {
        throw "Checkout mismatch: expected $inputSha, observed $observedHead."
    }

    Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
    if (-not (Get-Module -ListAvailable Pester | Where-Object Version -eq ([version]'5.9.0'))) {
        Install-Module Pester -RequiredVersion 5.9.0 -Scope CurrentUser -Force
    }
    if (-not (Get-Module -ListAvailable PSScriptAnalyzer | Where-Object Version -eq ([version]'1.24.0'))) {
        Install-Module PSScriptAnalyzer -RequiredVersion 1.24.0 -Scope CurrentUser -Force
    }
    Import-Module Pester -RequiredVersion 5.9.0 -Force -ErrorAction Stop
    Import-Module PSScriptAnalyzer -RequiredVersion 1.24.0 -Force -ErrorAction Stop

    Invoke-NativeChecked -Executable 'python' -Arguments @('.wincare-finalize/apply.py') `
        -FailureMessage 'Core release transformer failed'
    Invoke-NativeChecked -Executable 'python' -Arguments @('.wincare-finalize/prepare-finalization.py') `
        -FailureMessage 'Canonical brand preparation failed'
    Invoke-NativeChecked -Executable 'python' -Arguments @('.wincare-finalize/brand.py') `
        -FailureMessage 'Canonical brand integration failed'
    Invoke-NativeChecked -Executable 'python' -Arguments @('.wincare-finalize/release-boundary-fix.py') `
        -FailureMessage 'Release-boundary convergence failed'
    Invoke-NativeChecked -Executable 'python' -Arguments @('.wincare-finalize/pr5-contract-fixes.py') `
        -FailureMessage 'Runtime-contract remediation failed'
    Invoke-NativeChecked -Executable 'python' -Arguments @('tools/validate_source_references.py','.','--write') `
        -FailureMessage 'Source-reference refresh failed'

    $transientPaths = @(
        '.wincare-finalize',
        '.github/workflows/apply-pr5-final-remediation.yml',
        '.github/workflows/apply-windows-release-brand-finalization.yml',
        '.github/workflows/apply-windows-release-finalization.yml',
        '.github/workflows/apply-windows-release-finalization-v2.yml',
        '.github/workflows/apply-windows-release-finalization-v3.yml',
        '.github/workflows/windows-release-red.yml'
    )
    foreach ($relative in $transientPaths) {
        if (Test-Path -LiteralPath $relative) {
            Remove-Item -LiteralPath $relative -Recurse -Force
        }
    }

    git config user.name 'wincare-remediation-bot'
    git config user.email 'wincare-remediation-bot@users.noreply.github.com'
    git add -A
    Invoke-NativeChecked -Executable 'git' -Arguments @('diff','--cached','--check') `
        -FailureMessage 'Candidate whitespace validation failed'
    Invoke-NativeChecked -Executable 'git' -Arguments @(
        'commit','-m','fix: close PR 5 runtime and release regressions'
    ) -FailureMessage 'Candidate commit failed'
    $candidate = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $candidate -notmatch '^[a-f0-9]{40}$') {
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
        '-m','unittest','discover','-s','tools','-p','test_*.py','-v'
    ) -FailureMessage 'Python regression suite failed'
    Invoke-NativeChecked -Executable 'git' -Arguments @('diff','--check') `
        -FailureMessage 'Candidate diff validation failed'
    Assert-CleanTree -Purpose 'deterministic source and regression validation'

    $fixtureOutput = Join-Path $env:RUNNER_TEMP 'wincare-previous-release'
    $fixtureResultPath = Join-Path $fixtureOutput 'previous-release-fixture.json'
    & (Join-Path $rootPath 'tools\Build-PreviousReleaseFixture.ps1') `
        -Root $rootPath `
        -OutputDirectory $fixtureOutput `
        -ResultPath $fixtureResultPath
    . (Join-Path $rootPath 'tools\WinCare.Tooling.ps1')
    $fixtureResult = Read-WinCareToolingJson -LiteralPath $fixtureResultPath `
        -MaximumBytes 1048576 -MaximumDepth 32
    if ([string]$fixtureResult.status -ne 'passed') {
        throw 'Previous-release fixture did not report success.'
    }
    if ([string]$fixtureResult.archiveSha256 -notmatch '^[a-f0-9]{64}$') {
        throw 'Previous-release fixture did not report a valid archive digest.'
    }
    $previousArchive = [string]$fixtureResult.archive
    $previousVersion = [string]$fixtureResult.previousVersion
    $null = Get-WinCareToolingRegularFile -LiteralPath $previousArchive `
        -MaximumBytes 2147483648L -Purpose 'Previous-version release fixture'
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
    $reportPath = Join-Path $outputPath 'windows-validation-report.json'
    $report = Read-WinCareToolingJson -LiteralPath $reportPath `
        -MaximumBytes 16777216 -MaximumDepth 64
    if ([string]$report.status -ne 'passed') {
        throw 'Complete Windows validation report did not pass.'
    }
    Copy-Item -LiteralPath $fixtureResultPath `
        -Destination (Join-Path $outputPath 'previous-release-fixture.json') -Force
    Assert-CleanTree -Purpose 'complete Windows validation'

    if ((git rev-parse HEAD).Trim() -ne $candidate) {
        throw 'Local HEAD changed after candidate validation.'
    }
    $remoteSha = Get-RemoteBranchSha
    if ($remoteSha -ne $inputSha) {
        throw "PR branch advanced during validation: expected $inputSha, observed $remoteSha."
    }
    Invoke-NativeChecked -Executable 'git' -Arguments @('push','origin',"HEAD:$branchName") `
        -FailureMessage 'Publishing the verified candidate failed'

    @"
## Atomic PR #5 remediation

- Input head: `$inputSha`
- Verified candidate: `$candidate`
- Previous-version fixture: `$previousVersion`
- Result: all deterministic, Windows, Pester, .NET, release, and lifecycle gates passed
- Transient applicators removed from the published tree
"@ >> $env:GITHUB_STEP_SUMMARY
    Write-Host "Published fully verified candidate $candidate" -ForegroundColor Green
} catch {
    $failure = $_
    Write-Error $failure
    try {
        Publish-WinCareFailureReceipt -Failure $failure
    } catch {
        Write-Error "Failure receipt publication also failed: $_"
    }
    throw $failure
}
