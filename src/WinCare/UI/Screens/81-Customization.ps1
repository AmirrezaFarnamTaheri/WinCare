function Format-WinCareContextRuleRow {param($Item,$Width);"$(Limit-WinCareText $Item.Title ([math]::Max(24,$Width-35))) $($Item.Risk) enabled=$($Item.Enabled) drift=$($Item.Drifted)"}
function Show-WinCareCustomizationScreen {
    while($true){
        $states=try{@(Get-WinCareContextMenuRuleState)}catch{@()};$hosts=try{@(Get-WinCareCustomizationHost)}catch{@()}
        $menu=@(
            [pscustomobject]@{Label='Context-menu rules';Description="$($states.Count) validated rule(s), exact registry-tree rollback";Action='Rules'},
            [pscustomobject]@{Label='Enable a context-menu rule';Description='Compatibility, policy, executable, and exact-tree checks';Action='Enable';Disabled=$states.Count -eq 0},
            [pscustomobject]@{Label='Disable a context-menu rule';Description='Hash-bound removal with exact previous-tree recovery';Action='Disable';Disabled=$states.Count -eq 0},
            [pscustomobject]@{Label='Validate modern context-menu definition';Description='Data-only schema; no arbitrary script or macro execution';Action='Modern'},
            [pscustomobject]@{Label='Customization hosts';Description="$(@($hosts|Where-Object Installed).Count) detected host(s), mode=$(Get-WinCareConfig ExternalCustomizerMode)";Action='Hosts'},
            [pscustomobject]@{Label='Rainmeter skin inventory';Description='Sections, measures, meters, hashes, and relative paths';Action='Rainmeter'},
            [pscustomobject]@{Label='Windhawk mod inventory';Description='Manifest metadata and source hashes; no injection management';Action='Windhawk'},
            [pscustomobject]@{Label='ExplorerPatcher state';Description='Bounded registry snapshot and hash; no binary patching';Action='ExplorerPatcher'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Customization and context-menu governance' -Subtitle 'Declarative rules and read-only host inventory replace blind registry imports and global injection' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'Rules'{$lines=@($states|ForEach-Object{"$($_.Id) | $($_.Risk) | enabled=$($_.Enabled) | drift=$($_.Drifted) | compatible=$($_.Compatible) | $($_.Title) | $($_.CompatibilityReason)"});Show-WinCareTextPage -Title 'Context-menu rules' -Lines $lines}
                {$_ -in @('Enable','Disable')}{$item=Show-WinCareListSelector -Title "$($_) context-menu rule" -Items $states -Formatter ${function:Format-WinCareContextRuleRow} -SearchProperties @('Id','Title','Description');if($item){Invoke-WinCarePlanFromUi (New-WinCareContextMenuPlan -RuleId $item.Id -Enable:($_ -eq 'Enable'))}}
                'Modern'{$path=Read-WinCareInput -Prompt 'Definition JSON path';if($path){$defs=@(Import-WinCareModernContextMenuDefinition -LiteralPath $path);$lines=@($defs|ForEach-Object{"$($_.title) | exe=$($_.exe) | extensions=$(@($_.acceptExts)-join ',')"});Show-WinCareTextPage -Title 'Validated modern context-menu definitions' -Lines $lines}}
                'Hosts'{$lines=@($hosts|ForEach-Object{"$($_.Id) | installed=$($_.Installed) | running=$($_.Running) | writable=$($_.Writable) | mode=$($_.Mode) | paths=$(@($_.Paths)-join '; ')"});Show-WinCareTextPage -Title 'Customization hosts' -Lines $lines}
                'Rainmeter'{$items=@(Get-WinCareRainmeterSkinInventory);$lines=@($items|ForEach-Object{"$($_.RelativePath) | $($_.Size) bytes | measures=$(@($_.Measures)-join ',') | meters=$(@($_.Meters)-join ',') | $($_.Sha256)"});Show-WinCareTextPage -Title 'Rainmeter skins' -Lines $(if($lines.Count){$lines}else{@('No Rainmeter skins were found.')})}
                'Windhawk'{$items=@(Get-WinCareWindhawkModInventory);$lines=@($items|ForEach-Object{"$($_.Id) | $($_.Name) $($_.Version) | $($_.Author) | $($_.Sha256) | $($_.Path)"});Show-WinCareTextPage -Title 'Windhawk mods' -Lines $(if($lines.Count){$lines}else{@('No local Windhawk mod sources were found.')})}
                'ExplorerPatcher'{$state=Get-WinCareExplorerPatcherState;Show-WinCareTextPage -Title 'ExplorerPatcher state' -Lines @("Installed: $($state.Installed)","Registry path: $($state.RegistryPath)","Snapshot hash: $($state.SnapshotHash)",'','WinCare deliberately does not patch Explorer binaries or inject code into shell processes.')}
            }
        }catch{Show-WinCareMessage -Title 'Customization operation failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

