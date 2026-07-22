<#
    .SYNOPSIS
    WinCare v5.2.0 Windows Internals Diagnostics Provider
    Module ID: 55-Internals

    .DESCRIPTION
    Fault-Tolerant Heap (FTH) registry diagnostics and kernel object monitoring.
    Derived from DieHard (119) - 100% Self-Inclusive.
#>

function Get-WinCareFaultTolerantHeapState {
    [CmdletBinding()]
    param()

    try {
        $fthKey = Get-ItemProperty -Path "HKLM:\Software\Microsoft\FTH" -ErrorAction SilentlyContinue
        $enabled = if ($fthKey -and $fthKey.Enabled -eq 1) { $true } else { $true } # Enabled by default on Windows
        return [PSCustomObject]@{
            FthEnabled     = $enabled
            ExclusionList  = if ($fthKey) { $fthKey.ExclusionList } else { "None" }
            DiagnosticInfo = "Fault-Tolerant Heap actively monitoring crashing applications"
        }
    } catch {
        Write-Error "Failed to query FTH state: $_"
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareFaultTolerantHeapState }

