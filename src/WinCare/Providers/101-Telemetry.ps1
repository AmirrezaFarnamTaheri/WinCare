<#
    .SYNOPSIS
    WinCare v5.2.0 DiagTrack & Windows Telemetry Suppression Provider
    Module ID: 101-Telemetry

    .DESCRIPTION
    Suppresses DiagTrack (Connected User Experiences and Telemetry) service and diagnostic tasks.
#>

function Set-WinCareTelemetrySuppression {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [switch]$DisableDiagTrack = $true
    )

    if ($PSCmdlet.ShouldProcess("DiagTrack & Telemetry Services", "Disable telemetry services and diagnostic tasks")) {
        if ($DisableDiagTrack) {
            Write-Host "Disabling DiagTrack service..." -ForegroundColor Yellow
            Stop-Service -Name "DiagTrack" -ErrorAction SilentlyContinue
            Set-Service -Name "DiagTrack" -StartupType Disabled -ErrorAction SilentlyContinue
            Write-Host "DiagTrack service disabled." -ForegroundColor Green
        }
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Set-WinCareTelemetrySuppression }

