function Format-WinCareRemoteToolRow {param($Item,$Width);"$(Limit-WinCareText $Item.Name 24) installed:$($Item.Installed) proc:$(@($Item.Processes).Count) svc:$(@($Item.Services).Count) listen:$(@($Item.Listeners).Count)"}
function Format-WinCareRemoteConsentRow {param($Item,$Width);"$(Limit-WinCareText $Item.ToolId 20) $(Limit-WinCareText $Item.Status 10) $(Limit-WinCareText $Item.Operator 18) $(Limit-WinCareText $Item.ExpiresAt ([math]::Max(20,$Width-56)))"}
function Show-WinCareRemoteSupportScreen {
    while($true){
        $surface=@(Get-WinCareRemoteSupportSurface);$consents=@(Get-WinCareRemoteSupportConsent -IncludeTerminal);$open=@($consents|Where-Object Status -in @('Prepared','Active'));$expired=@($consents|Where-Object{[string]$_.Status -eq 'Expired' -and [string]$_.StoredStatus -in @('Prepared','Active')})
        $menu=@(
            [pscustomobject]@{Label='Remote-support surfaces';Description="$(@($surface|Where-Object{$_.Installed -or @($_.Processes).Count}).Count) detected tool surface(s)";Action='Surface'},
            [pscustomobject]@{Label='Consent records';Description="$($open.Count) open, $($consents.Count) total";Action='Consent'},
            [pscustomobject]@{Label='Create consent';Description='Explicit operator, reason, expiry, scope, clipboard and transfer flags';Action='Create'},
            [pscustomobject]@{Label='Transition consent';Description='Prepared -> Active -> Closed/Revoked; expired records cannot reactivate';Action='Transition';Disabled=$open.Count -eq 0},
            [pscustomobject]@{Label='Reconcile expired consent';Description="Persist $($expired.Count) effective expiry transition(s)";Action='Expire';Disabled=$expired.Count -eq 0},
            [pscustomobject]@{Label='Emergency input release';Description='Release local cursor clipping and modifiers';Action='Release'},
            [pscustomobject]@{Label='Emergency stop known processes';Description='Policy-gated exact PID/start-time/path/hash termination';Action='Terminate'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Remote-support governance' -Subtitle 'Inventory and explicit-consent evidence; no credential capture, covert access, or unattended enablement' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try {
            switch($choice.Action) {
            'Surface'{$selected=Show-WinCareListSelector -Title 'Remote-support surfaces' -Items $surface -Formatter ${function:Format-WinCareRemoteToolRow} -SearchProperties @('Name','Id','Publisher','InstallPaths','ProcessNames','ServiceNames');if($selected){$lines=[Collections.Generic.List[string]]::new();$lines.Add("Installed: $($selected.Installed)");$lines.Add("Publisher: $($selected.Publisher)");foreach($p in @($selected.Processes)){$lines.Add("Process: $($p.Name) PID $($p.ProcessId) $($p.Path)")};foreach($s in @($selected.Services)){$lines.Add("Service: $($s.Name) $($s.Status) $($s.StartType)")};foreach($l in @($selected.Listeners)){$lines.Add("Listener: $($l.LocalAddress):$($l.LocalPort) PID $($l.OwningProcess)")};$lines.Add("Config keys observed (values redacted): $(@($selected.ConfigKeys)-join ', ')");Show-WinCareTextPage -Title $selected.Name -Lines @($lines)}}
                    'Consent' {
                    $selected=Show-WinCareListSelector -Title 'Consent records' -Items $consents -Formatter ${function:Format-WinCareRemoteConsentRow} -SearchProperties @('ToolId','Operator','Reason','Status','Id')
                    if($selected){
                        $lines=@("Tool: $($selected.ToolId)","Status: $($selected.Status)","Operator: $($selected.Operator)","Reason: $($selected.Reason)","Created: $($selected.CreatedAt)","Expires: $($selected.ExpiresAt)","Closed: $($selected.ClosedAt)","Scope: $($selected.DeviceScope)","Clipboard: $($selected.AllowClipboard)","File transfer: $($selected.AllowFileTransfer)")
                        Show-WinCareTextPage -Title "Consent $($selected.Id)" -Lines $lines
                    }
                }
            'Create'{$tool=Show-WinCareMenu -Title 'Remote-support tool' -Items @(Get-WinCareRemoteSupportCatalog|ForEach-Object{[pscustomobject]@{Label=$_.Name;Description=(@($_.ProcessNames)-join ', ');Id=$_.Id}});if(-not $tool){continue};$operator=Read-WinCareInput -Prompt 'Remote operator identity';$reason=Read-WinCareInput -Prompt 'Reason';$minutesText=Read-WinCareInput -Prompt 'Consent minutes' -Default ([string](Get-WinCareConfig 'RemoteConsentDefaultMinutes'));$minutes=60;if(-not[int]::TryParse($minutesText,[ref]$minutes)){throw 'Consent duration is invalid.'};$scope=Read-WinCareInput -Prompt 'Scope (ThisDevice/TwoDevicePair)' -Default 'ThisDevice';$clip=(Read-WinCareInput -Prompt 'Allow clipboard? (y/N)' -Default 'N') -match '^(?i)y';$files=(Read-WinCareInput -Prompt 'Allow file transfer? (y/N)' -Default 'N') -match '^(?i)y';Invoke-WinCarePlanFromUi (New-WinCareRemoteSupportConsentPlan -ToolId $tool.Id -Operator $operator -Reason $reason -DurationMinutes $minutes -DeviceScope $scope -AllowClipboard $clip -AllowFileTransfer $files)}
            'Transition'{$selected=Show-WinCareListSelector -Title 'Select consent' -Items $open -Formatter ${function:Format-WinCareRemoteConsentRow} -SearchProperties @('ToolId','Operator','Status','Id');if($selected){$state=Read-WinCareInput -Prompt 'New state (Active/Closed/Revoked)';if($state -in @('Active','Closed','Revoked')){Invoke-WinCarePlanFromUi (New-WinCareRemoteSupportConsentStatePlan -Id $selected.Id -State $state)}}}
            'Expire'{Invoke-WinCarePlanFromUi (New-WinCareRemoteSupportExpiryReconciliationPlan)}
            'Release'{Invoke-WinCarePlanFromUi (New-WinCareRemoteSupportEmergencyPlan)}
            'Terminate'{$processes=@($surface|ForEach-Object{@($_.Processes)});$selected=Show-WinCareListSelector -Title 'Known remote-support processes' -Subtitle 'Space selects exact processes; policy may deny termination' -Items $processes -Formatter {param($item,$width);"$(Limit-WinCareText $item.Name 22) PID $($item.ProcessId) $(Limit-WinCareText $item.Path ([math]::Max(20,$width-38)))"} -SearchProperties @('Name','Path','ProcessId') -MultiSelect;if($selected){Invoke-WinCarePlanFromUi (New-WinCareRemoteSupportEmergencyPlan -ProcessId @($selected.ProcessId) -TerminateKnownProcesses)}}
            }
        } catch {
            Show-WinCareMessage -Title 'Remote-support operation failed' -Lines @($_.Exception.Message) -Kind Error
        }
    }
}

