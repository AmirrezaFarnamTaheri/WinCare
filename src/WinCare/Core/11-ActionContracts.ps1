


function ConvertTo-WinCareParameterDictionary {
    param([object]$Value)
    if($null -eq $Value){return @{}}
    $result=@{};if($Value -is [Collections.IDictionary]){foreach($key in $Value.Keys){$result[[string]$key]=$Value[$key]};return $result}
    foreach($property in $Value.PSObject.Properties){$result[$property.Name]=$property.Value};$result
}

function Test-WinCareActionContract {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action,[switch]$ForElevation,[switch]$SkipCompensatorValidation)
    try{
        $requiredActionFields=@('SchemaVersion','Id','Type','Label','Risk','Parameters','RequiresAdmin','Reversible','EstimatedBytes','Verification','Preconditions','Postconditions','Compensator','TimeoutSeconds','IdempotencyKey','Tags','Compatibility','SourceRecords','RecoveryDescription','RestartPossible')
        $properties=if($Action -is [Collections.IDictionary]){@($Action.Keys)}else{@($Action.PSObject.Properties.Name)}
        $missingAction=@($requiredActionFields|Where-Object{$_ -notin $properties});if($missingAction.Count){throw "Action is missing field(s): $($missingAction -join ', ')"}
        $unknownAction=@($properties|Where-Object{$_ -notin @('PSTypeName')+$requiredActionFields});if($unknownAction.Count){throw "Action contains unknown field(s): $($unknownAction -join ', ')"}
        if([int]$Action.SchemaVersion -ne 3){throw 'Unsupported action schema version.'}
        if([string]$Action.Id -notmatch '^[a-f0-9]{32}$'){throw 'Action ID is invalid.'}
        if([string]::IsNullOrWhiteSpace([string]$Action.Label) -or ([string]$Action.Label).Length -gt 240){throw 'Action label is invalid.'}
        $contract=Get-WinCareActionContract -Type ([string]$Action.Type);if(-not $contract){throw "Unknown action type: $($Action.Type)"}
        if([string]$Action.Risk -notin @('ReadOnly','Low','Moderate','High','Critical')){throw 'Action risk is invalid.'}
        if((Get-WinCareRiskRank ([string]$Action.Risk)) -lt (Get-WinCareRiskRank ([string]$contract.MinimumRisk))){throw "Action risk is below the contract minimum of $($contract.MinimumRisk)."}
        if([int]$Action.TimeoutSeconds -lt 1 -or [int]$Action.TimeoutSeconds -gt [int]$contract.MaximumTimeout){throw "Action timeout exceeds its contract maximum of $($contract.MaximumTimeout) seconds."}
        if([long]$Action.EstimatedBytes -lt 0){throw 'EstimatedBytes cannot be negative.'}
        if([string]$Action.IdempotencyKey -notmatch '^[a-f0-9]{64}$'){throw 'Idempotency key is invalid.'}
        if($contract.AlwaysAdmin -and -not [bool]$Action.RequiresAdmin){throw 'Action contract requires administrator privileges.'}
        if($ForElevation -and (-not [bool]$Action.RequiresAdmin -or -not [bool]$contract.ElevationAllowed)){throw 'Action is not eligible for elevation.'}
        $parameters=ConvertTo-WinCareParameterDictionary $Action.Parameters
        $missing=@($contract.Required|Where-Object{$_ -notin @($parameters.Keys)});if($missing.Count){throw "Action parameter(s) missing: $($missing -join ', ')"}
        $unknown=@($parameters.Keys|Where-Object{$_ -notin @($contract.Allowed)});if($unknown.Count){throw "Unknown action parameter(s): $($unknown -join ', ')"}
        switch([string]$Action.Type){
            {$_ -in @('DeleteCleanupTarget','ClearDeliveryOptimization')} {
                if(([string]$parameters.Id) -notmatch '^[a-z0-9][a-z0-9-]{2,80}$'){throw 'Cleanup target ID is invalid.'}
                if([string]::IsNullOrWhiteSpace([string]$parameters.Name) -or ([string]$parameters.Name).Length -gt 160){throw 'Cleanup target name is invalid.'}
                if([string]::IsNullOrWhiteSpace([string]$parameters.Path) -or ([string]$parameters.Path).Length -gt 32760){throw 'Cleanup target path is invalid.'}
                if(([string]$parameters.Pattern).Length -gt 260 -or [string]$parameters.Pattern -match '[\\/:]' -or [string]$parameters.Pattern -match '(^|[.])[.](?:$|[.])'){throw 'Cleanup target pattern is invalid.'}
                if([int]$parameters.MinimumAgeHours -lt 0 -or [int]$parameters.MinimumAgeHours -gt 87600){throw 'Cleanup minimum age is outside 0..87600 hours.'}
                $allowedKinds=if([string]$Action.Type -eq 'ClearDeliveryOptimization'){@('NativeDeliveryOptimization')}else{@('Files','DirectoryContents')}
                if([string]$parameters.Kind -notin $allowedKinds){throw "Cleanup target kind is invalid for $($Action.Type)."}
            }
            {$_ -in @('RemoveAppxPackage','RemoveAppxPackageAllUsers')} {
                if([string]$parameters.PackageFullName -notmatch '^[A-Za-z0-9._~-]{3,512}$'){throw 'AppX package full name is invalid.'}
                if($parameters.ContainsKey('Name') -and ([string]$parameters.Name).Length -gt 256){throw 'AppX package name is invalid.'}
            }
            {$_ -in @('ClearWindowsUpdateDownloadCache','StartComponentCleanup')} {
                if($parameters.Count -ne 0){throw 'The fixed maintenance action does not accept parameters.'}
            }
            'RunProcess' {
                if($ForElevation){
                    $file=[IO.Path]::GetFileName([string]$parameters.FilePath).ToLowerInvariant();$arguments=@($parameters.Arguments)
                    if($file -ne 'ipconfig.exe' -or $arguments.Count -ne 1 -or [string]$arguments[0] -ne '/flushdns'){throw 'The elevated generic process contract only permits ipconfig.exe /flushdns.'}
                }
            }
            {$_ -in @('SetRegistryValue','RemoveRegistryValue','RestoreRegistryBackup','ApplyRegistryTree','RemoveRegistryTree','RestoreRegistryTree')} {
                if(([string]$parameters.Path) -notmatch '^(HKLM|HKCU|HKCR|HKU|HKCC):\\'){throw 'Registry action path is invalid.'}
                if(([string]$parameters.Path) -match '^HKLM:' -and -not [bool]$Action.RequiresAdmin){throw 'Machine registry actions must require administrator privileges.'}
            }
            'ApplyRegistryTree' {
                if(([string]$parameters.Path) -notmatch '^(HKCU|HKLM):\\Software\\(Classes|Microsoft\\Windows\\CurrentVersion\\Explorer)\\'){throw 'Registry tree action path is outside approved context-menu roots.'}
                if([string]$parameters.ExpectedSnapshotHash -notmatch '^[a-f0-9]{64}$'){throw 'Registry snapshot hash is invalid.'}
                if(@($parameters.Values).Count -gt 128 -or @($parameters.DeleteValues).Count -gt 128 -or @($parameters.DeleteSubKeys).Count -gt 64){throw 'Registry tree action exceeds bounded entry limits.'}
                foreach($value in @($parameters.Values)){
                    $dictionary=ConvertTo-WinCareParameterDictionary $value
                    $null=Test-WinCareStrictObjectKeys -InputObject $dictionary -AllowedKeys @('Name','Value','ValueType') -Context 'registry tree value'
                    if(-not $dictionary.ContainsKey('Name') -or -not $dictionary.ContainsKey('Value') -or [string]$dictionary.ValueType -notin @('String','ExpandString','Binary','DWord','MultiString','QWord')){throw 'Registry tree value is invalid.'}
                    if(([string]$dictionary.Name).Length -gt 260 -or [string]$dictionary.Name -match '[\/]' -or [string]$dictionary.Name -match "`0"){throw 'Registry tree value name is invalid.'}
                    $null=ConvertTo-WinCareRegistryValuePayload -Value $dictionary.Value -ValueType ([string]$dictionary.ValueType)
                }
                foreach($name in @($parameters.DeleteValues)){if([string]$name -match '[\\/]' -or ([string]$name).Length -gt 260){throw 'Registry value deletion name is invalid.'}}
                foreach($name in @($parameters.DeleteSubKeys)){if([string]$name -notmatch '^[^\\/:*?"<>|]{1,260}$'){throw 'Registry subkey deletion name is invalid.'}}
            }
            'RemoveRegistryTree' {
                if(([string]$parameters.Path) -notmatch '^(HKCU|HKLM):\\Software\\(Classes|Microsoft\\Windows\\CurrentVersion\\Explorer)\\'){throw 'Registry tree action path is outside approved context-menu roots.'}
                if([string]$parameters.ExpectedSnapshotHash -notmatch '^[a-f0-9]{64}$'){throw 'Registry snapshot hash is invalid.'}
            }
            'RestoreRegistryTree' {
                if(([string]$parameters.Path) -notmatch '^(HKCU|HKLM):\\Software\\(Classes|Microsoft\\Windows\\CurrentVersion\\Explorer)\\'){throw 'Registry tree action path is outside approved context-menu roots.'}
                if($null -eq $parameters.Snapshot){throw 'Registry snapshot is required.'}
                if([string]$parameters.ExpectedCurrentHash -notmatch '^[a-f0-9]{64}$'){throw 'Registry recovery expected-current hash is invalid.'}
                $null=Test-WinCareRegistryTreeSnapshot -Snapshot $parameters.Snapshot
            }
            'UpsertMaintenanceWindow' {
                if($null -eq $parameters.Window){throw 'Maintenance window is required.'}
                if([long]$parameters.ExpectedRevision -lt 0){throw 'Maintenance expected revision is invalid.'}
                if([string]$parameters.ExpectedRecordHash -notmatch '^(absent|[a-f0-9]{64})$'){throw 'Maintenance expected record hash is invalid.'}
                $null=Test-WinCareMaintenanceRecord -Record $parameters.Window
            }
            'SetMaintenanceWindowState' {
                if([string]$parameters.Id -notmatch '^[a-f0-9]{32}$' -or [string]$parameters.State -notin @('Scheduled','Notice','Active','Completed','Cancelled')){throw 'Maintenance state transition parameters are invalid.'}
                if([long]$parameters.ExpectedRevision -lt 0 -or [string]$parameters.ExpectedRecordHash -notmatch '^[a-f0-9]{64}$'){throw 'Maintenance state precondition is invalid.'}
                if(([string](Get-WinCarePropertyValue $parameters 'Message' '')).Length -gt 2000){throw 'Maintenance transition message is too long.'}
            }
            'RestoreMaintenanceWindowRecord' {
                if([string]$parameters.Id -notmatch '^[a-f0-9]{32}$' -or $parameters.Exists -isnot [bool]){throw 'Maintenance restore identity is invalid.'}
                if([string]$parameters.ExpectedCurrentHash -notmatch '^(absent|[a-f0-9]{64})$'){throw 'Maintenance restore expected-current hash is invalid.'}
                if([bool]$parameters.Exists){if($null -eq $parameters.Record){throw 'Maintenance restore record is required.'};$null=Test-WinCareMaintenanceRecord -Record $parameters.Record}
                elseif($null -ne $parameters.Record){throw 'Absent maintenance restore cannot contain a record.'}
            }
            {$_ -in @('OfflineImageAddDriver','OfflineImageRemoveDriver','OfflineImageRemoveDriverBatch','OfflineImageAddPackage','OfflineImageSetFeature')} {
                if([string]::IsNullOrWhiteSpace([string]$parameters.ImagePath) -or ([string]$parameters.ImagePath).Length -gt 32760){throw 'Offline image path is invalid.'}
                if([string]$Action.Type -eq 'OfflineImageAddDriver' -and ([string]::IsNullOrWhiteSpace([string]$parameters.DriverPath) -or ([string]$parameters.DriverPath).Length -gt 32760)){throw 'Offline driver path is invalid.'}
                if([string]$Action.Type -eq 'OfflineImageRemoveDriver' -and [string]$parameters.PublishedName -notmatch '^oem\d+\.inf$'){throw 'Offline driver published name is invalid.'}
                if([string]$Action.Type -eq 'OfflineImageRemoveDriverBatch'){
                    $names=@($parameters.PublishedNames);if($names.Count -lt 1 -or $names.Count -gt 256 -or @($names|Where-Object{[string]$_ -notmatch '^oem\d+\.inf$'}).Count){throw 'Offline driver batch is invalid.'}
                }
                if([string]$Action.Type -eq 'OfflineImageAddPackage' -and ([string]$parameters.PackagePath -notmatch '\.(cab|msu)$')){throw 'Offline package must be a CAB or MSU file.'}
                if([string]$Action.Type -eq 'OfflineImageSetFeature' -and ([string]$parameters.FeatureName -notmatch '^[A-Za-z0-9._-]{1,256}$' -or [string]$parameters.State -notin @('Enabled','Disabled') -or [string]$parameters.BeforeState -notin @('Enabled','Disabled'))){throw 'Offline feature parameters are invalid.'}
            }
            {$_ -in @('WriteManagedFile','RemoveManagedFile')} {
                if([string]$parameters.ExpectedBeforeHash -notmatch '^(absent|[a-f0-9]{64})$'){throw 'Managed file expected hash is invalid.'}
                $roots=@($parameters.AllowedRoots);if($roots.Count -lt 1 -or $roots.Count -gt 16){throw 'Managed file action must declare 1..16 allowed roots.'}
                foreach($root in $roots){
                    if([string]::IsNullOrWhiteSpace([string]$root) -or ([string]$root).Length -gt 32760){throw 'Managed file allowed root is invalid.'}
                    if(-not(Test-WinCarePathWithin -Child ([string]$root) -Parent ([string]$script:WinCareState.Root) -AllowEqual)){throw 'Managed file roots must remain under the WinCare state root.'}
                }
                if(-not @($roots|Where-Object{Test-WinCarePathWithin -Child ([string]$parameters.Path) -Parent ([string]$_) -AllowEqual}).Count){throw 'Managed file path is outside its declared roots.'}
                if([string]$Action.Type -eq 'WriteManagedFile'){
                    try{$content=[Convert]::FromBase64String([string]$parameters.ContentBase64)}catch{throw 'Managed file content is not valid base64.'}
                    if($content.Length -gt 1048576){throw 'Managed file content exceeds 1 MiB.'}
                }
            }
            'SetFirewallProfileState' {
                $profiles=@($parameters.Profiles);if($profiles.Count -lt 1 -or $profiles.Count -gt 3 -or @($profiles|Where-Object{[string]$_ -notin @('Domain','Private','Public')}).Count){throw 'Firewall profile list is invalid.'}
                if($parameters.Enabled -isnot [bool]){throw 'Firewall profile Enabled must be Boolean.'}
            }
            'SetLocalUserState' {if([string]$parameters.Name -notmatch '^[^\/:*?"<>|]{1,256}$' -or $parameters.Enabled -isnot [bool]){throw 'Local user state parameters are invalid.'}}
            'ImportLocalGroupPolicyBackup' {
                foreach($field in @('BackupHash','LgpoHash')){if([string]$parameters[$field] -notmatch '^[a-f0-9]{64}$'){throw "$field is invalid."}}
                if([string]::IsNullOrWhiteSpace([string]$parameters.BackupPath) -or [string]::IsNullOrWhiteSpace([string]$parameters.LgpoPath)){throw 'LGPO import paths are required.'}
            }
            'ConfigureSysmon' {
                if([string]$parameters.ExecutableHash -notmatch '^[a-f0-9]{64}$' -or [string]$parameters.ConfigHash -notmatch '^[a-f0-9]{64}$'){throw 'Sysmon hashes are invalid.'}
                if([string]$parameters.Mode -notin @('Install','Update')){throw 'Sysmon mode is invalid.'};if([string]$parameters.Mode -eq 'Update'){if([string]$parameters.RollbackConfigPath -eq '' -or [string]$parameters.RollbackConfigHash -notmatch '^[a-f0-9]{64}$'){throw 'Sysmon update requires a trusted rollback configuration.'}}
            }
            'UninstallSysmon' {if([string]$parameters.ExecutableHash -notmatch '^[a-f0-9]{64}$'){throw 'Sysmon executable hash is invalid.'}}
            'SetPageFileConfiguration' {
                if([string]$parameters.Mode -notin @('SystemManaged','Custom','Disabled')){throw 'Page-file mode is invalid.'}
                if([string]$parameters.Mode -eq 'Disabled' -and [string]$Action.Risk -ne 'Critical'){throw 'Page-file disabling requires Critical risk.'}
                if([string]$parameters.BeforeHash -notmatch '^[a-f0-9]{64}$'){throw 'Page-file before-state hash is invalid.'}
                $settings=@($parameters.Settings);if($settings.Count -gt 8){throw 'Page-file setting count exceeds eight.'}
                if([string]$parameters.Mode -eq 'Custom' -and $settings.Count -lt 1){throw 'Custom page-file mode requires at least one setting.'}
                if([string]$parameters.Mode -ne 'Custom' -and $settings.Count -gt 0){throw 'Only custom page-file mode accepts explicit settings.'}
                $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                foreach($setting in $settings){$record=Test-WinCarePageFileSettingRecord -Setting $setting;if(-not $names.Add([string]$record.Name)){throw 'Duplicate page-file drive setting.'}}
            }
            'RestorePageFileConfiguration' {
                if($null -eq $parameters.Snapshot){throw 'Page-file recovery snapshot is required.'}
                if([string]$parameters.ExpectedCurrentHash -notmatch '^[a-f0-9]{64}$'){throw 'Page-file recovery hash is invalid.'}
                $snapshot=ConvertTo-WinCareParameterDictionary $parameters.Snapshot
                if([string](Get-WinCarePropertyValue $snapshot 'Mode' '') -notin @('SystemManaged','Custom','Disabled')){throw 'Page-file recovery mode is invalid.'}
                if([string](Get-WinCarePropertyValue $snapshot 'Hash' '') -notmatch '^[a-f0-9]{64}$'){throw 'Page-file recovery snapshot hash is invalid.'}
                $settings=@(Get-WinCarePropertyValue $snapshot 'Settings' @());if($settings.Count -gt 8){throw 'Page-file recovery setting count exceeds eight.'}
                foreach($setting in $settings){$null=Test-WinCarePageFileSettingRecord -Setting $setting}
                $snapshotPayload=[ordered]@{Mode=[string]$snapshot.Mode;AutomaticManagedPagefile=[bool]$snapshot.AutomaticManagedPagefile;Settings=$settings}
                $snapshotHash=Get-WinCareSha256Text -Text (ConvertTo-WinCareCanonicalJson -InputObject $snapshotPayload -Depth 20)
                if($snapshotHash -ne [string]$snapshot.Hash){throw 'Page-file recovery snapshot integrity hash does not match its contents.'}
            }
            'SetSecurityControlState' {
                if([string]$parameters.Control -notin @('DefenderRealtime','SmartScreenShell','Uac','WindowsUpdateStack')){throw 'Security control is invalid.'}
                if($parameters.Enabled -isnot [bool]){throw 'Security-control state must be Boolean.'}
                if([bool]$parameters.Enabled){throw 'SetSecurityControlState is reduction-only; exact restoration requires RestoreSecurityControlState and an authenticated record.'}
                if([int]$parameters.DurationMinutes -lt 5 -or [int]$parameters.DurationMinutes -gt 1440){throw 'Security-maintenance duration is outside 5..1440 minutes.'}
                if([string]::IsNullOrWhiteSpace([string]$parameters.Reason) -or ([string]$parameters.Reason).Length -gt 500){throw 'Security-maintenance reason is invalid.'}
                if([string]$parameters.EnvironmentClass -notin @('Workstation','Test','DisposableLab')){throw 'Security-maintenance environment class is invalid.'}
                if([string]$parameters.BeforeHash -notmatch '^[a-f0-9]{64}$'){throw 'Security-control before-state hash is invalid.'}
                if([string]$parameters.Control -eq 'Uac' -and [string]$parameters.EnvironmentClass -ne 'DisposableLab'){throw 'UAC disabling is limited to a disposable lab environment.'}
                if([string]$parameters.Control -eq 'Uac' -and [string]$Action.Risk -ne 'Critical'){throw 'UAC disabling requires Critical risk.'}
            }
            'RestoreSecurityControlState' {
                if([string]$parameters.RecordId -notmatch '^[a-f0-9]{32}$'){throw 'Security-maintenance record ID is invalid.'}
                if([string]$parameters.Control -notin @('DefenderRealtime','SmartScreenShell','Uac','WindowsUpdateStack')){throw 'Security recovery control is invalid.'}
                if($null -eq $parameters.Snapshot){throw 'Security recovery snapshot is required.'}
                $null=Test-WinCareSecurityControlSnapshot -Snapshot $parameters.Snapshot -ExpectedControl ([string]$parameters.Control)
                if([string]$parameters.ExpectedCurrentHash -notmatch '^[a-f0-9]{64}$'){throw 'Security recovery hash is invalid.'}
            }
            'SetLegacySecurityControlState' {
                if([string]$parameters.Control -notin @('DefenderRealtime','SmartScreenShell','Uac','WindowsUpdateStack') -or $parameters.Enabled -isnot [bool] -or [bool]$parameters.Enabled){throw 'Legacy security reduction must permanently disable one supported control.'}
                if([string]$parameters.BeforeHash -notmatch '^[a-f0-9]{64}$' -or [string]$parameters.ProfileId -notmatch '^[a-z0-9][a-z0-9._-]{2,80}$'){throw 'Legacy security reduction identity or precondition is invalid.'}
                if([string]$Action.Risk -ne 'Critical'){throw 'Permanent security reduction requires Critical risk.'}
            }
            'RestoreLegacySecurityControlState' {
                if([string]$parameters.Control -notin @('DefenderRealtime','SmartScreenShell','Uac','WindowsUpdateStack') -or [string]$parameters.ExpectedCurrentHash -notmatch '^[a-f0-9]{64}$'){throw 'Legacy security recovery parameters are invalid.'}
                $null=Test-WinCareSecurityControlSnapshot -Snapshot $parameters.Snapshot -ExpectedControl ([string]$parameters.Control)
            }
            'ResetDisplayOverrides' {
                if([string]$parameters.BeforeHash -notmatch '^[a-f0-9]{64}$' -or [string]$Action.Risk -ne 'Critical'){throw 'Display override reset requires a valid snapshot hash and Critical risk.'}
                $paths=@($parameters.DevicePaths);if($paths.Count -gt 128){throw 'Display override reset exceeds 128 device paths.'}
                foreach($path in $paths){if([string]$path -notmatch '^HKLM:\SYSTEM\CurrentControlSet\Enum\DISPLAY\[^\]+\[^\]+\Device Parameters$'){throw 'Display override reset contains an unsafe device path.'}}
            }
            'RestoreDisplayOverrides' {
                if([string]$parameters.ExpectedCurrentHash -notmatch '^[a-f0-9]{64}$'){throw 'Display override recovery hash is invalid.'}
                $null=Test-WinCareDisplayOverrideSnapshot -Snapshot $parameters.Snapshot
            }
            'SetHibernateState' {
                if($parameters.Enabled -isnot [bool] -or $parameters.Before -isnot [bool]){throw 'Hibernate state values must be Boolean.'}
            }
            'SetFolderAppearance' {
                if([string]$parameters.Path -match "`0" -or [string]::IsNullOrWhiteSpace([string]$parameters.Path)){throw 'Folder appearance path is invalid.'}
                $icon=Resolve-WinCareFolderIconResource -IconResource ([string]$parameters.IconResource)
                if([string]$parameters.IconResourceSha256 -notmatch '^[a-f0-9]{64}$' -or [string]$icon.Sha256 -ne [string]$parameters.IconResourceSha256){throw 'Folder icon resource hash is invalid or stale.'}
                if([string]$parameters.ExpectedBeforeHash -notmatch '^[a-f0-9]{64}$' -or [string]$parameters.ExpectedAfterHash -notmatch '^[a-f0-9]{64}$'){throw 'Folder appearance hashes are invalid.'}
                if([string](Get-WinCarePropertyValue $parameters.Before 'Hash' '') -ne [string]$parameters.ExpectedBeforeHash){throw 'Folder appearance before snapshot does not match its hash.'}
            }
            'RestoreFolderAppearance' {
                if([string]$parameters.ExpectedCurrentHash -notmatch '^[a-f0-9]{64}$'){throw 'Folder appearance recovery hash is invalid.'}
                if([string](Get-WinCarePropertyValue $parameters.Snapshot 'Hash' '') -notmatch '^[a-f0-9]{64}$'){throw 'Folder appearance recovery snapshot is invalid.'}
            }
            'RunVerifiedPeerTool' {
                if([string]$parameters.ToolId -notin @('ViVeTool','Adb')){throw 'Verified peer tool identifier is invalid.'}
                if([string]$parameters.Sha256 -notmatch '^[a-f0-9]{64}$'){throw 'Verified peer tool hash is invalid.'}
                if($parameters.Mutation -isnot [bool]){throw 'Verified peer tool mutation flag must be Boolean.'}
                $null=Test-WinCareVerifiedPeerToolArguments -ToolId ([string]$parameters.ToolId) -Arguments @($parameters.Arguments) -Mutation ([bool]$parameters.Mutation)
                if($parameters.Contains('ExpectedInputSha256') -and [string]$parameters.ExpectedInputSha256 -notmatch '^[a-f0-9]{64}$'){throw 'Verified peer tool input hash is invalid.'}
                if([string]$parameters.ToolId -eq 'Adb'){$shape=Get-WinCareAdbCommandShape -Arguments @($parameters.Arguments);if($shape.Verb -in @('push','install') -and -not $parameters.Contains('ExpectedInputSha256')){throw 'Mutating ADB file input requires an exact preview hash.'}}
                $codes=@($parameters.SuccessExitCodes);if($codes.Count -lt 1 -or $codes.Count -gt 32 -or @($codes|Where-Object{[int]$_ -lt 0 -or [int]$_ -gt 65535}).Count){throw 'Verified peer tool success exit codes are invalid.'}
            }
            'RunNetworkExperiment' {
                if([string]$parameters.Setting -notin @('AutoTuningLevel','EcnCapability','ReceiveSideScaling','ReceiveSegmentCoalescing')){throw 'TCP experiment setting is invalid.'}
                $allowed=Get-WinCareTcpAllowedValue -Setting ([string]$parameters.Setting);if([string]$parameters.Value -notin $allowed){throw 'TCP experiment value is invalid.'}
                if([string]$parameters.BeforeHash -notmatch '^[a-f0-9]{64}$'){throw 'TCP before-state hash is invalid.'}
                $allowed=Get-WinCareTcpAllowedValue -Setting ([string]$parameters.Setting);if([string]$parameters.BeforeValue -notin $allowed){throw 'TCP before-state value is invalid.'}
                if([int]$parameters.SampleCount -lt 3 -or [int]$parameters.SampleCount -gt 30){throw 'Network sample count is outside 3..30.'}
                if([int]$parameters.TimeoutMilliseconds -lt 250 -or [int]$parameters.TimeoutMilliseconds -gt 10000){throw 'Network timeout is outside 250..10000 milliseconds.'}
                if([double]$parameters.MinimumImprovementPercent -lt 0 -or [double]$parameters.MinimumImprovementPercent -gt 100){throw 'Minimum improvement is outside 0..100 percent.'}
                if([double]$parameters.MaximumRegressionPercent -lt 0 -or [double]$parameters.MaximumRegressionPercent -gt 100){throw 'Maximum regression is outside 0..100 percent.'}
                $hosts=@($parameters.TargetHosts);if($hosts.Count -lt 1 -or $hosts.Count -gt 8){throw 'Network experiment requires 1..8 target hosts.'}
                foreach($hostName in $hosts){if(-not(Test-WinCareDnsHostName -HostName ([string]$hostName))){throw 'Network experiment target host is invalid.'}}
                $worstCaseSeconds=[math]::Ceiling(($hosts.Count*[int]$parameters.SampleCount*((3*[int]$parameters.TimeoutMilliseconds)+100)*2)/1000.0)+15
                if($worstCaseSeconds -gt 1500){throw 'Network experiment exceeds the 25-minute provider bound.'}
                if($null -eq $parameters.Baseline){throw 'Network experiment baseline is required.'}
                $baseline=ConvertTo-WinCareParameterDictionary $parameters.Baseline
                $null=Test-WinCareStrictObjectKeys -InputObject $baseline -AllowedKeys @('SchemaVersion','Targets','Port','SampleCount','SuccessCount','SuccessRate','MedianDnsMs','MedianPingMs','MedianConnectMs','CompositeLatencyMs','Samples','MeasuredAt') -Context 'network experiment baseline'
                if([int](Get-WinCarePropertyValue $baseline 'SchemaVersion' 0) -ne 1 -or [int](Get-WinCarePropertyValue $baseline 'Port' 0) -ne 443){throw 'Network experiment baseline schema or port is invalid.'}
                $baselineTargets=@(Get-WinCarePropertyValue $baseline 'Targets' @()|ForEach-Object{([string]$_).ToLowerInvariant()}|Sort-Object -Unique)
                $normalizedHosts=@($hosts|ForEach-Object{([string]$_).ToLowerInvariant()}|Sort-Object -Unique)
                if((ConvertTo-WinCareCanonicalJson $baselineTargets) -ne (ConvertTo-WinCareCanonicalJson $normalizedHosts)){throw 'Network experiment baseline targets do not match the action targets.'}
                try{$measuredAt=[datetimeoffset]::Parse([string](Get-WinCarePropertyValue $baseline 'MeasuredAt' ''))}catch{throw 'Network experiment baseline timestamp is invalid.'}
                $baselineAge=[datetimeoffset]::UtcNow-$measuredAt.ToUniversalTime();if($baselineAge.TotalMinutes -lt -1 -or $baselineAge.TotalMinutes -gt 15){throw 'Network experiment baseline is stale or future-dated.'}
                $baselineSamples=@(Get-WinCarePropertyValue $baseline 'Samples' @())
                $expectedSamples=$normalizedHosts.Count*[int]$parameters.SampleCount
                if($baselineSamples.Count -ne $expectedSamples -or [int](Get-WinCarePropertyValue $baseline 'SampleCount' 0) -ne $expectedSamples -or $baselineSamples.Count -gt 240){throw 'Network experiment baseline sample set is invalid.'}
                foreach($sample in $baselineSamples){
                    $sampleMap=ConvertTo-WinCareParameterDictionary $sample
                    $null=Test-WinCareStrictObjectKeys -InputObject $sampleMap -AllowedKeys @('Target','Port','Resolved','DnsMs','PingMs','ConnectMs','Success','Error','Timestamp') -Context 'network baseline sample'
                    if(([string]$sampleMap.Target).ToLowerInvariant() -notin $normalizedHosts -or [int]$sampleMap.Port -ne 443 -or $sampleMap.Resolved -isnot [bool] -or $sampleMap.Success -isnot [bool]){throw 'Network baseline sample identity or Boolean state is invalid.'}
                    foreach($field in @('DnsMs','PingMs','ConnectMs')){$value=Get-WinCarePropertyValue $sampleMap $field $null;if($null -ne $value -and ([double]$value -lt 0 -or [double]$value -gt ([int]$parameters.TimeoutMilliseconds*2))){throw "Network baseline sample $field is outside its bound."}}
                }
                $baselineRate=[double](Get-WinCarePropertyValue $baseline 'SuccessRate' -1);if($baselineRate -lt 0.5 -or $baselineRate -gt 1){throw 'Network experiment baseline success rate is invalid.'}
                $baselineLatency=Get-WinCarePropertyValue $baseline 'CompositeLatencyMs' $null;if($null -eq $baselineLatency -or [double]$baselineLatency -le 0 -or [double]$baselineLatency -gt ([int]$parameters.TimeoutMilliseconds*6)){throw 'Network experiment baseline latency is invalid.'}
            }
            'RestoreTcpGlobalSetting' {
                if([string]$parameters.Setting -notin @('AutoTuningLevel','EcnCapability','ReceiveSideScaling','ReceiveSegmentCoalescing')){throw 'TCP recovery setting is invalid.'}
                $allowed=Get-WinCareTcpAllowedValue -Setting ([string]$parameters.Setting);if([string]$parameters.Value -notin $allowed -or [string]$parameters.ExpectedCurrentValue -notin $allowed){throw 'TCP recovery values are invalid.'}
            }
            'CaptureEtwTrace' {
                if([string]$parameters.Profile -notin @('GeneralProfile','CPU','FileIO','Network')){throw 'ETW profile is invalid.'}
                if([int]$parameters.DurationSeconds -lt 5 -or [int]$parameters.DurationSeconds -gt 300){throw 'ETW duration is outside 5..300 seconds.'}
                if([int]$parameters.ProcessId -lt 0){throw 'ETW process ID is invalid.'}
                if([string]$parameters.Destination -notmatch '\.etl$'){throw 'ETW destination must be an ETL file.'}
                if($script:WinCareState.ContainsKey('Root') -and -not(Test-WinCarePathWithin -Child ([string]$parameters.Destination) -Parent (Join-Path $script:WinCareState.Root 'Reports'))){throw 'ETW destination is outside the WinCare reports root.'}
                if(@($parameters.ProcessModules).Count -gt 2000){throw 'ETW process-module snapshot exceeds its bound.'}
            }
            'QuarantineInjectionSurface' {
                if([string]$parameters.SurfaceId -notmatch '^[a-f0-9]{32}$' -or [string]$parameters.ExpectedHash -notmatch '^[a-f0-9]{64}$'){throw 'Injection-surface identity is invalid.'}
            }
            'RestoreInjectionSurface' {
                if([string]$parameters.RecordId -notmatch '^[a-f0-9]{32}$' -or [string]$parameters.ExpectedCurrentHash -notmatch '^[a-f0-9]{64}$'){throw 'Injection-surface recovery parameters are invalid.'}
            }
            'StartPowerSession' {
                if([string]$parameters.Id -notmatch '^[a-f0-9]{32}$'){throw 'Power-session ID is invalid.'}
                if([string]$parameters.Mode -notin @('System','Display','Away')){throw 'Power-session mode is invalid.'}
                if($parameters.PreventDisplay -isnot [bool] -or $parameters.AwayMode -isnot [bool]){throw 'Power-session flags must be Boolean.'}
                if(([string]$parameters.Mode -eq 'System' -and ([bool]$parameters.PreventDisplay -or [bool]$parameters.AwayMode)) -or ([string]$parameters.Mode -eq 'Display' -and (-not [bool]$parameters.PreventDisplay -or [bool]$parameters.AwayMode)) -or ([string]$parameters.Mode -eq 'Away' -and (-not [bool]$parameters.PreventDisplay -or -not [bool]$parameters.AwayMode))){throw 'Power-session mode and flags are inconsistent.'}
                if([int]$parameters.RefreshSeconds -lt 5 -or [int]$parameters.RefreshSeconds -gt 300){throw 'Power-session refresh interval is outside 5..300 seconds.'}
                if(([string]$parameters.Reason).Length -lt 1 -or ([string]$parameters.Reason).Length -gt 1000){throw 'Power-session reason is invalid.'}
                if([string]$parameters.ExpiresAt){$expires=[datetime]::Parse([string]$parameters.ExpiresAt).ToUniversalTime();if($expires -le [datetime]::UtcNow.AddSeconds(-30) -or $expires -gt [datetime]::UtcNow.AddDays(8)){throw 'Power-session expiry is invalid.'}}
            }
            'StopPowerSession' {
                if([string]$parameters.Id -notmatch '^[a-f0-9]{32}$' -or [int]$parameters.ExpectedHostProcessId -lt 0){throw 'Power-session stop identity is invalid.'}
                if([int]$parameters.ExpectedHostProcessId -gt 0 -and [string]::IsNullOrWhiteSpace([string]$parameters.ExpectedHostProcessStartTime)){throw 'Power-session process start time is required when a process ID is supplied.'}
            }
            {$_ -in @('SetWindowBounds','SetWindowTopmost','ActivateWindow')} {
                if([long]$parameters.WindowHandle -le 0 -or [int]$parameters.ProcessId -le 0){throw 'Window identity is invalid.'}
                if([string]$parameters.ProcessStartTime -notmatch '^\d{4}-\d{2}-\d{2}T' -or [string]$parameters.ExpectedTitleHash -notmatch '^[a-f0-9]{64}$'){throw 'Window process or title identity is invalid.'}
                if([string]$Action.Type -eq 'SetWindowBounds'){
                    foreach($field in @('Width','Height','ExpectedCurrentWidth','ExpectedCurrentHeight')){if([int]$parameters[$field] -lt 1 -or [int]$parameters[$field] -gt 100000){throw 'Window dimensions are invalid.'}}
                    foreach($field in @('Left','Top','ExpectedCurrentLeft','ExpectedCurrentTop')){if([int64]$parameters[$field] -lt -100000 -or [int64]$parameters[$field] -gt 100000){throw 'Window position is outside supported bounds.'}}
                    $monitorFields=@('MonitorDevice','ExpectedMonitorWorkLeft','ExpectedMonitorWorkTop','ExpectedMonitorWorkWidth','ExpectedMonitorWorkHeight')
                    $present=@($monitorFields|Where-Object{$parameters.ContainsKey($_)})
                    if($present.Count -notin @(0,$monitorFields.Count)){throw 'Window monitor precondition fields must be supplied together.'}
                    if($present.Count -gt 0){if([string]::IsNullOrWhiteSpace([string]$parameters.MonitorDevice) -or [int]$parameters.ExpectedMonitorWorkWidth -lt 1 -or [int]$parameters.ExpectedMonitorWorkHeight -lt 1){throw 'Window monitor precondition is invalid.'}}
                }
                if([string]$Action.Type -eq 'SetWindowTopmost' -and ($parameters.Topmost -isnot [bool] -or $parameters.ExpectedCurrentTopmost -isnot [bool])){throw 'Topmost state and precondition must be Boolean.'}
            }
            'ReplaceColorPaletteStore' {
                if([string]$parameters.ExpectedCurrentHash -notmatch '^[a-f0-9]{64}$'){throw 'Color-palette expected hash is invalid.'}
                $null=Test-WinCareColorPaletteStore -Store (ConvertTo-WinCareParameterDictionary $parameters.Store)
            }
            'WriteLocalNote' {
                if([string]$parameters.Id -notmatch '^[a-f0-9]{32}$' -or [string]$parameters.ExpectedBeforeHash -notmatch '^[a-f0-9]{64}$'){throw 'Local-note identity or precondition is invalid.'}
                $metadata=ConvertTo-WinCareParameterDictionary $parameters.Metadata;$null=Test-WinCareLocalNoteMetadata -Metadata $metadata
                if([string]$metadata.Id -ne [string]$parameters.Id){throw 'Local-note metadata identity mismatch.'}
                try{$body=[Convert]::FromBase64String([string]$parameters.BodyBase64)}catch{throw 'Local-note body is not valid Base64.'}
                if($body.Length -gt 1MB){throw 'Local-note body exceeds 1 MiB.'}
            }
            'RemoveLocalNote' {
                if([string]$parameters.Id -notmatch '^[a-f0-9]{32}$' -or [string]$parameters.ExpectedBeforeHash -notmatch '^[a-f0-9]{64}$'){throw 'Local-note removal precondition is invalid.'}
            }
            'RestoreLocalNote' {
                if([string]$parameters.Id -notmatch '^[a-f0-9]{32}$' -or [string]$parameters.ExpectedCurrentHash -notmatch '^[a-f0-9]{64}$'){throw 'Local-note recovery precondition is invalid.'}
                $snapshot=ConvertTo-WinCareParameterDictionary $parameters.Snapshot;$null=Test-WinCareStrictObjectKeys -InputObject $snapshot -AllowedKeys @('Exists','Metadata','Body','Hash') -Context 'local-note recovery snapshot'
                if($snapshot.Exists -isnot [bool] -or [string]$snapshot.Hash -notmatch '^[a-f0-9]{64}$'){throw 'Local-note recovery snapshot is invalid.'}
                if([bool]$snapshot.Exists){$metadata=ConvertTo-WinCareParameterDictionary $snapshot.Metadata;$null=Test-WinCareLocalNoteMetadata -Metadata $metadata;if([string]$metadata.Id -ne [string]$parameters.Id){throw 'Local-note recovery identity mismatch.'};if([Text.Encoding]::UTF8.GetByteCount([string]$snapshot.Body) -gt 1MB){throw 'Local-note recovery body exceeds 1 MiB.'}}
            }
            'ReplaceRemoteConsentStore' {
                if([string]$parameters.ExpectedCurrentHash -notmatch '^[a-f0-9]{64}$'){throw 'Remote-consent expected hash is invalid.'}
                $null=Test-WinCareRemoteConsentStore -Store (ConvertTo-WinCareParameterDictionary $parameters.Store)
            }
            'TerminateRemoteSupportProcess' {
                if([int]$parameters.ProcessId -le 0 -or [string]$parameters.Name -notmatch '^[A-Za-z0-9_.-]{1,128}$'){throw 'Remote-support process identity is invalid.'}
                if([string]$parameters.StartTime -notmatch '^\d{4}-\d{2}-\d{2}T' -or [string]$parameters.Sha256 -notmatch '^[a-f0-9]{64}$'){throw 'Remote-support process start time or image hash is invalid.'}
                if([string]::IsNullOrWhiteSpace([string]$parameters.Path) -or ([string]$parameters.Path).Length -gt 32760){throw 'Remote-support process path is invalid.'}
            }
            'ReplaceDownloadStore' {
                if([string]$parameters.ExpectedHash -notmatch '^[a-f0-9]{64}$'){throw 'Download-store expected hash is invalid.'}
                $null=Test-WinCareDownloadStore -Store (ConvertTo-WinCareParameterDictionary $parameters.Store)
            }
            {$_ -in @('StartDownloadTransfer','SetDownloadTransferState','ReconcileDownloadTransfer','CancelDownloadTransfer')} {
                if([string]$parameters.Id -notmatch '^[a-f0-9]{32}$' -or [string]$parameters.ExpectedRecordHash -notmatch '^[a-f0-9]{64}$'){throw 'Download action identity or record hash is invalid.'}
                if([string]$Action.Type -eq 'SetDownloadTransferState' -and [string]$parameters.State -notin @('Suspended','Transferring')){throw 'Download transfer state is invalid.'}
            }
            'CaptureTelemetrySeries' {
                if([string]::IsNullOrWhiteSpace([string]$parameters.Name) -or ([string]$parameters.Name).Length -gt 160){throw 'Telemetry capture name is invalid.'}
                if([int]$parameters.DurationSeconds -lt 1 -or [int]$parameters.DurationSeconds -gt 3600 -or [int]$parameters.IntervalSeconds -lt 1 -or [int]$parameters.IntervalSeconds -gt 60 -or [int]$parameters.TopProcessCount -lt 1 -or [int]$parameters.TopProcessCount -gt 20){throw 'Telemetry capture bounds are invalid.'};if([string]$parameters.ExpectedHistoryHash -notmatch '^[a-f0-9]{64}$'){throw 'Telemetry history hash is invalid.'}
            }
            'OpenLaunchTarget' {
                if([string]$parameters.Id -notmatch '^[a-f0-9]{64}$' -or [string]$parameters.ExpectedHash -notmatch '^[a-f0-9]{64}$'){throw 'Launch-target identity is invalid.'}
                if([string]$parameters.Kind -notin @('Executable','Shortcut','ControlPanel','SettingsUri')){throw 'Launch-target kind is invalid.'}
                if([string]::IsNullOrWhiteSpace([string]$parameters.Target) -or ([string]$parameters.Target).Length -gt 32760){throw 'Launch-target payload exceeds its bounds.'}
            }
            'CreateSteamCloudBackup' {
                if([string]$parameters.UserId -notmatch '^\d{1,20}$' -or [string]$parameters.AppId -notmatch '^\d{1,20}$'){throw 'Steam user or application identity is invalid.'}
                if([string]$parameters.ExpectedSnapshotHash -notmatch '^[a-f0-9]{64}$'){throw 'Steam snapshot hash is invalid.'}
            }
            'RemoveGameStateBackup' {if([string]$parameters.ExpectedSha256 -notmatch '^[a-f0-9]{64}$'){throw 'Game-state backup hash is invalid.'}}
            'RestoreSteamCloudBackup' {
                foreach($field in @('BackupSha256','ExpectedCurrentSnapshotHash')){if([string]$parameters[$field] -notmatch '^[a-f0-9]{64}$'){throw "Steam restore $field is invalid."}}
            }
            'OfflineImageRemoveProvisionedAppx' {
                if([string]::IsNullOrWhiteSpace([string]$parameters.ImagePath) -or [string]$parameters.PackageName -notmatch '^[A-Za-z0-9._~-]{3,512}$' -or ([string]$parameters.DisplayName).Length -gt 256){throw 'Offline provisioned-package parameters are invalid.'}
            }
            'OfflineImageStartComponentCleanup' {
                if([string]::IsNullOrWhiteSpace([string]$parameters.ImagePath) -or $parameters.ResetBase -isnot [bool]){throw 'Offline component-cleanup parameters are invalid.'}
                if([bool]$parameters.ResetBase){throw 'ResetBase is not admitted by WinCare.'}
            }
            'ReplaceWorkspaceLayoutStore' {
                if([string]$parameters.ExpectedCurrentHash -notmatch '^[a-f0-9]{64}$'){throw 'Workspace-layout expected hash is invalid.'}
                $null=Test-WinCareWorkspaceLayoutStore -Store (ConvertTo-WinCareParameterDictionary $parameters.Store)
            }
            'SetShellExtensionState' {if(([string]$parameters.RegistryPath) -notmatch '^HKEY_(CURRENT_USER|LOCAL_MACHINE)\\Software\\Classes\\'){throw 'Shell registry path is invalid.'};if(([string]$parameters.RegistryPath) -match '^HKEY_LOCAL_MACHINE\\' -and -not [bool]$Action.RequiresAdmin){throw 'Machine shell changes must require administrator privileges.'}}
            'SetServiceStartMode' {if([string]$parameters.Name -notmatch '^[A-Za-z0-9_.-]{1,256}$' -or [string]$parameters.StartMode -notin @('Automatic','AutomaticDelayedStart','Manual','Disabled')){throw 'Service action parameters are invalid.'}}
            'SetServiceRuntimeState' {if([string]$parameters.Name -notmatch '^[A-Za-z0-9_.-]{1,256}$' -or [string]$parameters.State -notin @('Running','Stopped')){throw 'Service runtime-state parameters are invalid.'}}
            'SetOptionalFeatureState' {if(([string]$parameters.State) -notin @('Enabled','Disabled')){throw 'Optional feature state is invalid.'}}
            'SetCapabilityState' {if(([string]$parameters.State) -notin @('Installed','Removed','NotPresent')){throw 'Capability state is invalid.'}}
            'SetMasterVolume' {if([int]$parameters.Percent -lt 0 -or [int]$parameters.Percent -gt 100){throw 'Volume is outside 0..100.'}}
            'SetBrightness' {if([int]$parameters.Percent -lt 0 -or [int]$parameters.Percent -gt 100){throw 'Brightness is outside 0..100.'}}
            'SetMonitorVcpFeature' {
                if([string]$parameters.DeviceName -notmatch '^\\\\\.\\DISPLAY[0-9]{1,3}$' -or [int]$parameters.PhysicalIndex -lt 0 -or [int]$parameters.PhysicalIndex -gt 63){throw 'Monitor identity is invalid.'}
                if(([string]$parameters.Description).Length -gt 256 -or [int]$parameters.FeatureCode -notin @(16,18)){throw 'Monitor VCP feature is invalid.'}
                foreach($field in @('Value','ExpectedCurrent','ExpectedMaximum')){if([long]$parameters[$field] -lt 0 -or [long]$parameters[$field] -gt 65535){throw "Monitor VCP $field is outside 0..65535."}}
                if([int]$parameters.Value -gt [int]$parameters.ExpectedMaximum -or [int]$parameters.ExpectedCurrent -gt [int]$parameters.ExpectedMaximum){throw 'Monitor VCP values exceed the expected maximum.'}
            }
            'OpenExplorerLocation' {
                if([string]::IsNullOrWhiteSpace([string]$parameters.Path) -or ([string]$parameters.Path).Length -gt 32760 -or [string]$parameters.ExpectedPathHash -notmatch '^[a-f0-9]{64}$'){throw 'Explorer location parameters are invalid.'}
                if([string]$parameters.Path -match '^(?i)(https?|ftp):' -or [string]$parameters.Path -match '^[\\]{2}'){throw 'Explorer locations must be local filesystem paths.'}
            }
            'LaunchAppxApplication' {
                if([string]$parameters.Aumid -notmatch '^[A-Za-z0-9._~-]{3,512}![A-Za-z0-9._~-]{1,256}$' -or [string]$parameters.PackageFullName -notmatch '^[A-Za-z0-9._~-]{3,512}$' -or [string]$parameters.ManifestSha256 -notmatch '^[a-f0-9]{64}$'){throw 'AppX launch identity is invalid.'}
            }
            'WuaSetHidden' {if(([string]$parameters.UpdateId) -notmatch '^[0-9a-fA-F-]{36}$'){throw 'Windows Update identity is invalid.'}}
            {$_ -in @('WuaDownload','WuaInstall','WuaUninstall')} {if(@($parameters.UpdateIds).Count -lt 1 -or @($parameters.UpdateIds).Count -gt 100){throw 'Windows Update action must contain 1..100 update identities.'};foreach($id in @($parameters.UpdateIds)){if(([string]$id) -notmatch '^[0-9a-fA-F-]{36}$'){throw 'Windows Update identity is invalid.'}}}
            'DeployWdacPolicy' {if(([string]$parameters.Sha256) -notmatch '^[a-f0-9]{64}$'){throw 'WDAC policy hash is invalid.'}}
            'InstallWingetPackage' {if(([string]$parameters.PackageId) -notmatch '^[A-Za-z0-9][A-Za-z0-9._+-]{1,127}$'){throw 'WinGet package identity is invalid.'};if($parameters.ContainsKey('Version') -and [string]$parameters.Version -notmatch '^[A-Za-z0-9._+~-]{1,80}$'){throw 'WinGet package version is invalid.'};if($parameters.ContainsKey('Scope') -and [string]$parameters.Scope -notin @('user','machine')){throw 'WinGet scope is invalid.'}}
            'InstallDeclarativeExtension' {if(([string]$parameters.ArchiveSha256) -notmatch '^[a-f0-9]{64}$' -or ([string]$parameters.ExtensionId) -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$'){throw 'Extension installation parameters are invalid.'}}
            'RemoveDeclarativeExtension' {if(([string]$parameters.ExtensionId) -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$'){throw 'Extension ID is invalid.'}}
            'DeleteAdmittedCleanupFiles' {
                if([string]$parameters.CatalogSha256 -notmatch '^[a-f0-9]{64}$'){throw 'Cleanup catalog hash is invalid.'}
                if([string]::IsNullOrWhiteSpace([string]$parameters.CatalogPath) -or ([string]$parameters.CatalogPath).Length -gt 32760){throw 'Cleanup catalog path is invalid.'}
                $roots=@($parameters.AllowedRoots);if($roots.Count -lt 1 -or $roots.Count -gt 16){throw 'Cleanup action must declare 1..16 allowed roots.'}
                foreach($root in $roots){if([string]::IsNullOrWhiteSpace([string]$root) -or ([string]$root).Length -gt 32760){throw 'Cleanup action contains an invalid allowed root.'}}
                $entries=@($parameters.Entries);if($entries.Count -lt 1 -or $entries.Count -gt 1000){throw 'Cleanup action must contain 1..1000 admitted file entries.'}
                if([long]$parameters.MaximumBytes -lt 1MB -or [long]$parameters.MaximumBytes -gt 2GB){throw 'Cleanup byte limit is outside 1 MiB..2 GiB.'}
                foreach($entry in $entries){
                    $ep=ConvertTo-WinCareParameterDictionary $entry
                    $null=Test-WinCareStrictObjectKeys -InputObject $ep -AllowedKeys @('Path','Length','LastWriteUtcTicks','Sha256','Section','Rule') -RequiredKeys @('Path','Length','LastWriteUtcTicks','Sha256') -Context 'cleanup manifest entry'
                    if([string]::IsNullOrWhiteSpace([string]$ep.Path) -or [string]$ep.Sha256 -notmatch '^[a-f0-9]{64}$' -or [long]$ep.Length -lt 0 -or [long]$ep.LastWriteUtcTicks -lt 0){throw 'Cleanup manifest entry is invalid.'}
                    if(-not @($roots|Where-Object{Test-WinCarePathWithin -Child ([string]$ep.Path) -Parent ([string]$_) -AllowEqual}).Count){throw 'Cleanup entry is outside its declared roots.'}
                }
            }
            {$_ -in @('RelocateDirectoryWithJunction','RestoreRelocatedDirectory')} {
                $hashField=if($_ -eq 'RelocateDirectoryWithJunction'){'ExpectedSourceHash'}else{'ExpectedDestinationHash'}
                if([string]$parameters[$hashField] -notmatch '^[a-f0-9]{64}$'){throw 'Relocation content hash is invalid.'}
                if($_ -eq 'RelocateDirectoryWithJunction' -and [string]$parameters.ProfileId -notmatch '^[A-Za-z0-9._-]{3,80}$'){throw 'Relocation profile ID is invalid.'}
                $roots=@($parameters.AllowedRoots);if($roots.Count -lt 1 -or $roots.Count -gt 16){throw 'Relocation action must declare 1..16 allowed roots.'}
                foreach($root in $roots){if([string]::IsNullOrWhiteSpace([string]$root) -or ([string]$root).Length -gt 32760){throw 'Relocation action contains an invalid allowed root.'}}
                foreach($path in @([string]$parameters.Source,[string]$parameters.Destination)){
                    if([string]::IsNullOrWhiteSpace($path) -or $path.Length -gt 32760){throw 'Relocation path is invalid.'}
                    if(-not @($roots|Where-Object{Test-WinCarePathWithin -Child $path -Parent ([string]$_) -AllowEqual}).Count){throw 'Relocation path is outside its declared roots.'}
                }
                if([string]$parameters.Source -eq [string]$parameters.Destination){throw 'Relocation source and destination must differ.'}
                $names=@($parameters.ProcessNames);if($names.Count -lt 1 -or $names.Count -gt 16){throw 'Relocation must declare 1..16 process names.'}
                foreach($name in $names){if([string]$name -notmatch '^[A-Za-z0-9._-]{1,128}$'){throw 'Relocation process name is invalid.'}}
                if([long]$parameters.MaximumBytes -lt 0 -or [long]$parameters.MaximumBytes -gt 2TB){throw 'Relocation byte limit is invalid.'}
            }
            'MoveFile' {
                if(([string]$parameters.ExpectedSourceHash) -notmatch '^[a-f0-9]{64}$'){throw 'Move source content hash is invalid.'}
                $roots=@($parameters.AllowedRoots)
                if($roots.Count -lt 1 -or $roots.Count -gt 16){throw 'Move action must declare 1..16 allowed roots.'}
                foreach($root in $roots){
                    if([string]::IsNullOrWhiteSpace([string]$root) -or ([string]$root).Length -gt 32760){throw 'Move action contains an invalid allowed root.'}
                    if(-not(Test-WinCarePathWithin -Child ([string]$root) -Parent ([string]$script:WinCareState.Root) -AllowEqual)){throw 'Move roots must remain under the WinCare state root.'}
                }
                if([string]::IsNullOrWhiteSpace([string]$parameters.Source) -or [string]::IsNullOrWhiteSpace([string]$parameters.Destination)){throw 'Move source and destination are required.'}
                foreach($path in @([string]$parameters.Source,[string]$parameters.Destination)){if(-not @($roots|Where-Object{Test-WinCarePathWithin -Child $path -Parent ([string]$_) -AllowEqual}).Count){throw 'Move path is outside its declared roots.'}}
            }
        }
        if(-not $SkipCompensatorValidation){
            $descriptor=Get-WinCarePropertyValue $Action 'Compensator' $null
            $descriptorType=if($null -ne $descriptor){[string](Get-WinCarePropertyValue $descriptor 'Type' '')}else{''}
            $hasDescriptor=-not [string]::IsNullOrWhiteSpace($descriptorType)
            $dynamicCompensatorTypes=@('DisableStartupItem','ApplyRegistryTree','OfflineImageAddDriver','ImportLocalGroupPolicyBackup','UpsertMaintenanceWindow','SetMaintenanceWindowState','SetPageFileConfiguration','SetSecurityControlState','SetLegacySecurityControlState','ResetDisplayOverrides','RunNetworkExperiment','QuarantineInjectionSurface','StartPowerSession','StartDownloadTransfer','CreateSteamCloudBackup','RestoreSteamCloudBackup')
            if([bool]$Action.Reversible){
                if(-not $hasDescriptor -and [string]$Action.Type -notin $dynamicCompensatorTypes){throw 'Reversible action does not provide an executable compensator.'}
                if($hasDescriptor){
                    $null=Test-WinCareStrictObjectKeys -InputObject $descriptor -AllowedKeys @('Type','Parameters','RequiresAdmin') -Context 'compensator descriptor'
                    $compensatorContract=Get-WinCareActionContract -Type $descriptorType
                    if(-not $compensatorContract){throw "Compensator type is unknown: $descriptorType"}
                    $compensatorParameters=ConvertTo-WinCareParameterDictionary (Get-WinCarePropertyValue $descriptor 'Parameters' @{})
                    $compensatorAdmin=[bool](Get-WinCarePropertyValue $descriptor 'RequiresAdmin' $Action.RequiresAdmin)
                    $candidate=New-WinCareAction -Type $descriptorType -Label ("Validate compensator for: {0}" -f [string]$Action.Label) -Risk ([string]$compensatorContract.MinimumRisk) -Parameters $compensatorParameters -RequiresAdmin $compensatorAdmin -Reversible $false -TimeoutSeconds ([Math]::Min([int]$Action.TimeoutSeconds,[int]$compensatorContract.MaximumTimeout)) -Tags @('CompensatorValidation') -SourceRecords @($Action.SourceRecords)
                    $compensatorValidation=Test-WinCareActionContract -Action $candidate -SkipCompensatorValidation
                    if(-not $compensatorValidation.Success){throw "Compensator is invalid: $($compensatorValidation.Message)"}
                }
            }elseif($hasDescriptor){throw 'A non-reversible action cannot declare a compensator.'}
        }
        New-WinCareResult -Success $true -Message 'Action contract validated.' -Code 'ActionContractValid'
    }catch{New-WinCareResult -Success $false -Status Blocked -Code 'InvalidActionContract' -Message $_.Exception.Message -ExitCode 22}
}

function Test-WinCarePlanContract {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Plan)
    try{
        if([int]$Plan.SchemaVersion -ne 3){throw 'Unsupported plan schema version.'}
        if([string]$Plan.Id -notmatch '^[a-f0-9]{32}$'){throw 'Plan ID is invalid.'}
        if([string]::IsNullOrWhiteSpace([string]$Plan.Title) -or ([string]$Plan.Title).Length -gt 240){throw 'Plan title is invalid.'}
        $actions=@($Plan.Actions);if($actions.Count -gt [int]$Plan.MaxActions){throw 'Plan exceeds its declared action limit.'}
        if($actions.Count -gt [int](Get-WinCarePolicy 'MaximumPlanActions')){throw 'Plan exceeds the policy action limit.'}
        $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($action in $actions){$valid=Test-WinCareActionContract -Action $action;if(-not $valid.Success){throw "Action '$($action.Label)' is invalid: $($valid.Message)"};if(-not $ids.Add([string]$action.Id)){throw "Duplicate action ID: $($action.Id)"}}
        New-WinCareResult -Success $true -Message 'Plan contract validated.' -Code 'PlanContractValid'
    }catch{New-WinCareResult -Success $false -Status Blocked -Code 'InvalidPlanContract' -Message $_.Exception.Message -ExitCode 22}
}
