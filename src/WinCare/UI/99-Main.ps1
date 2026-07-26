#requires -Version 7.2
<#
    .SYNOPSIS
    WinCare Main Console UI Shell
    Module ID: 99-Main

    .DESCRIPTION
    Main CLI interactive loop, splash banner, and screen router.
#>

function Show-WinCareSplash {
    Clear-WinCareScreen
    $heading='Windows Operations & Diagnostics'
    Write-WinCareHeader -Title ("WinCare {0} — {1}" -f $script:WinCareVersion,$heading)
    Write-WinCareText -Text "Safety-first local Windows operations" -Role "Accent"
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
