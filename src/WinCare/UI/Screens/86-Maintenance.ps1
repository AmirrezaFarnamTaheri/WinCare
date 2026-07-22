function Format-WinCareMaintenanceRow {
    param($Item,$Width)
    "$(Limit-WinCareText $Item.Name ([math]::Max(24,$Width-44))) $(Limit-WinCareText $Item.EffectiveState 12) $($Item.StartAt.ToString('yyyy-MM-dd HH:mm')) - $($Item.EndAt.ToString('HH:mm'))"
}

function Show-WinCareMaintenanceScreen {
    while ($true) {
        $windows = @(Get-WinCareMaintenanceWindow -IncludeTerminal)
        $open = @($windows | Where-Object EffectiveState -notin @('Completed','Cancelled'))
        $menu = @(
            [pscustomobject]@{Label='Maintenance windows';Description="$($open.Count) open, $($windows.Count) total";Action='List'},
            [pscustomobject]@{Label='Schedule window';Description='Validated time range, notice, tags, restart flag, and optional playbook';Action='Create'},
            [pscustomobject]@{Label='Transition window';Description='Closed lifecycle with explicit allowed transitions';Action='Transition';Disabled=($open.Count -eq 0)},
            [pscustomobject]@{Label='Metrics';Description='Local counts and next-window timestamp';Action='Metrics'},
            [pscustomobject]@{Label='Export Prometheus text file';Description='Local file only; no listener or remote telemetry';Action='Export'},
            [pscustomobject]@{Label='Open maintenance folder';Description=(Join-Path $script:WinCareState.Root 'Maintenance');Action='Folder'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice = Show-WinCareMenu -Title 'Maintenance control plane' -Subtitle 'Scheduled -> Notice -> Active -> Completed/Cancelled' -Items $menu
        if (-not $choice -or $choice.Action -eq 'Back') { return }
        try {
            switch ($choice.Action) {
                'List' {
                    $selected = Show-WinCareListSelector -Title 'Maintenance windows' -Items $windows -Formatter ${function:Format-WinCareMaintenanceRow} -SearchProperties @('Name','State','EffectiveState','PlaybookId','Id')
                    if ($selected) {
                        Show-WinCareTextPage -Title $selected.Name -Lines @("ID: $($selected.Id)","State: $($selected.State) (effective $($selected.EffectiveState))","Start: $($selected.StartAt)","Notice: $($selected.NoticeAt)","End: $($selected.EndAt)","Playbook: $($selected.PlaybookId)","Restart: $($selected.RequiresRestart)","Tags: $(@($selected.Tags)-join ', ')",'',"Description: $($selected.Description)")
                    }
                }
                'Create' {
                    $name = Read-WinCareInput -Prompt 'Window name'
                    $startText = Read-WinCareInput -Prompt 'Start date/time (local, e.g. 2026-08-01 03:00)'
                    $endText = Read-WinCareInput -Prompt 'End date/time (local)'
                    $playbook = Read-WinCareInput -Prompt 'Optional playbook ID' -Default ''
                    $restart = Read-WinCareInput -Prompt 'May require restart? (y/N)' -Default 'N'
                    if ($name -and $startText -and $endText) {
                        $start = [datetimeoffset]::Parse($startText)
                        $end = [datetimeoffset]::Parse($endText)
                        $record = New-WinCareMaintenanceWindowRecord -Name $name -StartAt $start -EndAt $end -PlaybookId $playbook -RequiresRestart:($restart -match '^(?i)y') -Tags @('WinCare')
                        Invoke-WinCarePlanFromUi (New-WinCareMaintenanceUpsertPlan -Window $record)
                    }
                }
                'Transition' {
                    $selected = Show-WinCareListSelector -Title 'Select maintenance window' -Items $open -Formatter ${function:Format-WinCareMaintenanceRow} -SearchProperties @('Name','State','EffectiveState','Id')
                    if ($selected) {
                        $state = Read-WinCareInput -Prompt 'New state (Notice/Active/Completed/Cancelled)'
                        $message = Read-WinCareInput -Prompt 'Transition message' -Default ''
                        if ($state -in @('Notice','Active','Completed','Cancelled')) {
                            Invoke-WinCarePlanFromUi (New-WinCareMaintenanceTransitionPlan -Id $selected.Id -State $state -Message $message)
                        }
                    }
                }
                'Metrics' {
                    $metrics = Get-WinCareMaintenanceMetrics
                    $promLines = ConvertTo-WinCarePrometheusMaintenanceMetrics -Metrics $metrics
                    Show-WinCareTextPage -Title 'Maintenance metrics' -Lines (@("Total: $($metrics.Total)","Scheduled: $($metrics.Scheduled)","Notice: $($metrics.Notice)","Active: $($metrics.Active)","Completed: $($metrics.Completed)","Cancelled: $($metrics.Cancelled)","Next start Unix: $($metrics.NextStartUnix)","Upcoming restart-required: $($metrics.UpcomingRestartRequired)",'') + $promLines)
                }
                'Export' {
                    $default = Join-Path (Join-Path $script:WinCareState.Root 'Metrics') 'maintenance.prom'
                    $path = Read-WinCareInput -Prompt 'Metrics destination' -Default $default
                    if ($path) {
                        Invoke-WinCarePlanFromUi (New-WinCareMaintenanceMetricsExportPlan -Destination $path)
                    }
                }
                'Folder' {
                    Start-Process (Join-Path $script:WinCareState.Root 'Maintenance')
                }
            }
        } catch {
            Show-WinCareMessage -Title 'Maintenance operation failed' -Lines @($_.Exception.Message) -Kind Error
        }
    }
}
