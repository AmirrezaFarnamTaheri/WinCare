$script:WinCareGuiActionCatalogCache = $null
$script:WinCareGuiNavigationCache = $null

function Get-WinCareGuiDataRoot {
    [CmdletBinding()]
    param()
    Join-Path $script:WinCareModuleRoot 'Data/Gui'
}

function Get-WinCareGuiNavigation {
    [CmdletBinding()]
    param([switch]$Refresh)
    if($Refresh -or $null -eq $script:WinCareGuiNavigationCache){
        $path=Join-Path (Get-WinCareGuiDataRoot) 'navigation.json'
        $script:WinCareGuiNavigationCache=@(Read-WinCareBoundedJson -LiteralPath $path -MaximumBytes 1048576 -Depth 20)
    }
    @($script:WinCareGuiNavigationCache)
}

function Get-WinCareGuiGeneratedCategory {
    param([Parameter(Mandatory)][string]$Command)
    switch -Regex($Command){
        '^(security|wdac|sysmon|group-policy|hardening|injection|credential|appcontainer)'{'Security';break}
        '^(health|hardware|storage|cleanup|pagefile|toolkit|boot|bcd|internals)'{'Health';break}
        '^(wua|updates|maintenance|automation|reports|cancel|deep-clean|appx|archive|servicing|offline)'{'Maintenance';break}
        '^(network|tcp|wifi|peer-wlan)'{'Network';break}
        '^(window|monitor|monitors|workspace|studio|launcher|color|note|browser|remote|power|steam|game|downloads|terminal|context-menu|customization|rainmeter|windhawk|explorer|fullscreen-shell)'{'Workspace';break}
        '^(experience-cards|shell-hardware-cards|gui-resource-audit|ui-automation|knowledge|widget)'{'Evidence';break}
        '^(presets|preset|catalog|playbook|legacy-unsafe)'{'Profiles';break}
        default{'Advanced'}
    }
}

function Get-WinCareGuiGeneratedIcon {
    param([Parameter(Mandatory)][string]$Category)
    switch($Category){
        'Security' { '\ue72e' }
        'Health' { '\ue95e' }
        'Maintenance' { '\ue90f' }
        'Network' { '\ue968' }
        'Workspace' { '\ue737' }
        'Evidence' { '\ue9d2' }
        'Profiles' { '\ue713' }
        default { '\ue8cb' }
    }
}

function Get-WinCareGuiActionCatalog {
    [CmdletBinding()]
    param([switch]$Refresh)
    if($Refresh -or $null -eq $script:WinCareGuiActionCatalogCache){
        $path=Join-Path (Get-WinCareGuiDataRoot) 'actions.json'
        $curated=@(Read-WinCareBoundedJson -LiteralPath $path -MaximumBytes 1048576 -Depth 40)
        $commands=@(Get-WinCareHeadlessCommandName)
        $readOnly=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($name in @(Get-WinCareBrokerReadOnlyCommandName)){$null=$readOnly.Add([string]$name)}
        $critical=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($name in @('preset','playbook','pagefile-set','security-control-reduce','injection-surface-quarantine','wua-uninstall','wdac-deploy','legacy-unsafe','hardening-apply','peer-display-reset','studio-xbox-fse','offline-reduction-apply','appx-selection','offline-appx-selection','deep-clean','experience-privacy-apply','run-automation','group-policy-import','sysmon-uninstall','offline-package-add')){$null=$critical.Add($name)}
        $low=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($name in @('monitor-control-set','explorer-session-save','explorer-session-restore','appx-launch','hardware-report','terminal-export')){$null=$low.Add($name)}
        $known=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $items=[Collections.Generic.List[object]]::new()
        foreach($entry in $curated){
            if([string]$entry.command -notin $commands){throw "GUI action '$($entry.id)' references unknown command '$($entry.command)'."}
            if(-not $known.Add([string]$entry.command+"|"+[string]$entry.id)){throw "Duplicate GUI action identity: $($entry.id)"}
            $isReadOnly=$readOnly.Contains([string]$entry.command)
            $risk=if($entry.PSObject.Properties['risk']){[string]$entry.risk}elseif($isReadOnly){'ReadOnly'}else{'Moderate'}
            $isCritical=if($entry.PSObject.Properties['critical']){[bool]$entry.critical}else{$risk -eq 'Critical'}
            $arguments=if($entry.PSObject.Properties['defaultArguments']){$entry.defaultArguments}else{[pscustomobject]@{}}
            $items.Add([pscustomobject]@{
                Id=[string]$entry.id;Title=[string]$entry.title;Description=[string]$entry.description
                Category=[string]$entry.category;Icon=[string]$entry.icon;Command=[string]$entry.command
                Risk=$risk;IsReadOnly=$isReadOnly;CanApply=(-not $isReadOnly);Critical=$isCritical
                Featured=if($entry.PSObject.Properties['featured']){[bool]$entry.featured}else{$false}
                DefaultArgumentsJson=($arguments|ConvertTo-Json -Depth 30)
                ArgumentHelp=[string]$entry.argumentHelp;Keywords=if($entry.PSObject.Properties['keywords']){@($entry.keywords)}else{@()}
                SourceRecords=if($entry.PSObject.Properties['sourceRecords']){@($entry.sourceRecords)}else{@()}
                CatalogKind='Curated'
            })
        }
        $curatedCommands=@($curated|ForEach-Object{[string]$_.command})
        $textInfo=[Globalization.CultureInfo]::InvariantCulture.TextInfo
        foreach($command in $commands){
            if($command -in $curatedCommands){continue}
            $category=Get-WinCareGuiGeneratedCategory -Command $command
            $isReadOnly=$readOnly.Contains($command)
            $isCritical=$critical.Contains($command)
            $risk=if($isReadOnly){'ReadOnly'}elseif($isCritical){'Critical'}elseif($low.Contains($command)){'Low'}else{'High'}
            $title=$textInfo.ToTitleCase(($command -replace '-',' '))
            $items.Add([pscustomobject]@{
                Id="command-$command";Title=$title;Description='Advanced headless capability. Review the command contract and supply explicit JSON arguments before execution.'
                Category=$category;Icon=Get-WinCareGuiGeneratedIcon -Category $category;Command=$command
                Risk=$risk;IsReadOnly=$isReadOnly;CanApply=(-not $isReadOnly);Critical=$isCritical;Featured=$false
                DefaultArgumentsJson='{}';ArgumentHelp='Use the documented headless command arguments. Invalid or missing arguments fail closed.'
                Keywords=@('advanced','headless',$command);SourceRecords=@();CatalogKind='Generated'
            })
        }
        $script:WinCareGuiActionCatalogCache=@($items)
    }
    @($script:WinCareGuiActionCatalogCache)
}

function Test-WinCareGuiCatalog {
    [CmdletBinding()]
    param()
    $errors=[Collections.Generic.List[string]]::new()
    $commands=@(Get-WinCareHeadlessCommandName)
    $items=@(Get-WinCareGuiActionCatalog -Refresh)
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($item in $items){
        if(-not $ids.Add([string]$item.Id)){$errors.Add("Duplicate GUI action ID: $($item.Id)")}
        if([string]$item.Command -notin $commands){$errors.Add("Unknown GUI command: $($item.Command)")}
        if([string]$item.Risk -notin @('ReadOnly','Low','Moderate','High','Critical')){$errors.Add("Invalid GUI risk: $($item.Risk)")}
        try{$parsed=$item.DefaultArgumentsJson|ConvertFrom-Json -AsHashtable -Depth 40;if($parsed -isnot [Collections.IDictionary]){$errors.Add("Arguments for $($item.Id) are not a JSON object.")}}catch{$errors.Add("Invalid arguments JSON for $($item.Id): $($_.Exception.Message)")}
        if($item.Critical -and $item.Risk -ne 'Critical'){$errors.Add("Critical GUI action is not labeled Critical: $($item.Id)")}
    }
    foreach($command in $commands){if($command -notin @($items.Command)){$errors.Add("Headless command missing from GUI catalog: $command")}}
    [pscustomobject]@{Success=($errors.Count-eq 0);Actions=$items.Count;Commands=$commands.Count;Errors=@($errors)}
}
