function Show-WinCareCleanerPreviewStudioScreen {
    while($true){
        $items=@(
            [pscustomobject]@{Label='Capability cards';Description='Search the cleaner and preview capability catalog';Action='Cards'},
            [pscustomobject]@{Label='Disk-pressure assessment';Description='Measure free space and select a bounded cleanup profile';Action='Pressure'},
            [pscustomobject]@{Label='Disk-pressure cleanup';Description='Preview or apply canonical cleanup targets only';Action='PressureApply'},
            [pscustomobject]@{Label='Schedule disk-pressure cleanup';Description='Create a validated conservative or balanced automation task';Action='Schedule'},
            [pscustomobject]@{Label='Winapp2 assessment';Description='Admit FileKey rules into an exact file manifest';Action='Winapp2'},
            [pscustomobject]@{Label='Winapp2 cleanup';Description='Preview or delete only the unchanged admitted files';Action='Winapp2Apply'},
            [pscustomobject]@{Label='Relocation profiles';Description='Review supported cache and application-data moves';Action='RelocationProfiles'},
            [pscustomobject]@{Label='Relocation assessment';Description='Check processes, paths, size, and content hash';Action='RelocationAssess'},
            [pscustomobject]@{Label='Relocate application data';Description='Copy, hash-verify, junction, and journal recovery';Action='RelocationApply'},
            [pscustomobject]@{Label='Local file preview';Description='Bounded, redacted, non-executing inspection';Action='Preview'},
            [pscustomobject]@{Label='Registered preview handlers';Description='Assess COM handlers without loading them';Action='Handlers'},
            [pscustomobject]@{Label='Back';Description='Return to the main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Cleaner and File Preview Studio' -Subtitle 'Exact manifests, local-only preview, no process killing, no active-content execution' -Items $items
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'Cards'{$q=Read-WinCareInput -Prompt 'Optional search text' -Default '';Show-WinCareTextPage -Title 'Cleaner and preview capabilities' -Lines @(Get-WinCareCleanerPreviewCapabilityCard -Query $q|ForEach-Object{"$($_.Title) [$($_.Risk)] - $($_.Summary)"})}
                'Pressure'{$a=Get-WinCareDiskPressureAssessment;Show-WinCareTextPage -Title 'Disk-pressure assessment' -Lines @("Drive: $($a.Drive)","Free: $([math]::Round($a.FreeBytes/1GB,2)) GiB ($($a.FreePercent)%)","Recommended: $($a.RecommendedProfile)","Eligible targets: $($a.TargetCount)","Estimated reclaim: $([math]::Round($a.EstimatedReclaimBytes/1MB,2)) MiB",'', 'Excluded:')+@($a.ExcludedBehaviors|ForEach-Object{"- $_"})}
                'PressureApply'{$profile=Read-WinCareInput -Prompt 'Auto, Conservative, Balanced, or Emergency' -Default 'Auto';Invoke-WinCarePlanFromUi (New-WinCareDiskPressureCleanupPlan -Profile $profile)}
                'Schedule'{$profile=Read-WinCareInput -Prompt 'Conservative or Balanced' -Default 'Conservative';$schedule=Read-WinCareInput -Prompt 'Schedule' -Default 'weekly:Sunday@03:00';Invoke-WinCarePlanFromUi (New-WinCareDiskPressureAutomationPlan -Profile $profile -Schedule $schedule)}
                'Winapp2'{$path=Read-WinCareInput -Prompt 'Local winapp2.ini or .txt path';$a=Get-WinCareWinapp2Assessment -LiteralPath $path;Show-WinCareTextPage -Title 'Winapp2 assessment' -Lines @("Admitted files: $($a.FileCount)","Bytes: $($a.TotalBytes)","Truncated: $($a.Truncated)","Rejected rules: $(@($a.Catalog.Rejected).Count)")}
                'Winapp2Apply'{$path=Read-WinCareInput -Prompt 'Local winapp2.ini or .txt path';Invoke-WinCarePlanFromUi (New-WinCareWinapp2CleanupPlan -LiteralPath $path)}
                'RelocationProfiles'{Show-WinCareTextPage -Title 'Relocation profiles' -Lines @(Get-WinCareApplicationDataRelocationProfile|ForEach-Object{"$($_.Id) - $($_.Title) [$($_.Source)]"})}
                'RelocationAssess'{$id=Read-WinCareInput -Prompt 'Profile ID';$root=Read-WinCareInput -Prompt 'Destination root';$a=Get-WinCareApplicationDataRelocationAssessment -ProfileId $id -DestinationRoot $root;Show-WinCareTextPage -Title 'Relocation assessment' -Lines @("Source: $($a.Source)","Destination: $($a.Destination)","Bytes: $($a.Bytes)","Files: $($a.Files)","Ready: $($a.Ready)","Running processes: $(@($a.RunningProcesses.ProcessName)-join ', ')")}
                'RelocationApply'{$id=Read-WinCareInput -Prompt 'Profile ID';$root=Read-WinCareInput -Prompt 'Destination root';Invoke-WinCarePlanFromUi (New-WinCareApplicationDataRelocationPlan -ProfileId $id -DestinationRoot $root)}
                'Preview'{$path=Read-WinCareInput -Prompt 'Local file or directory path';$include=(Read-WinCareInput -Prompt 'Include redacted text? (y/N)' -Default 'N') -match '^(?i)y(es)?$';$p=Get-WinCareFilePreview -LiteralPath $path -IncludeText:$include;Show-WinCareTextPage -Title 'Local file preview' -Lines @((ConvertTo-Json $p -Depth 12)-split "`r?`n")}
                'Handlers'{Show-WinCareTextPage -Title 'Registered preview handlers' -Lines @(Get-WinCarePreviewHandlerAssessment|ForEach-Object{"$($_.Scope) $($_.Clsid) $($_.Name) signature=$($_.Signature) present=$($_.ServerPresent)"})}
            }
        }catch{Show-WinCareMessage -Title 'Cleaner and File Preview Studio failed' -Lines @($_.Exception.Message) -Kind Danger}
    }
}

