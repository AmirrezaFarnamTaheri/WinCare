function Format-WinCareCatalogRuleRow { param($Rule,$Width); "$(Limit-WinCareText $Rule.title ([math]::Max(25,$Width-22))) $(Limit-WinCareText $Rule.category 12) $(Limit-WinCareText $Rule.risk 8)" }
function Format-WinCarePresetRow { param($Preset,$Width); "$(Limit-WinCareText $Preset.title ([math]::Max(25,$Width-10))) $(@($Preset.ruleIds).Count) rules" }

function Show-WinCarePolicyProfilesScreen {
    while($true){
        $menu=@(
            [pscustomobject]@{Label='Apply a curated preset';Description='Safe, Privacy, Performance, Gaming, Developer, Security, or Repair';Action='Presets'},
            [pscustomobject]@{Label='Browse individual rules';Description='Compatibility-aware, reversible configuration catalog';Action='Rules'},
            [pscustomobject]@{Label='View effective policy';Description='Machine, organization, user, and built-in precedence';Action='Policy'},
            [pscustomobject]@{Label='Open policy folder';Description=(Get-WinCareDataRoot);Action='Folder'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Profiles and policy' -Subtitle 'No blanket service disabling, registry cleaners, or undocumented optimization hacks' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'Presets'{$preset=Show-WinCareListSelector -Title 'Curated presets' -Items @(Get-WinCarePreset) -Formatter ${function:Format-WinCarePresetRow} -SearchProperties @('title','description','id');if($preset){Invoke-WinCarePlanFromUi (New-WinCarePresetPlan -PresetId $preset.id)}}
            'Rules'{$rules=@(Get-WinCareCatalog|Where-Object{Test-WinCareCatalogRulePolicy $_ -and Test-WinCareRuleCompatibility $_});$selected=@(Show-WinCareListSelector -Title 'Configuration rules' -Subtitle 'Space selects multiple rules' -Items $rules -Formatter ${function:Format-WinCareCatalogRuleRow} -SearchProperties @('title','description','category','id','risk') -MultiSelect);if($selected.Count){Invoke-WinCarePlanFromUi (New-WinCareCatalogPlan -RuleId @($selected.id))}}
            'Policy'{$policy=Get-WinCarePolicy;$lines=@($policy.GetEnumerator()|Sort-Object Key|ForEach-Object{"$($_.Key): $($_.Value -join ', ')"});Show-WinCareTextPage -Title 'Effective policy' -Lines $lines -Subtitle 'Higher-authority policy cannot be weakened by user settings'}
            'Folder'{$null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @((Get-WinCareDataRoot))}
        }
    }
}

