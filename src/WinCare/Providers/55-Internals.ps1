<#
.SYNOPSIS
Observed Windows Fault-Tolerant Heap diagnostics.
#>
function Get-WinCareFaultTolerantHeapState {
    [CmdletBinding()]
    param()

    if (-not $IsWindows) {
        return [pscustomobject]@{
            Supported = $false; Configured = $false; Enabled = $null
            EffectiveState = 'Unsupported'; Exclusions = @(); MitigatedApplications = @()
            EvidenceType = 'PlatformCapability'; Error = 'Fault-Tolerant Heap is a Windows-only facility.'
        }
    }

    $path = 'HKLM:\Software\Microsoft\FTH'
    try {
        $key = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
        $properties = if ($key) { Get-ItemProperty -LiteralPath $path -ErrorAction Stop } else { $null }
        $enabledValue = if ($properties -and $properties.PSObject.Properties['Enabled']) { [int]$properties.Enabled } else { $null }
        $exclusions = @()
        if ($properties -and $properties.PSObject.Properties['ExclusionList']) {
            $exclusions = @($properties.ExclusionList | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        }
        $mitigated = @()
        $statePath = Join-Path $path 'State'
        if (Test-Path -LiteralPath $statePath) {
            $mitigated = @(Get-ChildItem -LiteralPath $statePath -ErrorAction SilentlyContinue | Select-Object -ExpandProperty PSChildName)
        }
        [pscustomobject]@{
            Supported = $true
            Configured = $null -ne $enabledValue
            Enabled = if ($null -eq $enabledValue) { $null } else { $enabledValue -ne 0 }
            EffectiveState = if ($null -eq $enabledValue) { 'NotExplicitlyConfigured' } elseif ($enabledValue -ne 0) { 'Enabled' } else { 'Disabled' }
            Exclusions = $exclusions
            MitigatedApplications = $mitigated
            RegistryPath = $path
            EvidenceType = 'RegistryObservation'
            Error = $null
        }
    } catch {
        [pscustomobject]@{
            Supported = $true; Configured = $false; Enabled = $null
            EffectiveState = 'Unknown'; Exclusions = @(); MitigatedApplications = @()
            RegistryPath = $path; EvidenceType = 'RegistryObservation'; Error = $_.Exception.Message
        }
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareFaultTolerantHeapState }
