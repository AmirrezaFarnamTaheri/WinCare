function Get-WinCareActionContractTable {
    [CmdletBinding()]
    param()
    if($script:WinCareState.ContainsKey('ActionContracts')){return $script:WinCareState.ActionContracts}
    $table=[ordered]@{}
    function Add-Contract([string]$Type,[string[]]$Required,[string[]]$Allowed,[string]$MinimumRisk='Low',[bool]$AlwaysAdmin=$false,[bool]$ElevationAllowed=$true,[int]$MaximumTimeout=21600){
        $table[$Type]=[pscustomobject]@{Type=$Type;Required=@($Required);Allowed=@($Allowed);MinimumRisk=$MinimumRisk;AlwaysAdmin=$AlwaysAdmin;ElevationAllowed=$ElevationAllowed;MaximumTimeout=$MaximumTimeout}
    }
    Add-Contract 'UninstallRegistryApp' @('ApplicationId','Name','FilePath','Arguments','SuccessExitCodes') @('ApplicationId','Name','FilePath','Arguments','SuccessExitCodes') 'Moderate' $false $true
    Add-Contract 'RemoveAppxPackage' @('PackageFullName') @('PackageFullName','Name') 'Moderate' $false $true
    Add-Contract 'RemoveAppxPackageAllUsers' @('PackageFullName','Name') @('PackageFullName','Name') 'Critical' $true $true 21600
    Add-Contract 'ResetAppxPackage' @('PackageFullName') @('PackageFullName','Name') 'High' $false $true
    Add-Contract 'RepairApplication' @('Name','FilePath','Arguments','SuccessExitCodes') @('Name','FilePath','Arguments','SuccessExitCodes') 'Moderate' $false $true
    Add-Contract 'DeleteCleanupTarget' @('Id','Name','Path','Pattern','MinimumAgeHours','Kind') @('Id','Name','Path','Pattern','MinimumAgeHours','Kind') 'Low' $false $true
    Add-Contract 'ClearDeliveryOptimization' @('Id','Name','Path','Pattern','MinimumAgeHours','Kind') @('Id','Name','Path','Pattern','MinimumAgeHours','Kind') 'Moderate' $true $true
    Add-Contract 'ClearRecycleBin' @() @('Drive') 'Moderate' $false $true
    Add-Contract 'ClearWindowsUpdateDownloadCache' @() @() 'Critical' $true $true 900
    Add-Contract 'StartComponentCleanup' @() @() 'High' $true $true 21600
    Add-Contract 'ResetStoreCache' @() @() 'Low' $false $true
    Add-Contract 'SetStorageSense' @('Enable','Previous') @('Enable','Previous') 'Moderate' $false $true
    Add-Contract 'DisableStartupItem' @('Id','Name','Command','Source','Location','Scope') @('Id','Name','Command','Source','Location','FilePath','Scope','ValueKind') 'Moderate' $false $true
    Add-Contract 'EnableStartupItem' @('Name','Scope','BackupPath') @('Name','Scope','BackupPath') 'Moderate' $false $true
    Add-Contract 'RunHealthCommand' @('Operation','FilePath','Arguments','Timeout') @('Operation','FilePath','Arguments','Timeout') 'ReadOnly' $true $true 21600
    Add-Contract 'RunDefenderScan' @('ScanType') @('ScanType','Path') 'Moderate' $true $true 86400
    Add-Contract 'UpdateDefenderSignatures' @() @() 'Low' $true $true 3600
    Add-Contract 'UpgradeWingetPackage' @('Arguments','Description') @('Arguments','Description') 'Moderate' $false $true 21600
    Add-Contract 'InstallWingetPackage' @('PackageId') @('PackageId','Version','Scope') 'Moderate' $false $true 21600
    Add-Contract 'MoveFile' @('Source','Destination','ExpectedSourceHash','AllowedRoots') @('Source','Destination','ExpectedSourceHash','AllowedRoots') 'Moderate' $false $true
    Add-Contract 'DeleteAdmittedCleanupFiles' @('CatalogPath','CatalogSha256','Entries','AllowedRoots','MaximumBytes') @('CatalogPath','CatalogSha256','Entries','AllowedRoots','MaximumBytes') 'High' $false $true 3600
    Add-Contract 'RelocateDirectoryWithJunction' @('ProfileId','Source','Destination','ExpectedSourceHash','ProcessNames','AllowedRoots','MaximumBytes') @('ProfileId','Source','Destination','ExpectedSourceHash','ProcessNames','AllowedRoots','MaximumBytes') 'High' $false $true 21600
    Add-Contract 'RestoreRelocatedDirectory' @('Source','Destination','ExpectedDestinationHash','ProcessNames','AllowedRoots','MaximumBytes') @('Source','Destination','ExpectedDestinationHash','ProcessNames','AllowedRoots','MaximumBytes') 'High' $false $true 21600
    Add-Contract 'RunProcess' @('FilePath','Arguments') @('FilePath','Arguments','SuccessExitCodes') 'Moderate' $false $true 21600
    Add-Contract 'SetRegistryValue' @('Path','Name','Value') @('Path','Name','Value','ValueType','Before') 'Low' $false $true
    Add-Contract 'RemoveRegistryValue' @('Path','Name') @('Path','Name','Before') 'Low' $false $true
    Add-Contract 'ApplyRegistryTree' @('Path','Values','DeleteValues','DeleteSubKeys','ExpectedSnapshotHash') @('Path','Values','DeleteValues','DeleteSubKeys','ExpectedSnapshotHash') 'Moderate' $false $true
    Add-Contract 'RemoveRegistryTree' @('Path','ExpectedSnapshotHash') @('Path','ExpectedSnapshotHash') 'High' $false $true
    Add-Contract 'RestoreRegistryTree' @('Path','Snapshot','ExpectedCurrentHash') @('Path','Snapshot','ExpectedCurrentHash') 'Moderate' $false $true
    Add-Contract 'SetServiceStartMode' @('Name','StartMode') @('Name','StartMode','Before') 'High' $true $true
    Add-Contract 'SetServiceRuntimeState' @('Name','State') @('Name','State','Before') 'High' $true $true 300
    Add-Contract 'SetScheduledTaskState' @('TaskPath','TaskName','Enabled') @('TaskPath','TaskName','Enabled','Before') 'Moderate' $false $true
    Add-Contract 'SetOptionalFeatureState' @('Name','State') @('Name','State','Before') 'Moderate' $true $true 21600
    Add-Contract 'SetCapabilityState' @('Name','State') @('Name','State','Before') 'Moderate' $true $true 21600
    Add-Contract 'RemoveProvisionedAppx' @('PackageName') @('PackageName') 'High' $true $true 21600
    Add-Contract 'SetPowerScheme' @('Scheme') @('Scheme','Before') 'Low' $false $true
    Add-Contract 'SetHostsEntries' @('BlockId','Enabled','Entries') @('BlockId','Enabled','Entries','BeforeHash') 'High' $true $true
    Add-Contract 'SetFirewallRuleState' @('Name','Enabled') @('Name','Enabled','Before') 'High' $true $true
    Add-Contract 'SetFirewallProfileState' @('Profiles','Enabled') @('Profiles','Enabled','Before') 'High' $true $true
    Add-Contract 'SetLocalUserState' @('Name','Enabled') @('Name','Enabled','Before') 'High' $true $true
    Add-Contract 'SetDefenderPreference' @('Name','Value') @('Name','Value','Before') 'Low' $true $true
    Add-Contract 'SetProcessTuning' @('ProcessId','Priority') @('ProcessId','Priority','AffinityMask','ProcessStartTime') 'Moderate' $false $true
    Add-Contract 'TrimProcessWorkingSet' @('ProcessId','ProcessStartTime','MinimumWorkingSetBytes') @('ProcessId','ProcessStartTime','MinimumWorkingSetBytes') 'Low' $false $false 30
    Add-Contract 'ResetNetworkAdapter' @('InterfaceName','InterfaceGuid','BeforeStatus') @('InterfaceName','InterfaceGuid','BeforeStatus') 'High' $true $true 120
    Add-Contract 'SetMasterVolume' @('Percent') @('Percent','Mute') 'Low' $false $false
    Add-Contract 'SetBrightness' @('Percent') @('Percent','InstanceName') 'Low' $false $false
    Add-Contract 'ClearClipboard' @() @() 'Moderate' $false $false
    Add-Contract 'WuaSetHidden' @('UpdateId','Hidden') @('UpdateId','Hidden') 'Moderate' $true $true
    Add-Contract 'WuaDownload' @('UpdateIds') @('UpdateIds') 'Moderate' $true $true 21600
    Add-Contract 'WuaInstall' @('UpdateIds') @('UpdateIds') 'High' $true $true 43200
    Add-Contract 'WuaUninstall' @('UpdateIds') @('UpdateIds') 'Critical' $true $true 43200
    Add-Contract 'DeployWdacPolicy' @('Path','PolicyId','Sha256','Enforced') @('Path','PolicyId','Sha256','Enforced') 'High' $true $true 3600
    Add-Contract 'RemoveWdacPolicy' @('PolicyId') @('PolicyId') 'Critical' $true $true 3600
    Add-Contract 'SetShellExtensionState' @('RegistryPath','Enabled') @('RegistryPath','Enabled','Before') 'Moderate' $false $true
    Add-Contract 'BcdExport' @('Destination') @('Destination') 'Moderate' $true $true 600
    Add-Contract 'RestoreRegistryBackup' @('Path','Name','Exists') @('Path','Name','Exists','Value','ValueType') 'Low' $false $true
    Add-Contract 'RegisterAutomationTask' @('Profile') @('Profile') 'Moderate' $false $true
    Add-Contract 'RemoveAutomationTask' @('Profile') @('Profile') 'Moderate' $false $true
    Add-Contract 'InstallDeclarativeExtension' @('ArchivePath','ArchiveSha256','ExtensionId') @('ArchivePath','ArchiveSha256','ExtensionId') 'High' $false $false
    Add-Contract 'RemoveDeclarativeExtension' @('ExtensionId') @('ExtensionId') 'Moderate' $false $false
    Add-Contract 'UpsertMaintenanceWindow' @('Window','ExpectedRevision','ExpectedRecordHash') @('Window','ExpectedRevision','ExpectedRecordHash') 'Moderate' $false $false
    Add-Contract 'SetMaintenanceWindowState' @('Id','State','ExpectedRevision','ExpectedRecordHash') @('Id','State','Message','ExpectedRevision','ExpectedRecordHash') 'Moderate' $false $false
    Add-Contract 'RestoreMaintenanceWindowRecord' @('Id','Exists','Record','ExpectedCurrentHash') @('Id','Exists','Record','ExpectedCurrentHash') 'Moderate' $false $false
    Add-Contract 'OfflineImageAddDriver' @('ImagePath','DriverPath','Recurse','ForceUnsigned') @('ImagePath','DriverPath','Recurse','ForceUnsigned') 'High' $true $true 21600
    Add-Contract 'OfflineImageRemoveDriver' @('ImagePath','PublishedName') @('ImagePath','PublishedName') 'High' $true $true 21600
    Add-Contract 'OfflineImageRemoveDriverBatch' @('ImagePath','PublishedNames') @('ImagePath','PublishedNames') 'High' $true $true 21600
    Add-Contract 'OfflineImageAddPackage' @('ImagePath','PackagePath','IgnoreCheck') @('ImagePath','PackagePath','IgnoreCheck') 'Critical' $true $true 21600
    Add-Contract 'OfflineImageSetFeature' @('ImagePath','FeatureName','State','BeforeState') @('ImagePath','FeatureName','State','BeforeState') 'High' $true $true 21600
    Add-Contract 'WriteManagedFile' @('Path','ContentBase64','ExpectedBeforeHash','AllowedRoots') @('Path','ContentBase64','ExpectedBeforeHash','AllowedRoots') 'High' $false $true
    Add-Contract 'RemoveManagedFile' @('Path','ExpectedBeforeHash','AllowedRoots') @('Path','ExpectedBeforeHash','AllowedRoots') 'High' $false $true
    Add-Contract 'ImportLocalGroupPolicyBackup' @('BackupPath','BackupHash','LgpoPath','LgpoHash') @('BackupPath','BackupHash','LgpoPath','LgpoHash') 'Critical' $true $true 21600
    Add-Contract 'ConfigureSysmon' @('ExecutablePath','ExecutableHash','ConfigPath','ConfigHash','Mode','RollbackConfigPath','RollbackConfigHash') @('ExecutablePath','ExecutableHash','ConfigPath','ConfigHash','Mode','RollbackConfigPath','RollbackConfigHash') 'High' $true $true 3600
    Add-Contract 'UninstallSysmon' @('ExecutablePath','ExecutableHash') @('ExecutablePath','ExecutableHash') 'Critical' $true $true 3600
    Add-Contract 'SetPageFileConfiguration' @('Mode','Settings','BeforeHash') @('Mode','Settings','BeforeHash') 'High' $true $true 600
    Add-Contract 'RestorePageFileConfiguration' @('Snapshot','ExpectedCurrentHash') @('Snapshot','ExpectedCurrentHash') 'High' $true $true 600
    Add-Contract 'SetSecurityControlState' @('Control','Enabled','DurationMinutes','Reason','EnvironmentClass','BeforeHash') @('Control','Enabled','DurationMinutes','Reason','EnvironmentClass','BeforeHash') 'High' $true $true 900
    Add-Contract 'RestoreSecurityControlState' @('RecordId','Control','Snapshot','ExpectedCurrentHash') @('RecordId','Control','Snapshot','ExpectedCurrentHash') 'High' $true $true 900
    Add-Contract 'SetLegacySecurityControlState' @('Control','Enabled','BeforeHash','ProfileId') @('Control','Enabled','BeforeHash','ProfileId') 'Critical' $true $true 900
    Add-Contract 'RestoreLegacySecurityControlState' @('Control','Snapshot','ExpectedCurrentHash') @('Control','Snapshot','ExpectedCurrentHash') 'High' $true $true 900
    Add-Contract 'ResetDisplayOverrides' @('BeforeHash','DevicePaths') @('BeforeHash','DevicePaths') 'Critical' $true $true 600
    Add-Contract 'RestoreDisplayOverrides' @('Snapshot','ExpectedCurrentHash') @('Snapshot','ExpectedCurrentHash') 'High' $true $true 600
    Add-Contract 'SetHibernateState' @('Enabled','Before') @('Enabled','Before') 'High' $true $true 120
    Add-Contract 'RunNetworkExperiment' @('Setting','Value','BeforeHash','BeforeValue','TargetHosts','SampleCount','TimeoutMilliseconds','MinimumImprovementPercent','MaximumRegressionPercent','Baseline') @('Setting','Value','BeforeHash','BeforeValue','TargetHosts','SampleCount','TimeoutMilliseconds','MinimumImprovementPercent','MaximumRegressionPercent','Baseline') 'High' $true $true 1800
    Add-Contract 'RestoreTcpGlobalSetting' @('Setting','Value','ExpectedCurrentValue') @('Setting','Value','ExpectedCurrentValue') 'High' $true $true 600
    Add-Contract 'CaptureEtwTrace' @('Profile','DurationSeconds','Destination','ProcessId','ProcessModules') @('Profile','DurationSeconds','Destination','ProcessId','ProcessModules') 'Moderate' $true $true 3900
    Add-Contract 'QuarantineInjectionSurface' @('SurfaceId','ExpectedHash') @('SurfaceId','ExpectedHash') 'Critical' $true $true 600
    Add-Contract 'RestoreInjectionSurface' @('RecordId','ExpectedCurrentHash') @('RecordId','ExpectedCurrentHash') 'Critical' $true $true 600
    Add-Contract 'StartPowerSession' @('Id','Mode','PreventDisplay','AwayMode','ExpiresAt','RefreshSeconds','Reason') @('Id','Mode','PreventDisplay','AwayMode','ExpiresAt','RefreshSeconds','Reason') 'Low' $false $false 120
    Add-Contract 'StopPowerSession' @('Id','ExpectedHostProcessId','ExpectedHostProcessStartTime') @('Id','ExpectedHostProcessId','ExpectedHostProcessStartTime') 'Low' $false $false 60
    Add-Contract 'SetWindowBounds' @('WindowHandle','ProcessId','ProcessStartTime','ExpectedTitleHash','ExpectedCurrentLeft','ExpectedCurrentTop','ExpectedCurrentWidth','ExpectedCurrentHeight','Left','Top','Width','Height') @('WindowHandle','ProcessId','ProcessStartTime','ExpectedTitleHash','ExpectedCurrentLeft','ExpectedCurrentTop','ExpectedCurrentWidth','ExpectedCurrentHeight','Left','Top','Width','Height','MonitorDevice','ExpectedMonitorWorkLeft','ExpectedMonitorWorkTop','ExpectedMonitorWorkWidth','ExpectedMonitorWorkHeight') 'Low' $false $false 60
    Add-Contract 'SetWindowTopmost' @('WindowHandle','ProcessId','ProcessStartTime','ExpectedTitleHash','ExpectedCurrentTopmost','Topmost') @('WindowHandle','ProcessId','ProcessStartTime','ExpectedTitleHash','ExpectedCurrentTopmost','Topmost') 'Low' $false $false 60
    Add-Contract 'ActivateWindow' @('WindowHandle','ProcessId','ProcessStartTime','ExpectedTitleHash') @('WindowHandle','ProcessId','ProcessStartTime','ExpectedTitleHash') 'Low' $false $false 30
    Add-Contract 'ReleaseLocalInput' @() @() 'Low' $false $false 30
    Add-Contract 'ReplaceColorPaletteStore' @('Store','ExpectedCurrentHash') @('Store','ExpectedCurrentHash') 'Low' $false $false 60
    Add-Contract 'WriteLocalNote' @('Id','Metadata','BodyBase64','ExpectedBeforeHash') @('Id','Metadata','BodyBase64','ExpectedBeforeHash') 'Low' $false $false 60
    Add-Contract 'RemoveLocalNote' @('Id','ExpectedBeforeHash') @('Id','ExpectedBeforeHash') 'Moderate' $false $false 60
    Add-Contract 'RestoreLocalNote' @('Id','Snapshot','ExpectedCurrentHash') @('Id','Snapshot','ExpectedCurrentHash') 'Low' $false $false 60
    Add-Contract 'ReplaceRemoteConsentStore' @('Store','ExpectedCurrentHash') @('Store','ExpectedCurrentHash') 'Moderate' $false $false 60
    Add-Contract 'TerminateRemoteSupportProcess' @('ProcessId','Name','StartTime','Path','Sha256') @('ProcessId','Name','StartTime','Path','Sha256') 'High' $false $false 60
    Add-Contract 'ReplaceDownloadStore' @('Store','ExpectedHash') @('Store','ExpectedHash') 'Low' $false $false 60
    Add-Contract 'StartDownloadTransfer' @('Id','ExpectedRecordHash') @('Id','ExpectedRecordHash') 'Low' $false $false 120
    Add-Contract 'SetDownloadTransferState' @('Id','State','ExpectedRecordHash') @('Id','State','ExpectedRecordHash') 'Low' $false $false 120
    Add-Contract 'ReconcileDownloadTransfer' @('Id','ExpectedRecordHash') @('Id','ExpectedRecordHash') 'Low' $false $false 300
    Add-Contract 'CancelDownloadTransfer' @('Id','ExpectedRecordHash') @('Id','ExpectedRecordHash') 'Moderate' $false $false 120
    Add-Contract 'CaptureTelemetrySeries' @('Name','DurationSeconds','IntervalSeconds','TopProcessCount','ExpectedHistoryHash') @('Name','DurationSeconds','IntervalSeconds','TopProcessCount','ExpectedHistoryHash') 'ReadOnly' $false $false 3900
    Add-Contract 'OpenLaunchTarget' @('Id','Kind','Target','ExpectedHash') @('Id','Kind','Target','ExpectedHash') 'Low' $false $false 60
    Add-Contract 'CreateSteamCloudBackup' @('UserId','AppId','SourceRoot','Destination','ExpectedSnapshotHash') @('UserId','AppId','SourceRoot','Destination','ExpectedSnapshotHash') 'Low' $false $false 21600
    Add-Contract 'RemoveGameStateBackup' @('BackupPath','ExpectedSha256') @('BackupPath','ExpectedSha256') 'Low' $false $false 300
    Add-Contract 'RestoreSteamCloudBackup' @('BackupPath','BackupSha256','TargetRoot','ExpectedCurrentSnapshotHash') @('BackupPath','BackupSha256','TargetRoot','ExpectedCurrentSnapshotHash') 'High' $false $false 21600
    Add-Contract 'OfflineImageRemoveProvisionedAppx' @('ImagePath','PackageName','DisplayName') @('ImagePath','PackageName','DisplayName') 'High' $true $true 21600
    Add-Contract 'OfflineImageStartComponentCleanup' @('ImagePath','ResetBase') @('ImagePath','ResetBase') 'High' $true $true 21600
    Add-Contract 'ReplaceWorkspaceLayoutStore' @('Store','ExpectedCurrentHash') @('Store','ExpectedCurrentHash') 'Low' $false $false 60
    Add-Contract 'SetFolderAppearance' @('Path','IconResource','IconResourceSha256','LocalizedName','Before','ExpectedBeforeHash','ExpectedAfterHash') @('Path','IconResource','IconResourceSha256','LocalizedName','Before','ExpectedBeforeHash','ExpectedAfterHash') 'Moderate' $false $false 120
    Add-Contract 'RestoreFolderAppearance' @('Snapshot','ExpectedCurrentHash') @('Snapshot','ExpectedCurrentHash') 'Moderate' $false $false 120
    Add-Contract 'RunVerifiedPeerTool' @('ToolId','FilePath','Sha256','Arguments','Mutation','SuccessExitCodes') @('ToolId','FilePath','Sha256','Arguments','Mutation','SuccessExitCodes','ExpectedInputSha256') 'Moderate' $false $true 3600
    Add-Contract 'SetMonitorVcpFeature' @('DeviceName','PhysicalIndex','Description','FeatureCode','Value','ExpectedCurrent','ExpectedMaximum') @('DeviceName','PhysicalIndex','Description','FeatureCode','Value','ExpectedCurrent','ExpectedMaximum') 'Low' $false $false 60
    Add-Contract 'OpenExplorerLocation' @('Path','ExpectedPathHash') @('Path','ExpectedPathHash') 'Low' $false $false 60
    Add-Contract 'LaunchAppxApplication' @('Aumid','PackageFullName','ManifestSha256') @('Aumid','PackageFullName','ManifestSha256') 'Low' $false $false 60
    Add-Contract 'CreateSystemRestorePoint' @('Description','RestorePointType') @('Description','RestorePointType') 'Moderate' $true $true 300
    $script:WinCareState.ActionContracts=$table
    return $table
}

function Get-WinCareActionContract {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Type)
    $table=Get-WinCareActionContractTable
    if(-not $table.Contains($Type)){return $null}
    $table[$Type]
}
