function Show-WinCareQuickActionsScreen {
    while($true){
        $targets=@(Get-WinCareCleanupTarget|Where-Object{$_.Exists -and $_.FileCount -gt 0 -and $_.Risk -eq 'Low'})
        $menu=@(
            [pscustomobject]@{Label='Preview low-risk cleanup';Description="Review $($targets.Count) low-risk category/categories";Action='Cleanup'},
            [pscustomobject]@{Label='Apply Safe usability preset';Description='File extensions, This PC, and conservative menu timing';Action='SafePreset'},
            [pscustomobject]@{Label='Review high-impact startup entries';Description='Open the existing reversible startup manager';Action='Startup'},
            [pscustomobject]@{Label='Search Windows updates';Description='Use the Windows Update Agent API';Action='Wua'},
            [pscustomobject]@{Label='Run Windows health assessment';Description='Read-only health findings';Action='Health'},
            [pscustomobject]@{Label='Create system report';Description='Export a redacted report';Action='Report'},
            [pscustomobject]@{Label='Run safe maintenance playbook';Description='Compose conservative cleanup, policy, and usability actions';Action='Playbook'},
            [pscustomobject]@{Label='Review maintenance windows';Description='Open lifecycle, notice, metrics, and local export';Action='Maintenance'},
            [pscustomobject]@{Label='Open command palette';Description='Search every WinCare workspace';Action='Palette'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Quick actions' -Subtitle 'Common workflows using the same plan, policy, and journal engine' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'Cleanup'{if($targets.Count){Invoke-WinCarePlanFromUi (New-WinCareCleanupPlan -Targets $targets)}else{Show-WinCareMessage -Title 'Cleanup' -Lines @('No low-risk cleanup files are currently eligible.') -Kind Success}}
            'SafePreset'{Invoke-WinCarePlanFromUi (New-WinCarePresetPlan -PresetId safe)}
            'Startup'{Show-WinCareStartupScreen}
            'Wua'{Show-WinCareWindowsUpdateScreen}
            'Health'{Show-WinCareHealthFindings}
            'Report'{Show-WinCareReportsScreen}
            'Playbook'{try{Invoke-WinCarePlanFromUi (New-WinCarePlaybookPlan -PlaybookId 'safe-maintenance-baseline')}catch{Show-WinCareMessage -Title 'Playbook unavailable' -Lines @($_.Exception.Message) -Kind Error}}
            'Maintenance'{Show-WinCareMaintenanceScreen}
            'Palette'{Show-WinCareCommandPalette}
        }
    }
}

