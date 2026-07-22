<#
    .SYNOPSIS
    WinCare v5.2.0 Main Console UI Shell
    Module ID: 99-Main

    .DESCRIPTION
    Main CLI interactive loop, splash banner, and screen router.
#>

function Show-WinCareSplash {
    Clear-WinCareScreen
    Write-WinCareHeader -Title "WinCare v5.2.0 System Care & Diagnostics Suite"
    Write-WinCareText -Text "Self-Inclusive Polyglot Maintenance Framework (171 Integrated Subsystems)" -Role "Accent"
    Write-Host ""
}

function Show-WinCareDashboardScreen {
    Show-WinCareSplash
    Write-WinCareText -Text "--- System Overview ---" -Role "Primary"
    Write-WinCareText -Text "OS: $([System.Environment]::OSVersion.VersionString)" -Role "Text"
    Write-WinCareText -Text "64-Bit Architecture: $([System.Environment]::Is64BitOperatingSystem)" -Role "Text"
    Write-WinCareText -Text "Machine Name: $env:COMPUTERNAME" -Role "Text"
    Write-Host ""
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Show-WinCareSplash, Show-WinCareDashboardScreen }
