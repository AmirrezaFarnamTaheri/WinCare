function Format-WinCareWuaRow { param($Item,$Width); "$(Limit-WinCareText $Item.Title ([math]::Max(30,$Width-34))) $(Limit-WinCareText (($Item.KB -join ',')) 14) $(Limit-WinCareText (Format-WinCareBytes $Item.MaxDownloadSize) 12) $(if($Item.IsDownloaded){'DL'}else{'--'})" }
function Show-WinCareWindowsUpdateScreen {
    while($true){
        $menu=@(
            [pscustomobject]@{Label='Search available updates';Description='Windows Update Agent search, not screen scraping';Action='Search'},
            [pscustomobject]@{Label='Review hidden updates';Description='Unhide selected updates';Action='Hidden'},
            [pscustomobject]@{Label='Update history';Description='Result codes, IDs, and timestamps';Action='History'},
            [pscustomobject]@{Label='Open native Windows Update';Description='Windows Settings';Action='Settings'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Windows Update Agent' -Subtitle 'Search, download, install, hide, unhide, uninstall, history, and reboot evidence' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'Search'{
                Clear-WinCareScreen;Write-WinCareHeader -Title 'Windows Update search';Write-WinCareText ' Searching Windows Update Agent...' Accent
                try{$updates=@(Get-WinCareWuaUpdate)}catch{Show-WinCareMessage -Title 'Windows Update search failed' -Lines @($_.Exception.Message) -Kind Error;continue}
                $selected=@(Show-WinCareListSelector -Title 'Available Windows updates' -Subtitle 'Space selects multiple updates' -Items $updates -Formatter ${function:Format-WinCareWuaRow} -SearchProperties @('Title','KB','Categories','MsrcSeverity') -MultiSelect -EmptyMessage 'No available updates were found.')
                if($selected.Count){$action=Show-WinCareMenu -Title 'Selected update action' -Items @([pscustomobject]@{Label='Download';Description='Download without installing';Action='Download'},[pscustomobject]@{Label='Install';Description='Download if needed, then install';Action='Install'},[pscustomobject]@{Label='Hide';Description='Hide from normal search results';Action='Hide'},[pscustomobject]@{Label='Cancel';Description='Return without changes';Action='Cancel'});switch($action.Action){'Download'{Invoke-WinCarePlanFromUi (New-WinCareWuaDownloadPlan -UpdateId @($selected.Id))}'Install'{Invoke-WinCarePlanFromUi (New-WinCareWuaInstallPlan -UpdateId @($selected.Id))}'Hide'{Invoke-WinCarePlanFromUi (New-WinCareWuaHiddenPlan -UpdateId @($selected.Id) -Hidden $true)}}}
            }
            'Hidden'{$updates=@(Get-WinCareWuaUpdate -Criteria 'IsInstalled=0 and IsHidden=1');$selected=@(Show-WinCareListSelector -Title 'Hidden updates' -Items $updates -Formatter ${function:Format-WinCareWuaRow} -SearchProperties @('Title','KB') -MultiSelect);if($selected.Count){Invoke-WinCarePlanFromUi (New-WinCareWuaHiddenPlan -UpdateId @($selected.Id) -Hidden $false)}}
            'History'{$lines=@(Get-WinCareWuaHistory -Count 250|ForEach-Object{"$($_.Date.ToString('u')) | $($_.ResultCode) | $($_.Operation) | $($_.Title) | $($_.HResult)"});Show-WinCareTextPage -Title 'Windows Update history' -Lines $(if($lines.Count){$lines}else{@('No update history entries were returned.')})}
            'Settings'{$null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('ms-settings:windowsupdate')}
        }
    }
}

