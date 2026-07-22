function Format-WinCareBrowserRow {
    param($Item,$Width)
    "$(Limit-WinCareText $Item.Name 18) proc:$($Item.RunningProcesses) win:$($Item.VisibleWindows) profile:$($Item.Profiles) ext:$($Item.Extensions) risk:$($Item.HighRiskExtensions)"
}
function Format-WinCareBrowserExtensionRow {
    param($Item,$Width)
    "$(Limit-WinCareText $Item.Browser 12) $(Limit-WinCareText $Item.Name ([math]::Max(24,$Width-52))) $(Limit-WinCareText $Item.PermissionRisk 10) $(Limit-WinCareText $Item.Version 14)"
}
function Show-WinCareBrowserWorkspaceScreen {
    while($true){
        $summary=@(Get-WinCareBrowserWorkspaceSummary)
        $menu=@(
            [pscustomobject]@{Label='Browser overview';Description='Processes, visible windows, profiles, extensions, and risk counts';Action='Summary'},
            [pscustomobject]@{Label='Extension inventory';Description='Manifest identities, versions, path confinement, and permission risk';Action='Extensions'},
            [pscustomobject]@{Label='High-risk extensions';Description='All-host, proxy, debugger, native-messaging, or broad content access';Action='HighRisk'},
            [pscustomobject]@{Label='Open browser extension settings';Description='Use browser-owned mutation surfaces';Action='Settings'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Browser workspace' -Subtitle 'Local process/window/profile/extension evidence; no debugging-port attachment or injected counting extension' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try {
            switch($choice.Action) {
                'Summary' {
                    $selected=Show-WinCareListSelector -Title 'Browser overview' -Items $summary -Formatter ${function:Format-WinCareBrowserRow} -SearchProperties @('Name','Id')
                    if($selected){
                        Show-WinCareTextPage -Title $selected.Name -Lines @(
                            "Installed: $($selected.Installed)","Processes: $($selected.RunningProcesses)",
                            "Visible windows: $($selected.VisibleWindows)","Profiles: $($selected.Profiles)",
                            "Extensions: $($selected.Extensions)","High-risk extensions: $($selected.HighRiskExtensions)",
                            'Exact tab count is unavailable without a browser debugging interface or extension and is intentionally not inferred.'
                        )
                    }
                }
                'Extensions'{
                    $extensions=@(Get-WinCareBrowserExtensionInventory -Limit ([int](Get-WinCareConfig 'BrowserExtensionInventoryLimit')))
                    $selected=Show-WinCareListSelector -Title 'Browser extensions' -Items $extensions -Formatter ${function:Format-WinCareBrowserExtensionRow} -SearchProperties @('Browser','Name','ExtensionId','PermissionRisk','Permissions','Version')
                    if($selected){
                        Show-WinCareTextPage -Title $selected.Name -Lines @(
                            "Browser: $($selected.Browser)","ID: $($selected.ExtensionId)","Version: $($selected.Version)",
                            "Risk: $($selected.PermissionRisk)","Profile: $($selected.Profile)","Path: $($selected.Path)",
                            "Manifest SHA-256: $($selected.ManifestSha256)","Path confined: $($selected.PathConfined)","Unpacked: $($selected.Unpacked)",
                            "Permissions: $(@($selected.Permissions)-join ', ')"
                        )
                    }
                }
                'HighRisk'{
                    $extensions=@(Get-WinCareBrowserExtensionInventory -Limit ([int](Get-WinCareConfig 'BrowserExtensionInventoryLimit'))|Where-Object PermissionRisk -in @('High','Critical'))
                    $null=Show-WinCareListSelector -Title 'High-risk browser extensions' -Items $extensions -Formatter ${function:Format-WinCareBrowserExtensionRow} -SearchProperties @('Browser','Name','ExtensionId','Permissions')
                }
                'Settings'{
                    $browser=Show-WinCareMenu -Title 'Browser settings' -Items @(
                        [pscustomobject]@{Label='Microsoft Edge extensions';Description='edge://extensions';FilePath='microsoft-edge:';Arguments='edge://extensions'},
                        [pscustomobject]@{Label='Chrome extensions';Description='chrome://extensions';FilePath='chrome.exe';Arguments='chrome://extensions'},
                        [pscustomobject]@{Label='Firefox add-ons';Description='about:addons';FilePath='firefox.exe';Arguments='about:addons'}
                    )
                    if($browser){Start-Process -FilePath $browser.FilePath -ArgumentList @($browser.Arguments)}
                }
            }
        } catch {Show-WinCareMessage -Title 'Browser workspace failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

