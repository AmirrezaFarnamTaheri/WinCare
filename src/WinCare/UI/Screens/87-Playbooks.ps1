function Format-WinCarePlaybookRow {param($Item,$Width);"$(Limit-WinCareText $Item.title ([math]::Max(24,$Width-38))) $($Item.risk) $(@($Item.steps).Count) step(s)"}
function Show-WinCarePlaybooksScreen {
    while($true){
        $playbooks=@(Get-WinCarePlaybook)
        $menu=@(
            [pscustomobject]@{Label='Playbook catalog';Description="$($playbooks.Count) strict declarative playbook(s)";Action='List'},
            [pscustomobject]@{Label='Preview or apply playbook';Description='Composes only existing WinCare plans; no scripts, macros, or arbitrary commands';Action='Apply'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Declarative playbooks' -Subtitle 'Preset + catalog + app profile + context-menu + exact WinGet identities, deduplicated through one action engine' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        $selected=Show-WinCareListSelector -Title 'Playbooks' -Items $playbooks -Formatter ${function:Format-WinCarePlaybookRow} -SearchProperties @('id','title','description','risk')
        if(-not $selected){continue}
        if($choice.Action -eq 'List'){$compat=Test-WinCarePlaybookCompatibility $selected;Show-WinCareTextPage -Title $selected.title -Subtitle "risk=$($selected.risk), compatible=$($compat.Compatible)" -Lines @($selected.description,"ID: $($selected.id)","Reason: $($compat.Reason)",'','Steps:')+@($selected.steps|ForEach-Object{"$($_.type): $($_.id)"})+@('','Evidence:')+@($selected.sourceRecords)}
        else{try{Invoke-WinCarePlanFromUi (New-WinCarePlaybookPlan -PlaybookId $selected.id)}catch{Show-WinCareMessage -Title 'Playbook blocked' -Lines @($_.Exception.Message) -Kind Error}}
    }
}

