<#
    .SYNOPSIS
    WinCare v5.2.0 System Troubleshooting & Repair Toolkit Provider
    Module ID: 105-SystemToolkit

    .DESCRIPTION
    Runs SFC, DISM component repair, and Kiosk lockout recovery.
    Derived from Win10_TroubleShooter (106 / 169) - 100% Self-Inclusive.
#>

function Invoke-WinCareSystemRepairCheck {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [switch]$RunSfc = $false
    )

    if ($PSCmdlet.ShouldProcess("System File Integrity", "Execute SFC /Scannow system repair")) {
        if ($RunSfc) {
            Write-Host "Running sfc.exe /scannow..." -ForegroundColor Yellow
            sfc.exe /scannow
            Write-Host "System File Checker scan finished." -ForegroundColor Green
        }
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Invoke-WinCareSystemRepairCheck }

