function Format-WinCarePowerSessionRow {
    param($Item,$Width)
    $expiry=if($Item.ExpiresAt){try{([datetime]$Item.ExpiresAt).ToLocalTime().ToString('MM-dd HH:mm')}catch{'invalid'}}else{'indefinite'}
    "$(Limit-WinCareText $Item.Mode 10) $(Limit-WinCareText $Item.Status 14) $(Limit-WinCareText $expiry 16) $(Limit-WinCareText $Item.Reason ([math]::Max(20,$Width-48)))"
}

function Show-WinCarePowerSessionsScreen {
    while($true){
        $sessions=@(Get-WinCarePowerSession -IncludeTerminal);$active=@($sessions|Where-Object Status -in @('Prepared','Active','StopRequested'))
        $menu=@(
            [pscustomobject]@{Label='Active sessions';Description="$($active.Count) active or pending session(s)";Action='List'},
            [pscustomobject]@{Label='Keep system awake';Description='Supported SetThreadExecutionState request; display may turn off';Action='System'},
            [pscustomobject]@{Label='Keep system and display awake';Description='Supported execution-state request with display hold';Action='Display'},
            [pscustomobject]@{Label='Away-mode session';Description='Policy-gated media/server behavior; not a default optimization';Action='Away'},
            [pscustomobject]@{Label='Stop session';Description='Stop an exact WinCare host and clear its request';Action='Stop';Disabled=$active.Count -eq 0},
            [pscustomobject]@{Label='Open power-session records';Description=(Get-WinCarePowerSessionRoot);Action='Folder'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Power sessions' -Subtitle 'Dedicated bounded hosts, heartbeat, expiry, exact identity checks, and explicit stop semantics' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'List' {
                    $selected=Show-WinCareListSelector -Title 'Power-session history' -Items $sessions -Formatter ${function:Format-WinCarePowerSessionRow} -SearchProperties @('Mode','Status','Reason','Id')
                    if($selected){
                        $expires=if($selected.ExpiresAt){[string]$selected.ExpiresAt}else{'Indefinite'}
                        $lines=@("Mode: $($selected.Mode)","Status: $($selected.Status)","Display held: $($selected.PreventDisplay)","Away mode: $($selected.AwayMode)","Created: $($selected.CreatedAt)","Started: $($selected.StartedAt)","Expires: $expires","Ended: $($selected.EndedAt)","Host PID: $($selected.HostProcessId)","Host alive: $($selected.HostAlive)","Heartbeat: $($selected.HeartbeatAt)","Reason: $($selected.Reason)")
                        Show-WinCareTextPage -Title "Power session $($selected.Id)" -Lines $lines
                    }
                }
                {$_ -in @('System','Display','Away')}{$durationText=Read-WinCareInput -Prompt 'Duration minutes (0 = indefinite)' -Default ([string](Get-WinCareConfig 'PowerSessionDefaultMinutes'));$duration=0;if(-not [int]::TryParse($durationText,[ref]$duration) -or $duration -lt 0 -or $duration -gt 10080){throw 'Duration must be within 0..10080 minutes.'};$reason=Read-WinCareInput -Prompt 'Operational reason' -Default 'Operator-requested awake session';Invoke-WinCarePlanFromUi (New-WinCarePowerSessionPlan -Mode $choice.Action -DurationMinutes $duration -RefreshSeconds ([int](Get-WinCareConfig 'PowerSessionRefreshSeconds')) -Reason $reason)}
                'Stop'{$selected=Show-WinCareListSelector -Title 'Stop power session' -Items $active -Formatter ${function:Format-WinCarePowerSessionRow} -SearchProperties @('Mode','Reason','Id');if($selected){Invoke-WinCarePlanFromUi (New-WinCarePowerSessionStopPlan -Id $selected.Id)}}
                'Folder'{$null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @((Get-WinCarePowerSessionRoot))}
            }
        }catch{Show-WinCareMessage -Title 'Power-session operation failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

