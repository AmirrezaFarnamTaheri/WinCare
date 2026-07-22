function Get-WinCareCatalogPath { Join-Path $script:WinCareModuleRoot 'Data\Catalog\rules.json' }
function Get-WinCarePresetPath { Join-Path $script:WinCareModuleRoot 'Data\Catalog\presets.json' }

function Test-WinCareCatalogRule {
    param([Parameter(Mandatory)][Collections.IDictionary]$Rule)
    $allowed=@('id','title','description','category','risk','requiresAdmin','reversible','restartPossible','tags','sourceRecords','compatibility','changes','recovery')
    $null=Test-WinCareStrictObjectKeys -InputObject $Rule -AllowedKeys $allowed -Context "catalog rule $($Rule.id)"
    if([string]$Rule.id -notmatch '^[a-z0-9][a-z0-9._-]{2,100}$'){throw "Invalid rule id: $($Rule.id)"}
    if([string]$Rule.risk -notin @('Low','Moderate','High','Critical')){throw "Invalid risk in rule $($Rule.id)"}
    if(@($Rule.changes).Count -eq 0){throw "Rule $($Rule.id) has no changes."}
    foreach($change in @($Rule.changes)){
        $null=Test-WinCareStrictObjectKeys -InputObject $change -AllowedKeys @('type','parameters','verification','compensator','preconditions','postconditions') -Context "change in $($Rule.id)"
        if(-not $change.type -or $null -eq $change.parameters -or $change.parameters -isnot [Collections.IDictionary]){throw "Rule $($Rule.id) has an incomplete change."}
        $contract=Get-WinCareActionContract -Type ([string]$change.type)
        if(-not $contract){throw "Rule $($Rule.id) references unknown action type $($change.type)."}
        if((Get-WinCareRiskRank ([string]$Rule.risk)) -lt (Get-WinCareRiskRank ([string]$contract.MinimumRisk))){throw "Rule $($Rule.id) risk is below the action contract minimum for $($change.type)."}
        if([bool]$contract.AlwaysAdmin -and -not[bool]$Rule.requiresAdmin){throw "Rule $($Rule.id) must require administrator privileges for $($change.type)."}
        $parameters=ConvertTo-WinCareParameterDictionary $change.parameters
        $missing=@($contract.Required|Where-Object{$_ -notin @($parameters.Keys)});if($missing.Count){throw "Rule $($Rule.id) is missing action parameter(s): $($missing -join ', ')"}
        $unknown=@($parameters.Keys|Where-Object{$_ -notin @($contract.Allowed)});if($unknown.Count){throw "Rule $($Rule.id) contains unknown action parameter(s): $($unknown -join ', ')"}
        $plainParameters=@{};foreach($key in $parameters.Keys){$plainParameters[[string]$key]=$parameters[$key]}
        $candidate=New-WinCareAction -Type ([string]$change.type) -Label "Validate catalog rule $($Rule.id)" -Risk ([string]$Rule.risk) -Parameters $plainParameters -RequiresAdmin ([bool]$Rule.requiresAdmin) -Reversible $false -Verification ([string]$change.verification) -Preconditions @($change.preconditions) -Postconditions @($change.postconditions) -TimeoutSeconds ([Math]::Min(3600,[int]$contract.MaximumTimeout)) -Tags @($Rule.tags) -SourceRecords @($Rule.sourceRecords)
        if([bool]$Rule.reversible -and -not $change.compensator -and [string]$change.type -notin @('SetRegistryValue','SetOptionalFeatureState','SetCapabilityState','SetDefenderPreference','SetServiceStartMode','SetScheduledTaskState','SetFirewallProfileState','SetLocalUserState','SetPowerScheme')){throw "Rule $($Rule.id) claims reversibility but $($change.type) has no static or provider-derived compensator."}
        $candidateValidation=Test-WinCareActionContract -Action $candidate
        if(-not $candidateValidation.Success){throw "Rule $($Rule.id) has invalid $($change.type) semantics: $($candidateValidation.Message)"}
        if($change.compensator){
            $null=Test-WinCareStrictObjectKeys -InputObject $change.compensator -AllowedKeys @('Type','Parameters','RequiresAdmin') -Context "compensator in $($Rule.id)"
            $compensatorContract=Get-WinCareActionContract -Type ([string]$change.compensator.Type)
            if(-not $compensatorContract){throw "Rule $($Rule.id) contains an unknown compensator type."}
            $compensatorParameters=ConvertTo-WinCareParameterDictionary $change.compensator.Parameters;$plainCompensator=@{};foreach($key in $compensatorParameters.Keys){$plainCompensator[[string]$key]=$compensatorParameters[$key]}
            $compensatorAction=New-WinCareAction -Type ([string]$change.compensator.Type) -Label "Validate compensator for $($Rule.id)" -Risk ([string]$compensatorContract.MinimumRisk) -Parameters $plainCompensator -RequiresAdmin ([bool]$change.compensator.RequiresAdmin -or [bool]$compensatorContract.AlwaysAdmin) -TimeoutSeconds ([Math]::Min(3600,[int]$compensatorContract.MaximumTimeout))
            $compensatorValidation=Test-WinCareActionContract -Action $compensatorAction
            if(-not $compensatorValidation.Success){throw "Rule $($Rule.id) has an invalid compensator: $($compensatorValidation.Message)"}
        }
    }
    foreach($tag in @($Rule.tags)){if([string]$tag -notmatch '^[A-Za-z0-9._-]{1,80}$'){throw "Rule $($Rule.id) contains an invalid tag."}}
    foreach($record in @($Rule.sourceRecords)){if([string]$record -notmatch '^[A-Za-z0-9._:-]{2,160}$'){throw "Rule $($Rule.id) contains an invalid source record."}}
    return $true
}

function Get-WinCareCatalog {
    [CmdletBinding()]param([switch]$Refresh)
    Ensure-WinCareState
    if(-not $Refresh -and $script:WinCareState.ContainsKey('catalog')){return $script:WinCareState.catalog}
    $document=Read-WinCareJsonHashtable -LiteralPath (Get-WinCareCatalogPath)
    $null=Test-WinCareStrictObjectKeys -InputObject $document -AllowedKeys @('schemaVersion','rules') -Context 'catalog'
    if([int]$document.schemaVersion -ne 1){throw 'Unsupported catalog schema.'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($rule in @($document.rules)){$null=Test-WinCareCatalogRule -Rule $rule;if(-not $seen.Add([string]$rule.id)){throw "Duplicate rule id: $($rule.id)"}}
    $all=[Collections.Generic.List[object]]::new();foreach($rule in @($document.rules)){$all.Add($rule)};if(Get-Command Get-WinCareExtensionCatalogRule -ErrorAction SilentlyContinue){foreach($rule in @(Get-WinCareExtensionCatalogRule)){$null=Test-WinCareCatalogRule -Rule $rule;if(-not $seen.Add([string]$rule.id)){throw "Duplicate rule id across built-in and extension catalogs: $($rule.id)"};$all.Add($rule)}}
    $script:WinCareState.catalog=@($all)
    return $script:WinCareState.catalog
}

function Get-WinCarePreset {
    [CmdletBinding()]param([string]$Id)
    $document=Read-WinCareJsonHashtable -LiteralPath (Get-WinCarePresetPath)
    $null=Test-WinCareStrictObjectKeys -InputObject $document -AllowedKeys @('schemaVersion','presets') -Context 'presets'
    if([int]$document.schemaVersion -ne 1){throw 'Unsupported preset schema.'}
    $catalogIds=@(Get-WinCareCatalog|ForEach-Object{[string]$_.id})
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($preset in @($document.presets)){
        $null=Test-WinCareStrictObjectKeys -InputObject $preset -AllowedKeys @('id','title','description','ruleIds') -Context 'preset'
        if([string]$preset.id -notmatch '^[a-z0-9][a-z0-9._-]{2,80}$' -or -not $seen.Add([string]$preset.id)){throw "Invalid or duplicate preset ID: $($preset.id)"}
        if([string]::IsNullOrWhiteSpace([string]$preset.title) -or @($preset.ruleIds).Count -eq 0){throw "Preset $($preset.id) is incomplete."}
        $unknown=@($preset.ruleIds|Where-Object{$_ -notin $catalogIds});if($unknown.Count){throw "Preset $($preset.id) references unknown rule(s): $($unknown -join ', ')"}
    }
    if($Id){return @($document.presets|Where-Object id -eq $Id|Select-Object -First 1)}
    return @($document.presets)
}

function Test-WinCareRuleCompatibility {
    param([object]$Rule)
    $build=try{[int](Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).BuildNumber}catch{0}
    $compat=$Rule.compatibility
    if($compat){
        if($compat.minBuild -and $build -lt [int]$compat.minBuild){return $false}
        if($compat.maxBuild -and $build -gt [int]$compat.maxBuild){return $false}
        $required=@($compat.requires);$available=@($required|Where-Object {Test-WinCareCapability $_});if($required.Count -gt 0 -and $available.Count -ne $required.Count){return $false}
    }
    return $true
}

function Get-WinCareRegistryValueState {
    param([string]$Path,[string]$Name)
    try{
        $key=Get-Item -LiteralPath $Path -ErrorAction Stop
        $names=@($key.GetValueNames())
        if($Name -notin $names){return [pscustomobject]@{Path=$Path;Name=$Name;Exists=$false;Value=$null;Kind=$null;ValueType='String'}}
        $value=$key.GetValue($Name,$null,'DoNotExpandEnvironmentNames')
        $kind=$key.GetValueKind($Name).ToString()
        [pscustomobject]@{Path=$Path;Name=$Name;Exists=$true;Value=$value;Kind=$kind;ValueType=$kind}
    }catch{[pscustomobject]@{Path=$Path;Name=$Name;Exists=$false;Value=$null;Kind=$null;ValueType='String'}}
}

function ConvertTo-WinCareCatalogAction {
    param([Parameter(Mandatory)][object]$Rule,[Parameter(Mandatory)][object]$Change)
    $parameters=[hashtable]$Change.parameters
    $compensator=@{}
    if($Change.compensator){
        $descriptor=ConvertTo-WinCareParameterDictionary $Change.compensator
        $compensatorParameters=ConvertTo-WinCareParameterDictionary $descriptor.Parameters
        $compensator=@{Type=[string]$descriptor.Type;Parameters=[hashtable]$compensatorParameters}
        if($descriptor.ContainsKey('RequiresAdmin')){$compensator.RequiresAdmin=[bool]$descriptor.RequiresAdmin}
    }elseif($Change.type -eq 'SetRegistryValue'){
        $before=Get-WinCareRegistryValueState -Path ([string]$parameters.Path) -Name ([string]$parameters.Name)
        $compensator=if($before.Exists){@{Type='SetRegistryValue';Parameters=@{Path=$parameters.Path;Name=$parameters.Name;Value=$before.Value;ValueType=$before.Kind}}}else{@{Type='RemoveRegistryValue';Parameters=@{Path=$parameters.Path;Name=$parameters.Name}}}
    }elseif($Change.type -eq 'SetOptionalFeatureState'){
        try{$feature=Get-WindowsOptionalFeature -Online -FeatureName $parameters.Name -ErrorAction Stop;$compensator=@{Type='SetOptionalFeatureState';RequiresAdmin=$true;Parameters=@{Name=$parameters.Name;State=if($feature.State -eq 'Enabled'){'Enabled'}else{'Disabled'}}}}catch{throw "Current optional-feature state could not be captured for reversible rule $($Rule.id): $($_.Exception.Message)"}
    }elseif($Change.type -eq 'SetCapabilityState'){
        try{$cap=Get-WindowsCapability -Online -Name $parameters.Name -ErrorAction Stop|Select-Object -First 1;$compensator=@{Type='SetCapabilityState';RequiresAdmin=$true;Parameters=@{Name=$parameters.Name;State=if($cap.State -eq 'Installed'){'Installed'}else{'Removed'}}}}catch{throw "Current capability state could not be captured for reversible rule $($Rule.id): $($_.Exception.Message)"}
    }elseif($Change.type -eq 'SetDefenderPreference'){
        try{$preference=Get-MpPreference -ErrorAction Stop;$before=Get-WinCarePropertyValue $preference ([string]$parameters.Name) $null;if($null -eq $before){throw "Preference '$($parameters.Name)' is unavailable."};$compensator=@{Type='SetDefenderPreference';RequiresAdmin=$true;Parameters=@{Name=$parameters.Name;Value=$before}}}catch{throw "Current Defender preference could not be captured for reversible rule $($Rule.id): $($_.Exception.Message)"}
    }
    elseif($Change.type -eq 'SetServiceStartMode'){
        try{
            $serviceName=([string]$parameters.Name).Replace("'","''")
            $serviceFilter="Name='$serviceName'"
            $service=Get-CimInstance Win32_Service -Filter $serviceFilter -ErrorAction Stop
            $before=switch([string]$service.StartMode){'Auto'{'Automatic'}'Automatic'{'Automatic'}'Manual'{'Manual'}'Disabled'{'Disabled'}default{'Manual'}}
            $compensator=@{Type='SetServiceStartMode';RequiresAdmin=$true;Parameters=@{Name=$parameters.Name;StartMode=$before}}
        }catch{throw "Current service state could not be captured for reversible rule $($Rule.id): $($_.Exception.Message)"}
    }elseif($Change.type -eq 'SetScheduledTaskState'){
        try{$task=Get-ScheduledTask -TaskPath ([string]$parameters.TaskPath) -TaskName ([string]$parameters.TaskName) -ErrorAction Stop;$compensator=@{Type='SetScheduledTaskState';RequiresAdmin=[bool]$Rule.requiresAdmin;Parameters=@{TaskPath=$parameters.TaskPath;TaskName=$parameters.TaskName;Enabled=([string]$task.State -ne 'Disabled')}}}catch{throw "Current scheduled-task state could not be captured for reversible rule $($Rule.id): $($_.Exception.Message)"}
    }elseif($Change.type -eq 'SetFirewallProfileState'){
        try{$before=@(Get-NetFirewallProfile -Profile @($parameters.Profiles) -ErrorAction Stop|ForEach-Object{@{Name=[string]$_.Name;Enabled=[bool]$_.Enabled}});if(@($before|Select-Object -ExpandProperty Enabled -Unique).Count -ne 1){throw 'Selected profiles do not share one prior state.'};$compensator=@{Type='SetFirewallProfileState';RequiresAdmin=$true;Parameters=@{Profiles=@($parameters.Profiles);Enabled=[bool]$before[0].Enabled}}}catch{throw "Current firewall profile state could not be captured for reversible rule $($Rule.id): $($_.Exception.Message)"}
    }elseif($Change.type -eq 'SetLocalUserState'){
        try{$user=Get-LocalUser -Name ([string]$parameters.Name) -ErrorAction Stop;$compensator=@{Type='SetLocalUserState';RequiresAdmin=$true;Parameters=@{Name=$parameters.Name;Enabled=[bool]$user.Enabled}}}catch{throw "Current local-user state could not be captured for reversible rule $($Rule.id): $($_.Exception.Message)"}
    }elseif($Change.type -eq 'SetPowerScheme'){
        try{$active=Invoke-WinCareProcess -FilePath powercfg.exe -ArgumentList @('/getactivescheme') -TimeoutSeconds 30;if(-not $active.Success -or [string]$active.Data.StdOut -notmatch '[0-9a-fA-F-]{36}'){throw 'Active power scheme could not be parsed.'};$compensator=@{Type='SetPowerScheme';Parameters=@{Scheme=$Matches[0]}}}catch{throw "Current power scheme could not be captured for reversible rule $($Rule.id): $($_.Exception.Message)"}
    }
    if([bool]$Rule.reversible -and $compensator.Count -eq 0){throw "Rule $($Rule.id) is marked reversible but no executable compensator could be produced."}
    New-WinCareAction -Type ([string]$Change.type) -Label ([string]$Rule.title) -Risk ([string]$Rule.risk) -Parameters $parameters -RequiresAdmin ([bool]$Rule.requiresAdmin) -Reversible ([bool]$Rule.reversible) -Verification ([string]$Change.verification) -Preconditions @($Change.preconditions) -Postconditions @($Change.postconditions) -Compensator $compensator -Tags @($Rule.tags)+@('Catalog','Idempotent') -Compatibility ([hashtable]$Rule.compatibility) -SourceRecords @($Rule.sourceRecords) -RecoveryDescription ([string]$Rule.recovery) -RestartPossible ([bool]$Rule.restartPossible)
}

function New-WinCareCatalogPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string[]]$RuleId,[string]$Title='Apply WinCare configuration rules')
    $catalog=Get-WinCareCatalog;$plan=New-WinCarePlan -Title $Title -Description 'Target-native, compatibility-aware Windows configuration plan.' -SourceRecords @('CG-TWEAK-POLICY')
    foreach($id in $RuleId){
        $rule=$catalog|Where-Object id -eq $id|Select-Object -First 1
        if(-not $rule){throw "Unknown catalog rule: $id"}
        if(-not (Test-WinCareCatalogRulePolicy -Rule $rule)){throw "Catalog rule is denied by policy: $id"}
        if(-not (Test-WinCareRuleCompatibility -Rule $rule)){throw "Catalog rule is not compatible with this Windows build: $id"}
        foreach($change in @($rule.changes)){$null=$plan.Actions.Add((ConvertTo-WinCareCatalogAction -Rule $rule -Change $change))}
    }
    return $plan
}

function New-WinCarePresetPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$PresetId)
    $preset=Get-WinCarePreset -Id $PresetId|Select-Object -First 1;if(-not $preset){throw "Unknown preset: $PresetId"}
    New-WinCareCatalogPlan -RuleId @($preset.ruleIds) -Title ([string]$preset.title)
}

function Get-WinCareAppRemovalProfile {
    [CmdletBinding()]param([string]$Id)
    $path=Join-Path $script:WinCareModuleRoot 'Data\Catalog\app-removal-profiles.json';$document=Read-WinCareJsonHashtable -LiteralPath $path
    $null=Test-WinCareStrictObjectKeys -InputObject $document -AllowedKeys @('schemaVersion','profiles') -Context 'app removal profiles';if([int]$document.schemaVersion-ne 1){throw 'Unsupported app removal profile schema.'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($profile in @($document.profiles)){
        $null=Test-WinCareStrictObjectKeys -InputObject $profile -AllowedKeys @('id','title','description','risk','patterns','sourceRecords') -Context "app removal profile $($profile.id)"
        if([string]$profile.id -notmatch '^[a-z0-9][a-z0-9._-]{2,80}$' -or -not $seen.Add([string]$profile.id)){throw "Invalid or duplicate app removal profile ID: $($profile.id)"}
        if([string]$profile.risk -notin @('Moderate','High','Critical')){throw "Invalid app removal profile risk: $($profile.risk)"}
        if(@($profile.patterns).Count -eq 0 -or @($profile.patterns).Count -gt 100){throw "App removal profile $($profile.id) has an invalid pattern count."}
        foreach($pattern in @($profile.patterns)){if([string]$pattern -notmatch '^[A-Za-z0-9_.-]+$'){throw "Unsafe app-removal pattern in $($profile.id): $pattern"}}
    }
    if($Id){return @($document.profiles|Where-Object id -eq $Id|Select-Object -First 1)};return @($document.profiles)
}

function Get-WinCareAppRemovalProfileMatch {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Profile)
    $installed=@(Get-AppxPackage -PackageTypeFilter Main,Bundle -ErrorAction SilentlyContinue|Where-Object{$name=$_.Name;[bool](@($Profile.patterns)|Where-Object{$name -like $_}|Select-Object -First 1)})
    $provisioned=@();if($script:WinCareState.IsAdmin){$provisioned=@(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue|Where-Object{$name=$_.DisplayName;[bool](@($Profile.patterns)|Where-Object{$name -like $_}|Select-Object -First 1)})}
    [pscustomobject]@{Installed=$installed;Provisioned=$provisioned;Profile=$Profile}
}

function New-WinCareAppRemovalProfilePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ProfileId,[switch]$IncludeProvisioned)
    $profile=Get-WinCareAppRemovalProfile -Id $ProfileId|Select-Object -First 1;if(-not $profile){throw "Unknown app removal profile: $ProfileId"}
    $match=Get-WinCareAppRemovalProfileMatch -Profile $profile;$plan=New-WinCarePlan -Title ([string]$profile.title) -Description ([string]$profile.description) -RollbackOnFailure $false -SourceRecords @($profile.sourceRecords)
    if($IncludeProvisioned){foreach($package in @($match.Provisioned)){$plan.Actions.Add((New-WinCareAction -Type RemoveProvisionedAppx -Label "Remove provisioned $($package.DisplayName)" -Risk High -Parameters @{PackageName=$package.PackageName} -RequiresAdmin $true -Reversible $false -Tags @('Appx','Debloat','Provisioned') -SourceRecords @($profile.sourceRecords) -RecoveryDescription 'Reinstall from Microsoft Store or installation media where available.'))}}
    foreach($package in @($match.Installed)){$plan.Actions.Add((New-WinCareAction -Type RemoveAppxPackage -Label "Remove $($package.Name) for current user" -Risk ([string]$profile.risk) -Parameters @{PackageFullName=$package.PackageFullName;Name=$package.Name} -RequiresAdmin $false -Reversible $false -Tags @('Appx','Debloat','CurrentUser') -SourceRecords @($profile.sourceRecords) -RecoveryDescription 'Reinstall from Microsoft Store where available.'))}
    return $plan
}
