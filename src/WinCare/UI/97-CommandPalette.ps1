function Get-WinCareFuzzyScore {
    param([Parameter(Mandatory)][string]$Text,[Parameter(Mandatory)][string]$Query)
    if([string]::IsNullOrWhiteSpace($Query)){return 0}
    $textValue=$Text.ToLowerInvariant();$queryValue=$Query.ToLowerInvariant();$position=-1;$score=0
    foreach($character in $queryValue.ToCharArray()){$next=$textValue.IndexOf($character,$position+1);if($next -lt 0){return -1};$gap=$next-$position-1;$score+=20-[math]::Min(15,$gap);if($next -eq 0 -or $textValue[$next-1] -in @(' ','-','/')){$score+=12};$position=$next}
    if($textValue.Contains($queryValue)){$score+=100};return $score
}

function Get-WinCareCommandPaletteItem {
    param([string]$Query='')
    $items=@(
        @{Label='Dashboard';Description='System overview and recommendations';Action='Dashboard';Keywords='home overview health'},
        @{Label='Quick actions';Description='Common maintenance workflows';Action='Quick';Keywords='cleanup health report'},
        @{Label='Applications';Description='Repair, reset, uninstall, and inventory';Action='Applications';Keywords='apps programs remove'},
        @{Label='Cleanup center';Description='Temporary files, caches, Store, Recycle Bin';Action='Cleanup';Keywords='clean cache disk'},
        @{Label='Storage';Description='Drives, large files, and Storage Sense';Action='Storage';Keywords='disk space files'},
        @{Label='Verified downloads';Description='BITS queue, mirrors, checksums, pause/resume, and atomic completion';Action='Downloads';Keywords='download bits motrix abdm kget qdm transfer mirror checksum'},
        @{Label='Operations telemetry';Description='Local CPU, memory, disk, network, process, and alert history';Action='Telemetry';Keywords='monitor metrics dashboard lite tabame edex cpu memory'},
        @{Label='Unified launcher';Description='Applications, settings, control targets, shortcuts, and safe calculator';Action='Launcher';Keywords='launch search pinpoint palette winkey calculator app'},
        @{Label='Profiles and policy';Description='Curated privacy, performance, gaming, developer, security profiles';Action='Profiles';Keywords='tweaks optimize privacy gaming'},
        @{Label='Desktop controls';Description='Volume, brightness, Wi-Fi, clipboard';Action='Desktop';Keywords='audio mute display wifi'},
        @{Label='Startup';Description='Reversible startup entry control';Action='Startup';Keywords='boot login autorun'},
        @{Label='Windows health';Description='DISM, SFC, CHKDSK, findings';Action='Health';Keywords='repair integrity'},
        @{Label='Security';Description='Defender, firewall, Secure Boot, TPM';Action='Security';Keywords='scan malware firewall'},
        @{Label='Windows Update Agent';Description='Search, download, install, hide, history';Action='Wua';Keywords='updates patches'},
        @{Label='WinGet updates';Description='Application package upgrades';Action='Updates';Keywords='winget packages'},
        @{Label='Network';Description='Adapters, DNS, ports, connectivity';Action='Network';Keywords='internet wifi dns'},
        @{Label='Windows internals';Description='Processes, memory, CPU, credentials, AppContainers';Action='Internals';Keywords='process memory cpu'},
        @{Label='Expert laboratory';Description='Pagefile, temporary security maintenance, TCP experiments, ETW and interception surfaces';Action='ExpertLab';Keywords='pagefile defender uac smartscreen update network tcp etw injection instrumentation'},
        @{Label='WDAC';Description='Application Control policy lifecycle and events';Action='WDAC';Keywords='code integrity hardening'},
        @{Label='Provisioning';Description='Analyze unattend and apply blueprints';Action='Provisioning';Keywords='unattend install setup'},
        @{Label='Boot and recovery';Description='BCD export, WinRE, Secure Boot';Action='Boot';Keywords='bcd recovery startup'},
        @{Label='Shell and desktop';Description='Context-menu handlers and shortcuts';Action='Shell';Keywords='explorer context menu'},
        @{Label='Customization governance';Description='Context-menu rules, Rainmeter, Windhawk, ExplorerPatcher inventory';Action='Customization';Keywords='context menu rainmeter windhawk explorer start'},
        @{Label='Widget dashboard';Description='Capability-aware local cards and snapshots';Action='Widgets';Keywords='widgets dashboard cards rainmeter metrics'},
        @{Label='Bluetooth telemetry';Description='Devices, battery observations, and events';Action='Bluetooth';Keywords='airpods beats battery pairing device'},
        @{Label='Security baselines';Description='Local Group Policy and Sysmon lifecycle';Action='Baseline';Keywords='lgpo sysmon hardening telemetry'},
        @{Label='Offline image servicing';Description='Mounted-image drivers, packages, and features';Action='Offline';Keywords='dism wim driver cab msu image'},
        @{Label='Maintenance control';Description='Windows, transitions, metrics, and local exports';Action='Maintenance';Keywords='maintenance notice schedule metrics'},
        @{Label='Declarative playbooks';Description='Validated composition of WinCare plans';Action='Playbooks';Keywords='playbook optimize harden debloat profile'},
        @{Label='Power sessions';Description='Timed awake, display, and away-mode holds';Action='Power';Keywords='awake sleep insomnia nosleep execution state'},
        @{Label='Window workspace';Description='Zones, monitors, topmost, focus, emergency release';Action='Windows';Keywords='zones layout fancyzones always top switcher window'},
        @{Label='Multi-window layouts';Description='Saved monitor-relative layouts and exact per-window rollback';Action='Layouts';Keywords='snap zones layout workspace arrangement'},
        @{Label='Steam and game-state protection';Description='Steam libraries, local cloud cache, backups, restore, and integrity findings';Action='GameState';Keywords='steam cloud save backup game trainer integrity'},
        @{Label='Color studio';Description='Screen pixel, HEX/RGB/HSL/HSV, palette, image metadata';Action='Color';Keywords='picker color eyedropper palette gif image'},
        @{Label='Local notes';Description='Private local Markdown notes';Action='Notes';Keywords='notes markdown scratchpad local private'},
        @{Label='Browser workspace';Description='Browser, profile, window, extension and permission inventory';Action='Browser';Keywords='tabs extensions edge chrome firefox browser'},
        @{Label='Remote-support governance';Description='Remote tools, consent, listeners, emergency release';Action='RemoteSupport';Keywords='rustdesk mouse proxy remote support consent'},
        @{Label='Automation';Description='Validated scheduled maintenance profiles';Action='Automation';Keywords='schedule tasks'},
        @{Label='Recovery and undo';Description='Operation journals and supported rollback';Action='Recovery';Keywords='undo journal'},
        @{Label='Reports and history';Description='Export reports and audit evidence';Action='Reports';Keywords='logs export'},
        @{Label='Troubleshooting knowledge';Description='Guided local workflows';Action='Knowledge';Keywords='help guide'},
        @{Label='Workspace and device studio';Description='Monitoring, files, layouts, packages, brightness, ADB, and Xbox FSE';Action='WorkspaceStudio';Keywords='workspace device monitoring file tabs layout package apk appx brightness adb xbox wezterm kanata syncthing'},
        @{Label='Settings';Description='Accessibility, thresholds, privacy, policy';Action='Settings';Keywords='theme ascii readonly'},
        @{Label='Help and safety';Description='Keyboard and safety model';Action='Help';Keywords='about shortcuts'}
    )|ForEach-Object{[pscustomobject]$_}
    $items=@($items)+@(Get-WinCareExtensionCommand|ForEach-Object{[pscustomobject]@{Label=$_.Label;Description=$_.Description;Action=$_.Action;Keywords=$_.Keywords;ExtensionId=$_.ExtensionId}})
    $history=if($script:WinCareState.ContainsKey('Config')){@(Get-WinCareConfig 'CommandPaletteHistory')}else{@()}
    $mode=[string](Get-WinCareConfig 'WorkspaceMode');$visibleActions=@((Get-WinCareMainMenuItems).Action)
    $ranked=foreach($item in $items){
        if($mode -ne 'All' -and $item.Action -notin $visibleActions){continue}
        $search="$($item.Label) $($item.Description) $($item.Keywords)";$score=Get-WinCareFuzzyScore -Text $search -Query $Query
        if($score -lt 0){continue};$historyIndex=[array]::IndexOf($history,$item.Action);if($historyIndex -ge 0){$score+=40-$historyIndex}
        $item|Add-Member -NotePropertyName Score -NotePropertyValue $score -Force;$item
    }
    @($ranked|Sort-Object @{Expression='Score';Descending=$true},Label|Select-Object -First ([int](Get-WinCareConfig 'CommandPaletteMaxResults')))
}
function Format-WinCareCommandRow {param($Item,$Width);"$(Limit-WinCareText $Item.Label ([math]::Max(24,$Width-36))) $(Limit-WinCareText $Item.Description 34)"}
function Show-WinCareCommandPalette {
    $query=Read-WinCareInput -Prompt 'Command or concept (blank shows all)' -Default ''
    $item=Show-WinCareListSelector -Title 'Command palette' -Subtitle 'Fuzzy ranked by command, synonym, context mode, and bounded usage history' -Items @(Get-WinCareCommandPaletteItem -Query $query) -Formatter ${function:Format-WinCareCommandRow} -SearchProperties @('Label','Description','Keywords')
    if($item){
        $history=[Collections.Generic.List[string]]::new()
        $history.Add([string]$item.Action)
        foreach($entry in @(Get-WinCareConfig 'CommandPaletteHistory')){if($entry -ne $item.Action -and $history.Count -lt 20){$history.Add([string]$entry)}}
        $null=Set-WinCareConfig -Name CommandPaletteHistory -Value @($history)
        Invoke-WinCareWorkspaceAction -Action $item.Action
    }
}

function Invoke-WinCareWorkspaceAction {
    param([Parameter(Mandatory)][string]$Action)
    switch($Action){
        'Dashboard'{Show-WinCareDashboardScreen};'Downloads'{Show-WinCareDownloadsScreen};'Telemetry'{Show-WinCareTelemetryScreen};'Launcher'{Show-WinCareLauncherScreen};'Layouts'{Show-WinCareWorkspaceLayoutsScreen};'GameState'{Show-WinCareGameStateScreen};'Quick'{Show-WinCareQuickActionsScreen};'Applications'{Show-WinCareApplicationsScreen};'Cleanup'{Show-WinCareCleanupScreen};'Storage'{Show-WinCareStorageScreen};'Profiles'{Show-WinCarePolicyProfilesScreen};'Desktop'{Show-WinCareDesktopControlsScreen};'Startup'{Show-WinCareStartupScreen};'Health'{Show-WinCareHealthScreen};'Security'{Show-WinCareSecurityScreen};'Wua'{Show-WinCareWindowsUpdateScreen};'Updates'{Show-WinCareUpdatesScreen};'Network'{Show-WinCareNetworkScreen};'Internals'{Show-WinCareInternalsScreen};'ExpertLab'{Show-WinCareExpertLabScreen};'WDAC'{Show-WinCareWdacScreen};'Baseline'{Show-WinCareSecurityBaselineScreen};'Provisioning'{Show-WinCareProvisioningScreen};'Offline'{Show-WinCareOfflineImageScreen};'Boot'{Show-WinCareBootScreen};'Shell'{Show-WinCareShellScreen};'Customization'{Show-WinCareCustomizationScreen};'Widgets'{Show-WinCareWidgetsScreen};'Bluetooth'{Show-WinCareBluetoothScreen};'Maintenance'{Show-WinCareMaintenanceScreen};'Playbooks'{Show-WinCarePlaybooksScreen};'Power'{Show-WinCarePowerSessionsScreen};'Windows'{Show-WinCareWindowWorkspaceScreen};'Color'{Show-WinCareColorStudioScreen};'Notes'{Show-WinCareLocalNotesScreen};'Browser'{Show-WinCareBrowserWorkspaceScreen};'RemoteSupport'{Show-WinCareRemoteSupportScreen};'WorkspaceStudio'{Show-WinCareWorkspaceStudioScreen};'CleanerPreview'{Show-WinCareCleanerPreviewStudioScreen};'PeerUtilities'{Show-WinCarePeerUtilitiesScreen};'Automation'{Show-WinCareAutomationScreen};'Recovery'{Show-WinCareRecoveryScreen};'Reports'{Show-WinCareReportsScreen};'Knowledge'{Show-WinCareKnowledgeScreen};'Settings'{Show-WinCareSettingsScreen};'Help'{Show-WinCareHelpScreen};'Palette'{Show-WinCareCommandPalette}
    }
}
