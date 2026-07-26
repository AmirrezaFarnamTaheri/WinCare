function Show-WinCareShellHardwareStudioScreen {
    while($true){
        $items=@(
            [pscustomobject]@{Label='Capability cards';Description='Search the Shell and Hardware Studio capability catalog';Action='Cards'},
            [pscustomobject]@{Label='Hardware inventory';Description='Bounded hardware and firmware inventory with redacted identifiers';Action='Hardware'},
            [pscustomobject]@{Label='Monitor controls';Description='DDC/CI brightness and contrast with exact recovery';Action='Monitor'},
            [pscustomobject]@{Label='Window search';Description='Fuzzy search by process and title';Action='Windows'},
            [pscustomobject]@{Label='Explorer sessions';Description='Capture, save, and restore canonical local folders';Action='Explorer'},
            [pscustomobject]@{Label='UI Automation snapshot';Description='Bounded accessibility-tree inspection with text redaction';Action='Ui'},
            [pscustomobject]@{Label='AppX runtime and launch';Description='Process-package mapping and manifest-verified activation';Action='Appx'},
            [pscustomobject]@{Label='Archive inspection';Description='No-extraction ZIP, AppX, MSIX, and NuGet safety audit';Action='Archive'},
            [pscustomobject]@{Label='Servicing media';Description='Read-only DISM metadata for WIM, ESD, and SWM';Action='Servicing'},
            [pscustomobject]@{Label='Terminal environment';Description='Sanitized Windows Terminal and ConEmu profiles';Action='Terminal'},
            [pscustomobject]@{Label='GUI resource audit';Description='Find missing and unused WPF XAML resources';Action='Gui'},
            [pscustomobject]@{Label='Full-screen shell assessment';Description='Assess custom shell and Xbox device-form state';Action='Shell'},
            [pscustomobject]@{Label='Back';Description='Return to peer utilities';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Shell and Hardware Studio' -Subtitle 'Read-only discovery by default; mutations remain typed, previewed, policy-gated, and journaled' -Items $items
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'Cards'{$q=Read-WinCareInput -Prompt 'Optional search text' -Default '';$rows=Get-WinCareShellHardwareCapabilityCard -Query $q;Show-WinCareTextPage -Title 'Shell and Hardware capabilities' -Lines @($rows|ForEach-Object{"$($_.Title) [$($_.Risk)] - $($_.Summary)"})}
                'Hardware'{$item=Get-WinCareHardwareInventory;Show-WinCareTextPage -Title 'Hardware inventory' -Lines @(($item|ConvertTo-Json -Depth 8)-split "`r?`n");if((Read-WinCareInput -Prompt 'Export JSON report? (y/N)' -Default 'N') -match '^(?i)y(es)?$'){Invoke-WinCarePlanFromUi (New-WinCareHardwareReportPlan)}}
                'Monitor'{$rows=@(Get-WinCareMonitorControlState|Where-Object Supported);Show-WinCareTextPage -Title 'Monitor controls' -Lines @($rows|ForEach-Object{"$($_.DeviceName) #$($_.PhysicalIndex) $($_.Description) $($_.Feature)=$($_.Current)/$($_.Maximum)"});if($rows.Count -and (Read-WinCareInput -Prompt 'Change a VCP value? (y/N)' -Default 'N') -match '^(?i)y(es)?$'){$row=$rows[[int](Read-WinCareInput -Prompt 'Zero-based row index' -Default '0')];$value=[int](Read-WinCareInput -Prompt 'New raw value' -Default ([string]$row.Current));Invoke-WinCarePlanFromUi (New-WinCareMonitorControlPlan -DeviceName $row.DeviceName -PhysicalIndex $row.PhysicalIndex -Feature $row.Feature -Value $value)}}
                'Windows'{$q=Read-WinCareInput -Prompt 'Window search';Show-WinCareTextPage -Title 'Window search' -Lines @(Get-WinCareWindowSearch -Query $q|ForEach-Object{"score=$($_.Score) $($_.ProcessName): $($_.Title)"})}
                'Explorer'{$current=Get-WinCareExplorerSession;Show-WinCareTextPage -Title 'Open Explorer locations' -Lines @($current|ForEach-Object{$_.Path});$op=Read-WinCareInput -Prompt 'SAVE, RESTORE, or blank' -Default '';if($op -match '^(?i)save$'){Invoke-WinCarePlanFromUi (New-WinCareExplorerSessionSavePlan)}elseif($op -match '^(?i)restore$'){$id=Read-WinCareInput -Prompt 'Session ID';Invoke-WinCarePlanFromUi (New-WinCareExplorerSessionRestorePlan -Id $id)}}
                'Ui'{$pid=[int](Read-WinCareInput -Prompt 'Process ID, or 0 for desktop' -Default '0');$snap=Get-WinCareUiAutomationSnapshot -ProcessId $pid;Show-WinCareTextPage -Title 'UI Automation snapshot' -Lines @("Nodes: $($snap.NodeCount)","Truncated: $($snap.Truncated)",'')+@($snap.Nodes|Select-Object -First 150|ForEach-Object{"$($_.Depth) $($_.ControlType) $($_.AutomationId) $($_.Name)"})}
                'Appx'{$rows=Get-WinCareAppxRuntimeInventory;Show-WinCareTextPage -Title 'Packaged processes' -Lines @($rows|ForEach-Object{"$($_.ProcessId) $($_.ProcessName) $($_.PackageFullName)"});$aumid=Read-WinCareInput -Prompt 'AUMID to launch, or blank' -Default '';if($aumid){Invoke-WinCarePlanFromUi (New-WinCareAppxLaunchPlan -Aumid $aumid)}}
                'Archive'{$item=Get-WinCareArchiveInspection -LiteralPath (Read-WinCareInput -Prompt 'Archive path');Show-WinCareTextPage -Title 'Archive inspection' -Lines @("Safe: $($item.Safe)","Members: $($item.MemberCount)","Expanded bytes: $($item.ExpandedBytes)",'')+@($item.Violations)}
                'Servicing'{$item=Get-WinCareServicingMediaInfo -LiteralPath (Read-WinCareInput -Prompt 'WIM, ESD, or SWM path');Show-WinCareTextPage -Title 'Servicing media' -Lines @("SHA-256: $($item.Sha256)","Images: $(@($item.Images).Count)",'')+@($item.Images|ForEach-Object{"Index $($_.Index): $($_.Name) $($_.Architecture) $($_.Size)"})}
                'Terminal'{$item=Get-WinCareTerminalEnvironment;Show-WinCareTextPage -Title 'Terminal environment' -Lines @(($item|ConvertTo-Json -Depth 8)-split "`r?`n");if((Read-WinCareInput -Prompt 'Export sanitized environment? (y/N)' -Default 'N') -match '^(?i)y(es)?$'){Invoke-WinCarePlanFromUi (New-WinCareTerminalProfileExportPlan)}}
                'Gui'{$item=Get-WinCareGuiResourceAudit;Show-WinCareTextPage -Title 'GUI resource audit' -Lines @("Success: $($item.Success)","Definitions: $($item.DefinitionCount)","References: $($item.ReferenceCount)","Missing: $($item.Missing -join ', ')","Unused: $($item.Unused -join ', ')")}
                'Shell'{$item=Get-WinCareFullscreenShellAssessment;Show-WinCareTextPage -Title 'Full-screen shell assessment' -Lines @(($item|Format-List *|Out-String -Width 180)-split "`r?`n")}
            }
        }catch{Show-WinCareMessage -Title 'Shell and Hardware Studio failed' -Lines @($_.Exception.Message) -Kind Danger}
    }
}

