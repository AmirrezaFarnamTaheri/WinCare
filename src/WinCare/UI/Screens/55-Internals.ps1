function Format-WinCareProcessInternalRow { param($Item,$Width); "$(Limit-WinCareText ($Item.Name+' ['+$Item.Id+']') ([math]::Max(24,$Width-35))) $(Limit-WinCareText (Format-WinCareBytes $Item.WorkingSetBytes) 12) $(Limit-WinCareText $Item.Priority 12) $(Limit-WinCareText $Item.Signature 10)" }
function Show-WinCareInternalsScreen {
    while($true){
        $menu=@(
            [pscustomobject]@{Label='Process internals';Description='Memory, CPU, handles, signatures, priority, and affinity';Action='Processes'},
            [pscustomobject]@{Label='Processor topology';Description='Sockets, physical cores, logical processors, virtualization';Action='Cpu'},
            [pscustomobject]@{Label='Memory state';Description='Physical, virtual, and page-file pressure without fake RAM cleaning';Action='Memory'},
            [pscustomobject]@{Label='Credential providers';Description='Provider CLSIDs, servers, and signature status';Action='Credentials'},
            [pscustomobject]@{Label='AppContainer capabilities';Description='Package manifest capabilities and signature kind';Action='Appx'},
            [pscustomobject]@{Label='Windows licensing summary';Description='Read-only slmgr output';Action='License'},
            [pscustomobject]@{Label='Bounded CPU diagnostic';Description='1-60 seconds; diagnostic load, not optimization';Action='Stress'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Windows internals' -Subtitle 'Read-only by default; process tuning is reversible and explicitly approved' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'Processes'{$item=Show-WinCareListSelector -Title 'Process internals' -Items @(Get-WinCareProcessInternals -Top 200) -Formatter ${function:Format-WinCareProcessInternalRow} -SearchProperties @('Name','Id','Path','Signature','Priority');if($item){$answer=Read-WinCareInput -Prompt 'New priority (Idle/BelowNormal/Normal/AboveNormal/High, blank to inspect)' -Default '';if($answer -in @('Idle','BelowNormal','Normal','AboveNormal','High')){Invoke-WinCarePlanFromUi (New-WinCareProcessTuningPlan -ProcessId $item.Id -Priority $answer)}}}
            'Cpu'{$c=Get-WinCareProcessorTopology;$lines=@("Sockets: $($c.Sockets)","Physical cores: $($c.PhysicalCores)","Logical processors: $($c.LogicalProcessors)","Firmware virtualization: $($c.VirtualizationFirmwareEnabled)","SLAT: $($c.SecondLevelAddressTranslation)",'')+@($c.Processors|ForEach-Object{"$($_.Name) | $($_.NumberOfCores) cores | $($_.NumberOfLogicalProcessors) logical | load $($_.LoadPercentage)%"});Show-WinCareTextPage -Title 'Processor topology' -Lines $lines}
            'Memory'{$m=Get-WinCareMemoryInternals;$lines=@("Total physical: $(Format-WinCareBytes $m.TotalPhysicalBytes)","Free physical: $(Format-WinCareBytes $m.FreePhysicalBytes)","Total virtual: $(Format-WinCareBytes $m.TotalVirtualBytes)","Free virtual: $(Format-WinCareBytes $m.FreeVirtualBytes)",'',$m.Rationale,'')+@($m.PageFiles|ForEach-Object{"$($_.Name): allocated $($_.AllocatedBaseSize) MB, used $($_.CurrentUsage) MB, peak $($_.PeakUsage) MB"});Show-WinCareTextPage -Title 'Memory state' -Lines $lines}
            'Credentials'{$lines=@(Get-WinCareCredentialProvider|ForEach-Object{"$($_.Kind) | $($_.Clsid) | $($_.Name) | $($_.Server) | signature $($_.Signature)"});Show-WinCareTextPage -Title 'Credential providers' -Lines $(if($lines.Count){$lines}else{@('No credential providers could be enumerated.')})}
            'Appx'{$query=Read-WinCareInput -Prompt 'Package name filter' -Default '';$lines=@(Get-WinCareAppContainerCapability -PackageName $query|ForEach-Object{"$($_.Name) | $($_.SignatureKind) | $($_.Capabilities -join ', ')"});Show-WinCareTextPage -Title 'AppContainer capabilities' -Lines $(if($lines.Count){$lines}else{@('No matching packages.')})}
            'License'{$r=Get-WinCareLicensingSummary;Show-WinCareResult -Result $r -Title 'Windows licensing'}
            'Stress'{$seconds=Read-WinCareInput -Prompt 'Duration seconds (1-60)' -Default '5';$threads=Read-WinCareInput -Prompt 'Worker threads' -Default ([string][math]::Min(2,[Environment]::ProcessorCount));if($seconds -match '^\d+$' -and $threads -match '^\d+$'){$r=Measure-WinCareCpuDiagnostic -Seconds ([int]$seconds) -Threads ([int]$threads);Show-WinCareTextPage -Title 'CPU diagnostic complete' -Lines @("Duration: $($r.DurationSeconds)s","Threads: $($r.Threads)","Completed: $($r.Completed)",$r.Purpose)}}
        }
    }
}

