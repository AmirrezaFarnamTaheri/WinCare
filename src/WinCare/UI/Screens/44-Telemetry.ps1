function Format-WinCareTelemetrySeriesRow {param($Item,$Width);"$(Limit-WinCareText $Item.Name ([math]::Max(24,$Width-44))) $($Item.Samples.Count) samples $($Item.DurationSeconds)s $($Item.StartedAt)"}
function Show-WinCareTelemetryScreen {
    while($true){
        $history=Get-WinCareTelemetryHistory;$series=@($history.Series)
        $menu=@(
            [pscustomobject]@{Label='Current snapshot';Description='CPU, memory, disk, network, process, thread, and handle observations';Action='Snapshot'},
            [pscustomobject]@{Label='Capture bounded series';Description='Local-only history with cancellation and thresholds';Action='Capture'},
            [pscustomobject]@{Label='Capture history';Description="$($series.Count) series retained locally";Action='History'},
            [pscustomobject]@{Label='Export evidence';Description='Write local canonical JSON under WinCare telemetry exports';Action='Export'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Operations telemetry' -Subtitle 'Bounded local observations; no remote telemetry or network listener' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{switch($choice.Action){
            'Snapshot'{$x=Get-WinCareTelemetrySnapshot;Show-WinCareTextPage -Title 'Current telemetry snapshot' -Lines @("Timestamp: $($x.Timestamp)","CPU: $($x.CpuPercent)%","Memory: $(Format-WinCareBytes $x.MemoryUsedBytes) / $(Format-WinCareBytes $x.MemoryTotalBytes) ($($x.MemoryPercent)%)","Disk: read $(Format-WinCareBytes $x.DiskReadBytesPerSecond)/s, write $(Format-WinCareBytes $x.DiskWriteBytesPerSecond)/s","Network: receive $(Format-WinCareBytes $x.NetworkReceivedBytesPerSecond)/s, send $(Format-WinCareBytes $x.NetworkSentBytesPerSecond)/s","Processes: $($x.ProcessCount), threads: $($x.ThreadCount), handles: $($x.HandleCount)",'Alerts:')+@($x.Alerts|ForEach-Object{"  $($_.Severity): $($_.Metric) $($_.Value) >= $($_.Threshold)"})}
            'Capture'{$durationText=Read-WinCareInput -Prompt 'Duration seconds' -Default '30';$intervalText=Read-WinCareInput -Prompt 'Interval seconds' -Default '2';$name=Read-WinCareInput -Prompt 'Capture name' -Default 'Manual capture';Invoke-WinCarePlanFromUi (New-WinCareTelemetryCapturePlan -Name $name -DurationSeconds ([int]$durationText) -IntervalSeconds ([int]$intervalText))}
            'History'{$selected=Show-WinCareListSelector -Title 'Telemetry history' -Items $series -Formatter ${function:Format-WinCareTelemetrySeriesRow} -SearchProperties @('Name','Id','StartedAt');if($selected){Show-WinCareTextPage -Title $selected.Name -Lines @("ID: $($selected.Id)","Started: $($selected.StartedAt)","Completed: $($selected.CompletedAt)","Interval: $($selected.IntervalSeconds)s","Duration: $($selected.DurationSeconds)s","Samples: $($selected.Samples.Count)")}}
            'Export'{$name="telemetry-$([datetime]::UtcNow.ToString('yyyyMMdd-HHmmss')).json";$destination=Join-Path (Join-Path (Get-WinCareTelemetryRoot) 'Exports') $name;Invoke-WinCarePlanFromUi (New-WinCareTelemetryExportPlan -Destination $destination)}
        }}catch{Show-WinCareMessage -Title 'Telemetry operation failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

