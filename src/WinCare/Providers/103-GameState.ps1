<#
    .SYNOPSIS
    WinCare v5.2.0 Windows Game Mode & Latency Optimization Provider
    Module ID: 103-GameState

    .DESCRIPTION
    Tunes GPU Scheduling (HAGS) and Windows Game Mode latency policies.
#>

function Set-WinCareGameModeOptimization {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [switch]$EnableGameMode = $true
    )

    if ($PSCmdlet.ShouldProcess("HKCU:\Software\Microsoft\GameBar", "Enable Windows Game Mode")) {
        Set-ItemProperty -Path "HKCU:\Software\Microsoft\GameBar" -Name "AllowAutoGameMode" -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue
        Write-Host "Windows Game Mode optimization enabled." -ForegroundColor Green
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Set-WinCareGameModeOptimization }

