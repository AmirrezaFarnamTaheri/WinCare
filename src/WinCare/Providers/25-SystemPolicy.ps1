<#
    .SYNOPSIS
    WinCare v5.2.0 System Policy & PReg Registry Hive Provider
    Module ID: 25-SystemPolicy

    .DESCRIPTION
    Reads and writes local Group Policy binary (.pol) files without external dependencies.
    Derived from PolicyPlus (054) - 100% Self-Inclusive via WinCare.Native.dll.
#>

$nativeDllPath = Join-Path $PSScriptRoot "..\..\..\bin\WinCare.Native.dll"
if (Test-Path $nativeDllPath) {
    try { Add-Type -Path $nativeDllPath -ErrorAction SilentlyContinue } catch {}
}

function Get-WinCarePolicyFileEntries {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [string]$PolFilePath
    )

    Write-Verbose "Reading PReg binary policy file: $PolFilePath"
    try {
        if (-not (Test-Path $PolFilePath)) {
            Write-Warning "Policy file does not exist: $PolFilePath"
            return @()
        }
        return [WinCare.Native.PolicyEngine]::LoadPolFile($PolFilePath)
    } catch {
        Write-Error "Failed to parse .pol file: $_"
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCarePolicyFileEntries }

