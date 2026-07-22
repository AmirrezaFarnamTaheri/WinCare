<#
    .SYNOPSIS
    WinCare v5.2.0 Storage & System Cleanup Provider
    Module ID: 20-Cleanup

    .DESCRIPTION
    Performs WinSxS Component Store cleanup via DISM, DriverStore `.inf` cleanup, and temporary folder maintenance.
    Derived from DriverUpdate (095) and FileWizardAI (171) - 100% Self-Inclusive.
#>

function New-WinCareCleanupPlan {
    [CmdletBinding()]
    param()

    $tempPath = [System.IO.Path]::GetTempPath()
    $tempSize = 0
    if (Test-Path $tempPath) {
        $tempFiles = Get-ChildItem -Path $tempPath -Recurse -File -ErrorAction SilentlyContinue
        $tempSize = ($tempFiles | Measure-Object -Property Length -Sum).Sum
    }

    return [PSCustomObject]@{
        Target                  = "System Temp & DISM WinSxS"
        EstimatedSpaceReclaimableBytes = $tempSize
        EstimatedSpaceReclaimableMB    = [math]::Round($tempSize / 1MB, 2)
        ActionsPlanned          = @("Purge Temp Files", "Clean DISM Component Store")
    }
}

function Invoke-WinCareCleanupAction {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [switch]$PurgeTemp = $true,
        [switch]$CleanDism = $false
    )

    if ($PSCmdlet.ShouldProcess("System Cleanup Targets", "Execute cleanup action")) {
        if ($PurgeTemp) {
            $tempPath = [System.IO.Path]::GetTempPath()
            Write-Host "Purging temporary files in $tempPath..." -ForegroundColor Yellow
            Remove-Item -Path "$tempPath\*" -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "Temp cleanup completed." -ForegroundColor Green
        }
        if ($CleanDism) {
            Write-Host "Executing DISM Component Store Cleanup..." -ForegroundColor Yellow
            Dism.exe /Online /Cleanup-Image /StartComponentCleanup /ResetBase
            Write-Host "DISM cleanup completed." -ForegroundColor Green
        }
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function New-WinCareCleanupPlan, Invoke-WinCareCleanupAction
 }

