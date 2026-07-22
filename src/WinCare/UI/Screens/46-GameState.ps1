function Format-WinCareSteamGameRow {param($Item,$Width);"$(Limit-WinCareText $Item.AppId 12) $(Limit-WinCareText $Item.Name ([math]::Max(24,$Width-48))) $(Limit-WinCareText $Item.LibraryPath 28)"}
function Format-WinCareSteamUserRow {param($Item,$Width);"$(Limit-WinCareText $Item.UserId 24) $(Limit-WinCareText $Item.Path ([math]::Max(30,$Width-28)))"}
function Show-WinCareGameStateScreen {
    while($true){
        $games=@(Get-WinCareSteamGame);$users=@(Get-WinCareSteamUser)
        $menu=@(
            [pscustomobject]@{Label='Steam libraries and games';Description="$($games.Count) manifest-backed installed game(s)";Action='Games'},
            [pscustomobject]@{Label='Steam local cloud cache';Description='Per-user, per-app local files with exact SHA-256 inventory';Action='Cloud';Disabled=$users.Count -eq 0},
            [pscustomobject]@{Label='Create local backup';Description='Manifested ZIP with every file hash';Action='Backup';Disabled=$users.Count -eq 0},
            [pscustomobject]@{Label='Restore local backup';Description='Policy-gated compare-before-restore with provider-internal rollback';Action='Restore'},
            [pscustomobject]@{Label='Game integrity findings';Description='Trainer/injector indicators and anti-cheat service evidence; detection only';Action='Integrity'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Steam and game-state protection' -Subtitle 'Local cache inventory, exact backups, guarded restore, and defensive integrity evidence' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{switch($choice.Action){
            'Games'{$null=Show-WinCareListSelector -Title 'Steam games' -Items $games -Formatter ${function:Format-WinCareSteamGameRow} -SearchProperties @('AppId','Name','LibraryPath')}
            'Cloud'{$user=Show-WinCareListSelector -Title 'Steam user' -Items $users -Formatter ${function:Format-WinCareSteamUserRow};if($user){$app=Read-WinCareInput -Prompt 'Steam AppId';if($app){$files=@(Get-WinCareSteamCloudFile -UserId $user.UserId -AppId $app);Show-WinCareTextPage -Title "Steam $app local cloud cache" -Lines @($files|ForEach-Object{"$(Format-WinCareBytes $_.SizeBytes) $($_.Sha256) $($_.RelativePath)"})}}}
            'Backup'{$user=Show-WinCareListSelector -Title 'Steam user' -Items $users -Formatter ${function:Format-WinCareSteamUserRow};if($user){$app=Read-WinCareInput -Prompt 'Steam AppId';if($app){Invoke-WinCarePlanFromUi (New-WinCareSteamCloudBackupPlan -UserId $user.UserId -AppId $app)}}}
            'Restore'{$path=Read-WinCareInput -Prompt 'WinCare Steam backup ZIP path';if($path){Invoke-WinCarePlanFromUi (New-WinCareSteamCloudRestorePlan -BackupPath $path)}}
            'Integrity'{$rootsText=Read-WinCareInput -Prompt 'Optional comma-separated scan roots' -Default ''; $roots=if($rootsText){$rootsText.Split(',',[StringSplitOptions]::RemoveEmptyEntries)|ForEach-Object{$_.Trim()}}else{@()};$findings=@(Get-WinCareGameIntegrityFinding -ScanRoot $roots);Show-WinCareTextPage -Title 'Game integrity findings' -Lines $(if($findings.Count){@($findings|ForEach-Object{"$($_.Severity) | $($_.Kind) | $($_.Name) | $($_.Evidence)"})}else{@('No configured indicators were observed.')})}
        }}catch{Show-WinCareMessage -Title 'Game-state operation failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

