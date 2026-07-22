function Get-WinCareExperienceStudioRoot {
    [CmdletBinding()]param()
    Ensure-WinCareState
    $root=Join-Path $script:WinCareState.Root 'ExperienceStudio'
    if(-not(Test-Path -LiteralPath $root)){ $null=New-Item -ItemType Directory -Path $root -Force }
    return $root
}

function New-WinCareExperienceRegistryAction {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Name,
        [AllowNull()][object]$Value,
        [ValidateSet('String','ExpandString','Binary','DWord','MultiString','QWord')][string]$ValueType='DWord',
        [Parameter(Mandatory)][string]$Label,
        [ValidateSet('Low','Moderate','High','Critical')][string]$Risk='Moderate',
        [string[]]$SourceRecords=@()
    )
    $before=Get-WinCareRegistryValueState -Path $Path -Name $Name
    $requiresAdmin=$Path -like 'HKLM:*'
    if($null -eq $Value){
        if(-not $before.Exists){return $null}
        return New-WinCareAction -Type RemoveRegistryValue -Label $Label -Risk $Risk -Parameters @{Path=$Path;Name=$Name;Before=$before} -RequiresAdmin $requiresAdmin -Reversible $true -Compensator @{Type='SetRegistryValue';RequiresAdmin=$requiresAdmin;Parameters=@{Path=$Path;Name=$Name;Value=$before.Value;ValueType=$before.Kind}} -Tags @('ExperienceStudio','Registry') -SourceRecords $SourceRecords -RecoveryDescription 'Restore the exact previous registry value.' -RestartPossible $true
    }
    $compensator=if($before.Exists){@{Type='SetRegistryValue';RequiresAdmin=$requiresAdmin;Parameters=@{Path=$Path;Name=$Name;Value=$before.Value;ValueType=$before.Kind}}}else{@{Type='RemoveRegistryValue';RequiresAdmin=$requiresAdmin;Parameters=@{Path=$Path;Name=$Name}}}
    return New-WinCareAction -Type SetRegistryValue -Label $Label -Risk $Risk -Parameters @{Path=$Path;Name=$Name;Value=$Value;ValueType=$ValueType;Before=$before} -RequiresAdmin $requiresAdmin -Reversible $true -Compensator $compensator -Tags @('ExperienceStudio','Registry') -SourceRecords $SourceRecords -RecoveryDescription 'Restore the exact previous registry value or remove the newly created value.' -RestartPossible $true
}

function Get-WinCareVisualAssetInfo {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath)
    $base=Get-WinCareImageMetadata -LiteralPath $LiteralPath
    $palette=[Collections.Generic.List[string]]::new()
    try{
        try{Add-Type -AssemblyName System.Drawing.Common -ErrorAction Stop}catch{Add-Type -AssemblyName System.Drawing -ErrorAction Stop}
        $image=[Drawing.Image]::FromFile([string]$base.Path,$false)
        try{
            foreach($entry in @($image.Palette.Entries)|Select-Object -First 256){
                $palette.Add(('#{0:X2}{1:X2}{2:X2}{3:X2}' -f $entry.A,$entry.R,$entry.G,$entry.B))
            }
        }finally{$image.Dispose()}
    }catch{Write-Verbose 'Indexed palette metadata was unavailable.'}
    [pscustomobject]@{
        Path=$base.Path;Sha256=$base.Sha256;Bytes=$base.Bytes;Width=$base.Width;Height=$base.Height
        PixelFormat=$base.PixelFormat;RawFormat=$base.RawFormat;FrameCount=$base.FrameCount
        FrameDelaysMilliseconds=@($base.FrameDelaysMilliseconds);Animated=$base.Animated
        Palette=@($palette|Select-Object -Unique);PaletteCount=@($palette|Select-Object -Unique).Count
        MetadataOnly=$true;Execution='None';SourceRecords=@('D130-SPRITE-METADATA','D130-PALETTE-WORKFLOW')
    }
}

function Get-WinCareSpriteSheetLayout {
    [CmdletBinding()]param(
        [Parameter(Mandatory)][ValidateRange(1,100000)][int]$SheetWidth,
        [Parameter(Mandatory)][ValidateRange(1,100000)][int]$SheetHeight,
        [Parameter(Mandatory)][ValidateRange(1,100000)][int]$FrameWidth,
        [Parameter(Mandatory)][ValidateRange(1,100000)][int]$FrameHeight,
        [ValidateRange(0,4096)][int]$Margin=0,
        [ValidateRange(0,4096)][int]$Spacing=0,
        [ValidateRange(0,4096)][int]$FrameCount=0
    )
    $usableWidth=$SheetWidth-(2*$Margin);$usableHeight=$SheetHeight-(2*$Margin)
    if($usableWidth -lt $FrameWidth -or $usableHeight -lt $FrameHeight){throw 'Sprite sheet does not contain one complete frame.'}
    $columns=[math]::Floor(($usableWidth+$Spacing)/($FrameWidth+$Spacing))
    $rows=[math]::Floor(($usableHeight+$Spacing)/($FrameHeight+$Spacing))
    $capacity=[int]($columns*$rows);if($capacity -gt 4096){throw 'Sprite sheet layout exceeds 4,096 frames.'}
    $count=if($FrameCount -gt 0){$FrameCount}else{$capacity};if($count -gt $capacity){throw 'Requested frame count exceeds sheet capacity.'}
    $frames=[Collections.Generic.List[object]]::new()
    for($i=0;$i -lt $count;$i++){$row=[math]::Floor($i/$columns);$column=$i%$columns;$frames.Add([pscustomobject]@{Index=$i;X=$Margin+$column*($FrameWidth+$Spacing);Y=$Margin+$row*($FrameHeight+$Spacing);Width=$FrameWidth;Height=$FrameHeight;Row=$row;Column=$column})}
    [pscustomobject]@{SheetWidth=$SheetWidth;SheetHeight=$SheetHeight;FrameWidth=$FrameWidth;FrameHeight=$FrameHeight;Margin=$Margin;Spacing=$Spacing;Columns=$columns;Rows=$rows;Capacity=$capacity;FrameCount=$count;Frames=@($frames);SourceRecords=@('D130-SPRITE-SHEET-LAYOUT')}
}

function New-WinCareVisualAssetManifestPlan {
    [CmdletBinding()]param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidatePattern('^[A-Za-z0-9._-]{3,100}\.json$')][string]$Name='visual-asset.json',
        [ValidateRange(1,100000)][int]$FrameWidth=1,
        [ValidateRange(1,100000)][int]$FrameHeight=1,
        [ValidateRange(0,4096)][int]$Margin=0,
        [ValidateRange(0,4096)][int]$Spacing=0,
        [switch]$IncludeSpriteLayout
    )
    $asset=Get-WinCareVisualAssetInfo -LiteralPath $LiteralPath
    $layout=$null;if($IncludeSpriteLayout){$layout=Get-WinCareSpriteSheetLayout -SheetWidth $asset.Width -SheetHeight $asset.Height -FrameWidth $FrameWidth -FrameHeight $FrameHeight -Margin $Margin -Spacing $Spacing}
    $payload=[ordered]@{SchemaVersion=1;CreatedAt=[datetime]::UtcNow.ToString('o');Asset=$asset;SpriteLayout=$layout;SourceRecords=@('D130-SPRITE-METADATA','D130-SPRITE-SHEET-LAYOUT','D135-CAPABILITY-PRESENTATION')}
    $root=Join-Path (Get-WinCareExperienceStudioRoot) 'VisualAssets';$null=New-Item -ItemType Directory -Path $root -Force
    $path=Join-Path $root $Name;$bytes=[Text.Encoding]::UTF8.GetBytes((ConvertTo-WinCareCanonicalJson -InputObject $payload -Depth 40)+"`n")
    $action=New-WinCareManagedFileWriteAction -Path $path -Content $bytes -AllowedRoots @($root) -Label 'Write visual asset manifest' -SourceRecords @('D130-SPRITE-METADATA','D130-SPRITE-SHEET-LAYOUT','D135-CAPABILITY-PRESENTATION')
    New-WinCarePlan -Title 'Create visual asset manifest' -Description 'Writes metadata and an optional sprite-sheet frame map under protected WinCare state without executing or editing the source asset.' -Actions @($action) -SourceRecords @($action.SourceRecords)
}

function Get-WinCareLocationPrivacyState {
    [CmdletBinding()]param()
    if(-not $IsWindows){return $null}
    $machine=Get-WinCareRegistryValueState -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors' -Name 'DisableLocation'
    $user=Get-WinCareRegistryValueState -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location' -Name 'Value'
    $effective=if($machine.Exists -and [int]$machine.Value -eq 1){'Deny'}elseif($user.Exists -and [string]$user.Value -eq 'Deny'){'Deny'}elseif($user.Exists -and [string]$user.Value -eq 'Allow'){'Allow'}else{'SystemManaged'}
    [pscustomobject]@{EffectiveMode=$effective;MachinePolicy=$machine;UserConsent=$user;ServiceMutation='None';SourceRecords=@('D137-LOCATION-PERMISSION-MODEL')}
}

function New-WinCareLocationPrivacyPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][ValidateSet('Allow','Deny','SystemManaged')][string]$Mode)
    if(-not $IsWindows){throw 'Location privacy plans require Windows.'}
    $records=@('D137-LOCATION-PERMISSION-MODEL','D137-SERVICE-STATUS-SEPARATION')
    $actions=[Collections.Generic.List[object]]::new()
    $machine=if($Mode -eq 'SystemManaged'){$null}elseif($Mode -eq 'Deny'){1}else{0}
    $user=if($Mode -eq 'SystemManaged'){$null}else{$Mode}
    $a=New-WinCareExperienceRegistryAction -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors' -Name 'DisableLocation' -Value $machine -ValueType DWord -Label "Set machine location policy to $Mode" -Risk High -SourceRecords $records;if($a){$actions.Add($a)}
    $a=New-WinCareExperienceRegistryAction -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location' -Name 'Value' -Value $user -ValueType String -Label "Set user location consent to $Mode" -Risk Moderate -SourceRecords $records;if($a){$actions.Add($a)}
    New-WinCarePlan -Title "Location privacy: $Mode" -Description 'Changes only documented policy and consent values. It does not disable the location service or mutate per-application consent stores.' -Actions @($actions) -SourceRecords $records
}

function Get-WinCareRemoteAccessProfile {
    [CmdletBinding()]param([string]$Id='')
    $profiles=@(
        [pscustomobject]@{Id='rdp';Title='Remote Desktop';Description='RDP listener policy, TermService start/runtime state, firewall rules, and optional drive redirection.';Risk='High';SourceRecords=@('D136-RDP-ENABLE-DISABLE')},
        [pscustomobject]@{Id='openssh';Title='OpenSSH Server';Description='OpenSSH capability admission, service start/runtime state, and the exact server firewall rule.';Risk='High';SourceRecords=@('D136-OPENSSH-ENABLE-DISABLE')}
    )
    if($Id){return @($profiles|Where-Object Id -eq $Id|Select-Object -First 1)}
    $profiles
}

function Get-WinCareRemoteAccessState {
    [CmdletBinding()]param()
    if(-not $IsWindows){return $null}
    $services=@('TermService','sshd','ssh-agent')|ForEach-Object{$name=$_;$escaped=$name.Replace("'","''");$service=Get-CimInstance Win32_Service -Filter "Name='$escaped'" -ErrorAction SilentlyContinue;[pscustomobject]@{Name=$name;Present=[bool]$service;State=if($service){$service.State}else{'Absent'};StartMode=if($service){$service.StartMode}else{'Absent'}}}
    $rules=@('RemoteDesktop-UserMode-In-TCP','RemoteDesktop-UserMode-In-UDP','OpenSSH-Server-In-TCP')|ForEach-Object{$rule=Get-NetFirewallRule -Name $_ -ErrorAction SilentlyContinue;[pscustomobject]@{Name=$_;Present=[bool]$rule;Enabled=if($rule){[bool]$rule.Enabled}else{$null}}}
    $capability=Get-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0' -ErrorAction SilentlyContinue|Select-Object -First 1
    [pscustomobject]@{RdpDeny=Get-WinCareRegistryValueState -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name 'fDenyTSConnections';RdpDriveRedirection=Get-WinCareRegistryValueState -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services' -Name 'fDisableCdm';Services=@($services);FirewallRules=@($rules);OpenSshCapability=if($capability){[string]$capability.State}else{'Unavailable'};SourceRecords=@('D136-RDP-ENABLE-DISABLE','D136-OPENSSH-ENABLE-DISABLE')}
}

function New-WinCareRemoteAccessPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][ValidateSet('rdp','openssh')][string]$ProfileId,[Parameter(Mandatory)][bool]$Enabled,[bool]$AllowDriveRedirection=$false)
    if(-not $IsWindows){throw 'Remote access plans require Windows.'}
    $profile=Get-WinCareRemoteAccessProfile -Id $ProfileId|Select-Object -First 1;$records=@($profile.SourceRecords)
    $plan=New-WinCarePlan -Title "$($profile.Title): $(if($Enabled){'Enable'}else{'Disable'})" -Description $profile.Description -SourceRecords $records
    if($ProfileId -eq 'rdp'){
        $a=New-WinCareExperienceRegistryAction -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name 'fDenyTSConnections' -Value $(if($Enabled){0}else{1}) -ValueType DWord -Label $(if($Enabled){'Enable Remote Desktop listener policy'}else{'Disable Remote Desktop listener policy'}) -Risk High -SourceRecords $records;if($a){$plan.Actions.Add($a)}
        if($Enabled){$a=New-WinCareExperienceRegistryAction -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services' -Name 'fDisableCdm' -Value $(if($AllowDriveRedirection){0}else{1}) -ValueType DWord -Label 'Configure Remote Desktop drive redirection' -Risk High -SourceRecords $records;if($a){$plan.Actions.Add($a)}}
        $service=Get-CimInstance Win32_Service -Filter "Name='TermService'" -ErrorAction SilentlyContinue
        if($service){
            $before=switch([string]$service.StartMode){'Auto'{'Automatic'}'Automatic'{'Automatic'}'Disabled'{'Disabled'}default{'Manual'}};$target=if($Enabled){'Automatic'}else{'Manual'}
            if($before -ne $target){$plan.Actions.Add((New-WinCareAction -Type SetServiceStartMode -Label "Set TermService to $target" -Risk High -Parameters @{Name='TermService';StartMode=$target;Before=$before} -RequiresAdmin $true -Reversible $true -Compensator @{Type='SetServiceStartMode';RequiresAdmin=$true;Parameters=@{Name='TermService';StartMode=$before}} -Tags @('ExperienceStudio','RemoteAccess') -SourceRecords $records -RecoveryDescription 'Restore the exact prior TermService start mode.'))}
            $beforeRuntime=if([string]$service.State -eq 'Running'){'Running'}else{'Stopped'};$targetRuntime=if($Enabled){'Running'}else{'Stopped'}
            if($beforeRuntime -ne $targetRuntime){$plan.Actions.Add((New-WinCareAction -Type SetServiceRuntimeState -Label "Set TermService runtime state to $targetRuntime" -Risk $(if($Enabled){'High'}else{'Critical'}) -Parameters @{Name='TermService';State=$targetRuntime;Before=$beforeRuntime} -RequiresAdmin $true -Reversible $true -Compensator @{Type='SetServiceRuntimeState';RequiresAdmin=$true;Parameters=@{Name='TermService';State=$beforeRuntime;Before=$targetRuntime}} -Tags @('ExperienceStudio','RemoteAccess','Runtime') -SourceRecords $records -RecoveryDescription 'Restore the exact prior TermService runtime state.'))}
        }
        foreach($name in @('RemoteDesktop-UserMode-In-TCP','RemoteDesktop-UserMode-In-UDP')){$rule=Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue;if($rule -and [bool]$rule.Enabled -ne $Enabled){$plan.Actions.Add((New-WinCareAction -Type SetFirewallRuleState -Label "$(if($Enabled){'Enable'}else{'Disable'}) firewall rule $name" -Risk High -Parameters @{Name=$name;Enabled=$Enabled;Before=[bool]$rule.Enabled} -RequiresAdmin $true -Reversible $true -Compensator @{Type='SetFirewallRuleState';RequiresAdmin=$true;Parameters=@{Name=$name;Enabled=[bool]$rule.Enabled}} -Tags @('ExperienceStudio','RemoteAccess','Firewall') -SourceRecords $records -RecoveryDescription 'Restore the exact prior firewall-rule state.'))}}
    }else{
        $capability=Get-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0' -ErrorAction SilentlyContinue|Select-Object -First 1
        if($Enabled -and -not $capability){throw 'OpenSSH Server capability inventory is unavailable.'}
        $installPending=$Enabled -and [string]$capability.State -ne 'Installed'
        if($installPending){
            $beforeCapability=[string]$capability.State;$plan.Description='Stage 1 installs the OpenSSH Server capability. Rerun this profile after servicing completes to configure and start sshd.'
            $plan.Actions.Add((New-WinCareAction -Type SetCapabilityState -Label 'Install OpenSSH Server capability' -Risk High -Parameters @{Name='OpenSSH.Server~~~~0.0.1.0';State='Installed';Before=$beforeCapability} -RequiresAdmin $true -Reversible $true -Compensator @{Type='SetCapabilityState';RequiresAdmin=$true;Parameters=@{Name='OpenSSH.Server~~~~0.0.1.0';State='Removed';Before='Installed'}} -TimeoutSeconds 21600 -Tags @('ExperienceStudio','RemoteAccess','OpenSSH') -SourceRecords $records -RecoveryDescription 'Remove the newly installed OpenSSH Server capability through supported servicing.' -RestartPossible $true))
        }else{
            foreach($spec in @(@{Name='sshd';EnabledMode='Automatic'},@{Name='ssh-agent';EnabledMode='Manual'})){
                $escaped=$spec.Name.Replace("'","''");$service=Get-CimInstance Win32_Service -Filter "Name='$escaped'" -ErrorAction SilentlyContinue
                if($service){
                    $before=switch([string]$service.StartMode){'Auto'{'Automatic'}'Automatic'{'Automatic'}'Disabled'{'Disabled'}default{'Manual'}};$target=if($Enabled){[string]$spec.EnabledMode}else{'Disabled'}
                    if($before -ne $target){$plan.Actions.Add((New-WinCareAction -Type SetServiceStartMode -Label "Set $($spec.Name) to $target" -Risk High -Parameters @{Name=$spec.Name;StartMode=$target;Before=$before} -RequiresAdmin $true -Reversible $true -Compensator @{Type='SetServiceStartMode';RequiresAdmin=$true;Parameters=@{Name=$spec.Name;StartMode=$before}} -Tags @('ExperienceStudio','RemoteAccess','OpenSSH') -SourceRecords $records -RecoveryDescription 'Restore the exact prior service start mode.'))}
                    $beforeRuntime=if([string]$service.State -eq 'Running'){'Running'}else{'Stopped'};$targetRuntime=if($Enabled -and $spec.Name -eq 'sshd'){'Running'}elseif(-not $Enabled){'Stopped'}else{$beforeRuntime}
                    if($beforeRuntime -ne $targetRuntime){$plan.Actions.Add((New-WinCareAction -Type SetServiceRuntimeState -Label "Set $($spec.Name) runtime state to $targetRuntime" -Risk High -Parameters @{Name=$spec.Name;State=$targetRuntime;Before=$beforeRuntime} -RequiresAdmin $true -Reversible $true -Compensator @{Type='SetServiceRuntimeState';RequiresAdmin=$true;Parameters=@{Name=$spec.Name;State=$beforeRuntime;Before=$targetRuntime}} -Tags @('ExperienceStudio','RemoteAccess','OpenSSH','Runtime') -SourceRecords $records -RecoveryDescription 'Restore the exact prior service runtime state.'))}
                }
            }
            $name='OpenSSH-Server-In-TCP';$rule=Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue;if($rule -and [bool]$rule.Enabled -ne $Enabled){$plan.Actions.Add((New-WinCareAction -Type SetFirewallRuleState -Label "$(if($Enabled){'Enable'}else{'Disable'}) firewall rule $name" -Risk High -Parameters @{Name=$name;Enabled=$Enabled;Before=[bool]$rule.Enabled} -RequiresAdmin $true -Reversible $true -Compensator @{Type='SetFirewallRuleState';RequiresAdmin=$true;Parameters=@{Name=$name;Enabled=[bool]$rule.Enabled}} -Tags @('ExperienceStudio','RemoteAccess','Firewall') -SourceRecords $records -RecoveryDescription 'Restore the exact prior firewall-rule state.'))}
        }
    }
    $plan
}

function Get-WinCarePowerConditionProfile {
    [CmdletBinding()]param([string]$Id='')
    $profiles=@(
        [pscustomobject]@{Id='battery-saver';Title='Battery saver';PowerScheme='a1841308-3541-4fab-bc81-f71556f20b4a';Brightness=35;Condition='Battery or charge below 35 percent';Risk='Low';SourceRecords=@('D140-CONDITION-SCHEDULER','D140-POWER-BRIGHTNESS')},
        [pscustomobject]@{Id='balanced-mobile';Title='Balanced mobile';PowerScheme='381b4222-f694-41f0-9685-ff5bb260df2e';Brightness=65;Condition='Normal mobile operation';Risk='Low';SourceRecords=@('D140-CONDITION-SCHEDULER','D140-POWER-BRIGHTNESS')},
        [pscustomobject]@{Id='performance-ac';Title='Performance on AC';PowerScheme='8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c';Brightness=100;Condition='AC power and explicit performance preference';Risk='Moderate';SourceRecords=@('D140-CONDITION-SCHEDULER','D140-POWER-BRIGHTNESS')}
    )
    if($Id){return @($profiles|Where-Object Id -eq $Id|Select-Object -First 1)}
    $profiles
}

function Get-WinCarePowerConditionAssessment {
    [CmdletBinding()]param()
    if(-not $IsWindows){return $null}
    $battery=Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue|Select-Object -First 1
    $line='Unknown';try{Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop;$line=[string]([System.Windows.Forms.SystemInformation]::PowerStatus.PowerLineStatus)}catch{}
    $charge=if($battery){[int]$battery.EstimatedChargeRemaining}else{$null}
    $recommended=if($line -eq 'Online'){'performance-ac'}elseif($null -ne $charge -and $charge -lt 35){'battery-saver'}else{'balanced-mobile'}
    [pscustomobject]@{PowerLineStatus=$line;ChargePercent=$charge;BatteryPresent=[bool]$battery;RecommendedProfile=$recommended;Profiles=@(Get-WinCarePowerConditionProfile);SourceRecords=@('D140-CONDITION-SCHEDULER','D140-POWER-BRIGHTNESS')}
}

function New-WinCarePowerConditionPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ProfileId)
    if(-not $IsWindows){throw 'Power condition plans require Windows.'}
    $profile=Get-WinCarePowerConditionProfile -Id $ProfileId|Select-Object -First 1;if(-not $profile){throw "Unknown power condition profile: $ProfileId"}
    $active=Invoke-WinCareProcess -FilePath powercfg.exe -ArgumentList @('/getactivescheme') -TimeoutSeconds 30;if(-not $active.Success -or [string]$active.Data.StdOut -notmatch '[0-9a-fA-F-]{36}'){throw 'Active power scheme could not be captured.'};$before=$Matches[0]
    $actions=[Collections.Generic.List[object]]::new();if($before -ne [string]$profile.PowerScheme){$actions.Add((New-WinCareAction -Type SetPowerScheme -Label "Activate $($profile.Title) power scheme" -Risk ([string]$profile.Risk) -Parameters @{Scheme=[string]$profile.PowerScheme;Before=$before} -Reversible $true -Compensator @{Type='SetPowerScheme';Parameters=@{Scheme=$before}} -Tags @('ExperienceStudio','PowerCondition') -SourceRecords @($profile.SourceRecords) -RecoveryDescription 'Restore the exact prior active power scheme.'))}
    try{$brightness=New-WinCareBrightnessPlan -Percent ([int]$profile.Brightness);foreach($action in @($brightness.Actions)){$action.SourceRecords=@('D140-POWER-BRIGHTNESS','D10-QUICK-CONTROLS');$actions.Add($action)}}catch{Write-Verbose 'Internal display brightness is unavailable; the power-scheme action remains valid.'}
    New-WinCarePlan -Title "Apply power condition: $($profile.Title)" -Description $profile.Condition -Actions @($actions) -SourceRecords @($profile.SourceRecords)
}

function Get-WinCareDownloadStrategy {
    [CmdletBinding()]param([string]$Id='')
    $store=Get-WinCareDownloadStore -Strict;$jobs=@($store.Jobs);if($Id){$jobs=@($jobs|Where-Object{[string]$_.Id -eq $Id})}
    foreach($job in $jobs|Sort-Object CreatedAt -Descending){
        [pscustomobject]@{Id=$job.Id;Name=$job.Name;Status=$job.Status;UrlCount=@($job.Urls).Count;Urls=@($job.Urls);Strategy=if(@($job.Urls).Count -gt 1){'Sequential mirror failover'}else{'Single source'};Transport='BITS';Priority=$job.Priority;MaxRetries=[int]$job.MaxRetries;HashAlgorithm=$job.HashAlgorithm;ExpectedHashPresent=![string]::IsNullOrWhiteSpace([string]$job.ExpectedHash);Scheduled=![string]::IsNullOrWhiteSpace([string]$job.ScheduledAt);ParallelChunking='Not admitted';RemoteDaemon='Not admitted';SourceRecords=@('D139-MIRROR-STRATEGY','D139-DAEMON-GUARDRAIL','D54-DOWNLOAD-QUEUE')}
    }
}

function Get-WinCarePrivacyProfile {
    [CmdletBinding()]param([string]$Id='')
    $balanced=@('privacy.advertising-id','privacy.tailored-experiences','privacy.disable-suggestions','privacy.disable-feedback-prompts','privacy.activity-history')
    $strict=@($balanced)+@('privacy.consumer-features-disabled','privacy.diagnostic-data-required-minimum','privacy.disable-cross-device-clipboard')
    $maximum=@($strict)+@('gaming.disable-background-capture','performance.menu-delay','explorer.show-extensions','explorer.launch-this-pc')
    $profiles=@(
        [pscustomobject]@{Id='balanced';Title='Balanced privacy';Description='Reversible privacy settings without weakening Defender, SmartScreen, UAC, or Windows Update.';Risk='Moderate';RuleIds=$balanced;SourceRecords=@('D141-PRIVACY-COLLECTION','D141-REVERSIBLE-SCRIPTS')},
        [pscustomobject]@{Id='strict';Title='Strict privacy';Description='Adds consumer-feature, diagnostic-data, and cross-device restrictions.';Risk='High';RuleIds=$strict;SourceRecords=@('D141-PRIVACY-COLLECTION','D141-SCRIPT-GUIDELINES')},
        [pscustomobject]@{Id='maximum';Title='Maximum reversible privacy';Description='Largest reversible privacy set that remains inside WinCare catalog and recovery contracts.';Risk='High';RuleIds=$maximum;SourceRecords=@('D141-PRIVACY-COLLECTION','D141-REVERSIBLE-SCRIPTS','D133-RYTUNEX-FEATURE-CATALOG')}
    )
    if($Id){return @($profiles|Where-Object Id -eq $Id|Select-Object -First 1)}
    $profiles
}

function New-WinCarePrivacyProfilePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ProfileId)
    $profile=Get-WinCarePrivacyProfile -Id $ProfileId|Select-Object -First 1;if(-not $profile){throw "Unknown privacy profile: $ProfileId"}
    $plan=New-WinCareCatalogPlan -RuleId @($profile.RuleIds) -Title $profile.Title
    $plan.Description=$profile.Description;$plan.SourceRecords=@($profile.SourceRecords);foreach($action in @($plan.Actions)){$action.SourceRecords=@($action.SourceRecords)+@($profile.SourceRecords)|Select-Object -Unique};$plan
}

function Get-WinCareExperienceCapabilityCard {
    [CmdletBinding()]param([string]$Query='')
    $cards=@(
        [pscustomobject]@{Id='visual-assets';Title='Visual assets and sprite sheets';Summary='Read-only image metadata, palette extraction, bounded frame maps, and protected manifest export.';Risk='Low';Commands=@('experience-visual-asset','experience-sprite-layout','experience-visual-manifest');SourceRecords=@('D130-SPRITE-METADATA','D130-SPRITE-SHEET-LAYOUT','D135-CAPABILITY-PRESENTATION')},
        [pscustomobject]@{Id='location-privacy';Title='Location privacy';Summary='Machine policy and current-user consent without disabling the location service.';Risk='High';Commands=@('experience-location-state','experience-location-set');SourceRecords=@('D137-LOCATION-PERMISSION-MODEL')},
        [pscustomobject]@{Id='remote-access';Title='Remote access';Summary='RDP and OpenSSH state and reversible typed plans.';Risk='High';Commands=@('experience-remote-profiles','experience-remote-state','experience-remote-set');SourceRecords=@('D136-RDP-ENABLE-DISABLE','D136-OPENSSH-ENABLE-DISABLE')},
        [pscustomobject]@{Id='power-conditions';Title='Power condition profiles';Summary='Battery and AC-aware power-scheme and brightness recommendations.';Risk='Moderate';Commands=@('experience-power-profiles','experience-power-assess','experience-power-apply');SourceRecords=@('D140-CONDITION-SCHEDULER','D140-POWER-BRIGHTNESS')},
        [pscustomobject]@{Id='download-strategy';Title='Download strategy';Summary='Explains WinCare BITS mirror failover, retry, scheduling, and integrity controls.';Risk='Low';Commands=@('experience-download-strategy');SourceRecords=@('D139-MIRROR-STRATEGY','D139-DAEMON-GUARDRAIL')},
        [pscustomobject]@{Id='privacy-profiles';Title='Privacy profiles';Summary='Machine-readable reversible profiles plus separately authorized destructive presets.';Risk='High';Commands=@('experience-privacy-profiles','experience-privacy-apply','legacy-unsafe');SourceRecords=@('D141-PRIVACY-COLLECTION','D133-RYTUNEX-FEATURE-CATALOG')}
    )
    if($Query){$q=$Query.ToLowerInvariant();return @($cards|Where-Object{([string]$_.Id).ToLowerInvariant().Contains($q)-or([string]$_.Title).ToLowerInvariant().Contains($q)-or([string]$_.Summary).ToLowerInvariant().Contains($q)})}
    $cards
}


