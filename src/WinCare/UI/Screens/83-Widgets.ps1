function Format-WinCareWidgetRow {param($Item,$Width);"$(Limit-WinCareText $Item.Title ([math]::Max(22,$Width-38))) $(Limit-WinCareText $Item.Status 16) $(Limit-WinCareText $Item.Summary 36)"}
function Show-WinCareWidgetsScreen {
    while($true){
        $snapshots=@(Get-WinCareWidgetSnapshot)
        $menu=@(
            [pscustomobject]@{Label='Dashboard snapshot';Description="$($snapshots.Count) configured widget(s), refresh=$(Get-WinCareConfig WidgetRefreshSeconds)s";Action='Snapshot'},
            [pscustomobject]@{Label='All available widgets';Description="$(@(Get-WinCareWidgetDefinition).Count) built-in provider(s)";Action='Catalog'},
            [pscustomobject]@{Label='Export local snapshot';Description='Exact-content JSON under the WinCare Widgets data root';Action='Export'},
            [pscustomobject]@{Label='Open widget data folder';Description=(Join-Path $script:WinCareState.Root 'Widgets');Action='Folder'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Widget dashboard' -Subtitle 'Local, capability-aware cards with bounded refresh and no telemetry or arbitrary skin execution' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'Snapshot'{$selected=Show-WinCareListSelector -Title 'Widget dashboard' -Subtitle 'Select a card for complete data' -Items $snapshots -Formatter ${function:Format-WinCareWidgetRow} -SearchProperties @('Title','Status','Summary','Id');if($selected){Show-WinCareTextPage -Title $selected.Title -Subtitle "$($selected.Status) at $($selected.CapturedAt)" -Lines @($selected.Summary,'',($selected.Data|ConvertTo-Json -Depth 20))+@($selected.Warnings)}}
            'Catalog'{$lines=@(Get-WinCareWidgetDefinition|ForEach-Object{"$($_.id) | $($_.provider) | min refresh $($_.minimumRefreshSeconds)s | $($_.title) | $($_.description)"});Show-WinCareTextPage -Title 'Widget catalog' -Lines $lines}
            'Export'{$default=Join-Path (Join-Path $script:WinCareState.Root 'Widgets') ("snapshot-$([datetime]::UtcNow.ToString('yyyyMMdd-HHmmss')).json");$path=Read-WinCareInput -Prompt 'Snapshot destination' -Default $default;if($path){try{Invoke-WinCarePlanFromUi (New-WinCareWidgetSnapshotExportPlan -Path $path)}catch{Show-WinCareMessage -Title 'Export blocked' -Lines @($_.Exception.Message) -Kind Error}}}
            'Folder'{Start-Process (Join-Path $script:WinCareState.Root 'Widgets')}
        }
    }
}

