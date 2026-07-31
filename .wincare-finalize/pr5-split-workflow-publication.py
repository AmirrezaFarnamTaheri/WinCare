#!/usr/bin/env python3
"""Publish the validated non-workflow tree separately from workflow YAML."""
from __future__ import annotations

import os
import tempfile
from pathlib import Path

ROOT = Path.cwd().resolve()
TARGET = ".wincare-finalize/pr5-remediate.ps1"
MAX_BYTES = 4 * 1024 * 1024


def read_text(relative: str) -> str:
    path = (ROOT / relative).resolve()
    if ROOT not in path.parents or not path.is_file() or path.is_symlink():
        raise RuntimeError(f"unsafe or missing file: {relative}")
    before = path.stat(follow_symlinks=False)
    if before.st_size <= 0 or before.st_size > MAX_BYTES:
        raise RuntimeError(f"empty or oversized file: {relative}")
    payload = path.read_bytes()
    after = path.stat(follow_symlinks=False)
    if len(payload) != before.st_size or (
        before.st_size,
        before.st_mtime_ns,
    ) != (
        after.st_size,
        after.st_mtime_ns,
    ):
        raise RuntimeError(f"file changed while reading: {relative}")
    return payload.decode("utf-8-sig")


def write_text(relative: str, text: str) -> None:
    path = (ROOT / relative).resolve()
    if ROOT not in path.parents or path.is_symlink():
        raise RuntimeError(f"unsafe destination: {relative}")
    descriptor, temporary = tempfile.mkstemp(
        prefix=path.name + ".",
        suffix=".tmp",
        dir=path.parent,
    )
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(text.encode("utf-8"))
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


old = """    if ((git rev-parse HEAD).Trim() -ne $candidate) {
        throw 'Local HEAD changed after candidate validation.'
    }
    $remoteSha = Get-RemoteBranchSha
    if ($remoteSha -ne $inputSha) {
        throw \"PR branch advanced during validation: expected $inputSha, observed $remoteSha.\"
    }
    Invoke-NativeChecked -Executable 'git' -Arguments @('push','origin',\"HEAD:$branchName\") `
        -FailureMessage 'Publishing the verified candidate failed'

    @\"
## Atomic PR #5 remediation

- Input head: `$inputSha`
- Verified candidate: `$candidate`
- Previous-version fixture: `$previousVersion`
- Result: all deterministic, Windows, Pester, .NET, release, and lifecycle gates passed
- Transient applicators removed from the published tree
\"@ >> $env:GITHUB_STEP_SUMMARY
    Write-Host \"Published fully verified candidate $candidate\" -ForegroundColor Green
"""

new = """    if ((git rev-parse HEAD).Trim() -ne $candidate) {
        throw 'Local HEAD changed after candidate validation.'
    }

    # The Actions token intentionally lacks workflow-editing permission. Preserve
    # the exact validated workflow delta as evidence, publish the validated
    # non-workflow tree, and let the workflow-capable GitHub connector apply the
    # recorded workflow delta as a separate audited commit.
    $workflowBundlePath = Join-Path $outputPath 'workflow-publication'
    if (Test-Path -LiteralPath $workflowBundlePath) {
        Remove-Item -LiteralPath $workflowBundlePath -Recurse -Force -ErrorAction Stop
    }
    [void][IO.Directory]::CreateDirectory((Join-Path $workflowBundlePath 'files'))

    $workflowChangeLines = @(
        git diff --name-status $inputSha $candidate -- .github/workflows
    )
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate the validated workflow delta.'
    }
    $workflowEntries = [Collections.Generic.List[object]]::new()
    foreach ($line in $workflowChangeLines) {
        if ([string]::IsNullOrWhiteSpace([string]$line)) { continue }
        $fields = @([string]$line -split \"`t\")
        if ($fields.Count -lt 2) {
            throw \"Malformed workflow delta entry: $line\"
        }
        $status = [string]$fields[0]
        $path = ([string]$fields[-1]).Replace('\\','/')
        if (-not $path.StartsWith('.github/workflows/', [StringComparison]::Ordinal)) {
            throw \"Workflow delta escaped the workflow directory: $path\"
        }
        $entry = [ordered]@{
            status = $status
            path = $path
        }
        if (-not $status.StartsWith('D', [StringComparison]::Ordinal)) {
            $sourcePath = Join-Path $rootPath ($path.Replace('/','\\'))
            $null = Get-WinCareToolingRegularFile -LiteralPath $sourcePath `
                -MaximumBytes 16777216 -Purpose 'Validated workflow publication file'
            $relativePath = $path.Substring('.github/workflows/'.Length)
            $destinationPath = Join-Path (Join-Path $workflowBundlePath 'files') $relativePath
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $destinationPath))
            Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
            $fileInfo = Get-Item -LiteralPath $sourcePath -Force -ErrorAction Stop
            $entry.bytes = [long]$fileInfo.Length
            $entry.sha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        $workflowEntries.Add([pscustomobject]$entry)
    }

    $validatedTree = (git rev-parse \"$candidate`^{tree}\").Trim()
    if ($LASTEXITCODE -ne 0 -or $validatedTree -notmatch '^[a-f0-9]{40}$') {
        throw 'Unable to resolve the validated candidate tree.'
    }
    $workflowManifest = [ordered]@{
        schema = 'wincare.workflow-publication/v1'
        inputSha = $inputSha
        validatedCandidate = $candidate
        validatedTree = $validatedTree
        runId = [string]$env:GITHUB_RUN_ID
        changes = @($workflowEntries)
    }
    [IO.File]::WriteAllText(
        (Join-Path $workflowBundlePath 'manifest.json'),
        (($workflowManifest | ConvertTo-Json -Depth 16) + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false)
    )

    Invoke-NativeChecked -Executable 'git' -Arguments @('reset','--soft',$inputSha) `
        -FailureMessage 'Unable to prepare the non-workflow publication commit'
    Invoke-NativeChecked -Executable 'git' -Arguments @(
        'restore',\"--source=$inputSha\",'--staged','--worktree','--','.github/workflows'
    ) -FailureMessage 'Unable to restore workflow files for split publication'

    $stagedWorkflowPaths = @(git diff --cached --name-only -- .github/workflows)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect staged workflow paths.' }
    if ($stagedWorkflowPaths.Count -ne 0) {
        throw \"Workflow files remained staged for the restricted push: $($stagedWorkflowPaths -join ', ')\"
    }
    $stagedPaths = @(git diff --cached --name-only)
    if ($LASTEXITCODE -ne 0 -or $stagedPaths.Count -eq 0) {
        throw 'The validated non-workflow publication commit is empty.'
    }
    Invoke-NativeChecked -Executable 'git' -Arguments @('diff','--cached','--check') `
        -FailureMessage 'Non-workflow publication whitespace validation failed'
    Invoke-NativeChecked -Executable 'git' -Arguments @(
        'commit','-m','fix: close PR 5 runtime and release regressions',
        '-m',\"Validated-Run: $($env:GITHUB_RUN_ID)\",
        '-m',\"Validated-Tree: $validatedTree\"
    ) -FailureMessage 'Creating the validated non-workflow publication commit failed'
    $publishableCandidate = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $publishableCandidate -notmatch '^[a-f0-9]{40}$') {
        throw 'Unable to resolve the publishable candidate commit.'
    }
    $publishedWorkflowDiff = @(git diff --name-only $inputSha $publishableCandidate -- .github/workflows)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to verify the split publication workflow boundary.' }
    if ($publishedWorkflowDiff.Count -ne 0) {
        throw \"Restricted push still contains workflow changes: $($publishedWorkflowDiff -join ', ')\"
    }
    Assert-CleanTree -Purpose 'split candidate publication preparation'

    $remoteSha = Get-RemoteBranchSha
    if ($remoteSha -ne $inputSha) {
        throw \"PR branch advanced during validation: expected $inputSha, observed $remoteSha.\"
    }
    Invoke-NativeChecked -Executable 'git' -Arguments @('push','origin',\"HEAD:$branchName\") `
        -FailureMessage 'Publishing the verified non-workflow candidate failed'

    @\"
## Atomic PR #5 remediation

- Input head: `$inputSha`
- Locally validated candidate: `$candidate`
- Validated tree: `$validatedTree`
- Published non-workflow candidate: `$publishableCandidate`
- Workflow delta: recorded in workflow-publication/manifest.json for connector publication
- Previous-version fixture: `$previousVersion`
- Result: all deterministic, Windows, Pester, .NET, release, and lifecycle gates passed
\"@ >> $env:GITHUB_STEP_SUMMARY
    Write-Host \"Published verified non-workflow candidate $publishableCandidate\" -ForegroundColor Green
"""

text = read_text(TARGET)
count = text.count(old)
if count != 1:
    raise RuntimeError(f"expected one publication anchor, found {count}")
updated = text.replace(old, new, 1)
write_text(TARGET, updated)

post = read_text(TARGET)
for marker in (
    "wincare.workflow-publication/v1",
    "git diff --name-status $inputSha $candidate -- .github/workflows",
    "Validated-Tree:",
    "Published verified non-workflow candidate",
    "Workflow files remained staged for the restricted push",
):
    if marker not in post:
        raise RuntimeError(f"split-publication postcondition missing: {marker}")

print("Applied split workflow/non-workflow publication boundary.")
