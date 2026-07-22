#requires -Version 7.2
<#
    .SYNOPSIS
    WinCare v1.0.0 Main Console UI Shell
    Module ID: 99-Main

    .DESCRIPTION
    Main CLI interactive loop, splash banner, and screen router.
#>

function Show-WinCareSplash {
    Clear-WinCareScreen
    Write-WinCareHeader -Title "WinCare v1.0.0 System Care & Diagnostics Suite"
    Write-WinCareText -Text "Self-Inclusive Polyglot Maintenance Framework" -Role "Accent"
    Write-Host ""
}

function Start-WinCare {
    [CmdletBinding()]
    param(
        [switch]$NoLogo,
        [switch]$Ascii,
        [switch]$ReadOnly,
        [ValidateSet('Normal','HighContrast','Monochrome')][string]$Theme = 'Normal'
    )
    Initialize-WinCareState -ReadOnly:$ReadOnly -SkipConfigSave
    if (-not $NoLogo) {
        Show-WinCareSplash
    }
    Show-WinCareDashboardScreen
}
