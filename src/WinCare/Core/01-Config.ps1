$script:WinCareState = @{}

function Get-WinCareDataRoot {
    [CmdletBinding()]
    param()
    $base = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { Join-Path $HOME '.local/share' }
    Join-Path $base 'WinCare'
}

function Get-WinCareDefaultConfig {
    [CmdletBinding()]
    param()
    [ordered]@{
        SchemaVersion             = 8
        Theme                     = 'Normal'
        Ascii                     = $false
        ReadOnly                  = $false
        ReducedMotion             = $false
        ScreenReaderMode          = $false
        PersistNavigation         = $true
        LargeFileThresholdGB      = 1
        CleanupMinimumAgeHours    = 24
        IncludeStoreApps          = $true
        LogRetentionDays          = 30
        RedactUserNameInReports   = $true
        RequirePreviewForChanges  = $true
        QuarantineRetentionDays   = 14
        Telemetry                 = 'Disabled'
        LastNavigationAction      = 'Dashboard'
        CommandPaletteHistory     = @()
        WidgetRefreshSeconds      = 15
        WidgetLayout              = @('system-health','storage-pressure','maintenance-state','update-status','security-posture')
        MaintenanceNoticeMinutes  = 60
        ExternalCustomizerMode    = 'ReadOnly'
        BluetoothScanTimeoutSeconds = 10
        SecurityMaintenanceDefaultMinutes = 60
        NetworkExperimentTargets     = @()
        NetworkExperimentSampleCount = 7
        NetworkExperimentTimeoutMilliseconds = 2000
        InstrumentationEventLimit = 100
        WorkspaceMode              = 'All'
        PowerSessionDefaultMinutes = 60
        PowerSessionRefreshSeconds = 30
        BrowserExtensionInventoryLimit = 5000
        RemoteConsentDefaultMinutes = 60
        CommandPaletteMaxResults  = 50
        DownloadMaximumConcurrent = 3
        DownloadRetryLimit        = 5
        DownloadMaximumBytes      = 107374182400
        TelemetryCpuAlertPercent  = 90
        TelemetryMemoryAlertPercent = 90
        TelemetryHistoryLimit     = 100
        LauncherIndexLimit        = 5000
        GameStateFileLimit        = 50000
        GameStateMaximumBytes     = 21474836480
        WorkspaceLayoutLimit      = 32
        FileWorkspaceItemLimit    = 128
        BrightnessScheduleLimit   = 64
        PackageArchiveEntryLimit  = 20000
    }
}

function Get-WinCareDefaultPolicy {
    [CmdletBinding()]
    param()
    [ordered]@{
        SchemaVersion            = 1
        PolicyName               = 'Built-in safe defaults'
        ReadOnly                 = $false
        RequirePreview           = $true
        RequireTypedHighRisk     = $true
        AllowCriticalActions     = $false
        AllowLegacyUnsafeProfiles = $false
        AllowPermanentSecurityReduction = $false
        AllowDisplayOverrideReset = $false
        AllowWdacEnforcement     = $false
        AllowLocalBroker         = $false
        AllowUnsignedExtensions  = $false
        AllowRegistryTreeRemoval = $false
        AllowExternalCustomizerWrites = $false
        AllowOfflineImageServicing = $false
        AllowPageFileDisable      = $false
        AllowSecurityControlReduction = $false
        AllowUacDisable           = $false
        AllowWindowsUpdateServiceDisable = $false
        AllowNetworkExperiments   = $false
        AllowExperimentalTcpSettings = $false
        AllowProcessInstrumentation = $true
        AllowInjectionSurfaceRemediation = $false
        AllowAwayModePowerSession = $false
        AllowIndefinitePowerSession = $false
        MaximumPowerSessionMinutes = 1440
        AllowRemoteSupportTermination = $false
        AllowInsecureDownloads    = $false
        AllowPrivateDownloadTargets = $false
        AllowOfflineImageReduction = $false
        AllowGameStateRestore     = $false
        AllowVerifiedPeerTools    = $false
        AllowAdbMutation          = $false
        AllowXboxFullscreenExperience = $false
        MaximumRemoteConsentMinutes = 240
        MaximumDownloadJobs       = 1000
        MaximumDownloadBytes      = 107374182400
        MaximumSecurityMaintenanceMinutes = 120
        MaximumNetworkExperimentMinutes = 15
        MaximumPlanActions       = 250
        MaximumEstimatedBytes    = 53687091200
        MaintenanceWindow        = ''
        AllowedActionTypes       = @('*')
        DeniedActionTypes        = @(
            'ReplaceBootLoader','DisableDefender','DisableWindowsUpdateService','DeleteEventLogs',
            'PurgePrefetch','BypassHardwareRequirements','InjectProcess','CreateRemoteThread',
            'WriteProcessMemory','InstallGlobalHook')
        TrustedExtensionSigners  = @()
        AllowedCatalogRuleIds    = @('*')
        DeniedCatalogRuleIds     = @()
        AllowedContextMenuRuleIds = @('*')
        DeniedContextMenuRuleIds  = @()
        AllowedPlaybookIds        = @('*')
        DeniedPlaybookIds         = @()
    }
}

function Test-WinCareConfigObject {
    param([Parameter(Mandatory)][Collections.IDictionary]$Config)
    $defaults=Get-WinCareDefaultConfig
    $null=Test-WinCareStrictObjectKeys -InputObject $Config -AllowedKeys @($defaults.Keys) -Context 'configuration'
    if ([int]$Config.SchemaVersion -notin @(1,2,3,4,5,6,7,8)) { throw 'Unsupported configuration schema.' }
    if ([string]$Config.Theme -notin @('Normal','HighContrast','Monochrome')) { throw 'Invalid theme.' }
    foreach ($name in @('Ascii','ReadOnly','ReducedMotion','ScreenReaderMode','PersistNavigation','IncludeStoreApps','RedactUserNameInReports','RequirePreviewForChanges')) {
        if ($Config[$name] -isnot [bool]) { throw "$name must be a Boolean." }
    }
    if ([int]$Config.LargeFileThresholdGB -lt 1 -or [int]$Config.LargeFileThresholdGB -gt 10240) { throw 'LargeFileThresholdGB is outside 1..10240.' }
    if ([int]$Config.CleanupMinimumAgeHours -lt 0 -or [int]$Config.CleanupMinimumAgeHours -gt 87600) { throw 'CleanupMinimumAgeHours is outside 0..87600.' }
    if ([int]$Config.LogRetentionDays -lt 1 -or [int]$Config.LogRetentionDays -gt 3650) { throw 'LogRetentionDays is outside 1..3650.' }
    if ([int]$Config.QuarantineRetentionDays -lt 1 -or [int]$Config.QuarantineRetentionDays -gt 3650) { throw 'QuarantineRetentionDays is outside 1..3650.' }
    if ([int]$Config.WidgetRefreshSeconds -lt 1 -or [int]$Config.WidgetRefreshSeconds -gt 3600) { throw 'WidgetRefreshSeconds is outside 1..3600.' }
    if ($Config.WidgetLayout -is [string] -or $Config.WidgetLayout -isnot [Collections.IEnumerable]) { throw 'WidgetLayout must be an array.' }
    $widgetLayout=@($Config.WidgetLayout); if($widgetLayout.Count -gt 32 -or @($widgetLayout|Where-Object{$_ -isnot [string] -or [string]$_ -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$'}).Count){throw 'WidgetLayout contains invalid entries.'}
    if ([int]$Config.MaintenanceNoticeMinutes -lt 0 -or [int]$Config.MaintenanceNoticeMinutes -gt 10080) { throw 'MaintenanceNoticeMinutes is outside 0..10080.' }
    if ([string]$Config.ExternalCustomizerMode -notin @('ReadOnly','BackupAndRestore','Managed')) { throw 'ExternalCustomizerMode is invalid.' }
    if ([int]$Config.BluetoothScanTimeoutSeconds -lt 1 -or [int]$Config.BluetoothScanTimeoutSeconds -gt 60) { throw 'BluetoothScanTimeoutSeconds is outside 1..60.' }
    if ([int]$Config.SecurityMaintenanceDefaultMinutes -lt 5 -or [int]$Config.SecurityMaintenanceDefaultMinutes -gt 1440) { throw 'SecurityMaintenanceDefaultMinutes is outside 5..1440.' }
    if ($Config.NetworkExperimentTargets -is [string] -or $Config.NetworkExperimentTargets -isnot [Collections.IEnumerable]) { throw 'NetworkExperimentTargets must be an array.' }
    $networkTargets=@($Config.NetworkExperimentTargets)
    if ($networkTargets.Count -gt 32 -or @($networkTargets|Where-Object{$_ -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$_) -or ([string]$_).Length -gt 253 -or [Uri]::CheckHostName(([string]$_).Trim()) -eq [UriHostNameType]::Unknown}).Count) { throw 'NetworkExperimentTargets contains invalid host names or IP addresses.' }
    if ([int]$Config.NetworkExperimentSampleCount -lt 3 -or [int]$Config.NetworkExperimentSampleCount -gt 30) { throw 'NetworkExperimentSampleCount is outside 3..30.' }
    if ([int]$Config.NetworkExperimentTimeoutMilliseconds -lt 250 -or [int]$Config.NetworkExperimentTimeoutMilliseconds -gt 10000) { throw 'NetworkExperimentTimeoutMilliseconds is outside 250..10000.' }
    if ([int]$Config.InstrumentationEventLimit -lt 1 -or [int]$Config.InstrumentationEventLimit -gt 5000) { throw 'InstrumentationEventLimit is outside 1..5000.' }
    if ([string]$Config.WorkspaceMode -notin @('All','Maintenance','Productivity','Support','Developer','Minimal')) { throw 'WorkspaceMode is invalid.' }
    if ([int]$Config.PowerSessionDefaultMinutes -lt 5 -or [int]$Config.PowerSessionDefaultMinutes -gt 10080) { throw 'PowerSessionDefaultMinutes is outside 5..10080.' }
    if ([int]$Config.PowerSessionRefreshSeconds -lt 5 -or [int]$Config.PowerSessionRefreshSeconds -gt 300) { throw 'PowerSessionRefreshSeconds is outside 5..300.' }
    if ([int]$Config.BrowserExtensionInventoryLimit -lt 100 -or [int]$Config.BrowserExtensionInventoryLimit -gt 50000) { throw 'BrowserExtensionInventoryLimit is outside 100..50000.' }
    if ([int]$Config.RemoteConsentDefaultMinutes -lt 5 -or [int]$Config.RemoteConsentDefaultMinutes -gt 1440) { throw 'RemoteConsentDefaultMinutes is outside 5..1440.' }
    if ([int]$Config.CommandPaletteMaxResults -lt 10 -or [int]$Config.CommandPaletteMaxResults -gt 200) { throw 'CommandPaletteMaxResults is outside 10..200.' }
    if ([int]$Config.DownloadMaximumConcurrent -lt 1 -or [int]$Config.DownloadMaximumConcurrent -gt 16) { throw 'DownloadMaximumConcurrent is outside 1..16.' }
    if ([int]$Config.DownloadRetryLimit -lt 0 -or [int]$Config.DownloadRetryLimit -gt 20) { throw 'DownloadRetryLimit is outside 0..20.' }
    if ([long]$Config.DownloadMaximumBytes -lt 1048576 -or [long]$Config.DownloadMaximumBytes -gt 1099511627776) { throw 'DownloadMaximumBytes is outside 1 MiB..1 TiB.' }
    foreach($name in @('TelemetryCpuAlertPercent','TelemetryMemoryAlertPercent')){if([int]$Config[$name] -lt 1 -or [int]$Config[$name] -gt 100){throw "$name is outside 1..100."}}
    if([int]$Config.TelemetryHistoryLimit -lt 1 -or [int]$Config.TelemetryHistoryLimit -gt 1000){throw 'TelemetryHistoryLimit is outside 1..1000.'}
    if([int]$Config.LauncherIndexLimit -lt 100 -or [int]$Config.LauncherIndexLimit -gt 50000){throw 'LauncherIndexLimit is outside 100..50000.'}
    if([int]$Config.GameStateFileLimit -lt 100 -or [int]$Config.GameStateFileLimit -gt 1000000){throw 'GameStateFileLimit is outside 100..1000000.'}
    if([long]$Config.GameStateMaximumBytes -lt 1048576 -or [long]$Config.GameStateMaximumBytes -gt 1099511627776){throw 'GameStateMaximumBytes is outside 1 MiB..1 TiB.'}
    if([int]$Config.WorkspaceLayoutLimit -lt 1 -or [int]$Config.WorkspaceLayoutLimit -gt 256){throw 'WorkspaceLayoutLimit is outside 1..256.'}
    if([int]$Config.FileWorkspaceItemLimit -lt 1 -or [int]$Config.FileWorkspaceItemLimit -gt 2000){throw 'FileWorkspaceItemLimit is outside 1..2000.'}
    if([int]$Config.BrightnessScheduleLimit -lt 1 -or [int]$Config.BrightnessScheduleLimit -gt 1000){throw 'BrightnessScheduleLimit is outside 1..1000.'}
    if([int]$Config.PackageArchiveEntryLimit -lt 100 -or [int]$Config.PackageArchiveEntryLimit -gt 200000){throw 'PackageArchiveEntryLimit is outside 100..200000.'}
    if ([string]$Config.Telemetry -ne 'Disabled') { throw 'WinCare telemetry is intentionally disabled; no other value is accepted.' }
    if ($Config.CommandPaletteHistory -is [string] -or $Config.CommandPaletteHistory -isnot [Collections.IEnumerable]) { throw 'CommandPaletteHistory must be an array.' }
    $history=@($Config.CommandPaletteHistory)
    if ($history.Count -gt 50 -or @($history|Where-Object{$_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) -or ([string]$_).Length -gt 80}).Count) { throw 'CommandPaletteHistory contains invalid entries.' }
    return $true
}

function Convert-WinCareConfigToCurrent {
    param([Parameter(Mandatory)][Collections.IDictionary]$Config)
    $result=Get-WinCareDefaultConfig
    foreach ($key in @($Config.Keys)) { if ($result.Contains($key)) { $result[$key]=$Config[$key] } }
    $result.SchemaVersion=8
    return $result
}

function Test-WinCarePolicyObject {
    param([Parameter(Mandatory)][Collections.IDictionary]$Policy,[string]$Source='policy')
    $defaults=Get-WinCareDefaultPolicy
    $null=Test-WinCareStrictObjectKeys -InputObject $Policy -AllowedKeys @($defaults.Keys) -Context $Source
    if ([int]$Policy.SchemaVersion -ne 1) { throw "Unsupported $Source schema." }
    if ([int]$Policy.MaximumPlanActions -lt 1 -or [int]$Policy.MaximumPlanActions -gt 10000) { throw "$Source MaximumPlanActions is invalid." }
    if ([long]$Policy.MaximumEstimatedBytes -lt 0) { throw "$Source MaximumEstimatedBytes is invalid." }
    if ([int]$Policy.MaximumSecurityMaintenanceMinutes -lt 5 -or [int]$Policy.MaximumSecurityMaintenanceMinutes -gt 1440) { throw "$Source MaximumSecurityMaintenanceMinutes is invalid." }
    if ([int]$Policy.MaximumNetworkExperimentMinutes -lt 1 -or [int]$Policy.MaximumNetworkExperimentMinutes -gt 25) { throw "$Source MaximumNetworkExperimentMinutes must be within 1..25." }
    if ([int]$Policy.MaximumPowerSessionMinutes -lt 5 -or [int]$Policy.MaximumPowerSessionMinutes -gt 10080) { throw "$Source MaximumPowerSessionMinutes must be within 5..10080." }
    if ([int]$Policy.MaximumRemoteConsentMinutes -lt 5 -or [int]$Policy.MaximumRemoteConsentMinutes -gt 1440) { throw "$Source MaximumRemoteConsentMinutes must be within 5..1440." }
    if ([int]$Policy.MaximumDownloadJobs -lt 1 -or [int]$Policy.MaximumDownloadJobs -gt 10000) { throw "$Source MaximumDownloadJobs must be within 1..10000." }
    if ([long]$Policy.MaximumDownloadBytes -lt 1048576 -or [long]$Policy.MaximumDownloadBytes -gt 1099511627776) { throw "$Source MaximumDownloadBytes must be within 1 MiB..1 TiB." }
    foreach ($name in @('ReadOnly','RequirePreview','RequireTypedHighRisk','AllowCriticalActions','AllowLegacyUnsafeProfiles','AllowPermanentSecurityReduction','AllowDisplayOverrideReset','AllowWdacEnforcement','AllowLocalBroker','AllowUnsignedExtensions','AllowRegistryTreeRemoval','AllowExternalCustomizerWrites','AllowOfflineImageServicing','AllowPageFileDisable','AllowSecurityControlReduction','AllowUacDisable','AllowWindowsUpdateServiceDisable','AllowNetworkExperiments','AllowExperimentalTcpSettings','AllowProcessInstrumentation','AllowInjectionSurfaceRemediation','AllowAwayModePowerSession','AllowIndefinitePowerSession','AllowRemoteSupportTermination','AllowInsecureDownloads','AllowPrivateDownloadTargets','AllowOfflineImageReduction','AllowGameStateRestore','AllowVerifiedPeerTools','AllowAdbMutation','AllowXboxFullscreenExperience')) {
        if ($Policy[$name] -isnot [bool]) { throw "$Source $name must be a Boolean." }
    }
    if ($Policy.MaintenanceWindow) { $null=Test-WinCareMaintenanceWindow -Window ([string]$Policy.MaintenanceWindow) }
    foreach ($listName in @('AllowedActionTypes','DeniedActionTypes','AllowedCatalogRuleIds','DeniedCatalogRuleIds','AllowedContextMenuRuleIds','DeniedContextMenuRuleIds','AllowedPlaybookIds','DeniedPlaybookIds')) {
        $list=@($Policy[$listName]); if($list.Count -gt 10000 -or @($list|Where-Object{$_ -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$_) -or ([string]$_).Length -gt 160}).Count){throw "$Source $listName contains invalid entries."}
    }
    foreach ($thumbprint in @($Policy.TrustedExtensionSigners)) {
        if ($thumbprint -notmatch '^[A-Fa-f0-9]{40,64}$') { throw "$Source contains an invalid signer thumbprint." }
    }
    return $true
}

function Merge-WinCarePolicy {
    param([Parameter(Mandatory)][Collections.IDictionary[]]$Policies)
    $result=Get-WinCareDefaultPolicy
    foreach ($policy in $Policies) {
        foreach ($key in $policy.Keys) { $result[$key]=$policy[$key] }
    }
    return $result
}

function Read-WinCareJsonHashtable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,16777216)][long]$MaximumBytes=4194304
    )
    Read-WinCareBoundedJson -LiteralPath $LiteralPath -MaximumBytes $MaximumBytes -Depth 50 -AsHashtable
}

function Get-WinCareEffectivePolicy {
    [CmdletBinding()]
    param()
    $sources=[System.Collections.Generic.List[object]]::new()
    $sources.Add((Get-WinCareDefaultPolicy))
    $userPolicy=Join-Path (Get-WinCareDataRoot) 'policy.json'
    $orgPolicy=$env:WINCARE_ORG_POLICY
    $machinePolicy=if ($env:ProgramData) { Join-Path $env:ProgramData 'WinCare\policy.json' } else { '' }
    foreach ($entry in @(
        @{Path=$userPolicy;Name='user policy'},
        @{Path=$orgPolicy;Name='organization policy'},
        @{Path=$machinePolicy;Name='machine policy'}
    )) {
        if ($entry.Path -and (Test-Path -LiteralPath $entry.Path -PathType Leaf)) {
            $policy=Read-WinCareJsonHashtable -LiteralPath $entry.Path
            $null=Test-WinCarePolicyObject -Policy $policy -Source $entry.Name
            $sources.Add($policy)
        }
    }
    $merged=Merge-WinCarePolicy -Policies @($sources)
    $null=Test-WinCarePolicyObject -Policy $merged -Source 'effective policy'
    return $merged
}

function Initialize-WinCareDataRootIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [switch]$VerifyOnly
    )
    $rootPath=Resolve-WinCareCanonicalPath -LiteralPath $Root -AllowMissing
    $identityPath=Join-Path $rootPath '.wincare-data.json'
    $rootHash=Get-WinCareSha256Text -Text $rootPath.ToLowerInvariant()
    if(Test-Path -LiteralPath $identityPath -PathType Leaf) {
        $identity=Read-WinCareBoundedJson -LiteralPath $identityPath -MaximumBytes 65536 -Depth 12 -AsHashtable
        $null=Test-WinCareStrictObjectKeys -InputObject $identity -AllowedKeys @(
            'SchemaVersion','Product','ManifestGuid','RootSha256','CreatedAt'
        ) -Context 'data-root identity'
        if([int]$identity.SchemaVersion -ne 1 -or [string]$identity.Product -ne 'WinCare') {
            throw 'WinCare data-root identity is invalid.'
        }
        if([string]$identity.ManifestGuid -ne 'f43eb775-7d08-42ec-9888-8b1bd79e90a3') {
            throw 'WinCare data-root identity has an unexpected product GUID.'
        }
        if([string]$identity.RootSha256 -ne $rootHash) {
            throw 'WinCare data-root identity is bound to a different path.'
        }
        return $identityPath
    }
    if(Test-Path -LiteralPath $identityPath) { throw 'WinCare data-root identity path is not a regular file.' }
    if($VerifyOnly) { return $null }
    if(-not(Test-Path -LiteralPath $rootPath -PathType Container)) {
        throw 'WinCare data root must exist before its identity is created.'
    }
    Write-WinCareAtomicJson -LiteralPath $identityPath -InputObject ([ordered]@{
        SchemaVersion=1
        Product='WinCare'
        ManifestGuid='f43eb775-7d08-42ec-9888-8b1bd79e90a3'
        RootSha256=$rootHash
        CreatedAt=[datetime]::UtcNow.ToString('o')
    }) -Depth 12
    return $identityPath
}

function Get-WinCareInitialConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ConfigPath,
        [Parameter(Mandatory)][bool]$ReadOnlyLocked,
        [Parameter(Mandatory)][Collections.Generic.List[string]]$InitializationWarnings
    )
    $config = Get-WinCareDefaultConfig
    if (Test-Path -LiteralPath $ConfigPath) {
        try {
            $loaded = Read-WinCareJsonHashtable -LiteralPath $ConfigPath
            $config = Convert-WinCareConfigToCurrent -Config $loaded
            $null   = Test-WinCareConfigObject -Config $config
        } catch {
            $configError = $_.Exception.Message
            if ($ReadOnlyLocked) {
                $warnMsg = "WinCare: settings.json is invalid and could not be repaired (read-only mode). Using safe defaults. Error: $configError"
                $InitializationWarnings.Add($warnMsg)
                Write-Warning $warnMsg
            } else {
                $corrupt = "$ConfigPath.corrupt.$([datetime]::UtcNow.ToString('yyyyMMddHHmmssfff')).$([guid]::NewGuid().ToString('N'))"
                $null = Assert-WinCareSafePath -LiteralPath $ConfigPath
                $null = Assert-WinCareSafePath -LiteralPath $corrupt -AllowMissing
                [IO.File]::Move($ConfigPath, $corrupt, $false)
                Write-Warning "WinCare: settings.json was invalid and has been quarantined to '$corrupt'. Using safe defaults. Error: $configError"
            }
            $config = Get-WinCareDefaultConfig
        }
    }
    return $config
}

function Initialize-WinCareState {
    [CmdletBinding()]
    param([switch]$Ascii,[switch]$ReadOnly,[string]$Theme='Normal',[switch]$SkipConfigSave)
    $root=Get-WinCareDataRoot
    $effectivePolicy=Get-WinCareEffectivePolicy
    $readOnlyRequested=$PSBoundParameters.ContainsKey('ReadOnly') -and [bool]$ReadOnly
    $readOnlyLocked=$readOnlyRequested -or [bool]$effectivePolicy.ReadOnly
    $initializationWarnings=[Collections.Generic.List[string]]::new()
    $stateDirectories=@(
        'Logs','Journal','Backups','Reports','Quarantine','Cache','Extensions','Automation',
        'Broker','Provisioning','Maintenance','Widgets','Customization','Metrics','Playbooks',
        'Images','SecurityMaintenance','NetworkExperiments','Instrumentation','PowerSessions',
        'ColorStudio','Notes','RemoteSupport','Browser','Downloads','TelemetrySnapshots',
        'Launcher','GameState','WorkspaceLayouts','ContainerLogs','WorkspaceStudio'
    )
    if(-not $readOnlyLocked) {
        foreach($name in $stateDirectories) {
            $null=New-Item -ItemType Directory -Path (Join-Path $root $name) -Force
        }
    }
    $dataIdentityPath = $null
    if (Test-Path -LiteralPath $root -PathType Container) {
        $dataIdentityPath = Initialize-WinCareDataRootIdentity -Root $root -VerifyOnly:$readOnlyLocked
    } elseif (-not $readOnlyLocked) {
        throw 'WinCare data root was not created successfully.'
    }
    $configPath = Join-Path $root 'settings.json'
    $config     = Get-WinCareInitialConfig -ConfigPath $configPath -ReadOnlyLocked $readOnlyLocked -InitializationWarnings $initializationWarnings
    $persisted=[ordered]@{}
    foreach($key in $config.Keys) { $persisted[$key]=$config[$key] }
    $script:WinCareState=@{
        Root=$root
        DataIdentityPath=$dataIdentityPath
        ConfigPath=$configPath
        Config=$config
        PersistedConfig=$persisted
        InitializationWarnings=@($initializationWarnings)
        SessionOverrides=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        ReadOnlyLocked=$readOnlyLocked
        IsAdmin=Test-WinCareAdministrator
        IsWindows=$IsWindows
        StartedAt=[datetime]::UtcNow
        SessionId=[guid]::NewGuid().ToString('N')
        Capabilities=@{}
        Navigation=@{}
        StatusBadges=@{}
        Policy=$effectivePolicy
    }
    if($PSBoundParameters.ContainsKey('Ascii')) {
        $script:WinCareState.Config.Ascii=[bool]$Ascii
        $null=$script:WinCareState.SessionOverrides.Add('Ascii')
    }
    if($PSBoundParameters.ContainsKey('ReadOnly')) {
        $script:WinCareState.Config.ReadOnly=[bool]$ReadOnly
        $null=$script:WinCareState.SessionOverrides.Add('ReadOnly')
    }
    if($PSBoundParameters.ContainsKey('Theme')) {
        $script:WinCareState.Config.Theme=$Theme
        $null=$script:WinCareState.SessionOverrides.Add('Theme')
    }
    if($readOnlyLocked) {
        $script:WinCareState.Config.ReadOnly=$true
        $script:WinCareState.ReadOnlyLocked=$true
    }
    Initialize-WinCareCapabilities
    if(-not $readOnlyLocked -and (Get-Command Repair-WinCareInterruptedOperation -ErrorAction SilentlyContinue)) {
        $script:WinCareState.InterruptedOperations=@(Repair-WinCareInterruptedOperation)
    } else {
        $script:WinCareState.InterruptedOperations=@()
    }
    if(-not $SkipConfigSave -and -not $readOnlyLocked) { Save-WinCareConfig }
}

function Ensure-WinCareState { if (-not $script:WinCareState.ContainsKey('Root')) { Initialize-WinCareState } }

function Save-WinCareConfig {
    [CmdletBinding()]param()
    if ($script:WinCareState.ReadOnlyLocked) { return }
    if (-not $script:WinCareState.ConfigPath) { return }
    $toSave=[ordered]@{}
    foreach ($key in $script:WinCareState.Config.Keys) {
        if ($script:WinCareState.SessionOverrides.Contains($key) -and $script:WinCareState.PersistedConfig.Contains($key)) {$toSave[$key]=$script:WinCareState.PersistedConfig[$key]} else {$toSave[$key]=$script:WinCareState.Config[$key]}
    }
    $null=Test-WinCareConfigObject -Config $toSave
    Write-WinCareAtomicJson -LiteralPath $script:WinCareState.ConfigPath -InputObject $toSave
}

function Get-WinCareConfig { [CmdletBinding()]param([string]$Name); if ($Name) {return $script:WinCareState.Config[$Name]}; return $script:WinCareState.Config }
function Get-WinCarePolicy { [CmdletBinding()]param([string]$Name); if ($Name) {return $script:WinCareState.Policy[$Name]}; return $script:WinCareState.Policy }

function Set-WinCareConfig {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Name,[Parameter(Mandatory)][object]$Value)
    $defaults=Get-WinCareDefaultConfig
    if (-not $defaults.Contains($Name)) { throw "Unknown configuration key: $Name" }
    if ($Name -eq 'ReadOnly' -and $script:WinCareState.ReadOnlyLocked -and -not [bool]$Value) { return $false }
    $candidate=[ordered]@{}; foreach ($key in $script:WinCareState.Config.Keys) {$candidate[$key]=$script:WinCareState.Config[$key]}; $candidate[$Name]=$Value
    $null=Test-WinCareConfigObject -Config $candidate
    $script:WinCareState.Config=$candidate; $script:WinCareState.PersistedConfig[$Name]=$Value
    $null=$script:WinCareState.SessionOverrides.Remove($Name); Save-WinCareConfig
    foreach ($cacheKey in @('cleanup-targets','apps:False','apps:True','catalog')) {$null=$script:WinCareState.Remove($cacheKey)}
    return $true
}

function Reset-WinCareConfig {
    [CmdletBinding()]param()
    $defaults=Get-WinCareDefaultConfig; $persisted=[ordered]@{}; foreach ($key in $defaults.Keys) {$persisted[$key]=$defaults[$key]}
    $script:WinCareState.Config=$defaults; $script:WinCareState.PersistedConfig=$persisted; $script:WinCareState.SessionOverrides.Clear()
    if ($script:WinCareState.ReadOnlyLocked) {$script:WinCareState.Config.ReadOnly=$true;$null=$script:WinCareState.SessionOverrides.Add('ReadOnly')}
    Save-WinCareConfig
}

function Initialize-WinCareCapabilities {
    [CmdletBinding()]param()
    $commands=@('winget.exe','Get-AppxPackage','Remove-AppxPackage','Reset-AppxPackage','Get-ComputerInfo','Get-MpComputerStatus','Start-MpScan','Update-MpSignature','Get-NetFirewallProfile','Get-Tpm','Confirm-SecureBootUEFI','Clear-RecycleBin','Delete-DeliveryOptimizationCache','Get-ScheduledTask','Get-CimInstance','Get-NetTCPConnection','Get-NetAdapter','Get-WindowsOptionalFeature','Get-WindowsCapability','Enable-WindowsOptionalFeature','Disable-WindowsOptionalFeature','Add-WindowsCapability','Remove-WindowsCapability','Get-CIPolicy','ConvertFrom-CIPolicy','Get-ProcessMitigation','Get-WinEvent','Get-BitLockerVolume','Get-Clipboard','Set-Clipboard','Get-AuthenticodeSignature','Register-ScheduledTask','Get-PnpDevice','Get-PnpDeviceProperty','Get-WindowsImage','Get-WindowsPackage','Get-WindowsDriver','Add-WindowsPackage','Add-WindowsDriver','Remove-WindowsDriver','Get-LocalUser','Get-LocalGroupMember','Get-MpPreference','Set-MpPreference','New-ScheduledTaskTrigger','New-ScheduledTaskAction','New-ScheduledTaskSettingsSet')
    foreach ($name in $commands) {$script:WinCareState.Capabilities[$name]=[bool](Get-Command $name -ErrorAction SilentlyContinue)}
    foreach ($exe in @('CiTool.exe','bcdedit.exe','reagentc.exe','netsh.exe','powercfg.exe','pnputil.exe','dism.exe','sfc.exe','wpr.exe','logman.exe')) {$script:WinCareState.Capabilities[$exe]=[bool](Get-Command $exe -ErrorAction SilentlyContinue)}
    $script:WinCareState.Capabilities.Windows=$IsWindows
    $script:WinCareState.Capabilities.VT=[bool](Get-WinCarePropertyValue $Host.UI 'SupportsVirtualTerminal' $false)
    try {$script:WinCareState.Capabilities.Online=Test-WinCareInternetConnection} catch {$script:WinCareState.Capabilities.Online=$false}
}

function Test-WinCareCapability { [CmdletBinding()]param([Parameter(Mandatory)][string]$Name); return [bool]$script:WinCareState.Capabilities[$Name] }

function Protect-WinCareConfigVault {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ConfigVaultPath)
    $safe = Assert-WinCareSafePath -LiteralPath $ConfigVaultPath -AllowMissing
    $tpmEnforced = $false
    try {
        $tpm = Get-CimInstance Win32_Tpm -ErrorAction SilentlyContinue
        if (-not $tpm -and $IsWindows) {
            $tpm = Get-CimInstance -Namespace 'root\cimv2\Security\MicrosoftTpm' -ClassName Win32_Tpm -ErrorAction SilentlyContinue
        }
        if ($tpm) {
            if ($null -ne $tpm.IsEnabled_InitialValue) {
                $tpmEnforced = [bool]$tpm.IsEnabled_InitialValue
            } elseif ($tpm.PSObject.Properties['IsEnabled']) {
                $tpmEnforced = [bool]$tpm.IsEnabled
            } else {
                $tpmEnforced = $true
            }
        }
    } catch {
        $tpmEnforced = $false
    }
    $vaultEncrypted = $false
    if (Test-Path -LiteralPath $safe) {
        $aclValid = $true
        if ($IsWindows) {
            try {
                $acl = Get-Acl -LiteralPath $safe -ErrorAction SilentlyContinue
                if ($acl) {
                    foreach ($rule in $acl.Access) {
                        if ($rule.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Allow) {
                            $hasFullControl = ($rule.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::FullControl) -eq [System.Security.AccessControl.FileSystemRights]::FullControl
                            if ($hasFullControl) {
                                $identity = [string]$rule.IdentityReference.Value
                                $isPrivileged = ($identity -match '(?i)SYSTEM|Administrators|S-1-5-18|S-1-5-32-544|S-1-5-500') -or
                                                ($acl.Owner -and $identity -eq $acl.Owner)
                                if (-not $isPrivileged) {
                                    $aclValid = $false
                                    break
                                }
                            }
                        }
                    }
                }
            } catch {
                $aclValid = $false
            }
        }
        if ($aclValid) {
            $item = Get-Item -LiteralPath $safe -Force -ErrorAction SilentlyContinue
            if ($item) {
                if (($item.Attributes -band [System.IO.FileAttributes]::Encrypted) -ne 0) {
                    $vaultEncrypted = $true
                } elseif (-not $item.PSIsContainer) {
                    try {
                        $bytes = Read-WinCareBoundedFileBytes -LiteralPath $safe
                        if ($bytes.Length -ge 8) {
                            if ($bytes[0] -eq 0x01 -and $bytes[1] -eq 0x00 -and $bytes[2] -eq 0x00 -and $bytes[3] -eq 0x00 -and
                                $bytes[4] -eq 0xD0 -and $bytes[5] -eq 0x8C -and $bytes[6] -eq 0x9D -and $bytes[7] -eq 0xDF) {
                                $vaultEncrypted = $true
                            }
                        }
                        if (-not $vaultEncrypted -and $bytes.Length -gt 0) {
                            $maxLen = [Math]::Min($bytes.Length, 512)
                            $headerText = [System.Text.Encoding]::UTF8.GetString($bytes, 0, $maxLen)
                            if ($headerText -match '(?i)DPAPI|ENCRYPTED|ZeroTrustVault|AQAAANCMnd8') {
                                $vaultEncrypted = $true
                            }
                        }
                    } catch {
                        $vaultEncrypted = $false
                    }
                }
            }
        }
    }
    [pscustomobject]@{
        ConfigVaultPath = $safe
        TpmBindingEnforced = [bool]$tpmEnforced
        ZeroTrustVaultEncrypted = [bool]$vaultEncrypted
        AuditedAt = [datetime]::UtcNow.ToString('o')
        EvidenceType = 'ZeroTrustConfigVaultProtection'
    }
}
