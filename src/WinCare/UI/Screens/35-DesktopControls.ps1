function Show-WinCareDesktopControlsScreen {
    while($true){
        $summary=Get-WinCareDesktopControlSummary
        $brightness=if(@($summary.Brightness).Count){(@($summary.Brightness.Brightness)-join ', ')+'%'}else{'Unavailable'}
        $menu=@(
            [pscustomobject]@{Label='Set master volume';Description="Current: $(if($null-ne $summary.VolumePercent){$summary.VolumePercent.ToString()+'%'}else{'Unavailable'})";Action='Volume';Disabled=$null-eq $summary.VolumePercent},
            [pscustomobject]@{Label='Toggle mute';Description="Current muted: $($summary.Muted)";Action='Mute';Disabled=$null-eq $summary.Muted},
            [pscustomobject]@{Label='Set display brightness';Description="Current: $brightness";Action='Brightness';Disabled=-not @($summary.Brightness).Count},
            [pscustomobject]@{Label='Wi-Fi profiles';Description='Names only; keys are never read';Action='Wifi'},
            [pscustomobject]@{Label='Clear current clipboard';Description='WinCare does not collect clipboard history';Action='Clipboard'},
            [pscustomobject]@{Label='Open Windows display settings';Description='Native settings surface';Action='Display'},
            [pscustomobject]@{Label='Open Windows sound settings';Description='Native settings surface';Action='Sound'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Desktop quick controls' -Subtitle 'Live state is sampled only while this screen is visible' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'Volume'{$value=Read-WinCareInput -Prompt 'Volume percent (0-100)' -Default ([string]$summary.VolumePercent);if($value -match '^\d{1,3}$' -and [int]$value -le 100){Invoke-WinCarePlanFromUi (New-WinCareMasterVolumePlan -Percent ([int]$value))}}
            'Mute'{Invoke-WinCarePlanFromUi (New-WinCareMasterVolumePlan -Percent ([int]$summary.VolumePercent) -Mute (-not [bool]$summary.Muted))}
            'Brightness'{$value=Read-WinCareInput -Prompt 'Brightness percent (0-100)' -Default ([string]@($summary.Brightness)[0].Brightness);if($value -match '^\d{1,3}$' -and [int]$value -le 100){Invoke-WinCarePlanFromUi (New-WinCareBrightnessPlan -Percent ([int]$value))}}
            'Wifi'{$lines=@(Get-WinCareWifiProfile|ForEach-Object{"$($_.Name)  [key material not read]"});Show-WinCareTextPage -Title 'Wi-Fi profiles' -Lines $(if($lines.Count){$lines}else{@('No profiles found or WLAN is unavailable.')})}
            'Clipboard'{Invoke-WinCarePlanFromUi (New-WinCareClearClipboardPlan)}
            'Display'{Open-WinCareDisplaySettings}
            'Sound'{Open-WinCareSoundSettings}
        }
    }
}

