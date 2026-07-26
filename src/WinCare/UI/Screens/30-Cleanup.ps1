function Format-WinCareCleanupRow {
    param([object]$Target,[int]$Width)
    $nameWidth=[math]::Max(24,[math]::Floor($Width*.55)); $riskWidth=10; $sizeWidth=$Width-$nameWidth-$riskWidth-2
    "$(Limit-WinCareText $Target.Name $nameWidth) $(Limit-WinCareText $Target.Risk $riskWidth) $(Limit-WinCareText (Format-WinCareBytes $Target.EstimatedBytes) $sizeWidth)"
}

function Show-WinCareCleanupScan {
    Clear-WinCareScreen
    Write-WinCareHeader -Title 'Cleanup scan'
    Write-WinCareText ' Measuring only documented cleanup categories. User documents and Downloads are excluded.' Accent
    $targets=@(Get-WinCareCleanupTarget -Refresh | Where-Object { $_.Exists -and $_.FileCount -gt 0 })
    if (-not $targets.Count) { Show-WinCareMessage -Title 'Cleanup scan' -Lines @('No eligible files were found in the built-in cleanup categories.') -Kind Success; return }
    $selected=@(Show-WinCareListSelector -Title 'Cleanup categories' -Subtitle 'Space estimates are recalculated after execution' -Items $targets -Formatter ${function:Format-WinCareCleanupRow} -SearchProperties @('Name','Path','Risk') -MultiSelect)
    if ($selected.Count) { Invoke-WinCarePlanFromUi (New-WinCareCleanupPlan -Targets $selected) }
}

function Show-WinCareCleanupScreen {
    while ($true) {
        $targets=@(Get-WinCareCleanupTarget)
        $bytes=[long](($targets | Measure-Object EstimatedBytes -Sum).Sum)
        $menu=@(
            [pscustomobject]@{ Label='Safe cleanup scan'; Description="Review $(Format-WinCareBytes $bytes) currently eligible"; Action='Scan' },
            [pscustomobject]@{ Label='Empty Recycle Bin'; Description='Requires a separate explicit confirmation'; Action='Recycle' },
            [pscustomobject]@{ Label='Reset Microsoft Store cache'; Description='Run the Windows WSReset utility'; Action='Store' },
            [pscustomobject]@{ Label='Open Disk Cleanup'; Description='Launch the native interactive cleanmgr utility'; Action='DiskCleanup'; Disabled=-not [bool](Get-Command cleanmgr.exe -ErrorAction SilentlyContinue) },
            [pscustomobject]@{ Label='Open Windows cleanup recommendations'; Description='Open the native Storage settings page'; Action='Settings' },
            [pscustomobject]@{ Label='Refresh estimates'; Description='Remeasure all cleanup categories'; Action='Refresh' },
            [pscustomobject]@{ Label='Back'; Description='Return to main menu'; Action='Back' }
        )
        $choice=Show-WinCareMenu -Title 'Cleanup center' -Subtitle 'Previewed, category-based cleanup without registry cleaners' -Items $menu
        if (-not $choice -or $choice.Action -eq 'Back') { return }
        switch ($choice.Action) {
            'Scan' { Show-WinCareCleanupScan }
            'Recycle' { Invoke-WinCarePlanFromUi (New-WinCareCleanupPlan -Targets @() -IncludeRecycleBin) }
            'Store' { Invoke-WinCarePlanFromUi (New-WinCareStoreCachePlan) }
            'DiskCleanup' { $null=Start-WinCareDetachedProcess -FilePath 'cleanmgr.exe' }
            'Settings' { $null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('ms-settings:storagerecommendations') }
            'Refresh' { $null=Get-WinCareCleanupTarget -Refresh }
        }
    }
}

