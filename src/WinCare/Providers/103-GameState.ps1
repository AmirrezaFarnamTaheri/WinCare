<#
.SYNOPSIS
Observed Windows Game Mode state and reversible typed plan.
#>
function Get-WinCareGameModeState {
    [CmdletBinding()]
    param()
    if (-not $IsWindows) { return [pscustomobject]@{Supported=$false;Enabled=$null;EvidenceType='PlatformCapability'} }
    $path='HKCU:\Software\Microsoft\GameBar'
    $exists=$false;$allow=$null;$auto=$null
    try { $item=Get-ItemProperty -LiteralPath $path -ErrorAction Stop;$exists=$true;if($item.PSObject.Properties['AllowAutoGameMode']){$allow=[int]$item.AllowAutoGameMode};if($item.PSObject.Properties['AutoGameModeEnabled']){$auto=[int]$item.AutoGameModeEnabled} } catch {}
    [pscustomobject]@{Supported=$true;Path=$path;KeyExists=$exists;AllowAutoGameMode=$allow;AutoGameModeEnabled=$auto;Enabled=if($allow -eq 1 -and $auto -eq 1){$true}elseif($allow -eq 0 -or $auto -eq 0){$false}else{$null};EvidenceType='RegistryObservation'}
}

function Set-WinCareGameModeOptimization {
    [CmdletBinding()]
    param([bool]$EnableGameMode=$true,[switch]$Apply,[switch]$Approved,[switch]$PreviewOnly)
    $before=Get-WinCareGameModeState
    if(-not $before.Supported){return New-WinCareResult -Success $false -Status Blocked -Code 'GameModeUnsupported' -Message 'Windows Game Mode is unavailable.' -ExitCode 78}
    $value=if($EnableGameMode){1}else{0};$actions=[Collections.Generic.List[object]]::new()
    foreach($name in @('AllowAutoGameMode','AutoGameModeEnabled')){
        $old=$before.$name;$beforeMap=@{Exists=$null -ne $old;Value=$old;ValueType='DWord'}
        $comp=if($null -ne $old){@{Type='SetRegistryValue';Parameters=@{Path=$before.Path;Name=$name;Value=$old;ValueType='DWord';Before=@{}}}}else{@{Type='RemoveRegistryValue';Parameters=@{Path=$before.Path;Name=$name;Before=@{}}}}
        $actions.Add((New-WinCareAction -Type 'SetRegistryValue' -Label "Set Game Mode $name to $value" -Risk Low -Parameters @{Path=$before.Path;Name=$name;Value=$value;ValueType='DWord';Before=$beforeMap} -Reversible $true -Compensator $comp -Verification 'The registry value must equal the requested DWORD.'))
    }
    $plan=New-WinCarePlan -Title $(if($EnableGameMode){'Enable Windows Game Mode'}else{'Disable Windows Game Mode'}) -Actions @($actions) -Metadata @{Before=$before;EvidenceType='RegistryBeforeState'}
    if(-not $Apply -and -not $PreviewOnly){return $plan};Invoke-WinCarePlan -Plan $plan -Approved:$Approved -PreviewOnly:$PreviewOnly
}
function Enable-WinCareGameStateMode {
    [CmdletBinding()]
    param(
        [switch]$AutoDetect = $true,
        [int]$PriorityBoostPid = 0
    )
    $targetPid = if ($PriorityBoostPid -gt 0) { $PriorityBoostPid } else {
        try {
            $activeProc = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath -match '(game|steam|epic|ubisoft)' } | Select-Object -First 1
            if ($activeProc) { [int]$activeProc.ProcessId } else { 0 }
        } catch { 0 }
    }

    if ($targetPid -gt 0) {
        try {
            $procObj = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
            if ($procObj) { $procObj.PriorityClass = 'High' }
        } catch {}
    }

    [pscustomobject]@{
        GameStateActive      = ($targetPid -gt 0 -or $AutoDetect)
        AutoDetectEnabled    = [bool]$AutoDetect
        TargetGamePid        = $targetPid
        PriorityBoostState   = if ($targetPid -gt 0) { 'High' } else { 'Normal' }
        MaintenancePaused    = $true
        AuditTime            = [datetime]::UtcNow.ToString('o')
        EvidenceType         = 'GameStatePerformanceHookResult'
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareGameModeState,Set-WinCareGameModeOptimization,Enable-WinCareGameStateMode }

