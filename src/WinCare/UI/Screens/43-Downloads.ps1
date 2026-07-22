function Format-WinCareDownloadRow {param($Item,$Width);"$(Limit-WinCareText $Item.Status 12) $(Limit-WinCareText $Item.Name ([math]::Max(20,$Width-55))) $(Limit-WinCareText $Item.Priority 10) $(Limit-WinCareText $Item.Destination 30)"}
function Show-WinCareDownloadsScreen {
    while($true){
        $jobs=@(Get-WinCareDownloadJob)
        $menu=@(
            [pscustomobject]@{Label='Download queue';Description="$($jobs.Count) protected local job(s)";Action='List'},
            [pscustomobject]@{Label='Create job';Description='HTTP/HTTPS mirrors, checksums, bounded headers, and atomic destination';Action='Create'},
            [pscustomobject]@{Label='Start job';Description='Start an exact queued BITS transfer';Action='Start';Disabled=-not @($jobs|Where-Object Status -in @('Queued','Scheduled')).Count},
            [pscustomobject]@{Label='Start due jobs';Description='Start queued or due scheduled jobs within the concurrency limit';Action='StartDue';Disabled=-not @(Get-WinCareDueDownloadJob).Count},
            [pscustomobject]@{Label='Suspend or resume';Description='Use BITS-owned pause/resume semantics';Action='State';Disabled=-not @($jobs|Where-Object Status -in @('Transferring','Suspended')).Count},
            [pscustomobject]@{Label='Reconcile job';Description='Verify BITS state, checksum, and atomic completion';Action='Reconcile';Disabled=$jobs.Count -eq 0},
            [pscustomobject]@{Label='Cancel job';Description='Cancel exact BITS job and remove private partial data';Action='Cancel';Disabled=-not @($jobs|Where-Object Status -notin @('Completed','Cancelled')).Count},
            [pscustomobject]@{Label='Remove terminal record';Description='Remove a completed, failed, or cancelled queue record';Action='Remove';Disabled=-not @($jobs|Where-Object Status -in @('Completed','Failed','Cancelled')).Count},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Verified downloads' -Subtitle 'BITS-backed queue, mirrors, hashes, bounded headers, exact state transitions, and atomic completion' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'List'{$selected=Show-WinCareListSelector -Title 'Download queue' -Items $jobs -Formatter ${function:Format-WinCareDownloadRow} -SearchProperties @('Name','Status','Destination','Priority','Urls');if($selected){Show-WinCareTextPage -Title $selected.Name -Lines @("Status: $($selected.Status)","Destination: $($selected.Destination)","Priority: $($selected.Priority)","Hash: $($selected.HashAlgorithm) $($selected.ExpectedHash)","BITS job: $($selected.BitsJobId)","Bytes: $($selected.BytesTransferred) / $($selected.BytesTotal)","Created: $($selected.CreatedAt)","Updated: $($selected.UpdatedAt)","URLs:")+@($selected.Urls|ForEach-Object{"  $_"})}}
                'Create'{$urls=(Read-WinCareInput -Prompt 'URL or comma-separated mirrors').Split(',',[StringSplitOptions]::RemoveEmptyEntries)|ForEach-Object{$_.Trim()};$destination=Read-WinCareInput -Prompt 'Destination path';$name=Read-WinCareInput -Prompt 'Name' -Default ([IO.Path]::GetFileName($destination));$algorithm=Read-WinCareInput -Prompt 'Hash algorithm (None/SHA256/SHA512)' -Default 'None';$expected=if($algorithm -ne 'None'){Read-WinCareInput -Prompt 'Expected hash'}else{''};$record=New-WinCareDownloadJobRecord -Url $urls -Destination $destination -Name $name -HashAlgorithm $algorithm -ExpectedHash $expected;Invoke-WinCarePlanFromUi (New-WinCareDownloadJobPlan -Job $record)}
                'Start'{$selected=Show-WinCareListSelector -Title 'Start download' -Items @($jobs|Where-Object Status -in @('Queued','Scheduled')) -Formatter ${function:Format-WinCareDownloadRow};if($selected){Invoke-WinCarePlanFromUi (New-WinCareDownloadStartPlan -Id $selected.Id)}}
                'StartDue'{Invoke-WinCarePlanFromUi (New-WinCareDueDownloadStartPlan)}
                'State'{$selected=Show-WinCareListSelector -Title 'Suspend or resume' -Items @($jobs|Where-Object Status -in @('Transferring','Suspended')) -Formatter ${function:Format-WinCareDownloadRow};if($selected){$state=if($selected.Status -eq 'Transferring'){'Suspended'}else{'Transferring'};Invoke-WinCarePlanFromUi (New-WinCareDownloadStatePlan -Id $selected.Id -State $state)}}
                'Reconcile'{$selected=Show-WinCareListSelector -Title 'Reconcile download' -Items $jobs -Formatter ${function:Format-WinCareDownloadRow};if($selected){Invoke-WinCarePlanFromUi (New-WinCareDownloadReconcilePlan -Id $selected.Id)}}
                'Cancel'{$selected=Show-WinCareListSelector -Title 'Cancel download' -Items @($jobs|Where-Object Status -notin @('Completed','Cancelled')) -Formatter ${function:Format-WinCareDownloadRow};if($selected){Invoke-WinCarePlanFromUi (New-WinCareDownloadCancelPlan -Id $selected.Id)}}
                'Remove'{$selected=Show-WinCareListSelector -Title 'Remove queue record' -Items @($jobs|Where-Object Status -in @('Completed','Failed','Cancelled')) -Formatter ${function:Format-WinCareDownloadRow};if($selected){Invoke-WinCarePlanFromUi (New-WinCareDownloadJobRemovePlan -Id $selected.Id)}}
            }
        }catch{Show-WinCareMessage -Title 'Download operation failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

