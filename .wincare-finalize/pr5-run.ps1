#requires -Version 7.2
[CmdletBinding()]
param([string]$Root = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
Set-Location -LiteralPath $rootPath
$inputSha = [string]$env:GITHUB_SHA
$branchName = [string]$env:GITHUB_REF_NAME
if ($inputSha -notmatch '^[a-f0-9]{40}$') { throw 'GITHUB_SHA is missing or invalid.' }
if ([string]::IsNullOrWhiteSpace($branchName)) { throw 'GITHUB_REF_NAME is missing.' }

function Invoke-CheckedCommand {
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

function Get-RemoteBranchSha {
    [CmdletBinding()]
    param()
    $remoteLine = git ls-remote origin "refs/heads/$branchName"
    if ($LASTEXITCODE -ne 0 -or -not $remoteLine) {
        throw 'Unable to resolve the remote PR branch.'
    }
    $sha = ([string]$remoteLine -split '\s+')[0]
    if ($sha -notmatch '^[a-f0-9]{40}$') { throw 'Remote branch SHA is invalid.' }
    return $sha
}

function Publish-PreflightFailureReceipt {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Management.Automation.ErrorRecord]$Failure)

    $remoteSha = Get-RemoteBranchSha
    if ($remoteSha -ne $inputSha) {
        Write-Warning "Branch advanced; preflight receipt not published. Expected $inputSha, observed $remoteSha."
        return
    }

    Invoke-CheckedCommand -Executable 'git' -Arguments @('reset','--hard',$inputSha) `
        -FailureMessage 'Unable to restore the input tree after preflight failure'
    Invoke-CheckedCommand -Executable 'git' -Arguments @('clean','-fdx') `
        -FailureMessage 'Unable to clean preflight residue'

    $directory = Join-Path $rootPath '.wincare-finalize'
    [void][IO.Directory]::CreateDirectory($directory)
    $message = [string]$Failure.Exception.Message
    if ($message.Length -gt 4096) { $message = $message.Substring(0, 4096) }
    $receipt = [ordered]@{
        schema = 'wincare.pr5-remediation.failure/v1'
        status = 'failed'
        phase = 'preflight-or-orchestration'
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
    Invoke-CheckedCommand -Executable 'git' -Arguments @(
        'add','--','.wincare-finalize/last-remediation-failure.json'
    ) -FailureMessage 'Unable to stage the preflight failure receipt'
    Invoke-CheckedCommand -Executable 'git' -Arguments @('diff','--cached','--check') `
        -FailureMessage 'Preflight failure-receipt whitespace validation failed'
    Invoke-CheckedCommand -Executable 'git' -Arguments @(
        'commit','-m',"chore: record failed PR 5 remediation run $($env:GITHUB_RUN_ID)"
    ) -FailureMessage 'Preflight failure-receipt commit failed'

    $remoteSha = Get-RemoteBranchSha
    if ($remoteSha -ne $inputSha) {
        Write-Warning "Branch advanced before receipt publication; expected $inputSha, observed $remoteSha."
        return
    }
    Invoke-CheckedCommand -Executable 'git' -Arguments @('push','origin',"HEAD:$branchName") `
        -FailureMessage 'Publishing the preflight failure receipt failed'
}

try {
    Invoke-CheckedCommand -Executable 'python' -Arguments @('.wincare-finalize/pr5-rbac-fix.py') `
        -FailureMessage 'Fail-closed RBAC correction failed'
    Invoke-CheckedCommand -Executable 'python' -Arguments @('.wincare-finalize/pr5-gui-contract-fix.py') `
        -FailureMessage 'Property-safe GUI critical contract correction failed'
    Invoke-CheckedCommand -Executable 'python' -Arguments @('.wincare-finalize/pr5-upgrade-lifecycle-fix.py') `
        -FailureMessage 'Upgrade archive and verdict-stream isolation failed'
    Invoke-CheckedCommand -Executable 'python' -Arguments @('.wincare-finalize/pr5-build-artifact-hygiene.py') `
        -FailureMessage 'Deterministic build-artifact hygiene correction failed'
    Invoke-CheckedCommand -Executable 'python' -Arguments @('.wincare-finalize/repair-pr5-remediator.py') `
        -FailureMessage 'Remediation process-isolation repair failed'
    Invoke-CheckedCommand -Executable 'python' -Arguments @('.wincare-finalize/pr5-split-workflow-publication.py') `
        -FailureMessage 'Split workflow publication correction failed'
    & .\.wincare-finalize\pr5-remediate.ps1 -Root $rootPath
} catch {
    $failure = $_
    Write-Host ("PR #5 orchestration failed: {0}" -f $failure.Exception.Message) -ForegroundColor Red
    try {
        Publish-PreflightFailureReceipt -Failure $failure
    } catch {
        Write-Warning "Preflight failure receipt publication also failed: $_"
    }
    throw $failure
}
