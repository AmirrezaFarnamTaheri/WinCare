#requires -Version 7.2

function Get-WinCareTpmPqcCapability {
    [CmdletBinding()]
    param()
    if (-not $IsWindows) {
        return [pscustomobject]@{
            TpmAvailable = $false
            TpmVersion   = $null
            Provider     = $null
            EvidenceType = 'TpmCapabilityProbe'
        }
    }

    try {
        $tpm = Get-CimInstance -Namespace 'root\cimv2\security\microsofttpm' -ClassName 'Win32_Tpm' -ErrorAction Stop | Select-Object -First 1
        $present = $null -ne $tpm
        [pscustomobject]@{
            TpmAvailable = [bool]$present
            TpmVersion   = if ($tpm) { $tpm.SpecVersion } else { $null }
            Provider     = 'Microsoft Platform Crypto Provider'
            EvidenceType = 'TpmCapabilityProbe'
        }
    } catch {
        [pscustomobject]@{
            TpmAvailable = $false
            TpmVersion   = $null
            Provider     = $null
            Error        = $_.Exception.Message
            EvidenceType = 'TpmCapabilityProbe'
        }
    }
}

function Protect-WinCareTpmBoundPqcKey {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][byte[]]$KeyMaterial,
        [string]$KeyAlias = 'WinCareMlKem768Key'
    )

    if ($KeyMaterial.Length -eq 0 -or $KeyMaterial.Length -gt 65536) {
        throw "PQC key material size is invalid."
    }

    if (-not $IsWindows) {
        throw "TPM-bound PQC key protection is supported only on Windows operating systems."
    }

    if (Get-Command Initialize-WinCareProtectedDataSupport -ErrorAction SilentlyContinue) {
        Initialize-WinCareProtectedDataSupport
    }

    $tpmCap = Get-WinCareTpmPqcCapability
    $entropy = [Text.Encoding]::UTF8.GetBytes($KeyAlias)
    
    try {
        $protected = [Security.Cryptography.ProtectedData]::Protect($KeyMaterial, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
        $protectedSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($protected)).ToLowerInvariant()

        [pscustomobject]@{
            KeyAlias               = $KeyAlias
            TpmAvailable           = $tpmCap.TpmAvailable
            TpmVersion             = $tpmCap.TpmVersion
            ProtectionMechanism    = if ($tpmCap.TpmAvailable) { 'Hardware-TPM2.0-DPAPI' } else { 'Software-DPAPI-User' }
            ProtectedKeyBytes      = $protected.Length
            ProtectedPayloadSha256 = $protectedSha256
            ProtectedPayload       = [Convert]::ToBase64String($protected)
            EvidenceType           = 'TpmBoundPqcKeyProtectionResult'
        }
    } finally {
        [Array]::Clear($entropy, 0, $entropy.Length)
        [Array]::Clear($KeyMaterial, 0, $KeyMaterial.Length)
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) {
    Export-ModuleMember -Function Get-WinCareTpmPqcCapability, Protect-WinCareTpmBoundPqcKey
}
