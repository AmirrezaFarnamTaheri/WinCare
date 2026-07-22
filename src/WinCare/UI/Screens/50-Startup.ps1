function Format-WinCareStartupRow {
    param([object]$Item,[int]$Width)
    $nameWidth=[math]::Max(20,[math]::Floor($Width*.34)); $sourceWidth=15; $scopeWidth=10; $commandWidth=$Width-$nameWidth-$sourceWidth-$scopeWidth-3
    "$(Limit-WinCareText $Item.Name $nameWidth) $(Limit-WinCareText $Item.Source $sourceWidth) $(Limit-WinCareText $Item.Scope $scopeWidth) $(Limit-WinCareText $Item.Command $commandWidth)"
}

function Format-WinCareDisabledStartupRow {
    param([object]$Item,[int]$Width)
    $nameWidth=[math]::Max(24,[math]::Floor($Width*.45)); $sourceWidth=18; $rest=$Width-$nameWidth-$sourceWidth-2
    "$(Limit-WinCareText $Item.Name $nameWidth) $(Limit-WinCareText $Item.Source $sourceWidth) $(Limit-WinCareText ([string]$Item.DisabledAt) $rest)"
}

function Show-WinCareStartupScreen {
    while ($true) {
        $active=@(Get-WinCareStartupItem)
        $disabled=@(Get-WinCareDisabledStartupItem)
        $menu=@(
            [pscustomobject]@{ Label='Review active startup items'; Description="$($active.Count) active item(s)"; Action='Active' },
            [pscustomobject]@{ Label='Disable startup items'; Description='Back up each entry before disabling'; Action='Disable' },
            [pscustomobject]@{ Label='Restore disabled items'; Description="$($disabled.Count) WinCare backup(s) available"; Action='Restore'; Disabled=($disabled.Count -eq 0) },
            [pscustomobject]@{ Label='Open Windows Startup Apps'; Description='Open the native Settings page'; Action='Settings' },
            [pscustomobject]@{ Label='Back'; Description='Return to main menu'; Action='Back' }
        )
        $choice=Show-WinCareMenu -Title 'Startup and background activity' -Subtitle 'Disable narrowly; preserve an exact restore record' -Items $menu
        if (-not $choice -or $choice.Action -eq 'Back') { return }
        switch ($choice.Action) {
            'Active' {
                $item=Show-WinCareListSelector -Title 'Active startup items' -Subtitle "$($active.Count) item(s)" -Items $active -Formatter ${function:Format-WinCareStartupRow} -SearchProperties @('Name','Command','Scope','Source')
                if ($item) { Show-WinCareTextPage -Title 'Startup item' -Subtitle $item.Name -Lines @("Name: $($item.Name)","Scope: $($item.Scope)","Source: $($item.Source)","Location: $($item.Location)",'',"Command: $($item.Command)") }
            }
            'Disable' {
                $items=@(Show-WinCareListSelector -Title 'Disable startup items' -Subtitle 'Select only entries whose effect you understand' -Items $active -Formatter ${function:Format-WinCareStartupRow} -SearchProperties @('Name','Command','Scope','Source') -MultiSelect)
                if ($items.Count) { Invoke-WinCarePlanFromUi (New-WinCareDisableStartupPlan $items) }
            }
            'Restore' {
                $items=@(Show-WinCareListSelector -Title 'Restore startup items' -Items $disabled -Formatter ${function:Format-WinCareDisabledStartupRow} -SearchProperties @('Name','Source','Scope') -MultiSelect)
                if ($items.Count) { Invoke-WinCarePlanFromUi (New-WinCareEnableStartupPlan $items) }
            }
            'Settings' { Start-Process 'ms-settings:startupapps' }
        }
    }
}

