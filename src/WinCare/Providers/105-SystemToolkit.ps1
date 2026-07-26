<#
.SYNOPSIS
Compatibility entry point for WinCare's typed Windows health-command plans.
#>
function Invoke-WinCareSystemRepairCheck {
    [CmdletBinding()]
    param(
        [ValidateSet('SfcScan','DismCheckHealth','DismScanHealth','DismRestoreHealth','ChkdskScan')]
        [string]$Operation='SfcScan',
        [switch]$Apply,[switch]$Approved,[switch]$PreviewOnly
    )
    if(-not(Get-Command New-WinCareHealthPlan -ErrorAction SilentlyContinue)){return New-WinCareResult -Success $false -Status Blocked -Code 'HealthPlanProviderUnavailable' -Message 'The canonical health-plan provider is unavailable.' -ExitCode 78}
    $plan=New-WinCareHealthPlan -Operation $Operation
    if(-not $Apply -and -not $PreviewOnly){return $plan}
    Invoke-WinCarePlan -Plan $plan -Approved:$Approved -PreviewOnly:$PreviewOnly
}
if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Invoke-WinCareSystemRepairCheck }
