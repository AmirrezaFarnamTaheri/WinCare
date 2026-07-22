function Format-WinCareApplicationRow {
    param([object]$App,[int]$Width)
    $nameWidth = [math]::Max(20,[math]::Floor($Width * .42))
    $publisherWidth = [math]::Max(14,[math]::Floor($Width * .25))
    $remaining = $Width - $nameWidth - $publisherWidth - 3
    "$(Limit-WinCareText $App.Name $nameWidth) $(Limit-WinCareText $App.Publisher $publisherWidth) $(Limit-WinCareText (Format-WinCareBytes $App.EstimatedBytes) $remaining)"
}

function Select-WinCareApplication {
    param([switch]$MultiSelect,[switch]$RepairableOnly,[switch]$UninstallableOnly)
    $apps = @(Get-WinCareInstalledApplication)
    if ($RepairableOnly) { $apps = @($apps | Where-Object RepairSupported) }
    if ($UninstallableOnly) { $apps = @($apps | Where-Object UninstallSupported) }
    Show-WinCareListSelector -Title 'Applications' -Subtitle "$($apps.Count) application(s)" -Items $apps -Formatter ${function:Format-WinCareApplicationRow} -SearchProperties @('Name','Publisher','Version') -MultiSelect:$MultiSelect
}

function Show-WinCareApplicationDetails {
    param([Parameter(Mandatory)][object]$Application)
    while ($true) {
        Clear-WinCareScreen
        Write-WinCareHeader -Title 'Application details' -Subtitle $Application.Name
        $rows = @(
            [pscustomobject]@{ Label='Name'; Value=$Application.Name },
            [pscustomobject]@{ Label='Version'; Value=$Application.Version },
            [pscustomobject]@{ Label='Publisher'; Value=$Application.Publisher },
            [pscustomobject]@{ Label='Size'; Value=$(if ($Application.EstimatedBytes) { Format-WinCareBytes $Application.EstimatedBytes } else { 'Unknown' }) },
            [pscustomobject]@{ Label='Installer'; Value=$Application.InstallerType },
            [pscustomobject]@{ Label='Scope'; Value=$Application.Scope },
            [pscustomobject]@{ Label='Architecture'; Value=$Application.Architecture },
            [pscustomobject]@{ Label='Source'; Value=$Application.Source },
            [pscustomobject]@{ Label='Location'; Value=$Application.InstallLocation },
            [pscustomobject]@{ Label='Repair supported'; Value=$Application.RepairSupported },
            [pscustomobject]@{ Label='Reset supported'; Value=$Application.ResetSupported },
            [pscustomobject]@{ Label='Uninstall supported'; Value=$Application.UninstallSupported }
        )
        foreach ($row in $rows) { Write-WinCareText " $(Limit-WinCareText $row.Label 20) $($row.Value)" Text }
        Write-WinCareText ''
        Write-WinCareText ' Press U uninstall, R repair, X reset app data, O open folder, Esc back.' Muted
        $key=Read-WinCareKey
        if ($key.Key -eq 'Escape') { return }
        switch ([char]::ToUpperInvariant($key.KeyChar)) {
            'U' { if ($Application.UninstallSupported) { Invoke-WinCarePlanFromUi (New-WinCareUninstallPlan @($Application)); return } }
            'R' { if ($Application.RepairSupported) { Invoke-WinCarePlanFromUi (New-WinCareRepairPlan $Application); return } }
            'X' { if ($Application.ResetSupported) { Invoke-WinCarePlanFromUi (New-WinCareResetAppxPlan $Application); return } }
            'O' {
                if ($Application.InstallLocation -and (Test-Path -LiteralPath $Application.InstallLocation)) { $null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @($Application.InstallLocation) }
            }
        }
    }
}

function Show-WinCareApplicationsScreen {
    while ($true) {
        $menu = @(
            [pscustomobject]@{ Label='Browse installed applications'; Description='Search and inspect normalized Registry, MSI, and AppX records'; Action='Browse' },
            [pscustomobject]@{ Label='Bulk uninstall'; Description='Select multiple applications and preview the complete plan'; Action='Uninstall' },
            [pscustomobject]@{ Label='Repair an application'; Description='Use MSI or vendor repair when supported'; Action='Repair' },
            [pscustomobject]@{ Label='Reset a Store application'; Description='Erase local app data and restore initial state; high risk'; Action='Reset'; Disabled=-not (Test-WinCareCapability 'Reset-AppxPackage') },
            [pscustomobject]@{ Label='Curated optional-app profiles'; Description='Preview exact current-user and provisioned AppX matches'; Action='Profiles' },
            [pscustomobject]@{ Label='Open Windows Installed Apps'; Description='Open the native Settings page'; Action='Settings' },
            [pscustomobject]@{ Label='Refresh inventory'; Description='Discard cached inventory and rescan'; Action='Refresh' },
            [pscustomobject]@{ Label='Back'; Description='Return to main menu'; Action='Back' }
        )
        $choice = Show-WinCareMenu -Title 'Applications' -Subtitle 'Discover, inspect, repair, and uninstall software' -Items $menu
        if (-not $choice -or $choice.Action -eq 'Back') { return }
        switch ($choice.Action) {
            'Browse' { $app=Select-WinCareApplication; if ($app) { Show-WinCareApplicationDetails $app } }
            'Uninstall' { $apps=@(Select-WinCareApplication -MultiSelect -UninstallableOnly); if ($apps.Count) { Invoke-WinCarePlanFromUi (New-WinCareUninstallPlan $apps) } }
            'Repair' { $app=Select-WinCareApplication -RepairableOnly; if ($app) { Invoke-WinCarePlanFromUi (New-WinCareRepairPlan $app) } }
            'Reset' {
                $apps=@(Get-WinCareInstalledApplication | Where-Object ResetSupported)
                $app=Show-WinCareListSelector -Title 'Reset Store application' -Subtitle 'Reset removes application-local data, settings, and sign-in state' -Items $apps -Formatter ${function:Format-WinCareApplicationRow} -SearchProperties @('Name','Publisher','Version')
                if ($app) { Invoke-WinCarePlanFromUi (New-WinCareResetAppxPlan $app) }
            }
            'Profiles' {
                $profiles=@(Get-WinCareAppRemovalProfile)
                $profile=Show-WinCareListSelector -Title 'Optional application profiles' -Subtitle 'No core system packages are included' -Items $profiles -Formatter { param($item,$width) "$(Limit-WinCareText $item.title ([math]::Max(25,$width-12))) $(Limit-WinCareText $item.risk 10)" } -SearchProperties @('title','description','id','risk')
                if($profile){
                    $includeProvisioned=$false
                    if($script:WinCareState.IsAdmin){$answer=Read-WinCareInput -Prompt 'Also remove provisioned copies for future users? (y/N)' -Default 'N';$includeProvisioned=$answer -match '^(?i)y'}
                    $plan=New-WinCareAppRemovalProfilePlan -ProfileId $profile.id -IncludeProvisioned:$includeProvisioned
                    if($plan.Actions.Count){Invoke-WinCarePlanFromUi $plan}else{Show-WinCareMessage -Title 'No matching packages' -Lines @('No installed packages matched this profile.') -Kind Success}
                }
            }
            'Settings' { Start-Process 'ms-settings:appsfeatures' }
            'Refresh' { $null=Get-WinCareInstalledApplication -Refresh; Show-WinCareMessage -Title 'Inventory refreshed' -Lines @('The installed application inventory was rescanned.') -Kind Success }
        }
    }
}

