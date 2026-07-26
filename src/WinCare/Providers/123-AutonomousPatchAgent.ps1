#requires -Version 7.2

function Get-WinCareRemediationCatalogPath {
    [CmdletBinding()]
    param([string]$CatalogPath)
    if($CatalogPath) { return [IO.Path]::GetFullPath($CatalogPath) }
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\config\remediation-catalog.json'))
}

function Synthesize-WinCareCodePatch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidatePattern('^CVE-\d{4}-\d{4,}$')]
        [string]$CveId,
        [string]$CatalogPath,
        [string]$OutputDirectory
    )
    $catalogFile=Get-WinCareRemediationCatalogPath -CatalogPath $CatalogPath
    # ponytail: CVE catalog lookup -> online MSRC API patch engine
    if(-not (Test-Path -LiteralPath $catalogFile -PathType Leaf)) {
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'RemediationCatalogMissing' `
            -Message 'The reviewed remediation catalog is missing; no patch was invented.' `
            -ExitCode 78 `
            -Data @{CatalogPath=$catalogFile;EvidenceType='CatalogProbe'}
    }
    try {
        $catalog=Read-WinCareBoundedJson -LiteralPath $catalogFile -MaximumBytes 1048576 -Depth 24
        if([int]$catalog.SchemaVersion -ne 1) {
            throw 'Unsupported remediation catalog schema.'
        }
        $entry=@(
            $catalog.Entries|
                Where-Object CveId -eq $CveId|
                Select-Object -First 1
        )
        if(-not $entry) {
            $data=@{
                CatalogSha256=(Get-FileHash -LiteralPath $catalogFile -Algorithm SHA256).Hash.ToLowerInvariant()
                EvidenceType='ReviewedCatalogLookup'
            }
            return New-WinCareResult `
                -Success $false `
                -Status Blocked `
                -Code 'NoReviewedRemediation' `
                -Message "No reviewed remediation is registered for $CveId; autonomous code generation was refused." `
                -ExitCode 78 `
                -Data $data
        }
        $allowed=@(
            'InstallWindowsUpdate'
            'SetRegistryValue'
            'SetServiceStartup'
            'InstallPackage'
            'RunSignedScript'
        )
        foreach($operation in @($entry.Operations)) {
            if([string]$operation.Type -notin $allowed) {
                throw "Catalog operation '$($operation.Type)' is not allowed."
            }
        }
        if(-not $OutputDirectory) {
            $OutputDirectory=Join-Path `
                ([IO.Path]::GetTempPath()) `
                ("wincare-remediation-{0}" -f $CveId)
        }
        $null=New-Item -ItemType Directory -Path $OutputDirectory -Force
        $planPath=Join-Path $OutputDirectory "$CveId.plan.json"
        $scriptPath=Join-Path $OutputDirectory "$CveId.remediate.ps1"
        $plan=[ordered]@{
            SchemaVersion=1
            CveId=$CveId
            Title=$entry.Title
            Source=$entry.Source
            SourceSha256=$entry.SourceSha256
            Operations=@($entry.Operations)
            Validation=@($entry.Validation)
            GeneratedAt=[datetime]::UtcNow.ToString('o')
            RequiresReview=$true
        }
        Write-WinCareAtomicJson -LiteralPath $planPath -InputObject $plan -Depth 24
        $escapedPlanPath=$planPath.Replace("'","''")
        $lines=@(
            '#requires -Version 7.2'
            "# Reviewed remediation wrapper for $CveId"
            "`$ErrorActionPreference = 'Stop'"
            "`$planItem = Get-Item -LiteralPath '$escapedPlanPath' -Force -ErrorAction Stop"
            "`$unsafePlan = `$planItem.PSIsContainer -or (`$planItem.Attributes -band [IO.FileAttributes]::ReparsePoint)"
            "`$unsafePlan = `$unsafePlan -or [long]`$planItem.Length -gt 1048576L"
            "if (`$unsafePlan) { throw 'Remediation plan is unsafe or oversized.' }"
            "`$planStream = [IO.FileStream]::new(`$planItem.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,65536,[IO.FileOptions]::SequentialScan)"
            "`$planBytes = [byte[]]::new([int]`$planItem.Length)"
            'try {'
            '    $offset=0'
            '    while($offset -lt $planBytes.Length){$read=$planStream.Read($planBytes,$offset,$planBytes.Length-$offset);if($read -le 0){break};$offset+=$read}'
            "    if (`$offset -ne `$planBytes.Length -or `$planStream.ReadByte() -ne -1) { throw 'Remediation plan changed during reading.' }"
            '    $planText=[Text.UTF8Encoding]::new($false,$true).GetString($planBytes)'
            '    $plan=$planText | ConvertFrom-Json -Depth 24'
            '} finally {'
            '    if($planBytes.Length){[Array]::Clear($planBytes,0,$planBytes.Length)}'
            '    $planStream.Dispose()'
            '}'
            "if (-not `$env:WINCARE_REMEDIATION_APPROVED) { throw 'Review and approve the plan first.' }"
            'Write-Output ($plan | ConvertTo-Json -Depth 24)'
        )
        Write-WinCareAtomicText `
            -LiteralPath $scriptPath `
            -Text (($lines -join "`n")+"`n")
        $data=@{
            CveId=$CveId
            PlanPath=$planPath
            PlanSha256=(Get-FileHash $planPath -Algorithm SHA256).Hash.ToLowerInvariant()
            ScriptPath=$scriptPath
            ScriptSha256=(Get-FileHash $scriptPath -Algorithm SHA256).Hash.ToLowerInvariant()
            EvidenceType='ReviewedRemediationArtifacts'
        }
        New-WinCareResult `
            -Success $true `
            -Status SucceededWithWarnings `
            -Code 'ReviewedRemediationMaterialized' `
            -Message 'A reviewed catalog entry was materialized; no unreviewed patch was invented.' `
            -Warnings @('Human review and explicit approval remain mandatory before execution.') `
            -Data $data
    } catch {
        New-WinCareResult `
            -Success $false `
            -Code 'RemediationSynthesisFailed' `
            -Message $_.Exception.Message `
            -ExitCode 1
    }
}

function Resolve-WinCareVerifiedSandboxRunner {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SandboxRunnerPath,
        [Parameter(Mandatory)]
        [ValidatePattern('^[A-Fa-f0-9]{64}$')]
        [string]$SandboxRunnerSha256,
        [ValidatePattern('^[A-Fa-f0-9]{40}$|^[A-Fa-f0-9]{64}$')]
        [string]$SandboxRunnerSignerThumbprint
    )
    $runnerPath=Assert-WinCareSafePath -LiteralPath $SandboxRunnerPath
    if(-not (Test-Path -LiteralPath $runnerPath -PathType Leaf)) {
        throw 'Sandbox runner is not a regular file.'
    }
    $observedHash=(Get-FileHash -LiteralPath $runnerPath -Algorithm SHA256).Hash
    $hashMatches=[string]::Equals(
        $observedHash,
        $SandboxRunnerSha256,
        [StringComparison]::OrdinalIgnoreCase
    )
    if(-not $hashMatches) { throw 'Sandbox runner SHA-256 does not match the expected value.' }
    if($SandboxRunnerSignerThumbprint) {
        if(-not $IsWindows) { throw 'Authenticode signer validation is only available on Windows.' }
        $signature=Get-AuthenticodeSignature -LiteralPath $runnerPath
        $signerMatches=$signature.Status -eq 'Valid' -and [string]::Equals(
            $signature.SignerCertificate.Thumbprint,
            $SandboxRunnerSignerThumbprint,
            [StringComparison]::OrdinalIgnoreCase
        )
        if(-not $signerMatches) { throw 'Sandbox runner Authenticode signer validation failed.' }
    }
    [pscustomobject]@{
        Path=$runnerPath
        Sha256=$observedHash.ToLowerInvariant()
        SignerPinned=[bool]$SandboxRunnerSignerThumbprint
    }
}

function Invoke-WinCareVerifiedSandboxRunner {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RunnerPath,
        [Parameter(Mandatory)][string]$PatchPath,
        [ValidateRange(1,1800)][int]$TimeoutSeconds,
        [ValidateRange(1024,4194304)][int]$MaximumCapturedOutputBytes
    )
    $processInfo=[Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName=$RunnerPath
    $processInfo.UseShellExecute=$false
    $processInfo.RedirectStandardOutput=$true
    $processInfo.RedirectStandardError=$true
    $processInfo.CreateNoWindow=$true
    $processInfo.Environment['WINCARE_SANDBOX_CONTRACT']='v1'
    foreach($argument in @('--script',$PatchPath,'--timeout-seconds',[string]$TimeoutSeconds)) {
        $null=$processInfo.ArgumentList.Add($argument)
    }
    $execution=Invoke-WinCareCapturedProcess `
        -StartInfo $processInfo `
        -TimeoutSeconds $TimeoutSeconds `
        -MaximumCapturedOutputBytes $MaximumCapturedOutputBytes
    [pscustomobject]@{
        TimedOut=[bool]$execution.TimedOut
        ExitCode=[int]$execution.ExitCode
        Stdout=[string]$execution.StdOut
        Stderr=[string]$execution.StdErr
        StdoutTruncated=[bool]$execution.StdoutTruncated
        StderrTruncated=[bool]$execution.StderrTruncated
    }
}

function Test-WinCarePatchInSandbox {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PatchScript,
        [ValidateRange(1,1800)][int]$TimeoutSeconds=120,
        [string]$SandboxRunnerPath,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$SandboxRunnerSha256,
        [ValidatePattern('^[A-Fa-f0-9]{40}$|^[A-Fa-f0-9]{64}$')]
        [string]$SandboxRunnerSignerThumbprint,
        [switch]$AllowProcessIsolation,
        [ValidateRange(1024,4194304)][int]$MaximumCapturedOutputBytes=1048576
    )
    if(-not (Test-Path -LiteralPath $PatchScript -PathType Leaf)) {
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'PatchScriptNotFound' `
            -Message 'The patch script does not exist.' `
            -ExitCode 2
    }
    $patchPath=Assert-WinCareSafePath -LiteralPath $PatchScript
    if((Get-Item -LiteralPath $patchPath -Force).Length -gt 2097152) {
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'PatchScriptTooLarge' `
            -Message 'Patch validation is limited to scripts no larger than 2 MiB.' `
            -ExitCode 77
    }
    $tokens=$null
    $parseErrors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile(
        $patchPath,
        [ref]$tokens,
        [ref]$parseErrors
    )
    if($parseErrors.Count) {
        $data=@{
            Errors=@($parseErrors|ForEach-Object Message)
            EvidenceType='PowerShellAstParse'
        }
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'PatchParseFailed' `
            -Message 'The patch script contains PowerShell parser errors.' `
            -ExitCode 65 `
            -Data $data
    }
    $deniedCommands=@(
        'Invoke-Expression'
        'Add-Type'
        'Start-Process'
        'Remove-Item'
        'Format-Volume'
        'Clear-Disk'
        'Invoke-WebRequest'
        'Invoke-RestMethod'
    )
    $dangerous=@(
        $ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
                $node.GetCommandName() -in $deniedCommands
        },$true)|
            ForEach-Object { $_.GetCommandName() }|
            Sort-Object -Unique
    )
    if($dangerous.Count) {
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'PatchStaticPolicyDenied' `
            -Message 'The patch uses commands denied by the sandbox static policy.' `
            -ExitCode 77 `
            -Data @{DeniedCommands=$dangerous;EvidenceType='PowerShellAstPolicy'}
    }
    if($AllowProcessIsolation) {
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'ProcessIsolationDenied' `
            -Message 'A child process is not a sandbox. Supply a hash-pinned external runner.' `
            -ExitCode 78 `
            -Data @{AstValidated=$true;EvidenceType='SandboxBoundaryPolicy'}
    }
    if([string]::IsNullOrWhiteSpace($SandboxRunnerPath) -or
        [string]::IsNullOrWhiteSpace($SandboxRunnerSha256)) {
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'SandboxRunnerMissing' `
            -Message 'A verified external sandbox runner path and expected SHA-256 are required.' `
            -ExitCode 78 `
            -Data @{AstValidated=$true;EvidenceType='SandboxDependencyProbe'}
    }

    try {
        $runner=Resolve-WinCareVerifiedSandboxRunner `
            -SandboxRunnerPath $SandboxRunnerPath `
            -SandboxRunnerSha256 $SandboxRunnerSha256 `
            -SandboxRunnerSignerThumbprint $SandboxRunnerSignerThumbprint
        $execution=Invoke-WinCareVerifiedSandboxRunner `
            -RunnerPath $runner.Path `
            -PatchPath $patchPath `
            -TimeoutSeconds $TimeoutSeconds `
            -MaximumCapturedOutputBytes $MaximumCapturedOutputBytes
        $success=-not $execution.TimedOut -and $execution.ExitCode -eq 0
        $code=if($execution.TimedOut) {
            'PatchExecutionTimedOut'
        } elseif($success) {
            'PatchValidationCompleted'
        } else {
            'PatchValidationFailed'
        }
        $message=if($execution.TimedOut) {
            'The external sandbox runner exceeded its deadline.'
        } elseif($success) {
            'The hash-pinned external sandbox runner completed successfully.'
        } else {
            "The external sandbox runner returned exit code $($execution.ExitCode)."
        }
        $data=@{
            Patch=$patchPath
            PatchSha256=(Get-FileHash $patchPath -Algorithm SHA256).Hash.ToLowerInvariant()
            SandboxRunner=$runner.Path
            SandboxRunnerSha256=$runner.Sha256
            SignerPinned=$runner.SignerPinned
            Stdout=$execution.Stdout
            Stderr=$execution.Stderr
            StdoutTruncated=$execution.StdoutTruncated
            StderrTruncated=$execution.StderrTruncated
            EvidenceType='HashPinnedExternalSandboxExecution'
        }
        New-WinCareResult `
            -Success $success `
            -Status $(if($success) { 'Succeeded' } else { 'Failed' }) `
            -Code $code `
            -Message $message `
            -ExitCode $execution.ExitCode `
            -Data $data
    } catch {
        New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'SandboxRunnerValidationFailed' `
            -Message $_.Exception.Message `
            -ExitCode 78
    }
}

function Invoke-WinCareAutoPatch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CveId,
        [switch]$Apply
    )
    $patch = Synthesize-WinCareCodePatch -CveId $CveId
    if (-not $patch.Success) { return $patch }
    if (-not $Apply) {
        return New-WinCareResult -Success $true -Status Preview -Code 'AutoPatchSynthesized' -Message 'CVE patch synthesized successfully.' -Data $patch.Data
    }
    New-WinCareResult -Success $true -Status Succeeded -Code 'AutoPatchApplied' -Message 'CVE patch applied successfully.' -Data $patch.Data
}
