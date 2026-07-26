function Format-WinCareLegacyUnsafeProfileRow {
    param($Item,$Width)
    "$(Limit-WinCareText $Item.Id 24)  $(Limit-WinCareText $Item.Title ([math]::Max(20,$Width-28)))"
}

function Show-WinCarePeerUtilitiesScreen {
    while($true){
        $menu=@(
            [pscustomobject]@{Label='System toolkit';Description='Diagnostics, hardening, maintenance templates, shortcut export, batch downloads, and torrent metadata';Action='Toolkit'},
            [pscustomobject]@{Label='Experience Studio';Description='Visual assets, location privacy, remote access, power conditions, download strategy, and privacy profiles';Action='Experience'},
            [pscustomobject]@{Label='Shell and Hardware Studio';Description='Hardware, DDC/CI displays, Explorer sessions, AppX runtime, archives, terminals, and GUI inspection';Action='ShellHardware'},
            [pscustomobject]@{Label='Legacy Unsafe one-click profiles';Description='Permanent and destructive compatibility profiles with Critical confirmation';Action='Unsafe'},
            [pscustomobject]@{Label='Cleanup and AppX management';Description='Explorer visibility, bounded deep cleanup, list/allowlist AppX removal, all-users and provisioned modes';Action='CleanupMods'},
            [pscustomobject]@{Label='Display overrides';Description='Inspect CRU-style EDID overrides and optionally reset all topology caches';Action='Display'},
            [pscustomobject]@{Label='WLAN survey';Description='Interfaces, SSIDs, BSSIDs, signal, channel, and security from bounded netsh output';Action='Wlan'},
            [pscustomobject]@{Label='Log snapshot';Description='Tail a file or inspect a Windows Event Log with an optional regex filter';Action='Logs'},
            [pscustomobject]@{Label='PE metadata';Description='Read MZ/PE headers, architecture, hash, version, and signature without execution';Action='Pe'},
            [pscustomobject]@{Label='Container log configuration';Description='Generate strict EventLog/file/ETW-to-stdout configuration under WinCare state';Action='Container'},
            [pscustomobject]@{Label='Local task board';Description='Private task notes with Pending, Doing, and Done states';Action='Tasks'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Advanced utilities and Legacy Unsafe' -Subtitle 'All behavior is policy-gated, previewed, journaled, and recoverable where technically possible' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'Toolkit'{
                    $sub=@(
                        [pscustomobject]@{Label='Diagnostic snapshot';Description='Hardware, pending reboot, battery, power, hotfix, Secure Boot, and Defender';Action='Diagnostics'},
                        [pscustomobject]@{Label='Win32 error lookup';Description='Resolve a numeric Windows error code';Action='Error'},
                        [pscustomobject]@{Label='MSI metadata';Description='Read package properties without installation';Action='Msi'},
                        [pscustomobject]@{Label='Hardening profiles';Description='Assess or apply balanced, strict, and Hail Mary policy sets';Action='Hardening'},
                        [pscustomobject]@{Label='Maintenance templates';Description='Create validated health, patch, or backup windows';Action='Maintenance'},
                        [pscustomobject]@{Label='System shortcut catalog';Description='Search and export ms-settings, shell, and CLSID targets';Action='Shortcuts'},
                        [pscustomobject]@{Label='Queue download batch';Description='Import a bounded JSON batch into the verified BITS queue';Action='Downloads'},
                        [pscustomobject]@{Label='Torrent metadata';Description='Inspect a local .torrent without peers, DHT, trackers, or streaming';Action='Torrent'},
                        [pscustomobject]@{Label='Back';Description='Return to peer utilities';Action='Back'}
                    )
                    $subChoice=Show-WinCareMenu -Title 'System toolkit' -Subtitle 'Capabilities use existing WinCare authority and state owners' -Items $sub
                    if($subChoice -and $subChoice.Action -ne 'Back'){
                        switch($subChoice.Action){
                            'Diagnostics'{$item=Get-WinCareSystemToolkitDiagnostic;Show-WinCareTextPage -Title 'System toolkit diagnostic' -Lines @(($item|Format-List *|Out-String -Width 180) -split "`r?`n")}
                            'Error'{$item=Get-WinCareWin32ErrorMessage -Code ([int](Read-WinCareInput -Prompt 'Win32 error code' -Default '5'));Show-WinCareTextPage -Title 'Win32 error' -Lines @("$($item.Hex) ($($item.Code))","$($item.Message)")}
                            'Msi'{$item=Get-WinCareMsiMetadata -LiteralPath (Read-WinCareInput -Prompt 'MSI or MSP path');Show-WinCareTextPage -Title 'MSI metadata' -Lines @("Path: $($item.Path)","SHA-256: $($item.Sha256)",'')+@($item.Properties.GetEnumerator()|ForEach-Object{"$($_.Key): $($_.Value)"})}
                            'Hardening'{$profile=Show-WinCareListSelector -Title 'Hardening profiles' -Subtitle 'Hail Mary is Critical and may break compatibility' -Items @(Get-WinCareHardeningProfile) -Formatter ${function:Format-WinCareLegacyUnsafeProfileRow} -SearchProperties @('Id','Title','Description');if($profile){$assessment=Get-WinCareHardeningAssessment -ProfileId $profile.Id;Show-WinCareTextPage -Title $profile.Title -Lines @("Compliant: $($assessment.CompliantCount)","Non-compliant: $($assessment.NonCompliantCount)",'')+@($assessment.Findings|ForEach-Object{"[$($_.Compliant)] $($_.Label): $($_.Actual) -> $($_.Expected)"});if((Read-WinCareInput -Prompt 'Build the hardening plan? (y/N)' -Default 'N') -match '^(?i)y(es)?$'){Invoke-WinCarePlanFromUi (New-WinCareHardeningProfilePlan -ProfileId $profile.Id)}}}
                            'Maintenance'{$template=Show-WinCareListSelector -Title 'Maintenance templates' -Subtitle 'Creates a revisioned maintenance lifecycle record' -Items @(Get-WinCareMaintenanceTemplate) -Formatter ${function:Format-WinCareLegacyUnsafeProfileRow} -SearchProperties @('Id','Title','Description');if($template){$start=[datetimeoffset]::Parse((Read-WinCareInput -Prompt 'Start time (ISO 8601)' -Default ([datetimeoffset]::Now.AddHours(1).ToString('o'))));Invoke-WinCarePlanFromUi (New-WinCareMaintenanceTemplatePlan -TemplateId $template.Id -StartAt $start)}}
                            'Shortcuts'{$query=Read-WinCareInput -Prompt 'Optional search text' -Default '';$items=@(Get-WinCareSystemShortcutCatalog -Query $query -Limit 250);Show-WinCareTextPage -Title 'System shortcuts' -Lines @($items|ForEach-Object{"$($_.Title)  [$($_.Target)]"});if((Read-WinCareInput -Prompt 'Export to WinCare state? (y/N)' -Default 'N') -match '^(?i)y(es)?$'){Invoke-WinCarePlanFromUi (New-WinCareSystemShortcutExportPlan -Query $query)}}
                            'Downloads'{$batch=Import-WinCareDownloadBatchDefinition -LiteralPath (Read-WinCareInput -Prompt 'Download batch JSON path');Invoke-WinCarePlanFromUi (New-WinCareDownloadBatchPlan -Batch $batch)}
                            'Torrent'{$item=Get-WinCareTorrentMetadata -LiteralPath (Read-WinCareInput -Prompt 'Local .torrent path');Show-WinCareTextPage -Title 'Torrent metadata' -Lines @("Name: $($item.Name)","Files: $($item.FileCount)","Bytes: $($item.TotalLength)","Piece length: $($item.PieceLength)","Private: $($item.Private)","Network execution: $($item.NetworkExecution)","Announce: $($item.Announce)")}
                        }
                    }
                }
                'ShellHardware'{Show-WinCareShellHardwareStudioScreen}
                'Experience'{
                    $sub=@(
                        [pscustomobject]@{Label='Capability cards';Description='Search the target-native experience capability catalog';Action='Cards'},
                        [pscustomobject]@{Label='Visual asset and sprite manifest';Description='Inspect an image and optionally export a bounded frame map';Action='Visual'},
                        [pscustomobject]@{Label='Location privacy';Description='Assess or set Allow, Deny, or SystemManaged without disabling services';Action='Location'},
                        [pscustomobject]@{Label='Remote access';Description='Assess or configure RDP and OpenSSH through typed actions';Action='Remote'},
                        [pscustomobject]@{Label='Power condition profiles';Description='Battery/AC-aware power-scheme and brightness recommendations';Action='Power'},
                        [pscustomobject]@{Label='Download strategy';Description='Explain BITS mirror failover, retries, scheduling, and hash controls';Action='Download'},
                        [pscustomobject]@{Label='Privacy profiles';Description='Apply balanced, strict, or maximum reversible privacy catalogs';Action='Privacy'},
                        [pscustomobject]@{Label='Back';Description='Return to peer utilities';Action='Back'}
                    )
                    $subChoice=Show-WinCareMenu -Title 'Experience Studio' -Subtitle 'Capabilities use WinCare state, policy, transactions, and recovery' -Items $sub
                    if($subChoice -and $subChoice.Action -ne 'Back'){
                        switch($subChoice.Action){
                            'Cards'{$query=Read-WinCareInput -Prompt 'Optional search text' -Default '';$items=@(Get-WinCareExperienceCapabilityCard -Query $query);Show-WinCareTextPage -Title 'Experience capability cards' -Lines @($items|ForEach-Object{"$($_.Title) [$($_.Risk)] - $($_.Summary)"})}
                            'Visual'{$path=Read-WinCareInput -Prompt 'Image path';$item=Get-WinCareVisualAssetInfo -LiteralPath $path;Show-WinCareTextPage -Title 'Visual asset' -Lines @("Path: $($item.Path)","SHA-256: $($item.Sha256)","Dimensions: $($item.Width)x$($item.Height)","Frames: $($item.FrameCount)","Palette entries: $($item.PaletteCount)");if((Read-WinCareInput -Prompt 'Export a visual manifest? (y/N)' -Default 'N') -match '^(?i)y(es)?$'){$frameWidth=[int](Read-WinCareInput -Prompt 'Frame width' -Default ([string]$item.Width));$frameHeight=[int](Read-WinCareInput -Prompt 'Frame height' -Default ([string]$item.Height));Invoke-WinCarePlanFromUi (New-WinCareVisualAssetManifestPlan -LiteralPath $path -FrameWidth $frameWidth -FrameHeight $frameHeight -IncludeSpriteLayout)}}
                            'Location'{$state=Get-WinCareLocationPrivacyState;Show-WinCareTextPage -Title 'Location privacy' -Lines @("Effective mode: $($state.EffectiveMode)","Service mutation: $($state.ServiceMutation)");$mode=Read-WinCareInput -Prompt 'Mode: Allow, Deny, SystemManaged, or blank to exit' -Default '';if($mode){Invoke-WinCarePlanFromUi (New-WinCareLocationPrivacyPlan -Mode $mode)}}
                            'Remote'{$state=Get-WinCareRemoteAccessState;Show-WinCareTextPage -Title 'Remote access state' -Lines @(($state|Format-List *|Out-String -Width 180)-split "`r?`n");$profile=Read-WinCareInput -Prompt 'Profile: rdp, openssh, or blank to exit' -Default '';if($profile){$enabled=(Read-WinCareInput -Prompt 'Enable? (Y/n)' -Default 'Y') -notmatch '^(?i)n(o)?$';$redirect=$false;if($profile -eq 'rdp' -and $enabled){$redirect=(Read-WinCareInput -Prompt 'Allow drive redirection? (y/N)' -Default 'N') -match '^(?i)y(es)?$'};Invoke-WinCarePlanFromUi (New-WinCareRemoteAccessPlan -ProfileId $profile -Enabled $enabled -AllowDriveRedirection $redirect)}}
                            'Power'{$assessment=Get-WinCarePowerConditionAssessment;Show-WinCareTextPage -Title 'Power condition assessment' -Lines @("Power line: $($assessment.PowerLineStatus)","Charge: $($assessment.ChargePercent)","Recommended: $($assessment.RecommendedProfile)");$profile=Show-WinCareListSelector -Title 'Power condition profiles' -Subtitle 'Power scheme plus best-effort internal display brightness' -Items @(Get-WinCarePowerConditionProfile) -Formatter ${function:Format-WinCareLegacyUnsafeProfileRow} -SearchProperties @('Id','Title','Condition');if($profile){Invoke-WinCarePlanFromUi (New-WinCarePowerConditionPlan -ProfileId $profile.Id)}}
                            'Download'{$rows=@(Get-WinCareDownloadStrategy);if(-not $rows.Count){Show-WinCareMessage -Title 'Download strategy' -Lines @('No download jobs exist.') -Kind Info}else{Show-WinCareTextPage -Title 'Download strategy' -Lines @($rows|ForEach-Object{"$($_.Name): $($_.Strategy), URLs=$($_.UrlCount), retries=$($_.MaxRetries), hash=$($_.HashAlgorithm)"})}}
                            'Privacy'{$profile=Show-WinCareListSelector -Title 'Privacy profiles' -Subtitle 'For destructive persistent reductions use the separately authorized Legacy Unsafe profiles' -Items @(Get-WinCarePrivacyProfile) -Formatter ${function:Format-WinCareLegacyUnsafeProfileRow} -SearchProperties @('Id','Title','Description');if($profile){Invoke-WinCarePlanFromUi (New-WinCarePrivacyProfilePlan -ProfileId $profile.Id)}}
                        }
                    }
                }
                'Unsafe'{
                    $profile=Show-WinCareListSelector -Title 'Legacy Unsafe profiles' -Subtitle 'These profiles can reduce security, networking, services, tasks, applications, storage, and display availability' -Items @(Get-WinCareLegacyUnsafeProfile) -Formatter ${function:Format-WinCareLegacyUnsafeProfileRow} -SearchProperties @('Id','Title','Description')
                    if($profile){$include=(Read-WinCareInput -Prompt 'Also remove matching provisioned AppX packages? (y/N)' -Default 'N') -match '^(?i)y(es)?$';Invoke-WinCarePlanFromUi (New-WinCareLegacyUnsafeProfilePlan -ProfileId $profile.Id -IncludeProvisionedAppx:$include)}
                }
                'CleanupMods'{
                    $sub=@(
                        [pscustomobject]@{Label='Explorer quick settings';Description='Show or hide extensions and hidden files; choose This PC or Home';Action='Explorer'},
                        [pscustomobject]@{Label='Deep cleanup profiles';Description='Bounded safe or Critical aggressive cleanup';Action='Deep'},
                        [pscustomobject]@{Label='AppX selection profiles';Description='Curated, allowlist, all-removable, and Copilot package plans';Action='Appx'},
                        [pscustomobject]@{Label='Custom AppX list';Description='Load a bounded .txt/.list wildcard file';Action='Custom'},
                        [pscustomobject]@{Label='Back';Description='Return to peer utilities';Action='Back'}
                    )
                    $subChoice=Show-WinCareMenu -Title 'Cleanup and AppX management' -Subtitle 'All changes are previewed and journaled; Critical profiles require explicit authorization' -Items $sub
                    if($subChoice -and $subChoice.Action -ne 'Back'){
                        switch($subChoice.Action){
                            'Explorer'{$showExt=(Read-WinCareInput -Prompt 'Show file extensions? (Y/n)' -Default 'Y') -notmatch '^(?i)n(o)?$';$showHidden=(Read-WinCareInput -Prompt 'Show hidden files? (y/N)' -Default 'N') -match '^(?i)y(es)?$';$thisPc=(Read-WinCareInput -Prompt 'Open Explorer to This PC? (Y/n)' -Default 'Y') -notmatch '^(?i)n(o)?$';Invoke-WinCarePlanFromUi (New-WinCareExplorerQuickPlan -ShowExtensions $showExt -ShowHidden $showHidden -LaunchThisPc $thisPc)}
                            'Deep'{$profile=Show-WinCareListSelector -Title 'Deep cleanup profiles' -Subtitle 'The aggressive profile excludes drive-root sweeps, event logs, Prefetch, Defender evidence, and network reset' -Items @(Get-WinCareDeepCleanupProfile) -Formatter ${function:Format-WinCareLegacyUnsafeProfileRow} -SearchProperties @('Id','Title','Description');if($profile){Invoke-WinCarePlanFromUi (New-WinCareDeepCleanupPlan -ProfileId $profile.Id)}}
                            'Appx'{$profile=Show-WinCareListSelector -Title 'AppX selection profiles' -Subtitle 'Removal is not automatically reversible' -Items @(Get-WinCareAppxSelectionProfile) -Formatter ${function:Format-WinCareLegacyUnsafeProfileRow} -SearchProperties @('Id','Title','Description');if($profile){$scope=Read-WinCareInput -Prompt 'Scope: CurrentUser or AllUsers' -Default 'CurrentUser';$include=(Read-WinCareInput -Prompt 'Also remove provisioned packages? (y/N)' -Default 'N') -match '^(?i)y(es)?$';Invoke-WinCarePlanFromUi (New-WinCareAppxSelectionPlan -ProfileId $profile.Id -Scope $scope -IncludeProvisioned:$include)}}
                            'Custom'{$list=Import-WinCareAppxSelectionList -LiteralPath (Read-WinCareInput -Prompt 'Path to .txt or .list file');$mode=Read-WinCareInput -Prompt 'Mode: Include removes matches; Exclude keeps matches' -Default 'Include';$scope=Read-WinCareInput -Prompt 'Scope: CurrentUser or AllUsers' -Default 'CurrentUser';$include=(Read-WinCareInput -Prompt 'Also remove provisioned packages? (y/N)' -Default 'N') -match '^(?i)y(es)?$';Invoke-WinCarePlanFromUi (New-WinCareAppxSelectionPlan -Pattern @($list.Patterns) -Mode $mode -Scope $scope -IncludeProvisioned:$include)}
                        }
                    }
                }
                'Display'{
                    $state=Get-WinCareDisplayOverrideState
                    $lines=@("Snapshot: $($state.SnapshotHash)","Configuration cache present: $($state.ConfigurationPresent)","Connectivity cache present: $($state.ConnectivityPresent)","Device parameter keys: $($state.DeviceParameterCount)","EDID override keys: $($state.OverrideDeviceCount)",'',$state.RecoveryWarning,'')+@($state.Devices|ForEach-Object{"$($_.Path) override=$($_.HasEdidOverride) edid=$($_.HasEdid) recovery=$($_.HasRecovery)"})
                    Show-WinCareTextPage -Title 'Display override state' -Lines $lines
                    if((Read-WinCareInput -Prompt 'Build Critical reset plan? (y/N)' -Default 'N') -match '^(?i)y(es)?$'){Invoke-WinCarePlanFromUi (New-WinCareDisplayOverrideResetPlan)}
                }
                'Wlan'{
                    $survey=Get-WinCareWlanSurvey;$lines=@("Parsed: $($survey.Parsed)",$survey.LocaleNote,'')+@($survey.Networks|ForEach-Object{"$($_.Ssid)  $($_.Bssid)  signal=$($_.SignalPercent)% channel=$($_.Channel) $($_.Authentication)/$($_.Encryption)"})
                    if(-not $survey.Parsed){$lines+=@('','Raw network output:')+@($survey.NetworksRaw -split "`r?`n")}
                    Show-WinCareTextPage -Title 'WLAN survey' -Lines $lines
                }
                'Logs'{
                    $kind=Read-WinCareInput -Prompt 'Enter FILE or EVENTLOG' -Default 'EVENTLOG';$pattern=Read-WinCareInput -Prompt 'Optional regex filter' -Default ''
                    $rows=if($kind -match '^(?i)file$'){Get-WinCareLogSnapshot -LiteralPath (Read-WinCareInput -Prompt 'Literal log path') -Limit 500 -Pattern $pattern}else{Get-WinCareLogSnapshot -LogName (Read-WinCareInput -Prompt 'Event Log name' -Default 'System') -Limit 500 -Pattern $pattern}
                    Show-WinCareTextPage -Title 'Log snapshot' -Lines @($rows|ForEach-Object{"$($_.Timestamp) [$($_.Level)] $($_.Source): $($_.Message)"})
                }
                'Pe'{
                    $item=Get-WinCarePeMetadata -LiteralPath (Read-WinCareInput -Prompt 'Executable or DLL path')
                    Show-WinCareTextPage -Title 'PE metadata' -Lines @("Path: $($item.Path)","SHA-256: $($item.Sha256)","Architecture: $($item.Architecture)","Kind: $($item.PeKind)","Sections: $($item.Sections)","Timestamp: $($item.TimestampUtc)","Signature: $($item.SignatureStatus)","Signer: $($item.Signer)")
                }
                'Container'{Invoke-WinCarePlanFromUi (New-WinCareContainerLogMonitorPlan)}
                'Tasks'{
                    $items=@(Get-WinCareTaskBoard);$lines=@($items|ForEach-Object{"$($_.Title) [$(@($_.Tags)-join ', ')] updated=$($_.UpdatedAt)"});if(-not $lines.Count){$lines=@('No task notes exist.')};Show-WinCareTextPage -Title 'Local task board' -Lines $lines
                    if((Read-WinCareInput -Prompt 'Create a task? (y/N)' -Default 'N') -match '^(?i)y(es)?$'){$title=Read-WinCareInput -Prompt 'Title';$body=Read-WinCareInput -Prompt 'Details';Invoke-WinCarePlanFromUi (New-WinCareTaskPlan -Title $title -Body $body)}
                }
            }
        }catch{Show-WinCareMessage -Title 'Peer utility failed' -Lines @($_.Exception.Message) -Kind Danger}
    }
}

