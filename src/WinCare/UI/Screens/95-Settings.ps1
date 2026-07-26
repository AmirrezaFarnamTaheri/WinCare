function Format-WinCareExtensionRow {param($Item,$Width);"$(Limit-WinCareText $Item.name ([math]::Max(24,$Width-32))) $(Limit-WinCareText $Item.version 12) $(Limit-WinCareText $Item.publisher 18)"}
function Show-WinCareExtensionManager {
    while($true){$extensions=@(Get-WinCareInstalledExtension);$menu=@(
        [pscustomobject]@{Label='Installed extensions';Description="$($extensions.Count) declarative extension(s)";Action='List'},
        [pscustomobject]@{Label='Install extension package';Description='ZIP admission, exact manifest, hashes, CMS signature policy, atomic activation';Action='Install'},
        [pscustomobject]@{Label='Remove extension';Description='Remove a data-only extension';Action='Remove';Disabled=$extensions.Count -eq 0},
        [pscustomobject]@{Label='Open extensions folder';Description=(Get-WinCareExtensionRoot);Action='Folder'},
        [pscustomobject]@{Label='Back';Description='Return to settings';Action='Back'}
    );$choice=Show-WinCareMenu -Title 'Declarative extensions' -Subtitle 'Extensions may add catalog rules and knowledge only; executable plugin code is never loaded' -Items $menu;if(-not $choice -or $choice.Action -eq 'Back'){return};switch($choice.Action){
        'List'{$lines=@($extensions|ForEach-Object{"$($_.id) | $($_.name) $($_.version) | $($_.publisher) | $($_.description)"});Show-WinCareTextPage -Title 'Installed extensions' -Lines $(if($lines.Count){$lines}else{@('No extensions are installed.')})}
        'Install'{$path=Read-WinCareInput -Prompt 'Extension ZIP path';if($path){try{Invoke-WinCarePlanFromUi (New-WinCareInstallExtensionPlan -ArchivePath $path)}catch{Show-WinCareMessage -Title 'Extension rejected' -Lines @($_.Exception.Message) -Kind Error}}}
        'Remove'{$ext=Show-WinCareListSelector -Title 'Remove extension' -Items $extensions -Formatter ${function:Format-WinCareExtensionRow} -SearchProperties @('name','id','publisher','version');if($ext){Invoke-WinCarePlanFromUi (New-WinCareRemoveExtensionPlan -ExtensionId ([string]$ext.id))}}
        'Folder'{$null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @((Get-WinCareExtensionRoot))}
    }}
}

function Get-WinCareSettingsMenu {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Config)
    $config=$Config
    return @(
            [pscustomobject]@{Label='Toggle read-only mode';Description=$(if($script:WinCareState.ReadOnlyLocked){'Locked on by launch or policy'}else{"Current: $($config.ReadOnly)"});Action='ReadOnly';Disabled=$script:WinCareState.ReadOnlyLocked},
            [pscustomobject]@{Label='Toggle ASCII rendering';Description="Current: $($config.Ascii)";Action='Ascii'},
            [pscustomobject]@{Label='Change theme';Description="Current: $($config.Theme)";Action='Theme'},
            [pscustomobject]@{Label='Toggle reduced motion';Description="Current: $($config.ReducedMotion)";Action='Motion'},
            [pscustomobject]@{Label='Toggle screen-reader mode';Description="Current: $($config.ScreenReaderMode)";Action='Reader'},
            [pscustomobject]@{Label='Toggle navigation persistence';Description="Current: $($config.PersistNavigation)";Action='Navigation'},
            [pscustomobject]@{Label='Require plan preview before changes';Description="Current: $($config.RequirePreviewForChanges)";Action='Preview'},
            [pscustomobject]@{Label='Include Microsoft Store applications';Description="Current: $($config.IncludeStoreApps)";Action='StoreApps'},
            [pscustomobject]@{Label='Large-file threshold';Description="Current: $($config.LargeFileThresholdGB) GB";Action='Threshold'},
            [pscustomobject]@{Label='Temporary-file minimum age';Description="Current: $($config.CleanupMinimumAgeHours) hours";Action='Age'},
            [pscustomobject]@{Label='Quarantine retention';Description="Current: $($config.QuarantineRetentionDays) days";Action='Retention'},
            [pscustomobject]@{Label='Log retention';Description="Current: $($config.LogRetentionDays) days";Action='LogRetention'},
            [pscustomobject]@{Label='Toggle report user-name redaction';Description="Current: $($config.RedactUserNameInReports)";Action='Redact'},
            [pscustomobject]@{Label='Widget refresh interval';Description="Current: $($config.WidgetRefreshSeconds) seconds";Action='WidgetRefresh'},
            [pscustomobject]@{Label='Widget layout';Description="Current: $(@($config.WidgetLayout)-join ', ')";Action='WidgetLayout'},
            [pscustomobject]@{Label='Maintenance notice lead time';Description="Current: $($config.MaintenanceNoticeMinutes) minutes";Action='MaintenanceNotice'},
            [pscustomobject]@{Label='External customizer mode';Description="Current: $($config.ExternalCustomizerMode)";Action='CustomizerMode'},
            [pscustomobject]@{Label='Bluetooth scan timeout';Description="Current: $($config.BluetoothScanTimeoutSeconds) seconds";Action='BluetoothTimeout'},
            [pscustomobject]@{
                Label='Network experiment targets'
                Description=$(if(@($config.NetworkExperimentTargets).Count){@($config.NetworkExperimentTargets)-join ', '}else{'Not configured; no public endpoint is probed automatically'})
                Action='NetworkTargets'
            },
            [pscustomobject]@{Label='Workspace mode';Description="Current: $($config.WorkspaceMode)";Action='WorkspaceMode'},
            [pscustomobject]@{Label='Default power-session duration';Description="Current: $($config.PowerSessionDefaultMinutes) minutes";Action='PowerDuration'},
            [pscustomobject]@{Label='Power-session refresh interval';Description="Current: $($config.PowerSessionRefreshSeconds) seconds";Action='PowerRefresh'},
            [pscustomobject]@{Label='Browser extension inventory limit';Description="Current: $($config.BrowserExtensionInventoryLimit)";Action='BrowserLimit'},
            [pscustomobject]@{Label='Default remote-consent duration';Description="Current: $($config.RemoteConsentDefaultMinutes) minutes";Action='RemoteConsent'},
            [pscustomobject]@{Label='Command palette result limit';Description="Current: $($config.CommandPaletteMaxResults)";Action='PaletteLimit'},
            [pscustomobject]@{Label='Declarative extensions';Description='Signed, hash-bound catalog and knowledge packages';Action='Extensions'},
            [pscustomobject]@{Label='View effective policy';Description=[string](Get-WinCarePolicy 'PolicyName');Action='Policy'},
            [pscustomobject]@{Label='Reset settings';Description='Restore product defaults';Action='Reset'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
}

function Show-WinCareSettingsScreen {
    while($true){
        $config=Get-WinCareConfig
        $menu=@(Get-WinCareSettingsMenu -Config $config)
        $choice=Show-WinCareMenu -Title 'Settings' -Subtitle "Configuration: $($script:WinCareState.ConfigPath) | Telemetry: permanently disabled" -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'ReadOnly'{$null=Set-WinCareConfig ReadOnly (-not [bool]$config.ReadOnly)};'Ascii'{$null=Set-WinCareConfig Ascii (-not [bool]$config.Ascii)};'Motion'{$null=Set-WinCareConfig ReducedMotion (-not [bool]$config.ReducedMotion)};'Reader'{$null=Set-WinCareConfig ScreenReaderMode (-not [bool]$config.ScreenReaderMode)};'Navigation'{$null=Set-WinCareConfig PersistNavigation (-not [bool]$config.PersistNavigation)};'Preview'{$null=Set-WinCareConfig RequirePreviewForChanges (-not [bool]$config.RequirePreviewForChanges)};'StoreApps'{$null=Set-WinCareConfig IncludeStoreApps (-not [bool]$config.IncludeStoreApps)}
            'Theme'{$theme=Show-WinCareMenu -Title 'Theme' -Items @([pscustomobject]@{Label='Normal';Description='Cyan accents and semantic status colors';Value='Normal'},[pscustomobject]@{Label='High contrast';Description='Bright foreground and yellow selection';Value='HighContrast'},[pscustomobject]@{Label='Monochrome';Description='Minimal color use';Value='Monochrome'});if($theme){$null=Set-WinCareConfig Theme $theme.Value}}
            'Threshold'{$text=Read-WinCareInput -Prompt 'Minimum size in GB' -Default ([string]$config.LargeFileThresholdGB);$value=1;if([int]::TryParse($text,[ref]$value)-and$value-ge 1-and$value-le 10240){$null=Set-WinCareConfig LargeFileThresholdGB $value}}
            'Age'{$text=Read-WinCareInput -Prompt 'Minimum age in hours' -Default ([string]$config.CleanupMinimumAgeHours);$value=24;if([int]::TryParse($text,[ref]$value)-and$value-ge 0-and$value-le 87600){$null=Set-WinCareConfig CleanupMinimumAgeHours $value}}
            'Retention'{$text=Read-WinCareInput -Prompt 'Quarantine retention days' -Default ([string]$config.QuarantineRetentionDays);$value=14;if([int]::TryParse($text,[ref]$value)-and$value-ge 1-and$value-le 3650){$null=Set-WinCareConfig QuarantineRetentionDays $value}}
            'LogRetention'{$text=Read-WinCareInput -Prompt 'Log retention days' -Default ([string]$config.LogRetentionDays);$value=30;if([int]::TryParse($text,[ref]$value)-and$value-ge 1-and$value-le 3650){$null=Set-WinCareConfig LogRetentionDays $value}}
            'Redact'{$null=Set-WinCareConfig RedactUserNameInReports (-not [bool]$config.RedactUserNameInReports)}
            'WidgetRefresh'{$text=Read-WinCareInput -Prompt 'Widget refresh seconds (1..3600)' -Default ([string]$config.WidgetRefreshSeconds);$value=15;if([int]::TryParse($text,[ref]$value)-and$value-ge 1-and$value-le 3600){$null=Set-WinCareConfig WidgetRefreshSeconds $value}}
            'WidgetLayout'{$available=@(Get-WinCareWidgetDefinition);$selected=Show-WinCareListSelector -Title 'Widget layout' -Subtitle 'Space selects; order follows catalog after deduplication' -Items $available -Formatter {param($item,$width);"$(Limit-WinCareText $item.title ([math]::Max(24,$width-28))) $($item.id)"} -SearchProperties @('title','id','description') -MultiSelect;if($selected){$null=Set-WinCareConfig WidgetLayout @($selected.id)}}
            'MaintenanceNotice'{$text=Read-WinCareInput -Prompt 'Default notice lead time in minutes' -Default ([string]$config.MaintenanceNoticeMinutes);$value=60;if([int]::TryParse($text,[ref]$value)-and$value-ge 0-and$value-le 10080){$null=Set-WinCareConfig MaintenanceNoticeMinutes $value}}
            'CustomizerMode'{$mode=Show-WinCareMenu -Title 'External customizer mode' -Items @([pscustomobject]@{Label='Read only';Description='Inventory only; no external host files are changed';Value='ReadOnly'},[pscustomobject]@{Label='Backup and restore';Description='Allow exact backup/restore workflows when a supported adapter exists';Value='BackupAndRestore'},[pscustomobject]@{Label='Managed';Description='Policy-gated managed integration for supported data surfaces';Value='Managed'});if($mode){$null=Set-WinCareConfig ExternalCustomizerMode $mode.Value}}
            'BluetoothTimeout'{$text=Read-WinCareInput -Prompt 'Bluetooth scan timeout seconds (1..60)' -Default ([string]$config.BluetoothScanTimeoutSeconds);$value=10;if([int]::TryParse($text,[ref]$value)-and$value-ge 1-and$value-le 60){$null=Set-WinCareConfig BluetoothScanTimeoutSeconds $value}}
            'NetworkTargets'{
                $text=Read-WinCareInput -Prompt 'Organization-controlled network hosts/IPs, comma-separated; blank disables probes' -Default (@($config.NetworkExperimentTargets)-join ',')
                $targets=@($text.Split(',',[StringSplitOptions]::RemoveEmptyEntries)|ForEach-Object{$_.Trim()}|Where-Object{$_})
                $null=Set-WinCareConfig NetworkExperimentTargets $targets
            }
            'WorkspaceMode'{$mode=Show-WinCareMenu -Title 'Workspace mode' -Items @(@('All','Maintenance','Productivity','Support','Developer','Minimal')|ForEach-Object{[pscustomobject]@{Label=$_;Description="Show the $_ workspace set";Value=$_}});if($mode){$null=Set-WinCareConfig WorkspaceMode $mode.Value}}
            'PowerDuration'{$text=Read-WinCareInput -Prompt 'Default duration minutes (5..10080)' -Default ([string]$config.PowerSessionDefaultMinutes);$value=60;if([int]::TryParse($text,[ref]$value)-and$value-ge 5-and$value-le 10080){$null=Set-WinCareConfig PowerSessionDefaultMinutes $value}}
            'PowerRefresh'{$text=Read-WinCareInput -Prompt 'Refresh seconds (5..300)' -Default ([string]$config.PowerSessionRefreshSeconds);$value=30;if([int]::TryParse($text,[ref]$value)-and$value-ge 5-and$value-le 300){$null=Set-WinCareConfig PowerSessionRefreshSeconds $value}}
            'BrowserLimit'{$text=Read-WinCareInput -Prompt 'Extension inventory limit (100..50000)' -Default ([string]$config.BrowserExtensionInventoryLimit);$value=5000;if([int]::TryParse($text,[ref]$value)-and$value-ge 100-and$value-le 50000){$null=Set-WinCareConfig BrowserExtensionInventoryLimit $value}}
            'RemoteConsent'{$text=Read-WinCareInput -Prompt 'Default consent minutes (5..1440)' -Default ([string]$config.RemoteConsentDefaultMinutes);$value=60;if([int]::TryParse($text,[ref]$value)-and$value-ge 5-and$value-le 1440){$null=Set-WinCareConfig RemoteConsentDefaultMinutes $value}}
            'PaletteLimit'{$text=Read-WinCareInput -Prompt 'Command palette result limit (10..200)' -Default ([string]$config.CommandPaletteMaxResults);$value=50;if([int]::TryParse($text,[ref]$value)-and$value-ge 10-and$value-le 200){$null=Set-WinCareConfig CommandPaletteMaxResults $value}}
            'Extensions'{Show-WinCareExtensionManager}
            'Policy'{$lines=@((Get-WinCarePolicy).GetEnumerator()|Sort-Object Key|ForEach-Object{"$($_.Key): $($_.Value -join ', ')"});Show-WinCareTextPage -Title 'Effective policy' -Lines $lines}
            'Reset'{$answer=Read-WinCareInput -Prompt 'Type RESET to restore defaults';if($answer -ceq 'RESET'){Reset-WinCareConfig}}
        }
    }
}

