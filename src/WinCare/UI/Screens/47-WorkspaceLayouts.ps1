function Format-WinCareWorkspaceLayoutRow {param($Item,$Width);"$(Limit-WinCareText $Item.Name ([math]::Max(24,$Width-34))) $($Item.Slots.Count) slots $($Item.UpdatedAt)"}
function Show-WinCareWorkspaceLayoutsScreen {
    while($true){
        $layouts=@(Get-WinCareWorkspaceLayout)
        $menu=@(
            [pscustomobject]@{Label='Saved layouts';Description="$($layouts.Count) monitor-relative layout(s)";Action='List'},
            [pscustomobject]@{Label='Create two-column layout';Description='Capture two process/title match rules on the primary monitor';Action='CreateTwo'},
            [pscustomobject]@{Label='Apply layout';Description='Bind exact current windows and apply with per-window rollback';Action='Apply';Disabled=$layouts.Count -eq 0},
            [pscustomobject]@{Label='Remove layout';Description='CAS-protected reversible catalog mutation';Action='Remove';Disabled=$layouts.Count -eq 0},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Multi-window layouts' -Subtitle 'Reusable monitor-relative layouts with exact process/window identity and rollback' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{switch($choice.Action){
            'List'{$selected=Show-WinCareListSelector -Title 'Saved layouts' -Items $layouts -Formatter ${function:Format-WinCareWorkspaceLayoutRow} -SearchProperties @('Name','Description','Id');if($selected){Show-WinCareTextPage -Title $selected.Name -Lines @("ID: $($selected.Id)","Description: $($selected.Description)",'Slots:')+@($selected.Slots|ForEach-Object{"  $($_.ProcessName) / $($_.TitlePattern) -> [$($_.LeftRatio),$($_.TopRatio),$($_.WidthRatio),$($_.HeightRatio)]"})}}
            'CreateTwo'{$name=Read-WinCareInput -Prompt 'Layout name' -Default 'Two columns';$left=Read-WinCareInput -Prompt 'Left process name (without .exe)';$right=Read-WinCareInput -Prompt 'Right process name (without .exe)';$slots=@(@{ProcessName=$left;TitlePattern='';LeftRatio=0;TopRatio=0;WidthRatio=0.5;HeightRatio=1;Required=$true},@{ProcessName=$right;TitlePattern='';LeftRatio=0.5;TopRatio=0;WidthRatio=0.5;HeightRatio=1;Required=$true});$record=New-WinCareWorkspaceLayoutRecord -Name $name -Description 'Operator-created two-column layout.' -Slot $slots;Invoke-WinCarePlanFromUi (New-WinCareWorkspaceLayoutSavePlan -Layout $record)}
            'Apply'{$selected=Show-WinCareListSelector -Title 'Apply layout' -Items $layouts -Formatter ${function:Format-WinCareWorkspaceLayoutRow};if($selected){Invoke-WinCarePlanFromUi (New-WinCareWorkspaceLayoutApplyPlan -Id $selected.Id)}}
            'Remove'{$selected=Show-WinCareListSelector -Title 'Remove layout' -Items $layouts -Formatter ${function:Format-WinCareWorkspaceLayoutRow};if($selected){Invoke-WinCarePlanFromUi (New-WinCareWorkspaceLayoutRemovePlan -Id $selected.Id)}}
        }}catch{Show-WinCareMessage -Title 'Workspace-layout operation failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

