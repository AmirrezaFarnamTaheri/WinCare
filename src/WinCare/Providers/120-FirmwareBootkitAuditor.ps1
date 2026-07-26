#requires -Version 7.2

function Audit-WinCareUefiDbxSignatures {
    [CmdletBinding()]
    param()

    if(-not $IsWindows) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'WindowsRequired' `
            -Message 'UEFI Secure Boot evidence collection requires Windows.' -ExitCode 78
    }
    $secureBoot=$null
    $setupMode=$null
    $dbx=$null
    $errors=[Collections.Generic.List[string]]::new()
    try { $secureBoot=Confirm-SecureBootUEFI -ErrorAction Stop }
    catch { $errors.Add("SecureBoot: $($_.Exception.Message)") }
    try {
        $dbxVariable=Get-SecureBootUEFI -Name dbx -ErrorAction Stop
        $bytes=[byte[]]$dbxVariable.Bytes
        $dbx=[pscustomobject]@{
            ByteLength=$bytes.Length
            Sha256=[Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($bytes)
            ).ToLowerInvariant()
            Attributes=$dbxVariable.Attributes
        }
    } catch { $errors.Add("dbx: $($_.Exception.Message)") }
    try {
        $setup=Get-SecureBootUEFI -Name SetupMode -ErrorAction Stop
        $setupMode=if($setup.Bytes.Count) { [int]$setup.Bytes[0] } else { $null }
    } catch { $errors.Add("SetupMode: $($_.Exception.Message)") }
    $assessment=if($secureBoot -eq $true -and $dbx) {
        'ObservedEnabled'
    } elseif($secureBoot -eq $false) {
        'ObservedDisabled'
    } else {
        'Unknown'
    }
    $success=$null -ne $secureBoot -or $null -ne $dbx
    New-WinCareResult -Success $success `
        -Status $(if($assessment -eq 'ObservedEnabled'){'Succeeded'}else{'SucceededWithWarnings'}) `
        -Code 'UefiBootEvidenceCollected' `
        -Message 'Secure Boot and dbx evidence were collected without inferring protection against a specific bootkit.' `
        -Warnings @($errors) -Data @{
            SecureBoot=$secureBoot
            SetupMode=$setupMode
            Dbx=$dbx
            Assessment=$assessment
            SpecificThreatMitigation='NotInferred'
            EvidenceType='UefiVariableEvidence'
        }
}

function Get-WinCareTpmPcrReport {
    [CmdletBinding()]
    param(
        [string]$OutputDirectory,
        [ValidateRange(1,536870912)][long]$MaximumMeasuredBootBytes=134217728
    )

    if(-not $IsWindows) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'WindowsRequired' `
            -Message 'TPM and measured-boot evidence collection requires Windows.' `
            -ExitCode 78
    }
    $tpm=$null
    $toolResult=$null
    $errors=[Collections.Generic.List[string]]::new()
    try { $tpm=Get-Tpm -ErrorAction Stop }
    catch { $errors.Add($_.Exception.Message) }
    if(Get-Command tpmtool.exe -ErrorAction SilentlyContinue) {
        $toolResult=Invoke-WinCareProcess -FilePath 'tpmtool.exe' `
            -ArgumentList @('getdeviceinformation') -TimeoutSeconds 60 `
            -MaximumCapturedOutputBytes 1048576
        if(-not $toolResult.Success) { $errors.Add("tpmtool: $($toolResult.Message)") }
    }
    $measuredBoot=$null
    $candidate=Join-Path $env:SystemRoot 'Logs\MeasuredBoot\0000000001-0000000000.log'
    if(Test-Path -LiteralPath $candidate -PathType Leaf) {
        try {
            $source=Assert-WinCareSafePath -LiteralPath $candidate
            $sourceItem=Get-Item -LiteralPath $source -Force
            if($sourceItem.Length -gt $MaximumMeasuredBootBytes) {
                throw "Measured-boot log exceeds the $MaximumMeasuredBootBytes byte bound."
            }
            $sourceHash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
            $copy=$null
            if($OutputDirectory) {
                $directory=Resolve-WinCareCanonicalPath -LiteralPath $OutputDirectory -AllowMissing
                $null=New-Item -ItemType Directory -Path $directory -Force
                $null=Assert-WinCareSafePath -LiteralPath $directory
                $copy=Join-Path $directory 'MeasuredBoot.log'
                $bytes=Read-WinCareBoundedFileBytes -LiteralPath $source -MaximumBytes $MaximumMeasuredBootBytes
                try { Write-WinCareAtomicBytes -LiteralPath $copy -Bytes $bytes }
                finally { [Array]::Clear($bytes,0,$bytes.Length) }
                $copyHash=(Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant()
                if($copyHash -ne $sourceHash) {
                    throw 'Measured-boot evidence copy hash mismatch.'
                }
            }
            $measuredBoot=[pscustomobject]@{
                Path=$source
                Bytes=[long]$sourceItem.Length
                Sha256=$sourceHash
                CopiedPath=$copy
            }
        } catch { $errors.Add("MeasuredBoot: $($_.Exception.Message)") }
    }
    $present=if($tpm){[bool]$tpm.TpmPresent}else{$false}
    $toolObserved=$toolResult -and $toolResult.Success
    $success=$present -or $toolObserved -or $null -ne $measuredBoot
    New-WinCareResult -Success $success `
        -Status $(if($present){'Succeeded'}else{'SucceededWithWarnings'}) `
        -Code 'TpmEvidenceCollected' `
        -Message 'TPM and measured-boot evidence were collected without inferring PCR integrity.' `
        -Warnings @($errors) -Data @{
            Tpm=$tpm
            TpmTool=if($toolResult){$toolResult.Data}else{$null}
            MeasuredBoot=$measuredBoot
            PcrBaselineValidation='NotPerformed'
            EvidenceType='BoundedTpmAndMeasuredBootEvidence'
        }
}
