function Format-WinCareLaunchTargetRow {param($Item,$Width);"$(Limit-WinCareText $Item.Kind 14) $(Limit-WinCareText $Item.Name ([math]::Max(24,$Width-48))) $(Limit-WinCareText $Item.Source 24)"}
function Show-WinCareLauncherScreen {
    while($true){
        $menu=@(
            [pscustomobject]@{Label='Search launch targets';Description='Applications, shortcuts, Control Panel, and safe Settings URIs';Action='Search'},
            [pscustomobject]@{Label='Safe calculator';Description='Arithmetic only; no expression evaluation or shell execution';Action='Calculate'},
            [pscustomobject]@{Label='Browse all targets';Description='Bounded local launcher index';Action='All'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Unified launcher intelligence' -Subtitle 'Weighted local discovery and exact target identity; arbitrary command strings are not accepted' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{switch($choice.Action){
            'Search'{$query=Read-WinCareInput -Prompt 'Application, setting, or control';$items=@(Get-WinCareLaunchTarget -Query $query -Limit ([int](Get-WinCareConfig 'CommandPaletteMaxResults')));$selected=Show-WinCareListSelector -Title 'Launch targets' -Items $items -Formatter ${function:Format-WinCareLaunchTargetRow} -SearchProperties @('Name','Kind','Source','Keywords');if($selected){Invoke-WinCarePlanFromUi (New-WinCareOpenLaunchTargetPlan -Id $selected.Id)}}
            'All'{$selected=Show-WinCareListSelector -Title 'All launch targets' -Items @(Get-WinCareLaunchTarget -Limit ([int](Get-WinCareConfig 'LauncherIndexLimit'))) -Formatter ${function:Format-WinCareLaunchTargetRow} -SearchProperties @('Name','Kind','Source','Keywords');if($selected){Invoke-WinCarePlanFromUi (New-WinCareOpenLaunchTargetPlan -Id $selected.Id)}}
            'Calculate'{$expression=Read-WinCareInput -Prompt 'Arithmetic expression';if($expression){$value=Invoke-WinCareCalculator -Expression $expression;Show-WinCareMessage -Title 'Calculator result' -Lines @("$expression = $value")}}
        }}catch{Show-WinCareMessage -Title 'Launcher operation failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

