function Format-WinCareUndoOperationRow {
    param([object]$Item,[int]$Width)
    $dateWidth=24; $countWidth=9; $rest=$Width-$dateWidth-$countWidth-2
    "$(Limit-WinCareText ([string]$Item.StartedAt) $dateWidth) $(Limit-WinCareText ([string]$Item.ActionCount) $countWidth) $(Limit-WinCareText $Item.Name $rest)"
}

function Show-WinCareRecoveryScreen {
    while ($true) {
        $operations=@(Get-WinCareUndoableOperation)
        $menu=@(
            [pscustomobject]@{ Label='Undo a reversible operation'; Description="$($operations.Count) operation(s) currently eligible"; Action='Undo'; Disabled=($operations.Count -eq 0) },
            [pscustomobject]@{ Label='Startup recovery'; Description='Open the Startup screen to restore backed-up entries'; Action='Startup' },
            [pscustomobject]@{ Label='Open Windows Recovery settings'; Description='System Restore, reset, and advanced startup are Windows-owned'; Action='Windows' },
            [pscustomobject]@{ Label='Open WinCare backup folder'; Description='Inspect startup backups and retained recovery data'; Action='Folder' },
            [pscustomobject]@{ Label='Back'; Description='Return to main menu'; Action='Back' }
        )
        $choice=Show-WinCareMenu -Title 'Recovery and undo' -Subtitle 'Only operations with a concrete reverse action are offered' -Items $menu
        if (-not $choice -or $choice.Action -eq 'Back') { return }
        switch ($choice.Action) {
            'Undo' {
                $operation=Show-WinCareListSelector -Title 'Undo operation' -Subtitle 'Supported: file moves, Storage Sense state, and startup disables' -Items $operations -Formatter ${function:Format-WinCareUndoOperationRow} -SearchProperties @('Name','StartedAt','ActionCount')
                if ($operation) {
                    $plan=New-WinCareUndoPlan $operation
                    if ($plan -and $plan.Actions.Count) {
                        if (Confirm-WinCarePlan $plan) {
                            Clear-WinCareScreen; Write-WinCareHeader -Title 'Undoing operation' -Subtitle $operation.Name
                            $result=Invoke-WinCarePlan -Plan $plan -Approved
                            if ($result.Success) { Set-WinCareOperationUndone -OperationId $operation.Id -UndoJournalPath $result.Data.JournalPath }
                            Show-WinCareResult -Result $result -Title 'Undo result'
                        }
                    } else { Show-WinCareMessage -Title 'Undo unavailable' -Lines @('The source state required for this undo is no longer present.') -Kind Warning }
                }
            }
            'Startup' { Show-WinCareStartupScreen }
            'Windows' { Start-Process 'ms-settings:recovery' }
            'Folder' { $null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @((Join-Path $script:WinCareState.Root 'Backups')) }
        }
    }
}

