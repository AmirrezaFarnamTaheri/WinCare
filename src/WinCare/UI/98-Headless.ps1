function Invoke-WinCareHeadlessPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Plan,[switch]$Apply)
    $preview=Invoke-WinCarePlan -Plan $Plan -PreviewOnly
    if(-not $Apply -or -not $preview.Success){return $preview}
    Invoke-WinCarePlan -Plan $Plan -Approved
}

function Get-WinCareHeadlessCommandName {
    @(
        'system','applications','cleanup-targets','storage','startup','health','security','network','catalog','presets','preset',
        'pagefile','pagefile-recommendation','pagefile-set','security-controls','security-maintenance','security-control-reduce','security-control-restore',
        'tcp-global','network-measure','network-experiments','network-experiment','injection-surfaces','process-modules','remote-thread-events','etw-sessions','etw-capture','injection-surface-quarantine',
        'wua-search','wua-history','wua-hide','wua-unhide','wua-download','wua-install','wua-uninstall',
        'wdac-policies','wdac-events','wdac-deploy','internals-processes','internals-memory','internals-cpu','credential-providers','appcontainer',
        'desktop-controls','wifi-profiles','shell-extensions','desktop-shortcuts','boot','bcd-export','unattend-analyze','provisioning-plan',
        'knowledge','reports','automation-profiles','run-automation','cancel-operation',
        'widgets','widget-catalog','widget-export','bluetooth','bluetooth-events',
        'maintenance','maintenance-create','maintenance-transition','maintenance-metrics','maintenance-export',
        'context-menu','context-menu-set','customization-hosts','rainmeter-skins','windhawk-mods','explorerpatcher','modern-context-validate',
        'playbooks','playbook','group-policy','group-policy-import','sysmon','sysmon-configure','sysmon-uninstall',
        'offline-images','offline-drivers','offline-packages','offline-features','offline-driver-add','offline-driver-remove','offline-package-add','offline-feature-set',
        'power-sessions','power-start','power-stop','windows','monitors','window-zones','window-zone-set','window-topmost','window-activate','input-release',
        'downloads','downloads-due','download-create','download-start','download-start-due','download-suspend','download-resume','download-reconcile','download-cancel','download-remove',
        'telemetry-snapshot','telemetry-history','telemetry-capture','telemetry-export',
        'launcher-search','launcher-open','calculator','steam-games','steam-users','steam-cloud-files','steam-backup','steam-restore','game-integrity',
        'offline-reduction-profiles','offline-reduction-assess','offline-reduction-apply','workspace-layouts','workspace-layout-save','workspace-layout-apply','workspace-layout-remove',
        'color-capture','color-palette','color-add','color-remove','image-metadata','notes','note-save','note-remove',
        'browsers','browser-extensions','remote-support','remote-consent','remote-consent-create','remote-consent-state','remote-consent-expire','remote-emergency',
        'studio-snapshot','studio-monitoring-export','studio-file-workspaces','studio-file-workspace-save','studio-layout-profiles','studio-layout-save','studio-package','studio-brightness-schedules','studio-brightness-save','studio-brightness-apply','studio-folder-appearance','studio-kanata-validate','studio-syncthing','studio-wezterm','studio-adb-inventory','studio-xbox-fse',
        'toolkit-diagnostics','toolkit-win32-error','toolkit-msi','hardening-profiles','hardening-assess','hardening-apply','maintenance-templates','maintenance-template-create','system-shortcuts','system-shortcuts-export','download-batch','torrent-metadata',
        'experience-cards','experience-visual-asset','experience-sprite-layout','experience-visual-manifest','experience-location-state','experience-location-set','experience-remote-profiles','experience-remote-state','experience-remote-set','experience-power-profiles','experience-power-assess','experience-power-apply','experience-download-strategy','experience-privacy-profiles','experience-privacy-apply',
        'shell-hardware-cards','hardware-inventory','hardware-report','monitor-controls','monitor-control-set','window-search','explorer-session','explorer-session-save','explorer-session-records','explorer-session-restore','ui-automation-snapshot','appx-runtime','appx-launch-targets','appx-launch','archive-inspect','servicing-media','terminal-environment','terminal-export','gui-resource-audit','fullscreen-shell-assess',
        'cleaner-preview-cards','cleaner-disk-pressure','cleaner-disk-pressure-schedule','cleaner-winapp2','cleaner-winapp2-run','cleaner-relocation-profiles','cleaner-relocation-assess','cleaner-relocation','file-preview-profiles','file-preview','file-preview-export','preview-handlers',
        'peer-display-overrides','peer-display-reset','peer-wlan','peer-log-tail','peer-pe','peer-container-log-config','peer-taskboard','peer-task-save','explorer-quick','deep-clean-profiles','deep-clean','appx-selection-profiles','appx-selection-assess','appx-selection','offline-appx-selection','legacy-unsafe-profiles','legacy-unsafe'
    )
}
function Get-WinCareBrokerReadOnlyCommandName {
    @(
        'system','applications','cleanup-targets','storage','startup','health','security','network','catalog','presets','wua-search','wua-history',
        'pagefile','pagefile-recommendation','security-controls','security-maintenance','tcp-global','network-measure','network-experiments','injection-surfaces','process-modules','remote-thread-events','etw-sessions',
        'wdac-policies','wdac-events','internals-processes','internals-memory','internals-cpu','credential-providers','appcontainer','desktop-controls',
        'wifi-profiles','shell-extensions','desktop-shortcuts','boot','unattend-analyze','knowledge','reports','automation-profiles',
        'widgets','widget-catalog','bluetooth','bluetooth-events','maintenance','maintenance-metrics','context-menu','customization-hosts',
        'rainmeter-skins','windhawk-mods','explorerpatcher','modern-context-validate','playbooks','group-policy','sysmon',
        'offline-images','offline-drivers','offline-packages','offline-features',
        'power-sessions','windows','monitors','window-zones','color-capture','color-palette','image-metadata','browsers','browser-extensions','remote-support','remote-consent',
        'downloads','downloads-due','telemetry-snapshot','telemetry-history','launcher-search','calculator','steam-games','steam-users','steam-cloud-files','game-integrity','offline-reduction-profiles','offline-reduction-assess','workspace-layouts','studio-snapshot','studio-file-workspaces','studio-layout-profiles','studio-package','studio-brightness-schedules','studio-kanata-validate','studio-syncthing',
        'toolkit-diagnostics','toolkit-win32-error','toolkit-msi','hardening-profiles','hardening-assess','maintenance-templates','system-shortcuts','torrent-metadata',
        'experience-cards','experience-visual-asset','experience-sprite-layout','experience-location-state','experience-remote-profiles','experience-remote-state','experience-power-profiles','experience-power-assess','experience-download-strategy','experience-privacy-profiles',
        'shell-hardware-cards','hardware-inventory','monitor-controls','window-search','explorer-session','explorer-session-records','ui-automation-snapshot','appx-runtime','appx-launch-targets','archive-inspect','servicing-media','terminal-environment','gui-resource-audit','fullscreen-shell-assess',
        'cleaner-preview-cards','cleaner-winapp2','cleaner-relocation-profiles','cleaner-relocation-assess','file-preview-profiles','file-preview','preview-handlers',
        'peer-display-overrides','peer-wlan','peer-log-tail','peer-pe','peer-taskboard','deep-clean-profiles','appx-selection-profiles','appx-selection-assess','legacy-unsafe-profiles'
    )
}

function Invoke-WinCareHeadlessCommand {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Command,[hashtable]$Arguments=@{},[switch]$Apply,[switch]$JsonObject)
    if($Command -notin (Get-WinCareHeadlessCommandName)){throw "Unknown WinCare command: $Command"}
    $data=switch($Command){
        'system'{Get-WinCareSystemSummary}
        'applications'{Get-WinCareInstalledApplication -IncludeStoreApps:([bool](Get-WinCarePropertyValue $Arguments 'IncludeStoreApps' $true)) -Refresh:([bool](Get-WinCarePropertyValue $Arguments 'Refresh' $false))}
        'cleanup-targets'{Get-WinCareCleanupTarget -Refresh:([bool](Get-WinCarePropertyValue $Arguments 'Refresh' $false))}
        'storage'{Get-WinCareDriveSummary}
        'startup'{Get-WinCareStartupItem}
        'health'{Get-WinCareHealthFinding}
        'security'{Get-WinCareSecuritySummary}
        'network'{Get-WinCareNetworkSummary}
        'pagefile'{Get-WinCarePageFileConfiguration}
        'pagefile-recommendation'{Get-WinCarePageFileRecommendation}
        'pagefile-set'{$plan=New-WinCarePageFilePlan -Mode ([string]$Arguments.Mode) -Settings @(Get-WinCarePropertyValue $Arguments 'Settings' @());Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'security-controls'{$control=[string](Get-WinCarePropertyValue $Arguments 'Control' '');if([string]::IsNullOrWhiteSpace($control)){Get-WinCareSecurityControlState}else{Get-WinCareSecurityControlState -Control $control}}
        'security-maintenance'{Get-WinCareSecurityMaintenanceRecord -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' '')) -IncludeRestored:([bool](Get-WinCarePropertyValue $Arguments 'IncludeRestored' $false))}
        'security-control-reduce'{$plan=New-WinCareSecurityMaintenancePlan -Control ([string]$Arguments.Control) -DurationMinutes ([int](Get-WinCarePropertyValue $Arguments 'DurationMinutes' (Get-WinCareConfig 'SecurityMaintenanceDefaultMinutes'))) -Reason ([string]$Arguments.Reason) -EnvironmentClass ([string](Get-WinCarePropertyValue $Arguments 'EnvironmentClass' 'Workstation'));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'security-control-restore'{$plan=New-WinCareSecurityMaintenanceRestorePlan -RecordId ([string]$Arguments.RecordId);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'tcp-global'{Get-WinCareTcpGlobalState}
        'network-measure'{Measure-WinCareNetworkEndpoint -TargetHost @((Get-WinCarePropertyValue $Arguments 'TargetHosts' @('www.microsoft.com','www.cloudflare.com'))) -SampleCount ([int](Get-WinCarePropertyValue $Arguments 'SampleCount' (Get-WinCareConfig 'NetworkExperimentSampleCount'))) -TimeoutMilliseconds ([int](Get-WinCarePropertyValue $Arguments 'TimeoutMilliseconds' (Get-WinCareConfig 'NetworkExperimentTimeoutMilliseconds'))) -Port ([int](Get-WinCarePropertyValue $Arguments 'Port' 443))}
        'network-experiments'{Get-WinCareNetworkExperimentHistory -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 100))}
        'network-experiment'{$plan=New-WinCareNetworkExperimentPlan -Setting ([string]$Arguments.Setting) -Value ([string]$Arguments.Value) -TargetHost @((Get-WinCarePropertyValue $Arguments 'TargetHosts' @('www.microsoft.com','www.cloudflare.com'))) -SampleCount ([int](Get-WinCarePropertyValue $Arguments 'SampleCount' (Get-WinCareConfig 'NetworkExperimentSampleCount'))) -TimeoutMilliseconds ([int](Get-WinCarePropertyValue $Arguments 'TimeoutMilliseconds' (Get-WinCareConfig 'NetworkExperimentTimeoutMilliseconds'))) -MinimumImprovementPercent ([double](Get-WinCarePropertyValue $Arguments 'MinimumImprovementPercent' 0)) -MaximumRegressionPercent ([double](Get-WinCarePropertyValue $Arguments 'MaximumRegressionPercent' 5));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'injection-surfaces'{Get-WinCareInjectionSurface -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' (Get-WinCareConfig 'InstrumentationEventLimit')))}
        'process-modules'{Get-WinCareProcessModuleInventory -ProcessId ([int]$Arguments.ProcessId) -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 1000))}
        'remote-thread-events'{Get-WinCareRemoteThreadEvent -MaxEvents ([int](Get-WinCarePropertyValue $Arguments 'MaxEvents' (Get-WinCareConfig 'InstrumentationEventLimit')))}
        'etw-sessions'{Get-WinCareEtwSession}
        'etw-capture'{$nullablePid=if([int](Get-WinCarePropertyValue $Arguments 'ProcessId' 0) -gt 0){[Nullable[int]]([int]$Arguments.ProcessId)}else{[Nullable[int]]$null};$plan=New-WinCareEtwCapturePlan -Profile ([string](Get-WinCarePropertyValue $Arguments 'Profile' 'GeneralProfile')) -DurationSeconds ([int](Get-WinCarePropertyValue $Arguments 'DurationSeconds' 15)) -Destination ([string](Get-WinCarePropertyValue $Arguments 'Destination' '')) -ProcessId $nullablePid;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'injection-surface-quarantine'{$plan=New-WinCareInjectionSurfaceQuarantinePlan -SurfaceId ([string]$Arguments.SurfaceId);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'catalog'{Get-WinCareCatalog}
        'presets'{Get-WinCarePreset}
        'preset'{$plan=New-WinCarePresetPlan -PresetId ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'wua-search'{Get-WinCareWuaUpdate -Criteria ([string](Get-WinCarePropertyValue $Arguments 'Criteria' 'IsInstalled=0 and IsHidden=0')) -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 250))}
        'wua-history'{Get-WinCareWuaHistory -Count ([int](Get-WinCarePropertyValue $Arguments 'Count' 100))}
        'wua-hide'{$plan=New-WinCareWuaHiddenPlan -UpdateId @($Arguments.UpdateIds) -Hidden $true;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'wua-unhide'{$plan=New-WinCareWuaHiddenPlan -UpdateId @($Arguments.UpdateIds) -Hidden $false;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'wua-download'{$plan=New-WinCareWuaDownloadPlan -UpdateId @($Arguments.UpdateIds);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'wua-install'{$plan=New-WinCareWuaInstallPlan -UpdateId @($Arguments.UpdateIds);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'wua-uninstall'{$plan=New-WinCareWuaUninstallPlan -UpdateId @($Arguments.UpdateIds);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'wdac-policies'{Get-WinCareWdacActivePolicy}
        'wdac-events'{Get-WinCareCodeIntegrityEvent -MaxEvents ([int](Get-WinCarePropertyValue $Arguments 'MaxEvents' 100))}
        'wdac-deploy'{$plan=New-WinCareWdacDeployPlan -PolicyPath ([string]$Arguments.Path);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'internals-processes'{Get-WinCareProcessInternals -Top ([int](Get-WinCarePropertyValue $Arguments 'Top' 50))}
        'internals-memory'{Get-WinCareMemoryInternals}
        'internals-cpu'{Get-WinCareProcessorTopology}
        'credential-providers'{Get-WinCareCredentialProvider}
        'appcontainer'{Get-WinCareAppContainerCapability -PackageName ([string](Get-WinCarePropertyValue $Arguments 'PackageName' ''))}
        'desktop-controls'{Get-WinCareDesktopControlSummary}
        'wifi-profiles'{Get-WinCareWifiProfile}
        'shell-extensions'{Get-WinCareShellExtension}
        'desktop-shortcuts'{Get-WinCareDesktopShortcut}
        'boot'{Get-WinCareBootDiagnostics}
        'bcd-export'{$plan=New-WinCareBcdExportPlan -Destination ([string]$Arguments.Destination);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'unattend-analyze'{Get-WinCareUnattendAnalysis -LiteralPath ([string]$Arguments.Path)}
        'provisioning-plan'{$blueprint=Import-WinCareProvisioningBlueprint -LiteralPath ([string]$Arguments.Path);$plan=New-WinCareProvisioningPlan -Blueprint $blueprint;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'knowledge'{Get-WinCareKnowledgeBase -Topic ([string](Get-WinCarePropertyValue $Arguments 'Topic' ''))}
        'reports'{New-WinCareSystemReportData}
        'automation-profiles'{Get-WinCareAutomationProfile}
        'run-automation'{Invoke-WinCareAutomationProfile -Id ([string]$Arguments.Id)}
        'cancel-operation'{if(-not $Apply){New-WinCareResult -Success $true -Status Preview -Code 'CancellationPreview' -Message 'Apply is required to request cancellation.' -Data @{OperationId=[string]$Arguments.OperationId}}else{Request-WinCareOperationCancellation -OperationId ([string]$Arguments.OperationId)}}
        'widgets'{Get-WinCareWidgetSnapshot -Id @($Arguments.Ids)}
        'widget-catalog'{Get-WinCareWidgetDefinition -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'widget-export'{$plan=New-WinCareWidgetSnapshotExportPlan -Path ([string]$Arguments.Path) -WidgetId @($Arguments.Ids);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'bluetooth'{Get-WinCareBluetoothDevice -IncludeDisconnected:([bool](Get-WinCarePropertyValue $Arguments 'IncludeDisconnected' $false))}
        'bluetooth-events'{Get-WinCareBluetoothEvent -MaxEvents ([int](Get-WinCarePropertyValue $Arguments 'MaxEvents' 100))}
        'maintenance'{Get-WinCareMaintenanceWindow -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' '')) -IncludeTerminal:([bool](Get-WinCarePropertyValue $Arguments 'IncludeTerminal' $false))}
        'maintenance-create'{
            $recordArgs=@{Name=[string]$Arguments.Name;StartAt=[datetimeoffset]::Parse([string]$Arguments.StartAt);EndAt=[datetimeoffset]::Parse([string]$Arguments.EndAt);Description=[string](Get-WinCarePropertyValue $Arguments 'Description' '');PlaybookId=[string](Get-WinCarePropertyValue $Arguments 'PlaybookId' '');Tags=@(Get-WinCarePropertyValue $Arguments 'Tags' @());RequiresRestart=[bool](Get-WinCarePropertyValue $Arguments 'RequiresRestart' $false)}
            $notice=[string](Get-WinCarePropertyValue $Arguments 'NoticeAt' '');if($notice){$recordArgs.NoticeAt=[datetimeoffset]::Parse($notice)}
            $record=New-WinCareMaintenanceWindowRecord @recordArgs;$plan=New-WinCareMaintenanceUpsertPlan -Window $record;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply
        }
        'maintenance-transition'{$plan=New-WinCareMaintenanceTransitionPlan -Id ([string]$Arguments.Id) -State ([string]$Arguments.State) -Message ([string](Get-WinCarePropertyValue $Arguments 'Message' ''));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'maintenance-metrics'{Get-WinCareMaintenanceMetrics}
        'maintenance-export'{$plan=New-WinCareMaintenanceMetricsExportPlan -Destination ([string]$Arguments.Path);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'context-menu'{Get-WinCareContextMenuRuleState -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'context-menu-set'{$plan=New-WinCareContextMenuPlan -RuleId ([string]$Arguments.Id) -Enable:([bool](Get-WinCarePropertyValue $Arguments 'Enable' $true));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'customization-hosts'{Get-WinCareCustomizationHost}
        'rainmeter-skins'{Get-WinCareRainmeterSkinInventory}
        'windhawk-mods'{Get-WinCareWindhawkModInventory}
        'explorerpatcher'{Get-WinCareExplorerPatcherState}
        'modern-context-validate'{Import-WinCareModernContextMenuDefinition -LiteralPath ([string]$Arguments.Path)}
        'playbooks'{Get-WinCarePlaybook -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'playbook'{$plan=New-WinCarePlaybookPlan -PlaybookId ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'group-policy'{Get-WinCareLocalGroupPolicyState}
        'group-policy-import'{$plan=New-WinCareLocalGroupPolicyImportPlan -BackupPath ([string]$Arguments.BackupPath) -LgpoPath ([string]$Arguments.LgpoPath);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'sysmon'{Get-WinCareSysmonState}
        'sysmon-configure'{$plan=New-WinCareSysmonConfigurationPlan -ExecutablePath ([string]$Arguments.ExecutablePath) -ConfigPath ([string]$Arguments.ConfigPath);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'sysmon-uninstall'{$plan=New-WinCareSysmonUninstallPlan -ExecutablePath ([string]$Arguments.ExecutablePath);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'offline-images'{Get-WinCareMountedWindowsImage}
        'offline-drivers'{Get-WinCareOfflineImageDriver -ImagePath ([string]$Arguments.ImagePath)}
        'offline-packages'{Get-WinCareOfflineImagePackage -ImagePath ([string]$Arguments.ImagePath)}
        'offline-features'{Get-WinCareOfflineImageFeature -ImagePath ([string]$Arguments.ImagePath)}
        'offline-driver-add'{$plan=New-WinCareOfflineDriverAddPlan -ImagePath ([string]$Arguments.ImagePath) -DriverPath ([string]$Arguments.DriverPath) -Recurse:([bool](Get-WinCarePropertyValue $Arguments 'Recurse' $false)) -ForceUnsigned:([bool](Get-WinCarePropertyValue $Arguments 'ForceUnsigned' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'offline-driver-remove'{$plan=New-WinCareOfflineDriverRemovePlan -ImagePath ([string]$Arguments.ImagePath) -PublishedName ([string]$Arguments.PublishedName);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'offline-package-add'{$plan=New-WinCareOfflinePackageAddPlan -ImagePath ([string]$Arguments.ImagePath) -PackagePath ([string]$Arguments.PackagePath) -IgnoreCheck:([bool](Get-WinCarePropertyValue $Arguments 'IgnoreCheck' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'offline-feature-set'{$plan=New-WinCareOfflineFeaturePlan -ImagePath ([string]$Arguments.ImagePath) -FeatureName ([string]$Arguments.FeatureName) -State ([string]$Arguments.State);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'power-sessions'{Get-WinCarePowerSession -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' '')) -IncludeTerminal:([bool](Get-WinCarePropertyValue $Arguments 'IncludeTerminal' $true))}
        'power-start'{$plan=New-WinCarePowerSessionPlan -Mode ([string](Get-WinCarePropertyValue $Arguments 'Mode' 'System')) -DurationMinutes ([int](Get-WinCarePropertyValue $Arguments 'DurationMinutes' (Get-WinCareConfig 'PowerSessionDefaultMinutes'))) -RefreshSeconds ([int](Get-WinCarePropertyValue $Arguments 'RefreshSeconds' (Get-WinCareConfig 'PowerSessionRefreshSeconds'))) -Reason ([string](Get-WinCarePropertyValue $Arguments 'Reason' 'Headless operator request'));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'power-stop'{$plan=New-WinCarePowerSessionStopPlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'windows'{Get-WinCareWindowInventory -IncludeUntitled:([bool](Get-WinCarePropertyValue $Arguments 'IncludeUntitled' $false)) -IncludeCloaked:([bool](Get-WinCarePropertyValue $Arguments 'IncludeCloaked' $false))}
        'monitors'{Get-WinCareMonitorInventory}
        'window-zones'{Get-WinCareWindowZoneDefinition -LayoutId ([string](Get-WinCarePropertyValue $Arguments 'LayoutId' '')) -ZoneId ([string](Get-WinCarePropertyValue $Arguments 'ZoneId' ''))}
        'window-zone-set'{$plan=New-WinCareWindowZonePlan -WindowHandle ([long]$Arguments.WindowHandle) -LayoutId ([string]$Arguments.LayoutId) -ZoneId ([string]$Arguments.ZoneId) -MonitorDevice ([string](Get-WinCarePropertyValue $Arguments 'MonitorDevice' ''));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'window-topmost'{$plan=New-WinCareWindowTopmostPlan -WindowHandle ([long]$Arguments.WindowHandle) -Topmost ([bool]$Arguments.Topmost);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'window-activate'{$plan=New-WinCareWindowActivatePlan -WindowHandle ([long]$Arguments.WindowHandle);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'input-release'{$plan=New-WinCareEmergencyInputReleasePlan;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'downloads'{Get-WinCareDownloadJob -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' '')) -IncludeTerminal:([bool](Get-WinCarePropertyValue $Arguments 'IncludeTerminal' $true))}
        'downloads-due'{Get-WinCareDueDownloadJob}
        'download-create'{$scheduledText=[string](Get-WinCarePropertyValue $Arguments 'ScheduledAt' '');$jobArgs=@{Url=@($Arguments.Urls);Destination=[string]$Arguments.Destination;Name=[string](Get-WinCarePropertyValue $Arguments 'Name' '');CollisionPolicy=[string](Get-WinCarePropertyValue $Arguments 'CollisionPolicy' 'Fail');HashAlgorithm=[string](Get-WinCarePropertyValue $Arguments 'HashAlgorithm' 'None');ExpectedHash=[string](Get-WinCarePropertyValue $Arguments 'ExpectedHash' '');Headers=(Get-WinCarePropertyValue $Arguments 'Headers' @{});Priority=[string](Get-WinCarePropertyValue $Arguments 'Priority' 'Normal');MaxRetries=[int](Get-WinCarePropertyValue $Arguments 'MaxRetries' (Get-WinCareConfig 'DownloadRetryLimit'))};if($scheduledText){$jobArgs.ScheduledAt=[datetimeoffset]::Parse($scheduledText)};$job=New-WinCareDownloadJobRecord @jobArgs;$plan=New-WinCareDownloadJobPlan -Job $job;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'download-start'{$plan=New-WinCareDownloadStartPlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'download-start-due'{$plan=New-WinCareDueDownloadStartPlan;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'download-suspend'{$plan=New-WinCareDownloadStatePlan -Id ([string]$Arguments.Id) -State Suspended;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'download-resume'{$plan=New-WinCareDownloadStatePlan -Id ([string]$Arguments.Id) -State Transferring;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'download-reconcile'{$plan=New-WinCareDownloadReconcilePlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'download-cancel'{$plan=New-WinCareDownloadCancelPlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'download-remove'{$plan=New-WinCareDownloadJobRemovePlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'telemetry-snapshot'{Get-WinCareTelemetrySnapshot -TopProcessCount ([int](Get-WinCarePropertyValue $Arguments 'TopProcessCount' 5))}
        'telemetry-history'{Get-WinCareTelemetryHistory}
        'telemetry-capture'{$plan=New-WinCareTelemetryCapturePlan -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' 'Headless capture')) -DurationSeconds ([int](Get-WinCarePropertyValue $Arguments 'DurationSeconds' 30)) -IntervalSeconds ([int](Get-WinCarePropertyValue $Arguments 'IntervalSeconds' 2)) -TopProcessCount ([int](Get-WinCarePropertyValue $Arguments 'TopProcessCount' 5));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'telemetry-export'{$plan=New-WinCareTelemetryExportPlan -Destination ([string]$Arguments.Path) -SeriesId ([string](Get-WinCarePropertyValue $Arguments 'SeriesId' ''));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'launcher-search'{Get-WinCareLaunchTarget -Query ([string](Get-WinCarePropertyValue $Arguments 'Query' '')) -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 100))}
        'launcher-open'{$plan=New-WinCareOpenLaunchTargetPlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'calculator'{Invoke-WinCareCalculator -Expression ([string]$Arguments.Expression)}
        'steam-games'{Get-WinCareSteamGame -AppId ([string](Get-WinCarePropertyValue $Arguments 'AppId' ''))}
        'steam-users'{Get-WinCareSteamUser}
        'steam-cloud-files'{Get-WinCareSteamCloudFile -UserId ([string]$Arguments.UserId) -AppId ([string]$Arguments.AppId)}
        'steam-backup'{$plan=New-WinCareSteamCloudBackupPlan -UserId ([string]$Arguments.UserId) -AppId ([string]$Arguments.AppId) -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' ''));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'steam-restore'{$plan=New-WinCareSteamCloudRestorePlan -BackupPath ([string]$Arguments.Path);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'game-integrity'{Get-WinCareGameIntegrityFinding -ScanRoot @((Get-WinCarePropertyValue $Arguments 'ScanRoots' @()))}
        'offline-reduction-profiles'{Get-WinCareOfflineReductionProfile -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'offline-reduction-assess'{Get-WinCareOfflineReductionAssessment -ImagePath ([string]$Arguments.ImagePath) -ProfileId ([string]$Arguments.ProfileId)}
        'offline-reduction-apply'{$plan=New-WinCareOfflineReductionPlan -ImagePath ([string]$Arguments.ImagePath) -ProfileId ([string]$Arguments.ProfileId) -IncludeComponentCleanup:([bool](Get-WinCarePropertyValue $Arguments 'IncludeComponentCleanup' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'workspace-layouts'{Get-WinCareWorkspaceLayout -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'workspace-layout-save'{$record=New-WinCareWorkspaceLayoutRecord -Name ([string]$Arguments.Name) -Description ([string](Get-WinCarePropertyValue $Arguments 'Description' '')) -Slot @($Arguments.Slots) -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''));$plan=New-WinCareWorkspaceLayoutSavePlan -Layout $record;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'workspace-layout-apply'{$plan=New-WinCareWorkspaceLayoutApplyPlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'workspace-layout-remove'{$plan=New-WinCareWorkspaceLayoutRemovePlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'color-capture'{Get-WinCareScreenColor -X (Get-WinCarePropertyValue $Arguments 'X' $null) -Y (Get-WinCarePropertyValue $Arguments 'Y' $null)}
        'color-palette'{Get-WinCareColorPalette -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'color-add'{$color=ConvertTo-WinCareColorRecord -Red ([int]$Arguments.Red) -Green ([int]$Arguments.Green) -Blue ([int]$Arguments.Blue) -Alpha ([int](Get-WinCarePropertyValue $Arguments 'Alpha' 255)) -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' '')) -Source 'Headless' -Tags @((Get-WinCarePropertyValue $Arguments 'Tags' @()));$plan=New-WinCareColorPaletteAddPlan -Color $color -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' '')) -Tags @((Get-WinCarePropertyValue $Arguments 'Tags' @()));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'color-remove'{$plan=New-WinCareColorPaletteRemovePlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'image-metadata'{Get-WinCareImageMetadata -LiteralPath ([string]$Arguments.Path)}
        'notes'{if([string](Get-WinCarePropertyValue $Arguments 'Query' '')){Search-WinCareLocalNote -Query ([string]$Arguments.Query) -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 500)) -IncludePrivateBody:([bool](Get-WinCarePropertyValue $Arguments 'IncludePrivateBody' $false))}else{Get-WinCareLocalNote -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' '')) -IncludeBody:([bool](Get-WinCarePropertyValue $Arguments 'IncludeBody' $false)) -IncludePrivateBody:([bool](Get-WinCarePropertyValue $Arguments 'IncludePrivateBody' $false))}}
        'note-save'{
            $id=[string](Get-WinCarePropertyValue $Arguments 'Id' '')
            $existing=if($id){Get-WinCareLocalNote -Id $id -IncludeBody -IncludePrivateBody|Select-Object -First 1}else{$null}
            $defaultBody=if($existing){[string]$existing.Body}else{''};$defaultTags=if($existing){@($existing.Tags)}else{@()}
            $defaultPrivate=if($existing){[bool]$existing.Private}else{$true};$defaultPinned=if($existing){[bool]$existing.Pinned}else{$false}
            $links=if($existing){@($existing.LinkedOperationIds)}else{@()};$sources=if($existing){@($existing.SourceRecords)}else{@('W3-LOCALNOTES-PLAIN-MARKDOWN')}
            $plan=New-WinCareLocalNotePlan -Id $id -Title ([string]$Arguments.Title) -Body ([string](Get-WinCarePropertyValue $Arguments 'Body' $defaultBody)) -Tags @((Get-WinCarePropertyValue $Arguments 'Tags' $defaultTags)) -Private ([bool](Get-WinCarePropertyValue $Arguments 'Private' $defaultPrivate)) -Pinned ([bool](Get-WinCarePropertyValue $Arguments 'Pinned' $defaultPinned)) -LinkedOperationIds $links -SourceRecords $sources
            Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply
        }
        'note-remove'{$plan=New-WinCareLocalNoteRemovePlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'browsers'{Get-WinCareBrowserWorkspaceSummary}
        'browser-extensions'{Get-WinCareBrowserExtensionInventory -BrowserId ([string](Get-WinCarePropertyValue $Arguments 'BrowserId' '')) -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' (Get-WinCareConfig 'BrowserExtensionInventoryLimit')))}
        'remote-support'{Get-WinCareRemoteSupportSurface}
        'remote-consent'{Get-WinCareRemoteSupportConsent -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' '')) -IncludeTerminal:([bool](Get-WinCarePropertyValue $Arguments 'IncludeTerminal' $true))}
        'remote-consent-create'{$plan=New-WinCareRemoteSupportConsentPlan -ToolId ([string]$Arguments.ToolId) -Reason ([string]$Arguments.Reason) -Operator ([string](Get-WinCarePropertyValue $Arguments 'Operator' '')) -DurationMinutes ([int](Get-WinCarePropertyValue $Arguments 'DurationMinutes' (Get-WinCareConfig 'RemoteConsentDefaultMinutes'))) -DeviceScope ([string](Get-WinCarePropertyValue $Arguments 'DeviceScope' 'ThisDevice')) -AllowClipboard ([bool](Get-WinCarePropertyValue $Arguments 'AllowClipboard' $false)) -AllowFileTransfer ([bool](Get-WinCarePropertyValue $Arguments 'AllowFileTransfer' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'remote-consent-state'{$plan=New-WinCareRemoteSupportConsentStatePlan -Id ([string]$Arguments.Id) -State ([string]$Arguments.State);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'remote-consent-expire'{$plan=New-WinCareRemoteSupportExpiryReconciliationPlan;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'remote-emergency'{$plan=New-WinCareRemoteSupportEmergencyPlan -ProcessId @((Get-WinCarePropertyValue $Arguments 'ProcessIds' @())) -TerminateKnownProcesses:([bool](Get-WinCarePropertyValue $Arguments 'Terminate' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'studio-snapshot'{Get-WinCareUnifiedSystemSnapshot -ProcessLimit ([int](Get-WinCarePropertyValue $Arguments 'ProcessLimit' 40))}
        'studio-monitoring-export'{$plan=New-WinCareMonitoringExportPlan -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' 'wincare-local'));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'studio-file-workspaces'{Get-WinCareFileWorkspace -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'studio-file-workspace-save'{$plan=New-WinCareFileWorkspacePlan -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' '')) -Name ([string]$Arguments.Name) -Path @($Arguments.Paths) -Bookmark @((Get-WinCarePropertyValue $Arguments 'Bookmarks' @())) -SearchPattern ([string](Get-WinCarePropertyValue $Arguments 'SearchPattern' ''));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'studio-layout-profiles'{Get-WinCareWorkspaceStudioProfile -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'studio-layout-save'{$plan=New-WinCareWorkspaceStudioLayoutPlan -ProfileId ([string]$Arguments.ProfileId) -ProcessName @($Arguments.ProcessNames) -TitlePattern @((Get-WinCarePropertyValue $Arguments 'TitlePatterns' @())) -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' ''));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'studio-package'{Get-WinCarePackageArchiveMetadata -LiteralPath ([string]$Arguments.Path)}
        'studio-brightness-schedules'{Get-WinCareBrightnessSchedule -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'studio-brightness-save'{$plan=New-WinCareBrightnessSchedulePlan -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' '')) -Name ([string]$Arguments.Name) -Step @($Arguments.Steps);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'studio-brightness-apply'{$plan=New-WinCareBrightnessScheduleApplyPlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'studio-folder-appearance'{$plan=New-WinCareFolderAppearancePlan -LiteralPath ([string]$Arguments.Path) -IconResource ([string]$Arguments.IconResource) -LocalizedName ([string](Get-WinCarePropertyValue $Arguments 'LocalizedName' ''));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'studio-kanata-validate'{Test-WinCareKanataConfiguration -LiteralPath ([string]$Arguments.Path)}
        'studio-syncthing'{Get-WinCareSyncthingState -ConfigPath ([string](Get-WinCarePropertyValue $Arguments 'ConfigPath' ''))}
        'studio-wezterm'{$plan=New-WinCareWezTermStatusBarPlan -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' 'wincare-status.lua'));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'studio-adb-inventory'{$plan=New-WinCareAdbInventoryPlan -AdbPath ([string]$Arguments.AdbPath) -Sha256 ([string]$Arguments.Sha256);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'studio-xbox-fse'{if($Apply -and [string](Get-WinCarePropertyValue $Arguments 'Acknowledgement' '') -cne 'I ACCEPT LEGACY UNSAFE MUTATIONS'){New-WinCareResult -Success $false -Status Blocked -Code 'LegacyUnsafeAcknowledgementRequired' -Message 'Set Acknowledgement exactly to I ACCEPT LEGACY UNSAFE MUTATIONS.' -ExitCode 5}else{$plan=New-WinCareXboxFullscreenExperiencePlan -ViVeToolPath ([string]$Arguments.ViVeToolPath) -Sha256 ([string]$Arguments.Sha256) -Enabled ([bool](Get-WinCarePropertyValue $Arguments 'Enabled' $true)) -BasicFeatureSet:([bool](Get-WinCarePropertyValue $Arguments 'BasicFeatureSet' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}}
        'toolkit-diagnostics'{Get-WinCareSystemToolkitDiagnostic}
        'toolkit-win32-error'{Get-WinCareWin32ErrorMessage -Code ([int]$Arguments.Code)}
        'toolkit-msi'{Get-WinCareMsiMetadata -LiteralPath ([string]$Arguments.Path)}
        'hardening-profiles'{Get-WinCareHardeningProfile -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'hardening-assess'{Get-WinCareHardeningAssessment -ProfileId ([string]$Arguments.ProfileId)}
        'hardening-apply'{
            $profileId=[string]$Arguments.ProfileId
            if($Apply -and $profileId -eq 'hailmary' -and [string](Get-WinCarePropertyValue $Arguments 'Acknowledgement' '') -cne 'I ACCEPT LEGACY UNSAFE MUTATIONS'){New-WinCareResult -Success $false -Status Blocked -Code 'LegacyUnsafeAcknowledgementRequired' -Message 'Set Acknowledgement exactly to I ACCEPT LEGACY UNSAFE MUTATIONS.' -ExitCode 5}
            else{$plan=New-WinCareHardeningProfilePlan -ProfileId $profileId;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        }
        'maintenance-templates'{Get-WinCareMaintenanceTemplate -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'maintenance-template-create'{$plan=New-WinCareMaintenanceTemplatePlan -TemplateId ([string]$Arguments.TemplateId) -StartAt ([datetimeoffset]::Parse([string]$Arguments.StartAt)) -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' ''));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'system-shortcuts'{Get-WinCareSystemShortcutCatalog -Query ([string](Get-WinCarePropertyValue $Arguments 'Query' '')) -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 250))}
        'system-shortcuts-export'{$plan=New-WinCareSystemShortcutExportPlan -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' 'system-shortcuts.json')) -Query ([string](Get-WinCarePropertyValue $Arguments 'Query' ''));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'download-batch'{$batch=Import-WinCareDownloadBatchDefinition -LiteralPath ([string]$Arguments.Path);$plan=New-WinCareDownloadBatchPlan -Batch $batch;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'torrent-metadata'{Get-WinCareTorrentMetadata -LiteralPath ([string]$Arguments.Path)}
        'experience-cards'{Get-WinCareExperienceCapabilityCard -Query ([string](Get-WinCarePropertyValue $Arguments 'Query' ''))}
        'experience-visual-asset'{Get-WinCareVisualAssetInfo -LiteralPath ([string]$Arguments.Path)}
        'experience-sprite-layout'{Get-WinCareSpriteSheetLayout -SheetWidth ([int]$Arguments.SheetWidth) -SheetHeight ([int]$Arguments.SheetHeight) -FrameWidth ([int]$Arguments.FrameWidth) -FrameHeight ([int]$Arguments.FrameHeight) -Margin ([int](Get-WinCarePropertyValue $Arguments 'Margin' 0)) -Spacing ([int](Get-WinCarePropertyValue $Arguments 'Spacing' 0)) -FrameCount ([int](Get-WinCarePropertyValue $Arguments 'FrameCount' 0))}
        'experience-visual-manifest'{$plan=New-WinCareVisualAssetManifestPlan -LiteralPath ([string]$Arguments.Path) -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' 'visual-asset.json')) -FrameWidth ([int](Get-WinCarePropertyValue $Arguments 'FrameWidth' 1)) -FrameHeight ([int](Get-WinCarePropertyValue $Arguments 'FrameHeight' 1)) -Margin ([int](Get-WinCarePropertyValue $Arguments 'Margin' 0)) -Spacing ([int](Get-WinCarePropertyValue $Arguments 'Spacing' 0)) -IncludeSpriteLayout:([bool](Get-WinCarePropertyValue $Arguments 'IncludeSpriteLayout' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'experience-location-state'{Get-WinCareLocationPrivacyState}
        'experience-location-set'{$plan=New-WinCareLocationPrivacyPlan -Mode ([string]$Arguments.Mode);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'experience-remote-profiles'{Get-WinCareRemoteAccessProfile -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'experience-remote-state'{Get-WinCareRemoteAccessState}
        'experience-remote-set'{$plan=New-WinCareRemoteAccessPlan -ProfileId ([string]$Arguments.ProfileId) -Enabled ([bool](Get-WinCarePropertyValue $Arguments 'Enabled' $true)) -AllowDriveRedirection ([bool](Get-WinCarePropertyValue $Arguments 'AllowDriveRedirection' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'experience-power-profiles'{Get-WinCarePowerConditionProfile -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'experience-power-assess'{Get-WinCarePowerConditionAssessment}
        'experience-power-apply'{$plan=New-WinCarePowerConditionPlan -ProfileId ([string]$Arguments.ProfileId);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'experience-download-strategy'{Get-WinCareDownloadStrategy -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'experience-privacy-profiles'{Get-WinCarePrivacyProfile -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'experience-privacy-apply'{$plan=New-WinCarePrivacyProfilePlan -ProfileId ([string]$Arguments.ProfileId);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'shell-hardware-cards'{Get-WinCareShellHardwareCapabilityCard -Query ([string](Get-WinCarePropertyValue $Arguments 'Query' ''))}
        'hardware-inventory'{Get-WinCareHardwareInventory -IncludeSensitiveIdentifiers:([bool](Get-WinCarePropertyValue $Arguments 'IncludeSensitiveIdentifiers' $false)) -MaximumItemsPerClass ([int](Get-WinCarePropertyValue $Arguments 'MaximumItemsPerClass' 128))}
        'hardware-report'{$plan=New-WinCareHardwareReportPlan -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' 'hardware-report.json')) -IncludeSensitiveIdentifiers:([bool](Get-WinCarePropertyValue $Arguments 'IncludeSensitiveIdentifiers' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'monitor-controls'{Get-WinCareMonitorControlState}
        'monitor-control-set'{$plan=New-WinCareMonitorControlPlan -DeviceName ([string]$Arguments.DeviceName) -PhysicalIndex ([int]$Arguments.PhysicalIndex) -Feature ([string]$Arguments.Feature) -Value ([int]$Arguments.Value);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'window-search'{Get-WinCareWindowSearch -Query ([string]$Arguments.Query) -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 50))}
        'explorer-session'{Get-WinCareExplorerSession}
        'explorer-session-save'{$plan=New-WinCareExplorerSessionSavePlan -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ('session-'+[datetime]::UtcNow.ToString('yyyyMMdd-HHmmss'))));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'explorer-session-records'{Get-WinCareExplorerSessionRecord -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'explorer-session-restore'{$plan=New-WinCareExplorerSessionRestorePlan -Id ([string]$Arguments.Id);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'ui-automation-snapshot'{Get-WinCareUiAutomationSnapshot -ProcessId ([int](Get-WinCarePropertyValue $Arguments 'ProcessId' 0)) -MaximumDepth ([int](Get-WinCarePropertyValue $Arguments 'MaximumDepth' 5)) -MaximumNodes ([int](Get-WinCarePropertyValue $Arguments 'MaximumNodes' 500)) -IncludeText:([bool](Get-WinCarePropertyValue $Arguments 'IncludeText' $false))}
        'appx-runtime'{Get-WinCareAppxRuntimeInventory -IncludeUnpackaged:([bool](Get-WinCarePropertyValue $Arguments 'IncludeUnpackaged' $false)) -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 512))}
        'appx-launch-targets'{Get-WinCareAppxLaunchTarget -Aumid ([string](Get-WinCarePropertyValue $Arguments 'Aumid' '')) -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 1000))}
        'appx-launch'{$plan=New-WinCareAppxLaunchPlan -Aumid ([string]$Arguments.Aumid);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'archive-inspect'{Get-WinCareArchiveInspection -LiteralPath ([string]$Arguments.LiteralPath) -MaximumMembers ([int](Get-WinCarePropertyValue $Arguments 'MaximumMembers' 50000)) -MaximumExpandedBytes ([long](Get-WinCarePropertyValue $Arguments 'MaximumExpandedBytes' 10737418240)) -MaximumCompressionRatio ([int](Get-WinCarePropertyValue $Arguments 'MaximumCompressionRatio' 200))}
        'servicing-media'{Get-WinCareServicingMediaInfo -LiteralPath ([string]$Arguments.LiteralPath) -TimeoutSeconds ([int](Get-WinCarePropertyValue $Arguments 'TimeoutSeconds' 300))}
        'terminal-environment'{Get-WinCareTerminalEnvironment}
        'terminal-export'{$plan=New-WinCareTerminalProfileExportPlan -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' 'terminal-environment.json'));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'gui-resource-audit'{Get-WinCareGuiResourceAudit -LiteralPath ([string](Get-WinCarePropertyValue $Arguments 'LiteralPath' ''))}
        'fullscreen-shell-assess'{Get-WinCareFullscreenShellAssessment}
        'cleaner-preview-cards'{Get-WinCareCleanerPreviewCapabilityCard -Query ([string](Get-WinCarePropertyValue $Arguments 'Query' ''))}
        'cleaner-disk-pressure'{$plan=New-WinCareDiskPressureCleanupPlan -Profile ([string](Get-WinCarePropertyValue $Arguments 'Profile' 'Auto')) -MinimumFreePercent ([int](Get-WinCarePropertyValue $Arguments 'MinimumFreePercent' 15)) -MinimumFreeBytes ([long](Get-WinCarePropertyValue $Arguments 'MinimumFreeBytes' 21474836480));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'cleaner-disk-pressure-schedule'{$plan=New-WinCareDiskPressureAutomationPlan -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' 'disk-pressure-cleanup')) -Profile ([string](Get-WinCarePropertyValue $Arguments 'Profile' 'Conservative')) -Schedule ([string](Get-WinCarePropertyValue $Arguments 'Schedule' 'weekly:Sunday@03:00')) -MinimumFreePercent ([int](Get-WinCarePropertyValue $Arguments 'MinimumFreePercent' 15)) -MinimumFreeBytes ([long](Get-WinCarePropertyValue $Arguments 'MinimumFreeBytes' 21474836480));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'cleaner-winapp2'{Get-WinCareWinapp2Assessment -LiteralPath ([string]$Arguments.LiteralPath) -Section @((Get-WinCarePropertyValue $Arguments 'Section' @())) -MinimumAgeHours ([int](Get-WinCarePropertyValue $Arguments 'MinimumAgeHours' 24)) -MaximumFiles ([int](Get-WinCarePropertyValue $Arguments 'MaximumFiles' 500)) -MaximumBytes ([long](Get-WinCarePropertyValue $Arguments 'MaximumBytes' 536870912))}
        'cleaner-winapp2-run'{$plan=New-WinCareWinapp2CleanupPlan -LiteralPath ([string]$Arguments.LiteralPath) -Section @((Get-WinCarePropertyValue $Arguments 'Section' @())) -MinimumAgeHours ([int](Get-WinCarePropertyValue $Arguments 'MinimumAgeHours' 24)) -MaximumFiles ([int](Get-WinCarePropertyValue $Arguments 'MaximumFiles' 500)) -MaximumBytes ([long](Get-WinCarePropertyValue $Arguments 'MaximumBytes' 536870912));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'cleaner-relocation-profiles'{Get-WinCareApplicationDataRelocationProfile -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'cleaner-relocation-assess'{Get-WinCareApplicationDataRelocationAssessment -ProfileId ([string]$Arguments.ProfileId) -DestinationRoot ([string]$Arguments.DestinationRoot)}
        'cleaner-relocation'{$plan=New-WinCareApplicationDataRelocationPlan -ProfileId ([string]$Arguments.ProfileId) -DestinationRoot ([string]$Arguments.DestinationRoot);Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'file-preview-profiles'{Get-WinCareFilePreviewProfile -Extension ([string](Get-WinCarePropertyValue $Arguments 'Extension' ''))}
        'file-preview'{Get-WinCareFilePreview -LiteralPath ([string]$Arguments.LiteralPath) -MaximumBytes ([int](Get-WinCarePropertyValue $Arguments 'MaximumBytes' 1048576)) -MaximumLines ([int](Get-WinCarePropertyValue $Arguments 'MaximumLines' 200)) -IncludeText:([bool](Get-WinCarePropertyValue $Arguments 'IncludeText' $false))}
        'file-preview-export'{$plan=New-WinCareFilePreviewExportPlan -LiteralPath ([string]$Arguments.LiteralPath) -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' 'file-preview.json')) -IncludeText:([bool](Get-WinCarePropertyValue $Arguments 'IncludeText' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'preview-handlers'{Get-WinCarePreviewHandlerAssessment -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 256))}
        'peer-display-overrides'{Get-WinCareDisplayOverrideState}
        'peer-display-reset'{$plan=New-WinCareDisplayOverrideResetPlan;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'peer-wlan'{Get-WinCareWlanSurvey}
        'peer-log-tail'{if([string](Get-WinCarePropertyValue $Arguments 'LogName' '')){Get-WinCareLogSnapshot -LogName ([string]$Arguments.LogName) -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 200)) -Pattern ([string](Get-WinCarePropertyValue $Arguments 'Pattern' ''))}else{Get-WinCareLogSnapshot -LiteralPath ([string]$Arguments.LiteralPath) -Limit ([int](Get-WinCarePropertyValue $Arguments 'Limit' 200)) -Pattern ([string](Get-WinCarePropertyValue $Arguments 'Pattern' ''))}}
        'peer-pe'{Get-WinCarePeMetadata -LiteralPath ([string]$Arguments.LiteralPath)}
        'peer-container-log-config'{$plan=New-WinCareContainerLogMonitorPlan -EventLog @((Get-WinCarePropertyValue $Arguments 'EventLog' @('System','Application'))) -FilePath @((Get-WinCarePropertyValue $Arguments 'FilePath' @())) -EtwProvider @((Get-WinCarePropertyValue $Arguments 'EtwProvider' @())) -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' 'peer-log-monitor.json'));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'peer-taskboard'{Get-WinCareTaskBoard -Status ([string](Get-WinCarePropertyValue $Arguments 'Status' 'All'))}
        'peer-task-save'{$plan=New-WinCareTaskPlan -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' '')) -Title ([string]$Arguments.Title) -Body ([string](Get-WinCarePropertyValue $Arguments 'Body' '')) -Status ([string](Get-WinCarePropertyValue $Arguments 'Status' 'TaskTodo')) -Pinned:([bool](Get-WinCarePropertyValue $Arguments 'Pinned' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'explorer-quick'{$params=@{};foreach($name in @('ShowExtensions','ShowHidden','LaunchThisPc')){if($Arguments.ContainsKey($name)){$params[$name]=[Nullable[bool]]([bool]$Arguments[$name])}};$plan=New-WinCareExplorerQuickPlan @params;Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}
        'deep-clean-profiles'{Get-WinCareDeepCleanupProfile -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'deep-clean'{$plan=New-WinCareDeepCleanupPlan -ProfileId ([string]$Arguments.ProfileId);if($Apply -and (Get-WinCarePropertyValue $plan.Metadata 'LegacyUnsafeProfile' '') -and [string](Get-WinCarePropertyValue $Arguments 'Acknowledgement' '') -cne 'I ACCEPT LEGACY UNSAFE MUTATIONS'){New-WinCareResult -Success $false -Status Blocked -Code 'LegacyUnsafeAcknowledgementRequired' -Message 'Set Acknowledgement exactly to I ACCEPT LEGACY UNSAFE MUTATIONS.' -ExitCode 5}else{Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}}
        'appx-selection-profiles'{Get-WinCareAppxSelectionProfile -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'appx-selection-assess'{Get-WinCareAppxSelectionAssessment -ProfileId ([string](Get-WinCarePropertyValue $Arguments 'ProfileId' '')) -Pattern @((Get-WinCarePropertyValue $Arguments 'Patterns' @())) -Mode ([string](Get-WinCarePropertyValue $Arguments 'Mode' 'Include')) -Scope ([string](Get-WinCarePropertyValue $Arguments 'Scope' 'CurrentUser')) -IncludeProvisioned:([bool](Get-WinCarePropertyValue $Arguments 'IncludeProvisioned' $false))}
        'appx-selection'{$patterns=@((Get-WinCarePropertyValue $Arguments 'Patterns' @()));if([string](Get-WinCarePropertyValue $Arguments 'ListPath' '')){$patterns=@((Import-WinCareAppxSelectionList -LiteralPath ([string]$Arguments.ListPath)).Patterns)};$plan=New-WinCareAppxSelectionPlan -ProfileId ([string](Get-WinCarePropertyValue $Arguments 'ProfileId' '')) -Pattern $patterns -Mode ([string](Get-WinCarePropertyValue $Arguments 'Mode' 'Include')) -Scope ([string](Get-WinCarePropertyValue $Arguments 'Scope' 'CurrentUser')) -IncludeProvisioned:([bool](Get-WinCarePropertyValue $Arguments 'IncludeProvisioned' $false));if($Apply -and (Get-WinCarePropertyValue $plan.Metadata 'LegacyUnsafeProfile' '') -and [string](Get-WinCarePropertyValue $Arguments 'Acknowledgement' '') -cne 'I ACCEPT LEGACY UNSAFE MUTATIONS'){New-WinCareResult -Success $false -Status Blocked -Code 'LegacyUnsafeAcknowledgementRequired' -Message 'Set Acknowledgement exactly to I ACCEPT LEGACY UNSAFE MUTATIONS.' -ExitCode 5}else{Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}}
        'offline-appx-selection'{$patterns=@((Get-WinCarePropertyValue $Arguments 'Patterns' @()));if([string](Get-WinCarePropertyValue $Arguments 'ListPath' '')){$patterns=@((Import-WinCareAppxSelectionList -LiteralPath ([string]$Arguments.ListPath)).Patterns)};$plan=New-WinCareOfflineAppxSelectionPlan -ImagePath ([string]$Arguments.ImagePath) -ProfileId ([string](Get-WinCarePropertyValue $Arguments 'ProfileId' '')) -Pattern $patterns -Mode ([string](Get-WinCarePropertyValue $Arguments 'Mode' 'Include'));if($Apply -and (Get-WinCarePropertyValue $plan.Metadata 'LegacyUnsafeProfile' '') -and [string](Get-WinCarePropertyValue $Arguments 'Acknowledgement' '') -cne 'I ACCEPT LEGACY UNSAFE MUTATIONS'){New-WinCareResult -Success $false -Status Blocked -Code 'LegacyUnsafeAcknowledgementRequired' -Message 'Set Acknowledgement exactly to I ACCEPT LEGACY UNSAFE MUTATIONS.' -ExitCode 5}else{Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}}
        'legacy-unsafe-profiles'{Get-WinCareLegacyUnsafeProfile -Id ([string](Get-WinCarePropertyValue $Arguments 'Id' ''))}
        'legacy-unsafe'{if($Apply -and [string](Get-WinCarePropertyValue $Arguments 'Acknowledgement' '') -cne 'I ACCEPT LEGACY UNSAFE MUTATIONS'){New-WinCareResult -Success $false -Status Blocked -Code 'LegacyUnsafeAcknowledgementRequired' -Message 'Set Acknowledgement exactly to I ACCEPT LEGACY UNSAFE MUTATIONS.' -ExitCode 5}else{$plan=New-WinCareLegacyUnsafeProfilePlan -ProfileId ([string]$Arguments.ProfileId) -IncludeProvisionedAppx:([bool](Get-WinCarePropertyValue $Arguments 'IncludeProvisionedAppx' $false));Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply}}
    }
    if($JsonObject){return $data}
    return $data
}
