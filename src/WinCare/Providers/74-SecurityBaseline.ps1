<#
    .SYNOPSIS
    WinCare v5.2.0 Defensive Security Baseline Provider
    Module ID: 74-SecurityBaseline

    .DESCRIPTION
    Audits Windows Driver Signature Enforcement (DSE), Hypervisor-Protected Code Integrity (HVCI),
    Vulnerable Driver Blocklist enforcement, and Active Directory Kerberos/SMB signing state.
    Unifies capabilities from KrbRelayUp (007) and TDL (153) - 100% Self-Inclusive.
#>

$nativeDllPath = Join-Path $PSScriptRoot "..\..\..\bin\WinCare.Native.dll"
if (Test-Path $nativeDllPath) {
    try { Add-Type -Path $nativeDllPath -ErrorAction SilentlyContinue } catch {}
}

function Get-WinCareSecurityBaselineState {
    [CmdletBinding()]
    param()

    Write-Verbose "Querying Driver Signature Enforcement, HVCI, and Kerberos Hardening via WinCare.Native.dll..."
    try {
        $ciState = [WinCare.Native.SecurityAuditor]::GetCodeIntegrityState()
        $krbState = [WinCare.Native.SecurityAuditor]::GetKerberosHardeningState()

        return [PSCustomObject]@{
            HvciEnabled                      = $ciState.HvciEnabled
            DriverSignatureEnforcementActive = $ciState.DriverSignatureEnforcementActive
            VulnerableDriverBlocklistEnabled = $ciState.VulnerableDriverBlocklistEnabled
            SmbSigningRequired               = $krbState.SmbSigningRequired
            LdapSigningRequired              = $krbState.LdapSigningRequired
            OverallHardeningScore            = if ($ciState.HvciEnabled -and $ciState.VulnerableDriverBlocklistEnabled) { "High (Hardened)" } else { "Moderate (Review Required)" }
        }
    } catch {
        Write-Error "Failed to query native SecurityAuditor: $_"
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareSecurityBaselineState
 }

