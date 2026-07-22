function Test-WinCareWildcardMatch {
    param([string]$Value,[string[]]$Patterns)
    foreach ($pattern in @($Patterns)) { if ($Value -like $pattern) { return $true } }
    return $false
}

function Test-WinCareActionPolicy {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $policy=Get-WinCarePolicy
    if (Test-WinCareWildcardMatch -Value ([string]$Action.Type) -Patterns @($policy.DeniedActionTypes)) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'PolicyDeniedAction' -Message "Action type is denied by policy: $($Action.Type)" -ExitCode 5
    }
    if (-not (Test-WinCareWildcardMatch -Value ([string]$Action.Type) -Patterns @($policy.AllowedActionTypes))) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'PolicyNotAllowed' -Message "Action type is not allowed by policy: $($Action.Type)" -ExitCode 5
    }
    if ($Action.Risk -eq 'Critical' -and -not [bool]$policy.AllowCriticalActions) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'CriticalActionsDisabled' -Message 'Critical actions are disabled by policy.' -ExitCode 5
    }
    if ($Action.Type -eq 'DeployWdacPolicy' -and (Get-WinCarePropertyValue $Action.Parameters 'Enforced' $false) -and -not [bool]$policy.AllowWdacEnforcement) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'WdacEnforcementDisabled' -Message 'WDAC enforcement deployment is disabled; audit-mode policies remain available.' -ExitCode 5
    }
    if ($Action.Type -eq 'SetPageFileConfiguration' -and [string](Get-WinCarePropertyValue $Action.Parameters 'Mode' '') -eq 'Disabled' -and -not [bool]$policy.AllowPageFileDisable) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'PageFileDisableDenied' -Message 'Page-file disabling is disabled by policy. System-managed or measured custom paging remains available.' -ExitCode 5
    }
    if ($Action.Type -eq 'SetSecurityControlState' -and -not [bool](Get-WinCarePropertyValue $Action.Parameters 'Enabled' $true)) {
        if (-not [bool]$policy.AllowSecurityControlReduction) { return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityReductionDenied' -Message 'Temporary security-control reduction is disabled by policy.' -ExitCode 5 }
        $control=[string](Get-WinCarePropertyValue $Action.Parameters 'Control' '')
        $duration=[int](Get-WinCarePropertyValue $Action.Parameters 'DurationMinutes' 0)
        if ($duration -gt [int]$policy.MaximumSecurityMaintenanceMinutes) { return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceTooLong' -Message 'Security-maintenance duration exceeds policy.' -ExitCode 5 }
        if ($control -eq 'Uac' -and -not [bool]$policy.AllowUacDisable) { return New-WinCareResult -Success $false -Status Blocked -Code 'UacDisableDenied' -Message 'UAC disabling is disabled by policy.' -ExitCode 5 }
        if ($control -eq 'WindowsUpdateStack' -and -not [bool]$policy.AllowWindowsUpdateServiceDisable) { return New-WinCareResult -Success $false -Status Blocked -Code 'WindowsUpdateDisableDenied' -Message 'Windows Update service suspension is disabled by policy.' -ExitCode 5 }
    }
    if ($Action.Type -eq 'SetLegacySecurityControlState') {
        if (-not [bool]$policy.AllowLegacyUnsafeProfiles) { return New-WinCareResult -Success $false -Status Blocked -Code 'LegacyUnsafeProfilesDenied' -Message 'Legacy Unsafe one-click profiles are disabled by policy.' -ExitCode 5 }
        if (-not [bool]$policy.AllowPermanentSecurityReduction) { return New-WinCareResult -Success $false -Status Blocked -Code 'PermanentSecurityReductionDenied' -Message 'Permanent security-control reduction is disabled by policy.' -ExitCode 5 }
        $control=[string](Get-WinCarePropertyValue $Action.Parameters 'Control' '')
        if ($control -eq 'Uac' -and -not [bool]$policy.AllowUacDisable) { return New-WinCareResult -Success $false -Status Blocked -Code 'UacDisableDenied' -Message 'UAC disabling is disabled by policy.' -ExitCode 5 }
        if ($control -eq 'WindowsUpdateStack' -and -not [bool]$policy.AllowWindowsUpdateServiceDisable) { return New-WinCareResult -Success $false -Status Blocked -Code 'WindowsUpdateDisableDenied' -Message 'Windows Update service disabling is disabled by policy.' -ExitCode 5 }
    }
    if ($Action.Type -eq 'ResetDisplayOverrides') {
        if (-not [bool]$policy.AllowLegacyUnsafeProfiles -or -not [bool]$policy.AllowDisplayOverrideReset) { return New-WinCareResult -Success $false -Status Blocked -Code 'DisplayOverrideResetDenied' -Message 'Display override reset is disabled by policy.' -ExitCode 5 }
    }
    if ($Action.Type -eq 'SetHibernateState' -and -not [bool]$policy.AllowLegacyUnsafeProfiles) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'LegacyUnsafeProfilesDenied' -Message 'Legacy Unsafe one-click profiles are disabled by policy.' -ExitCode 5
    }
    if ($Action.Type -eq 'RunVerifiedPeerTool') {
        if (-not [bool]$policy.AllowVerifiedPeerTools) { return New-WinCareResult -Success $false -Status Blocked -Code 'VerifiedPeerToolsDenied' -Message 'Execution of verified peer tools is disabled by policy.' -ExitCode 5 }
        $toolId=[string](Get-WinCarePropertyValue $Action.Parameters 'ToolId' '')
        $mutation=[bool](Get-WinCarePropertyValue $Action.Parameters 'Mutation' $false)
        if ($toolId -eq 'Adb' -and $mutation -and -not [bool]$policy.AllowAdbMutation) { return New-WinCareResult -Success $false -Status Blocked -Code 'AdbMutationDenied' -Message 'Mutating ADB commands are disabled by policy.' -ExitCode 5 }
        if ($toolId -eq 'ViVeTool' -and (-not [bool]$policy.AllowLegacyUnsafeProfiles -or -not [bool]$policy.AllowXboxFullscreenExperience)) { return New-WinCareResult -Success $false -Status Blocked -Code 'XboxFullscreenExperienceDenied' -Message 'Xbox Full Screen Experience mutations are disabled by policy.' -ExitCode 5 }
    }
    if ($Action.Type -eq 'RunNetworkExperiment') {
        if (-not [bool]$policy.AllowNetworkExperiments) { return New-WinCareResult -Success $false -Status Blocked -Code 'NetworkExperimentsDenied' -Message 'Network experiments are disabled by policy.' -ExitCode 5 }
        if ([string](Get-WinCarePropertyValue $Action.Parameters 'Setting' '') -in @('AutoTuningLevel','EcnCapability') -and -not [bool]$policy.AllowExperimentalTcpSettings) { return New-WinCareResult -Success $false -Status Blocked -Code 'ExperimentalTcpSettingDenied' -Message 'Experimental TCP setting changes are disabled by policy.' -ExitCode 5 }
        $samples=[int](Get-WinCarePropertyValue $Action.Parameters 'SampleCount' 0);$timeout=[int](Get-WinCarePropertyValue $Action.Parameters 'TimeoutMilliseconds' 0)
        $worstCaseSeconds=[math]::Ceiling((@((Get-WinCarePropertyValue $Action.Parameters 'TargetHosts' @())).Count*$samples*((3*$timeout)+100)*2)/1000.0)+15
        if ($worstCaseSeconds -gt ([int]$policy.MaximumNetworkExperimentMinutes*60)) { return New-WinCareResult -Success $false -Status Blocked -Code 'NetworkExperimentTooLong' -Message 'The experiment exceeds the policy runtime bound.' -ExitCode 5 }
    }
    if ($Action.Type -eq 'StartPowerSession') {
        $mode=[string](Get-WinCarePropertyValue $Action.Parameters 'Mode' '')
        $expires=[string](Get-WinCarePropertyValue $Action.Parameters 'ExpiresAt' '')
        if($mode -eq 'Away' -and -not [bool]$policy.AllowAwayModePowerSession){return New-WinCareResult -Success $false -Status Blocked -Code 'AwayModePowerSessionDenied' -Message 'Away-mode power sessions are disabled by policy.' -ExitCode 5}
        if(-not $expires -and -not [bool]$policy.AllowIndefinitePowerSession){return New-WinCareResult -Success $false -Status Blocked -Code 'IndefinitePowerSessionDenied' -Message 'Indefinite power sessions are disabled by policy.' -ExitCode 5}
        if($expires){$minutes=([datetime]::Parse($expires).ToUniversalTime()-[datetime]::UtcNow).TotalMinutes;if($minutes -gt [int]$policy.MaximumPowerSessionMinutes+1){return New-WinCareResult -Success $false -Status Blocked -Code 'PowerSessionTooLong' -Message 'Power-session duration exceeds policy.' -ExitCode 5}}
    }
    if ($Action.Type -eq 'ReplaceRemoteConsentStore') {
        $records=@((Get-WinCarePropertyValue (Get-WinCarePropertyValue $Action.Parameters 'Store' @{}) 'Records' @()))
        foreach($record in $records){if([string](Get-WinCarePropertyValue $record 'Status' '') -in @('Prepared','Active')){$duration=([datetime]::Parse([string](Get-WinCarePropertyValue $record 'ExpiresAt' '')).ToUniversalTime()-[datetime]::UtcNow).TotalMinutes;if($duration -gt [int]$policy.MaximumRemoteConsentMinutes+1){return New-WinCareResult -Success $false -Status Blocked -Code 'RemoteConsentTooLong' -Message 'Remote-support consent duration exceeds policy.' -ExitCode 5}}}
    }
    if ($Action.Type -eq 'TerminateRemoteSupportProcess' -and -not [bool]$policy.AllowRemoteSupportTermination) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'RemoteSupportTerminationDenied' -Message 'Remote-support process termination is disabled by policy.' -ExitCode 5
    }
    if ($Action.Type -eq 'CaptureEtwTrace' -and -not [bool]$policy.AllowProcessInstrumentation) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'ProcessInstrumentationDenied' -Message 'Process instrumentation is disabled by policy.' -ExitCode 5
    }
    if ($Action.Type -eq 'QuarantineInjectionSurface' -and -not [bool]$policy.AllowInjectionSurfaceRemediation) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'InjectionSurfaceRemediationDenied' -Message 'Injection-surface remediation is disabled by policy.' -ExitCode 5
    }
    return New-WinCareResult -Success $true -Message 'Action is allowed by policy.'
}

function Test-WinCarePlanPolicy {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Plan,[switch]$PreviewOnly)
    $policy=Get-WinCarePolicy
    $legacyProfile=[string](Get-WinCarePropertyValue $Plan.Metadata 'LegacyUnsafeProfile' '')
    if($legacyProfile -and -not [bool]$policy.AllowLegacyUnsafeProfiles){
        return New-WinCareResult -Success $false -Status Blocked -Code 'LegacyUnsafeProfilesDenied' -Message 'Legacy Unsafe one-click profiles are disabled by policy.' -ExitCode 5
    }
    if ($Plan.Actions.Count -gt [int]$policy.MaximumPlanActions) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'PlanTooLarge' -Message "Plan exceeds the policy maximum of $($policy.MaximumPlanActions) actions." -ExitCode 5
    }
    $estimated=[long](($Plan.Actions | Measure-Object EstimatedBytes -Sum).Sum)
    if ($estimated -gt [long]$policy.MaximumEstimatedBytes) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'EstimatedBytesExceeded' -Message 'Plan exceeds the policy data-change limit.' -ExitCode 5
    }
    if (-not $PreviewOnly -and $policy.MaintenanceWindow -and -not (Test-WinCareMaintenanceWindow -Window ([string]$policy.MaintenanceWindow))) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'OutsideMaintenanceWindow' -Message 'The current time is outside the configured maintenance window.' -ExitCode 5
    }
    foreach ($action in @($Plan.Actions)) {
        $result=Test-WinCareActionPolicy -Action $action
        if (-not $result.Success) { return $result }
    }
    return New-WinCareResult -Success $true -Message 'Plan is allowed by policy.'
}

function Test-WinCareCatalogRulePolicy {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Rule)
    $policy=Get-WinCarePolicy
    if (Test-WinCareWildcardMatch -Value ([string]$Rule.Id) -Patterns @($policy.DeniedCatalogRuleIds)) { return $false }
    return Test-WinCareWildcardMatch -Value ([string]$Rule.Id) -Patterns @($policy.AllowedCatalogRuleIds)
}
