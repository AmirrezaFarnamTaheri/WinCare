function Show-WinCareBootScreen {
    while($true){$menu=@(
        [pscustomobject]@{Label='Boot and recovery diagnostics';Description='BCD current entry, WinRE, Secure Boot, and firmware evidence';Action='Status'},
        [pscustomobject]@{Label='Export BCD backup';Description='Creates a hash-recorded BCD store backup';Action='Export'},
        [pscustomobject]@{Label='Open Recovery settings';Description='Native Windows recovery surface';Action='Recovery'},
        [pscustomobject]@{Label='Open Advanced startup';Description='Native Windows settings';Action='Advanced'},
        [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
    );$choice=Show-WinCareMenu -Title 'Boot and recovery' -Subtitle 'WinCare never replaces the Windows boot loader or writes raw EFI/NTFS structures' -Items $menu;if(-not $choice -or $choice.Action -eq 'Back'){return};switch($choice.Action){
        'Status'{$d=Get-WinCareBootDiagnostics;Show-WinCareTextPage -Title 'Boot diagnostics' -Lines @("Secure Boot: $($d.SecureBoot)","Firmware type: $($d.FirmwareType)","System drive: $($d.SystemDrive)",'',"WinRE:",$d.WinRE,'',"BCD current entry:",$d.Bcd,'',$d.ModificationPolicy)}
        'Export'{$default=Join-Path (Join-Path $script:WinCareState.Root 'Backups') ('bcd-'+(Get-Date -Format yyyyMMdd-HHmmss)+'.bak');$path=Read-WinCareInput -Prompt 'New BCD backup path' -Default $default;if($path){Invoke-WinCarePlanFromUi (New-WinCareBcdExportPlan -Destination $path)}}
        'Recovery'{$null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('ms-settings:recovery')}
        'Advanced'{$null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('ms-settings:recovery')}
    }}
}

