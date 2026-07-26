function Format-WinCareShellRow {param($Item,$Width);"$(Limit-WinCareText $Item.Name ([math]::Max(24,$Width-34))) $(Limit-WinCareText $Item.Scope 9) $(Limit-WinCareText $(if($Item.Enabled){'Enabled'}else{'Disabled'}) 10) $(Limit-WinCareText $Item.Value 12)"}
function Show-WinCareShellScreen {
    while($true){$menu=@(
        [pscustomobject]@{Label='Context-menu and shell entries';Description='Inspect and reversibly disable registry-registered handlers';Action='Extensions'},
        [pscustomobject]@{Label='Desktop shortcuts';Description='User and Public Desktop inventory';Action='Shortcuts'},
        [pscustomobject]@{Label='Open File Explorer options';Description='Native settings surface';Action='Options'},
        [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
    );$choice=Show-WinCareMenu -Title 'Shell and desktop' -Subtitle 'Dynamic shell inventory with system-theme-friendly terminal presentation' -Items $menu;if(-not $choice -or $choice.Action -eq 'Back'){return};switch($choice.Action){
        'Extensions'{$item=Show-WinCareListSelector -Title 'Shell entries' -Items @(Get-WinCareShellExtension) -Formatter ${function:Format-WinCareShellRow} -SearchProperties @('Name','RegistryPath','Value','Scope');if($item){Invoke-WinCarePlanFromUi (New-WinCareShellExtensionPlan -Extension $item -Enabled (-not $item.Enabled))}}
        'Shortcuts'{$lines=@(Get-WinCareDesktopShortcut|ForEach-Object{"$($_.Scope) | $($_.Name) | $($_.Path)"});Show-WinCareTextPage -Title 'Desktop shortcuts' -Lines $(if($lines.Count){$lines}else{@('No shortcuts were found.')})}
        'Options'{$null=Start-WinCareDetachedProcess -FilePath 'control.exe' -ArgumentList @('folders')}
    }}
}

