function Show-WinCareSecurityBaselineScreen {
    while($true){
        $policy=try{Get-WinCareLocalGroupPolicyState}catch{[pscustomobject]@{Exists=$false;Path='Unavailable';Sha256='unavailable';MachinePolicy=$false;UserPolicy=$false}}
        $sysmon=try{Get-WinCareSysmonState}catch{[pscustomobject]@{Installed=$false;Services=@();Drivers=@();RecentEvents=@()}}
        $menu=@(
            [pscustomobject]@{Label='Local Group Policy state';Description="exists=$($policy.Exists), machine=$($policy.MachinePolicy), user=$($policy.UserPolicy)";Action='PolicyState'},
            [pscustomobject]@{Label='Import LGPO backup';Description='Microsoft-signed LGPO.exe, exact backup hash, pre-import rollback backup';Action='PolicyImport'},
            [pscustomobject]@{Label='Sysmon state and recent events';Description="installed=$($sysmon.Installed), services=$(@($sysmon.Services).Count)";Action='SysmonState'},
            [pscustomobject]@{Label='Install or update Sysmon';Description='Microsoft signature and exact XML configuration validation';Action='SysmonConfigure'},
            [pscustomobject]@{Label='Uninstall Sysmon';Description='Critical, non-reversible removal through the signed executable';Action='SysmonUninstall';Disabled=-not $sysmon.Installed},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Security baseline and telemetry' -Subtitle 'Catalog settings, LGPO, Sysmon, WDAC, Defender, and firewall remain independently governed' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'PolicyState'{Show-WinCareTextPage -Title 'Local Group Policy state' -Lines @("Path: $($policy.Path)","Exists: $($policy.Exists)","Machine policy: $($policy.MachinePolicy)","User policy: $($policy.UserPolicy)","Content hash: $($policy.Sha256)")}
            'PolicyImport'{$backup=Read-WinCareInput -Prompt 'LGPO backup directory';$exe=Read-WinCareInput -Prompt 'Microsoft LGPO.exe path';if($backup -and $exe){try{Invoke-WinCarePlanFromUi (New-WinCareLocalGroupPolicyImportPlan -BackupPath $backup -LgpoPath $exe)}catch{Show-WinCareMessage -Title 'LGPO import blocked' -Lines @($_.Exception.Message) -Kind Error}}}
            'SysmonState'{$lines=@("Installed: $($sysmon.Installed)",'','Services:')+@($sysmon.Services|ForEach-Object{"$($_.Name) | $($_.Status) | $($_.StartType)"})+@('','Drivers:')+@($sysmon.Drivers|ForEach-Object{"$($_.Name) | $($_.Status) | $($_.StartType)"})+@('','Recent events:')+@($sysmon.RecentEvents|ForEach-Object{"$($_.TimeCreated) | $($_.Id) | $($_.LevelDisplayName) | $([string]$_.Message -replace '\s+',' ')"});Show-WinCareTextPage -Title 'Sysmon state' -Lines $lines}
            'SysmonConfigure'{$exe=Read-WinCareInput -Prompt 'Microsoft Sysmon executable path';$config=Read-WinCareInput -Prompt 'Sysmon XML configuration path';if($exe -and $config){try{Invoke-WinCarePlanFromUi (New-WinCareSysmonConfigurationPlan -ExecutablePath $exe -ConfigPath $config)}catch{Show-WinCareMessage -Title 'Sysmon configuration blocked' -Lines @($_.Exception.Message) -Kind Error}}}
            'SysmonUninstall'{$exe=Read-WinCareInput -Prompt 'Microsoft Sysmon executable path';if($exe){try{Invoke-WinCarePlanFromUi (New-WinCareSysmonUninstallPlan -ExecutablePath $exe)}catch{Show-WinCareMessage -Title 'Sysmon uninstall blocked' -Lines @($_.Exception.Message) -Kind Error}}}
        }
    }
}

