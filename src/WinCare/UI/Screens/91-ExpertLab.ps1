function Format-WinCareSecurityControlRow {
    param($Item,$Width)
    "$(Limit-WinCareText $Item.Control 24) available=$($Item.Available) enabled=$($Item.Enabled) hash=$(Limit-WinCareText $Item.Hash 12)"
}

function Format-WinCareSecurityMaintenanceRow {
    param($Item,$Width)
    "$(Limit-WinCareText $Item.Control 24) $($Item.Status) expires=$($Item.ExpiresAt) id=$(Limit-WinCareText $Item.Id 12)"
}

function Format-WinCareInjectionSurfaceRow {
    param($Item,$Width)
    "[$($Item.Risk)] $(Limit-WinCareText $Item.Kind 30) $(Limit-WinCareText $Item.Target ([math]::Max(20,$Width-55))) remediable=$($Item.RemediationSupported)"
}

function Show-WinCarePageFileExpertScreen {
    while($true){
        $state=Get-WinCarePageFileConfiguration
        $subtitle=if($state){"Mode $($state.Mode) | RAM $($state.TotalPhysicalMemoryMB) MB | dump $($state.CrashDump.Mode) | restart required for changes"}else{'Page-file state unavailable'}
        $menu=@(
            [pscustomobject]@{Label='Configuration and usage';Description='Current automatic/custom settings, usage, and crash-dump dependency';Action='State'},
            [pscustomobject]@{Label='Evidence-based recommendation';Description='Explain the safest default and current warnings';Action='Recommend'},
            [pscustomobject]@{Label='Use system-managed paging';Description='Recommended general-purpose configuration; reversible';Action='System'},
            [pscustomobject]@{Label='Set measured custom paging';Description='One fixed local drive, explicit initial/maximum MB, reversible';Action='Custom'},
            [pscustomobject]@{Label='Disable configured page files';Description='Critical lab-only operation; policy and crash-dump gates apply';Action='Disable'},
            [pscustomobject]@{Label='Back';Description='Return to expert laboratory';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Page-file management' -Subtitle $subtitle -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'State'{
                    $lines=@("Mode: $($state.Mode)","System managed: $($state.AutomaticManagedPagefile)","Physical memory: $($state.TotalPhysicalMemoryMB) MB","Virtual memory: total $($state.TotalVirtualMemoryMB) MB, free $($state.FreeVirtualMemoryMB) MB","Crash dump: $($state.CrashDump.Mode), page file expected=$($state.CrashDump.PageFileRequired)",'','Configured settings:')
                    $lines+=@($state.Settings|ForEach-Object{"  $($_.Name): initial $($_.InitialSizeMB) MB, maximum $($_.MaximumSizeMB) MB"})
                    $lines+=@('','Current usage:')+@($state.Usage|ForEach-Object{"  $($_.Name): allocated $($_.AllocatedBaseSizeMB) MB, current $($_.CurrentUsageMB) MB, peak $($_.PeakUsageMB) MB"})
                    Show-WinCareTextPage -Title 'Page-file configuration' -Lines $lines
                }
                'Recommend'{
                    $r=Get-WinCarePageFileRecommendation
                    $lines=@("Recommended mode: $($r.RecommendedMode)","Current mode: $($r.CurrentMode)",'',$r.Rationale,'',$r.DisableRecommendation,'','Warnings:')+@($r.Warnings|ForEach-Object{"  - $_"})
                    Show-WinCareTextPage -Title 'Page-file recommendation' -Lines $lines
                }
                'System'{Invoke-WinCarePlanFromUi (New-WinCarePageFilePlan -Mode SystemManaged)}
                'Custom'{
                    $drive=(Read-WinCareInput -Prompt 'Fixed drive letter' -Default $env:SystemDrive).TrimEnd('\')
                    if($drive -notmatch '^[A-Za-z]:$'){throw 'Enter a drive letter such as C:.'}
                    $initial=Read-WinCareInput -Prompt 'Initial size MB' -Default '4096'
                    $maximum=Read-WinCareInput -Prompt 'Maximum size MB' -Default '16384'
                    if($initial -notmatch '^\d+$' -or $maximum -notmatch '^\d+$'){throw 'Page-file sizes must be whole megabytes.'}
                    $setting=[pscustomobject]@{Name="$drive\pagefile.sys";InitialSizeMB=[uint32]$initial;MaximumSizeMB=[uint32]$maximum}
                    Invoke-WinCarePlanFromUi (New-WinCarePageFilePlan -Mode Custom -Settings @($setting))
                }
                'Disable'{Invoke-WinCarePlanFromUi (New-WinCarePageFilePlan -Mode Disabled)}
            }
        }catch{Show-WinCareMessage -Title 'Page-file operation unavailable' -Lines @($_.Exception.Message) -Kind Error}
    }
}

function Show-WinCareSecurityMaintenanceExpertScreen {
    while($true){
        $menu=@(
            [pscustomobject]@{Label='Security-control posture';Description='Defender real-time protection, SmartScreen, UAC, and update services';Action='State'},
            [pscustomobject]@{Label='Active maintenance records';Description='Authenticated expiry and exact-restoration records';Action='Records'},
            [pscustomobject]@{Label='Temporary Defender maintenance';Description='Expiring real-time protection reduction; Tamper Protection is never bypassed';Action='Defender'},
            [pscustomobject]@{Label='Temporary SmartScreen maintenance';Description='Expiring shell SmartScreen reduction with exact registry restoration';Action='SmartScreen'},
            [pscustomobject]@{Label='Temporary Windows Update suspension';Description='Expiring exact service-state suspension and restoration';Action='Update'},
            [pscustomobject]@{Label='Temporary UAC disable';Description='Critical; disposable-lab declaration, restart, expiry, and exact restoration';Action='Uac'},
            [pscustomobject]@{Label='Restore a maintenance record now';Description='Restore exact authenticated pre-maintenance state';Action='Restore'},
            [pscustomobject]@{Label='Back';Description='Return to expert laboratory';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Expiring security maintenance' -Subtitle 'Not an optimization: reductions are policy-gated, time-bounded, journaled, and automatically restored' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'State'{
                    $items=@(Get-WinCareSecurityControlState)
                    $selected=Show-WinCareListSelector -Title 'Security-control posture' -Items $items -Formatter ${function:Format-WinCareSecurityControlRow} -SearchProperties @('Control','Enabled','Available')
                    if($selected){Show-WinCareTextPage -Title $selected.Control -Lines @(($selected|ConvertTo-Json -Depth 20).Split("`n"))}
                }
                'Records'{
                    $items=@(Get-WinCareSecurityMaintenanceRecord -IncludeRestored)
                    $selected=Show-WinCareListSelector -Title 'Security maintenance records' -Items $items -Formatter ${function:Format-WinCareSecurityMaintenanceRow} -SearchProperties @('Control','Status','Id','Reason') -EmptyMessage 'No maintenance records exist.'
                    if($selected){Show-WinCareTextPage -Title 'Maintenance record' -Lines @(($selected|ConvertTo-Json -Depth 30).Split("`n"))}
                }
                'Restore'{
                    $record=Show-WinCareListSelector -Title 'Restore security protection' -Items @(Get-WinCareSecurityMaintenanceRecord) -Formatter ${function:Format-WinCareSecurityMaintenanceRow} -SearchProperties @('Control','Status','Id','Reason') -EmptyMessage 'No active maintenance records exist.'
                    if($record){Invoke-WinCarePlanFromUi (New-WinCareSecurityMaintenanceRestorePlan -RecordId $record.Id)}
                }
                default{
                    $control=switch($choice.Action){'Defender'{'DefenderRealtime'}'SmartScreen'{'SmartScreenShell'}'Update'{'WindowsUpdateStack'}'Uac'{'Uac'}}
                    $minutes=Read-WinCareInput -Prompt 'Duration minutes' -Default ([string](Get-WinCareConfig 'SecurityMaintenanceDefaultMinutes'))
                    $reason=Read-WinCareInput -Prompt 'Concrete maintenance reason' -Default ''
                    $environment=if($control -eq 'Uac'){'DisposableLab'}else{'Workstation'}
                    if($minutes -notmatch '^\d+$'){throw 'Duration must be whole minutes.'}
                    Invoke-WinCarePlanFromUi (New-WinCareSecurityMaintenancePlan -Control $control -DurationMinutes ([int]$minutes) -Reason $reason -EnvironmentClass $environment)
                }
            }
        }catch{Show-WinCareMessage -Title 'Security maintenance unavailable' -Lines @($_.Exception.Message) -Kind Error}
    }
}

function Show-WinCareNetworkExperimentExpertScreen {
    while($true){
        $menu=@(
            [pscustomobject]@{Label='TCP global state';Description='Language-neutral netsh dump and exact state hash';Action='State'},
            [pscustomobject]@{Label='Measure current network';Description='DNS, ICMP, TCP connect success and latency samples';Action='Measure'},
            [pscustomobject]@{Label='Run measured TCP experiment';Description='One allowlisted setting, exact baseline, acceptance threshold, automatic rollback';Action='Experiment'},
            [pscustomobject]@{Label='Experiment history';Description='Authenticated accepted and rejected measurement records';Action='History'},
            [pscustomobject]@{Label='Back';Description='Return to expert laboratory';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Measured network experiments' -Subtitle 'No Registry folklore: change one documented TCP global token, measure it, and reject regressions' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'State'{$state=Get-WinCareTcpGlobalState;Show-WinCareTextPage -Title 'TCP global state' -Lines @(($state|ConvertTo-Json -Depth 10).Split("`n"))}
                'Measure'{
                    $hosts=(Read-WinCareInput -Prompt 'Comma-separated DNS hosts' -Default 'www.microsoft.com,www.cloudflare.com').Split(',',[StringSplitOptions]::RemoveEmptyEntries)|ForEach-Object{$_.Trim()}
                    $result=Measure-WinCareNetworkEndpoint -TargetHost $hosts -SampleCount (Get-WinCareConfig 'NetworkExperimentSampleCount') -TimeoutMilliseconds (Get-WinCareConfig 'NetworkExperimentTimeoutMilliseconds')
                    Show-WinCareTextPage -Title 'Network measurement' -Lines @(($result|ConvertTo-Json -Depth 20).Split("`n"))
                }
                'History'{
                    $rows=@(Get-WinCareNetworkExperimentHistory)
                    $lines=@($rows|ForEach-Object{"$($_.CreatedAt) | $($_.Setting) $($_.BeforeValue) -> $($_.RequestedValue) | accepted=$($_.Accepted) | improvement=$($_.ImprovementPercent)% | $($_.DecisionReason)"})
                    Show-WinCareTextPage -Title 'Network experiment history' -Lines $(if($lines.Count){$lines}else{@('No experiment history exists.')})
                }
                'Experiment'{
                    $setting=Read-WinCareInput -Prompt 'Setting (AutoTuningLevel/EcnCapability/ReceiveSideScaling/ReceiveSegmentCoalescing)' -Default 'ReceiveSideScaling'
                    $allowed=Get-WinCareTcpAllowedValue -Setting $setting
                    if(-not $allowed.Count){throw 'Unknown TCP setting.'}
                    $value=Read-WinCareInput -Prompt "Value ($($allowed -join '/'))" -Default ([string]$allowed[0])
                    $hosts=(Read-WinCareInput -Prompt 'Comma-separated measurement hosts' -Default 'www.microsoft.com,www.cloudflare.com').Split(',',[StringSplitOptions]::RemoveEmptyEntries)|ForEach-Object{$_.Trim()}
                    $minimum=Read-WinCareInput -Prompt 'Minimum latency improvement percent' -Default '0'
                    $maximum=Read-WinCareInput -Prompt 'Maximum success-rate regression percent' -Default '5'
                    Invoke-WinCarePlanFromUi (New-WinCareNetworkExperimentPlan -Setting $setting -Value $value -TargetHost $hosts -SampleCount (Get-WinCareConfig 'NetworkExperimentSampleCount') -TimeoutMilliseconds (Get-WinCareConfig 'NetworkExperimentTimeoutMilliseconds') -MinimumImprovementPercent ([double]$minimum) -MaximumRegressionPercent ([double]$maximum))
                }
            }
        }catch{Show-WinCareMessage -Title 'Network experiment unavailable' -Lines @($_.Exception.Message) -Kind Error}
    }
}

function Show-WinCareInstrumentationExpertScreen {
    while($true){
        $menu=@(
            [pscustomobject]@{Label='Process-interception surfaces';Description='AppInit, IFEO, SilentProcessExit, AppCert, LSA, Winlogon, and WMI inventory';Action='Surfaces'},
            [pscustomobject]@{Label='Process module inventory';Description='Enumerate loaded modules, signatures, publishers, base addresses, and sizes';Action='Modules'},
            [pscustomobject]@{Label='Sysmon remote-thread events';Description='Read bounded Event ID 8 evidence when Sysmon is installed';Action='RemoteThreads'},
            [pscustomobject]@{Label='Active ETW sessions';Description='Read local Event Tracing sessions';Action='Sessions'},
            [pscustomobject]@{Label='Capture supported ETW trace';Description='Bounded WPR capture under WinCare Reports; no process injection';Action='Capture'},
            [pscustomobject]@{Label='Quarantine exact interception surface';Description='Critical policy-gated removal of one supported inventoried registry surface';Action='Quarantine'},
            [pscustomobject]@{Label='Back';Description='Return to expert laboratory';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Process instrumentation and injection defense' -Subtitle 'Supported observation and defensive quarantine only; WinCare never allocates or writes into another process' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'Surfaces'{
                    $surface=Show-WinCareListSelector -Title 'Process-interception surfaces' -Items @(Get-WinCareInjectionSurface -Limit 5000) -Formatter ${function:Format-WinCareInjectionSurfaceRow} -SearchProperties @('Kind','Target','Path','Name','Risk','Evidence') -EmptyMessage 'No inventoried interception surfaces were found.'
                    if($surface){Show-WinCareTextPage -Title 'Interception surface' -Lines @(($surface|ConvertTo-Json -Depth 20).Split("`n"))}
                }
                'Modules'{
                    $pidText=Read-WinCareInput -Prompt 'Process ID' -Default ([string]$PID);if($pidText -notmatch '^\d+$'){throw 'Process ID must be numeric.'}
                    $lines=@(Get-WinCareProcessModuleInventory -ProcessId ([int]$pidText)|ForEach-Object{"$($_.ModuleName) | $($_.CompanyName) | signature=$($_.Signature) | $($_.FileName)"})
                    Show-WinCareTextPage -Title 'Process modules' -Lines $lines
                }
                'RemoteThreads'{
                    $events=@(Get-WinCareRemoteThreadEvent -MaxEvents (Get-WinCareConfig 'InstrumentationEventLimit'))
                    $lines=@($events|ForEach-Object{"$($_.TimeCreated) | record $($_.RecordId) | $($_.Message)"})
                    Show-WinCareTextPage -Title 'Sysmon remote-thread events' -Lines $(if($lines.Count){$lines}else{@('No events were available. Sysmon Event ID 8 must be configured separately.')})
                }
                'Sessions'{$lines=@(Get-WinCareEtwSession|ForEach-Object{$_.Session});Show-WinCareTextPage -Title 'Active ETW sessions' -Lines $(if($lines.Count){$lines}else{@('No ETW sessions could be enumerated.')})}
                'Capture'{
                    $profile=Read-WinCareInput -Prompt 'Profile (GeneralProfile/CPU/FileIO/Network)' -Default 'GeneralProfile'
                    $seconds=Read-WinCareInput -Prompt 'Duration seconds (5-300)' -Default '15'
                    $pidText=Read-WinCareInput -Prompt 'Optional process ID for module before/after inventory' -Default ''
                    if($seconds -notmatch '^\d+$'){throw 'Duration must be numeric.'}
                    $nullable=if($pidText){if($pidText -notmatch '^\d+$'){throw 'Process ID must be numeric.'};[Nullable[int]]([int]$pidText)}else{[Nullable[int]]$null}
                    Invoke-WinCarePlanFromUi (New-WinCareEtwCapturePlan -Profile $profile -DurationSeconds ([int]$seconds) -ProcessId $nullable)
                }
                'Quarantine'{
                    $surface=Show-WinCareListSelector -Title 'Select exact interception surface' -Items @(Get-WinCareInjectionSurface -Limit 5000|Where-Object RemediationSupported) -Formatter ${function:Format-WinCareInjectionSurfaceRow} -SearchProperties @('Kind','Target','Path','Name','Risk') -EmptyMessage 'No safely remediable surfaces were found.'
                    if($surface){Invoke-WinCarePlanFromUi (New-WinCareInjectionSurfaceQuarantinePlan -SurfaceId $surface.SurfaceId)}
                }
            }
        }catch{Show-WinCareMessage -Title 'Instrumentation operation unavailable' -Lines @($_.Exception.Message) -Kind Error}
    }
}

function Show-WinCareExpertLabScreen {
    while($true){
        $menu=@(
            [pscustomobject]@{Label='Page-file management';Description='State, crash-dump compatibility, recommendation, custom/system/disabled plans';Action='PageFile'},
            [pscustomobject]@{Label='Expiring security maintenance';Description='Defender, SmartScreen, UAC, and update controls with automatic exact restoration';Action='SecurityMaintenance'},
            [pscustomobject]@{Label='Measured network experiments';Description='Allowlisted TCP settings with baselines, thresholds, history, and automatic rollback';Action='NetworkExperiments'},
            [pscustomobject]@{Label='Process instrumentation and injection defense';Description='ETW, modules, Sysmon remote-thread evidence, interception inventory, defensive quarantine';Action='Instrumentation'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Expert laboratory' -Subtitle 'Authority-bearing operations are disabled by safe policy unless an administrator deliberately enables their narrow gates' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'PageFile'{Show-WinCarePageFileExpertScreen}
            'SecurityMaintenance'{Show-WinCareSecurityMaintenanceExpertScreen}
            'NetworkExperiments'{Show-WinCareNetworkExperimentExpertScreen}
            'Instrumentation'{Show-WinCareInstrumentationExpertScreen}
        }
    }
}

