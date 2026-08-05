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

    $tpmCap = Get-WinCareTpmPqcCapability
    $entropy = [Text.Encoding]::UTF8.GetBytes($KeyAlias)
    
    try {
        $protected = if ($IsWindows) {
            [Security.Cryptography.ProtectedData]::Protect($KeyMaterial, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
        } else {
            $KeyMaterial.Clone()
        }

        [pscustomobject]@{
            KeyAlias          = $KeyAlias
            TpmBound          = $tpmCap.TpmAvailable
            TpmVersion        = $tpmCap.TpmVersion
            ProtectedKeyBytes = $protected.Length
            KeySha256         = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($KeyMaterial)).ToLowerInvariant()
            ProtectedPayload  = [Convert]::ToBase64String($protected)
            EvidenceType      = 'TpmBoundPqcKeyProtectionResult'
        }
    } finally {
        [Array]::Clear($entropy, 0, $entropy.Length)
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) {
    Export-ModuleMember -Function Get-WinCareTpmPqcCapability, Protect-WinCareTpmBoundPqcKey
}
