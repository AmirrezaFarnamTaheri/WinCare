function Format-WinCareAutomationRow {param($Item,$Width);"$(Limit-WinCareText $Item.title ([math]::Max(25,$Width-32))) $(Limit-WinCareText $Item.command 18) $(Limit-WinCareText $Item.schedule 14)"}
function Show-WinCareAutomationScreen {
    while($true){$profiles=@(Get-WinCareAutomationProfile);$menu=@(
        [pscustomobject]@{Label='Registered profiles';Description="$($profiles.Count) saved profile(s)";Action='List'},
        [pscustomobject]@{Label='Create profile';Description='Validated command, schedule, runtime, battery, and apply policy';Action='Create'},
        [pscustomobject]@{Label='Register saved profile';Description='Create or update a Task Scheduler task';Action='Register';Disabled=$profiles.Count -eq 0},
        [pscustomobject]@{Label='Remove scheduled profile';Description='Remove task and saved profile';Action='Remove';Disabled=$profiles.Count -eq 0},
        [pscustomobject]@{Label='Open automation folder';Description=(Join-Path $script:WinCareState.Root 'Automation');Action='Folder'},
        [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
    );$choice=Show-WinCareMenu -Title 'Automation' -Subtitle 'Every scheduled change retains explicit Apply, policy, runtime, and audit semantics' -Items $menu;if(-not $choice -or $choice.Action -eq 'Back'){return};switch($choice.Action){
        'List'{$lines=@($profiles|ForEach-Object{"$($_.id) | $($_.command) | $($_.schedule) | apply=$($_.apply) | runtime=$($_.maximumRuntimeMinutes)m"});Show-WinCareTextPage -Title 'Automation profiles' -Lines $(if($lines.Count){$lines}else{@('No profiles are configured.')})}
        'Create'{$id=Read-WinCareInput -Prompt 'Profile ID';$title=Read-WinCareInput -Prompt 'Title';$command=Read-WinCareInput -Prompt 'Headless command' -Default 'system';$schedule=Read-WinCareInput -Prompt 'Schedule (daily@HH:mm or weekly:Day@HH:mm)' -Default 'weekly:Sunday@03:00';$applyText=Read-WinCareInput -Prompt 'Apply modifying command? (y/N)' -Default 'N';$argsText=Read-WinCareInput -Prompt 'Arguments JSON' -Default '{}';try{$args=$argsText|ConvertFrom-Json -AsHashtable -Depth 20;$profile=[ordered]@{schemaVersion=1;id=$id;title=$title;command=$command;arguments=$args;apply=$applyText -match '^(?i)y';schedule=$schedule;maximumRuntimeMinutes=120;batteryAllowed=$false;networkRequired=$command -like 'wua-*';notification='Journal';sourceRecords=@('SRC:ecb10917e9')};$path=Save-WinCareAutomationProfile -Profile $profile;Show-WinCareMessage -Title 'Profile saved' -Lines @($path) -Kind Success}catch{Show-WinCareMessage -Title 'Profile rejected' -Lines @($_.Exception.Message) -Kind Error}}
        'Register'{$profile=Show-WinCareListSelector -Title 'Register automation' -Items $profiles -Formatter ${function:Format-WinCareAutomationRow} -SearchProperties @('title','command','schedule','id');if($profile){Invoke-WinCarePlanFromUi (New-WinCareAutomationTaskPlan -Profile $profile)}}
        'Remove'{$profile=Show-WinCareListSelector -Title 'Remove automation' -Items $profiles -Formatter ${function:Format-WinCareAutomationRow} -SearchProperties @('title','command','schedule','id');if($profile){Invoke-WinCarePlanFromUi (New-WinCareAutomationTaskPlan -Profile $profile -Remove)}}
        'Folder'{$null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @((Join-Path $script:WinCareState.Root 'Automation'))}
    }}
}

