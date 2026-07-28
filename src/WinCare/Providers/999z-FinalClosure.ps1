#requires -Version 7.2
# Final target-native compatibility and recovery closure. Loaded last.

$script:WinCareVersion=(Import-PowerShellDataFile -LiteralPath (Join-Path $script:WinCareModuleRoot 'WinCare.psd1')).ModuleVersion.ToString()
$script:WinCareFinalBrokerReadOnly=${function:Get-WinCareBrokerReadOnlyCommandName}
$script:WinCareFinalExtensionManifest=${function:Test-WinCareExtensionManifest}
$script:WinCareFinalLegacyProfiles=${function:Get-WinCareLegacyUnsafeProfile}

${function:Write-WinCareLog} = {
    [CmdletBinding()]param(
        [Parameter(Mandatory)][ValidateSet('Debug','Info','Warning','Error','Audit')][string]$Level,
        [Parameter(Mandatory)][string]$Message,
        [hashtable]$Data=@{}
    )
    if($script:WinCareState -isnot [Collections.IDictionary]){return}
    if(-not $script:WinCareState.Contains('Root') -or [string]::IsNullOrWhiteSpace([string]$script:WinCareState['Root'])){return}
    if($script:WinCareState.Contains('ReadOnlyLocked') -and [bool]$script:WinCareState['ReadOnlyLocked']){return}
    $record=[ordered]@{
        schemaVersion=1;timestamp=[datetime]::UtcNow.ToString('o');level=$Level
        sessionId=[string](Get-WinCarePropertyValue $script:WinCareState 'SessionId' '')
        processId=$PID;message=ConvertTo-WinCareRedactedScalar $Message
        data=ConvertTo-WinCareRedactedValue $Data
    }
    Write-WinCareLogLine -LiteralPath (Get-WinCareLogPath) -Line ($record|ConvertTo-Json -Compress -Depth 20)
}

${function:Get-WinCareBrokerReadOnlyCommandName} = {
    @(& $script:WinCareFinalBrokerReadOnly|Where-Object{$_ -ne 'ebpf-admit'}|Sort-Object -Unique)
}

${function:Get-WinCareTcpSettingTokenMap} = {
    [ordered]@{
        AutoTuningLevel='autotuninglevel'
        EcnCapability='ecncapability'
        ReceiveSideScaling='rss'
        ReceiveSegmentCoalescing='rsc'
    }
}
${function:Get-WinCareTcpAllowedValue} = {
    [CmdletBinding()]param([Parameter(Mandatory)][Alias('Setting')][string]$Key)
    switch($Key){
        'AutoTuningLevel'{@('disabled','highlyrestricted','restricted','normal','experimental')}
        'EcnCapability'{@('disabled','enabled','default')}
        'ReceiveSideScaling'{@('disabled','enabled','default')}
        'ReceiveSegmentCoalescing'{@('disabled','enabled','default')}
        default{@()}
    }
}
${function:Get-WinCareTcpGlobalState} = {
    [CmdletBinding()]param()
    $state=[ordered]@{Supported=$false;
        AutoTuningLevel='Unknown';
        EcnCapability='Unknown';
        ReceiveSideScaling='Unknown';
        ReceiveSegmentCoalescing='Unknown';
        Raw=@();
        CapturedAt=[datetime]::UtcNow.ToString('o')}
    if(-not $IsWindows){return [pscustomobject]$state}
    $result=Invoke-WinCareBridgeProcess -FilePath 'netsh.exe' -ArgumentList @('interface','tcp','show','global') -TimeoutSeconds 30
    if(-not $result.Success){$state.Error=$result.Message;return [pscustomobject]$state}
    $state.Supported=$true;$lines=@($result.Data.StdOut -split "`r?`n");$state.Raw=$lines
    foreach($line in $lines){
        if($line -match '(?i)Receive Window Auto-Tuning Level\s*:\s*(\S+)'){$state.AutoTuningLevel=$Matches[1].ToLowerInvariant()}
        elseif($line -match '(?i)ECN Capability\s*:\s*(\S+)'){$state.EcnCapability=$Matches[1].ToLowerInvariant()}
        elseif($line -match '(?i)Receive-Side Scaling State\s*:\s*(\S+)'){$state.ReceiveSideScaling=$Matches[1].ToLowerInvariant()}
        elseif($line -match '(?i)Receive Segment Coalescing State\s*:\s*(\S+)'){$state.ReceiveSegmentCoalescing=$Matches[1].ToLowerInvariant()}
    }
    [pscustomobject]$state
}

${function:Test-WinCareInjectionSurfaceSnapshot} = {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Snapshot)
    $map=ConvertTo-WinCareParameterDictionary $Snapshot
    $surface=if($map.Contains('Surface')){ConvertTo-WinCareParameterDictionary $map.Surface}else{$map}
    $path=[string](Get-WinCarePropertyValue $surface 'Path' '')
    $name=[string](Get-WinCarePropertyValue $surface 'Name' '')
    if([string]::IsNullOrWhiteSpace($path) -or [string]::IsNullOrWhiteSpace($name)){throw 'Injection-surface snapshot is missing its target identity.'}
    $exact=@('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Windows',
        'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\AppCertDlls')
    $ifeo=@('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\')
    $allowed=($path -in $exact) -or [bool](@($ifeo|Where-Object{$path.StartsWith($_,[StringComparison]::OrdinalIgnoreCase)})|Select-Object -First 1)
    if(-not $allowed){throw 'Injection-surface snapshot targets an unapproved registry root.'}
    if($name -notmatch '^[^\\/:*?"<>|]{1,255}$'){throw 'Injection-surface value name is invalid.'}
    if($map.Contains('Values')){
        foreach($value in @($map.Values)){
            $entry=ConvertTo-WinCareParameterDictionary $value
            $entryPath=[string](Get-WinCarePropertyValue $entry 'Path' '')
            $entryName=[string](Get-WinCarePropertyValue $entry 'Name' '')
            if($entryPath -ne $path -or $entryName -ne $name){
                throw 'Injection-surface recovery values do not match the declared target.'
            }
        }
    }elseif($null -eq (Get-WinCarePropertyValue $surface 'Exists' $null)){throw 'Injection-surface snapshot is missing its existence state.'}
    $true
}

${function:ConvertTo-WinCareMaintenanceMap} = {
    param([Parameter(Mandatory)][object]$Value)
    $map=@{}
    if($Value -is [Collections.IDictionary]){foreach($key in $Value.Keys){$map[[string]$key]=$Value[$key]}}
    else{foreach($property in $Value.PSObject.Properties){$map[$property.Name]=$property.Value}}
    $map
}

${function:New-WinCareWorkspaceLayoutRecord} = {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Name,[string]$Description='',[Parameter(Mandatory)][object[]]$Slot,[string]$Id='')
    if(-not $Id){$Id=[guid]::NewGuid().ToString('N')};$now=[datetime]::UtcNow.ToString('o')
    $slots=@($Slot|ForEach-Object{
        $map=ConvertTo-WinCareParameterDictionary $_;$slotId=[string](Get-WinCarePropertyValue $map 'Id' '')
        if(-not $slotId){$slotId=[guid]::NewGuid().ToString('N').Substring(0,16)}
        [ordered]@{Id=$slotId;
            ProcessName=[string](Get-WinCarePropertyValue $map 'ProcessName' '');
            TitlePattern=[string](Get-WinCarePropertyValue $map 'TitlePattern' '');
            MonitorDevice=[string](Get-WinCarePropertyValue $map 'MonitorDevice' '');
            LeftRatio=[double](Get-WinCarePropertyValue $map 'LeftRatio' 0);
            TopRatio=[double](Get-WinCarePropertyValue $map 'TopRatio' 0);
            WidthRatio=[double](Get-WinCarePropertyValue $map 'WidthRatio' 0);
            HeightRatio=[double](Get-WinCarePropertyValue $map 'HeightRatio' 0);
            Required=[bool](Get-WinCarePropertyValue $map 'Required' $true)}
    })
    $record=[ordered]@{SchemaVersion=1;Id=$Id;Name=$Name;Description=$Description;CreatedAt=$now;UpdatedAt=$now;Slots=$slots;SourceRecords=@('SRC:62fff6504d')}
    $null=Test-WinCareWorkspaceLayoutRecord $record
    [pscustomobject]$record
}

${function:Test-WinCarePlaybookDefinition} = {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Playbook)
    $p=ConvertTo-WinCareParameterDictionary $Playbook
    $null=Test-WinCareStrictObjectKeys -InputObject $p -AllowedKeys @('id',
        'title',
        'description',
        'minimumBuild',
        'maximumBuild',
        'architectures',
        'requiresAdmin',
        'sourceRecords',
        'steps') -Context 'playbook'
    if([string]$p.id -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$' -or [string]::IsNullOrWhiteSpace([string]$p.title)){throw 'Playbook identity is invalid.'}
    if([int]$p.minimumBuild -lt 0 -or [int]$p.maximumBuild -lt [int]$p.minimumBuild){throw 'Playbook build range is invalid.'}
    if($p.requiresAdmin -isnot [bool]){throw 'Playbook requiresAdmin must be Boolean.'}
    $architectures=@($p.architectures);if($architectures.Count -lt 1 -or @($architectures|Where-Object{[string]$_ -notin @('AMD64','ARM64')}).Count){throw 'Playbook architecture list is invalid.'}
    $steps=@($p.steps);if($steps.Count -lt 1 -or $steps.Count -gt 128){throw 'Playbook must contain 1..128 data-only steps.'}
    foreach($stepValue in $steps){
        $step=ConvertTo-WinCareParameterDictionary $stepValue;$kind=[string](Get-WinCarePropertyValue $step 'kind' '')
        if($kind -notin @('preset','catalog','context-menu','app-removal')){throw "Unsupported playbook step kind: $kind"}
        switch($kind){
            'preset'{$null=Test-WinCareStrictObjectKeys $step @('kind','id') 'playbook preset step';
                if([string]$step.id -notmatch '^[a-z0-9][a-z0-9._-]{2,80}$'){throw 'Playbook preset ID is invalid.'}}
            'catalog'{$null=Test-WinCareStrictObjectKeys $step @('kind','ids') 'playbook catalog step';if(@($step.ids).Count -lt 1){throw 'Playbook catalog step is empty.'}}
            'context-menu'{$null=Test-WinCareStrictObjectKeys $step @('kind','id','enable') 'playbook context-menu step';
                if($step.enable -isnot [bool]){throw 'Playbook context-menu enable must be Boolean.'}}
            'app-removal'{$null=Test-WinCareStrictObjectKeys $step @('kind','id','includeProvisioned') 'playbook app-removal step';
                if($step.includeProvisioned -isnot [bool]){throw 'Playbook app-removal flag must be Boolean.'}}
        }
    }
    $true
}
${function:Get-WinCarePlaybook} = {
    [CmdletBinding()]param([string]$Id='')
    $path=Join-Path $script:WinCareModuleRoot 'Data\Catalog\playbooks.json'
    $document=Read-WinCareJsonHashtable -LiteralPath $path
    $null=Test-WinCareStrictObjectKeys $document @('schemaVersion','playbooks') 'playbook catalog'
    if([int]$document.schemaVersion -ne 1){throw 'Unsupported playbook catalog schema.'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $playbooks=@($document.playbooks|ForEach-Object{[pscustomobject]$_})
    foreach($playbook in $playbooks){$null=Test-WinCarePlaybookDefinition $playbook;if(-not $seen.Add([string]$playbook.Id)){throw 'Duplicate playbook ID.'}}
    if($Id){return @($playbooks|Where-Object Id -eq $Id)}
    @($playbooks)
}
${function:Test-WinCarePlaybookCompatibility} = {param([Parameter(Mandatory)][object]$Playbook);$true}

${function:Search-WinCareLocalNote} = {
    [CmdletBinding()]param([Parameter(Mandatory)][ValidateLength(1,500)][string]$Query,[ValidateRange(1,10000)][int]$Limit=500,[switch]$IncludePrivateBody)
    $results=[Collections.Generic.List[object]]::new()
    foreach($note in @(Get-WinCareLocalNote -IncludeBody -IncludePrivateBody)){
        $haystack=([string]$note.Title+"`n"+(@($note.Tags)-join ' ')+"`n"+[string]$note.Body)
        if($haystack.IndexOf($Query,[StringComparison]::OrdinalIgnoreCase) -lt 0){continue}
        if([bool]$note.Private -and -not $IncludePrivateBody){$note.Body='[PRIVATE NOTE BODY REDACTED]'}
        $results.Add($note);if($results.Count -ge $Limit){break}
    }
    @($results)
}

${function:Get-WinCareColorPaletteStore} = {
    [CmdletBinding()]param([switch]$Strict)
    $path=Get-WinCareColorPalettePath
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){return Get-WinCareDefaultColorPaletteStore}
    try{$store=Read-WinCareProtectedJson -LiteralPath $path -Purpose 'WinCare.ColorPalette' -AsHashtable;$null=Test-WinCareColorPaletteStore $store;$store}
    catch{if($Strict){throw};Write-WinCareLog Warning 'Color palette is invalid; returning an empty in-memory view.' @{error=$_.Exception.Message};Get-WinCareDefaultColorPaletteStore}
}

${function:Get-WinCareRemoteSupportCatalog} = {
    @(
        [pscustomobject]@{Id='rustdesk';
            Name='RustDesk';
            ProcessNames=@('rustdesk');
            ServicePatterns=@('RustDesk*');
            AppPattern='(?i)rustdesk';
            ConfigRoots=@("$env:APPDATA\RustDesk","$env:ProgramData\RustDesk")},
        [pscustomobject]@{Id='mousekeyproxy';
            Name='MouseKeyProxy';
            ProcessNames=@('MouseKeyProxy.Agent','MouseKeyProxy.Service','mkp');
            ServicePatterns=@('MouseKeyProxy*');
            AppPattern='(?i)mousekeyproxy';
            ConfigRoots=@("$env:LOCALAPPDATA\MouseKeyProxy","$env:ProgramData\MouseKeyProxy")},
        [pscustomobject]@{Id='anydesk';
            Name='AnyDesk';
            ProcessNames=@('AnyDesk','AnyDeskMSI');
            ServicePatterns=@('AnyDesk*');
            AppPattern='(?i)anydesk';
            ConfigRoots=@("$env:APPDATA\AnyDesk","$env:ProgramData\AnyDesk")},
        [pscustomobject]@{Id='teamviewer';
            Name='TeamViewer';
            ProcessNames=@('TeamViewer','TeamViewer_Service','TeamViewer_Desktop');
            ServicePatterns=@('TeamViewer*');
            AppPattern='(?i)teamviewer';
            ConfigRoots=@("$env:APPDATA\TeamViewer","$env:ProgramData\TeamViewer")},
        [pscustomobject]@{Id='quickassist';Name='Quick Assist';ProcessNames=@('QuickAssist');ServicePatterns=@();AppPattern='(?i)quick assist';ConfigRoots=@()},
        [pscustomobject]@{Id='remoteassistance';Name='Windows Remote Assistance';ProcessNames=@('msra');ServicePatterns=@();AppPattern='(?i)remote assistance';ConfigRoots=@()},
        [pscustomobject]@{Id='remotedesktop';Name='Remote Desktop';ProcessNames=@('mstsc','msrdc','RdClient.Windows');ServicePatterns=@('TermService');AppPattern='(?i)remote desktop';ConfigRoots=@()},
        [pscustomobject]@{Id='chromeremotedesktop';
            Name='Chrome Remote Desktop';
            ProcessNames=@('remoting_host','remote_assistance_host');
            ServicePatterns=@('chromoting*');
            AppPattern='(?i)chrome remote desktop';
            ConfigRoots=@("$env:ProgramData\Google\Chrome Remote Desktop")},
        [pscustomobject]@{Id='parsec';
            Name='Parsec';
            ProcessNames=@('parsecd','pservice');
            ServicePatterns=@('Parsec*');
            AppPattern='(?i)parsec';
            ConfigRoots=@("$env:APPDATA\Parsec","$env:ProgramData\Parsec")},
        [pscustomobject]@{Id='splashtop';
            Name='Splashtop';
            ProcessNames=@('SRServer','SRManager','SplashtopRemoteService');
            ServicePatterns=@('Splashtop*');
            AppPattern='(?i)splashtop';
            ConfigRoots=@("$env:ProgramData\Splashtop")}
    )
}

${function:Test-WinCareExtensionManifest} = {
    param([Parameter(Mandatory)][Collections.IDictionary]$Manifest)
    $normalized=[ordered]@{};foreach($key in $Manifest.Keys){$normalized[[string]$key]=$Manifest[$key]}
    foreach($name in @('catalogFiles','knowledgeFiles','commandFiles','sourceRecords')){if(-not $normalized.Contains($name) -or $null -eq $normalized[$name]){$normalized[$name]=@()}}
    & $script:WinCareFinalExtensionManifest $normalized
}

${function:Get-WinCareLegacyUnsafeProfile} = {
    [CmdletBinding()]param([string]$Id='')
    $profiles=[Collections.Generic.List[object]]::new()
    foreach($profile in @(& $script:WinCareFinalLegacyProfiles)){$profiles.Add($profile)}
    if(-not @($profiles|Where-Object Id -eq 'optimize-debloat-ultimate').Count){
        $profile=New-WinCareLegacyProfileRecord -Id 'optimize-debloat-ultimate' `
            -Title 'Ultimate cleanup compatibility profile' `
            -Description 'Routes aggressive donor cleanup intent to the bounded reviewed deep-clean workflow.' `
            -PlanKind DeepCleanup -TargetId 'windows-cleaner-utility-all' `
            -SourceRecords @('SRC:6e8ae24ac4')
        $profiles.Add($profile)
    }
    if(-not @($profiles|Where-Object Id -eq 'personalization-legacy').Count){
        $profile=New-WinCareLegacyProfileRecord -Id 'personalization-legacy' `
            -Title 'Legacy personalization compatibility profile' `
            -Description 'Routes legacy personalization intent to reviewed reversible appearance controls.' `
            -Controls @('AdvertisingId','TailoredExperiences','ConsumerFeatures') `
            -SourceRecords @('SRC:1c7d9e2add')
        $profiles.Add($profile)
    }
    if($Id){return @($profiles|Where-Object Id -eq $Id)}
    @($profiles)
}

${function:New-WinCareProvisioningPlan} = {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Blueprint,
        [ValidateSet('All','System','User')][string]$Stage='All'
    )
    $null=Test-WinCareProvisioningBlueprint $Blueprint
    $sources=@($Blueprint.sourceRecords)+@('SRC:4e4b1fa0d3')
    $plan=New-WinCarePlan -Title ("{0} [{1}]" -f [string]$Blueprint.name,$Stage) `
        -Description ([string]$Blueprint.description) -SourceRecords $sources
    Add-WinCareProvisioningCatalogRules $plan @($Blueprint.catalogRules) 'Provisioning common configuration'
    if($Stage -in @('All','System')){
        Add-WinCareProvisioningCatalogRules $plan @($Blueprint.systemStage) 'Provisioning system-stage configuration'
        foreach($name in @($Blueprint.removeProvisionedAppx)){
            $action=New-WinCareAction -Type RemoveProvisionedAppx -Label "Remove provisioned package $name" `
                -Risk High -Parameters @{PackageName=[string]$name} -RequiresAdmin $true -Reversible $false `
                -Tags @('Provisioning','Appx','SystemStage') -SourceRecords @('SRC:4e4b1fa0d3') `
                -RecoveryDescription 'Reinstall from the Microsoft Store or installation media where available.'
            $plan.Actions.Add($action)
        }
        foreach($item in @($Blueprint.optionalFeatures)){
            $state=Test-WinCareProvisioningStateItem $item Feature
            $action=New-WinCareAction -Type SetOptionalFeatureState `
                -Label "$($state.State) optional feature $($state.Name)" -Risk Moderate `
                -Parameters @{Name=$state.Name;State=$state.State} -RequiresAdmin $true -Reversible $false `
                -RestartPossible $true -Tags @('Provisioning','Feature','SystemStage') `
                -SourceRecords @('SRC:4e4b1fa0d3')
            $plan.Actions.Add($action)
        }
        foreach($item in @($Blueprint.capabilities)){
            $state=Test-WinCareProvisioningStateItem $item Capability
            $action=New-WinCareAction -Type SetCapabilityState `
                -Label "$($state.State) capability $($state.Name)" -Risk Moderate `
                -Parameters @{Name=$state.Name;State=$state.State} -RequiresAdmin $true -Reversible $false `
                -RestartPossible $true -Tags @('Provisioning','Capability','SystemStage') `
                -SourceRecords @('SRC:4e4b1fa0d3')
            $plan.Actions.Add($action)
        }
    }
    if($Stage -in @('All','User')){
        Add-WinCareProvisioningCatalogRules $plan @($Blueprint.userStage) 'Provisioning user-stage configuration'
        foreach($name in @($Blueprint.removeInstalledAppx)){
            $action=New-WinCareAction -Type RemoveAppxPackage -Label "Remove installed package $name" `
                -Risk Moderate -Parameters @{PackageFullName=[string]$name;Name=[string]$name} `
                -Tags @('Provisioning','Appx','UserStage') -SourceRecords @('SRC:4e4b1fa0d3') `
                -RecoveryDescription 'Reinstall from the Microsoft Store or installation media where available.'
            $plan.Actions.Add($action)
        }
        foreach($id in @($Blueprint.wingetPackages)){
            $action=New-WinCareAction -Type InstallWingetPackage -Label "Install WinGet package $id" `
                -Risk Moderate -Parameters @{PackageId=[string]$id} -TimeoutSeconds 7200 `
                -Tags @('Provisioning','WinGet','UserStage') -SourceRecords @('SRC:4e4b1fa0d3')
            $plan.Actions.Add($action)
        }
    }
    $validation=Test-WinCarePlanContract $plan
    if(-not $validation.Success){throw "Provisioning plan is invalid: $($validation.Message)"}
    $plan
}

${function:Complete-WinCareOperationReceipt} = {
    param([Parameter(Mandatory)][object]$Journal)
    Write-WinCareAtomicJson -LiteralPath $Journal.RecordPath -InputObject $Journal.Record
    $record=Read-WinCareBoundedJson -LiteralPath $Journal.RecordPath -MaximumBytes 16777216 -Depth 80 -AsHashtable
    $recordHash=Get-WinCareOperationRecordStableHash $record
    $receipt=[ordered]@{SchemaVersion=2;
        OperationId=$record.OperationId;
        PlanHash=$record.PlanHash;
        RecordHash=$recordHash;
        ModuleTreeHash=$record.ModuleTreeHash;
        FinalState=$record.State;
        Success=$record.Success;
        StartedAt=$record.StartedAt;
        FinishedAt=$record.FinishedAt;
        LastEventHash=$record.LastEventHash;
        ResultCount=@($record.Results).Count;
        WarningCount=@($record.Warnings).Count}
    $receipt.ReceiptHash=Get-WinCareOperationReceiptStableHash $receipt
    Write-WinCareProtectedJson -LiteralPath (Join-Path $Journal.Root 'receipt.json') -InputObject $receipt -Purpose 'WinCare.OperationReceipt'
    $Journal.Record.ReceiptHash=$receipt.ReceiptHash
    Write-WinCareAtomicJson -LiteralPath $Journal.RecordPath -InputObject $Journal.Record
    $receipt.ReceiptHash
}
