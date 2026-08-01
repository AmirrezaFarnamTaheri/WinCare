#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: Bounded TCP state measurement, experiments, verification, and rollback.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function Get-WinCareTcpSettingTokenMap {
    [ordered]@{
        AutoTuningLevel='autotuninglevel'
        EcnCapability='ecncapability'
        ReceiveSideScaling='rss'
        ReceiveSegmentCoalescing='rsc'
    }
}

function Get-WinCareTcpAllowedValue {
    [CmdletBinding()]param([Parameter(Mandatory)][Alias('Setting')][string]$Key)
    switch($Key){
        'AutoTuningLevel'{@('disabled','highlyrestricted','restricted','normal','experimental')}
        'EcnCapability'{@('disabled','enabled','default')}
        'ReceiveSideScaling'{@('disabled','enabled','default')}
        'ReceiveSegmentCoalescing'{@('disabled','enabled','default')}
        default{@()}
    }
}

function Get-WinCareTcpGlobalState {
    [CmdletBinding()]param()
    $state=[ordered]@{Supported=$false;
        AutoTuningLevel='Unknown';
        EcnCapability='Unknown';
        ReceiveSideScaling='Unknown';
        ReceiveSegmentCoalescing='Unknown';
        Raw=@();
        CapturedAt=[datetime]::UtcNow.ToString('o')}
    if(-not $IsWindows){return [pscustomobject]$state}
    $result=Invoke-WinCareBridgeProcess -FilePath 'netsh.exe' -ArgumentList @('interface','tcp','show','global') -TimeoutSeconds 30
    if(-not $result.Success){$state.Error=$result.Message;return [pscustomobject]$state}
    $state.Supported=$true;$lines=@($result.Data.StdOut -split "`r?`n");$state.Raw=$lines
    foreach($line in $lines){
        if($line -match '(?i)Receive Window Auto-Tuning Level\s*:\s*(\S+)'){$state.AutoTuningLevel=$Matches[1].ToLowerInvariant()}
        elseif($line -match '(?i)ECN Capability\s*:\s*(\S+)'){$state.EcnCapability=$Matches[1].ToLowerInvariant()}
        elseif($line -match '(?i)Receive-Side Scaling State\s*:\s*(\S+)'){$state.ReceiveSideScaling=$Matches[1].ToLowerInvariant()}
        elseif($line -match '(?i)Receive Segment Coalescing State\s*:\s*(\S+)'){$state.ReceiveSegmentCoalescing=$Matches[1].ToLowerInvariant()}
    }
    [pscustomobject]$state
}

function Measure-WinCareNetworkEndpoint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Alias('TargetHost')][ValidateNotNullOrEmpty()][string[]]$Target,
        [ValidateRange(1,20)][int]$SampleCount=4,
        [ValidateRange(100,10000)][int]$TimeoutMilliseconds=1500,
        [ValidateRange(1,65535)][int]$Port=443
    )
    $endpoints=@(Resolve-WinCareNetworkProbeTarget -Targets $Target)
    if($endpoints.Count -eq 0){throw 'At least one explicit or configured network measurement target is required.'}
    foreach ($endpoint in $endpoints) {
        $samples=[Collections.Generic.List[double]]::new();$failures=0;$resolved=@()
        try{$resolved=@([Net.Dns]::GetHostAddresses($endpoint)|ForEach-Object{$_.IPAddressToString})}catch{}
        for($i=0;$i -lt $SampleCount;$i++){
            try{$ping=[Net.NetworkInformation.Ping]::new();try{$reply=$ping.Send($endpoint,$TimeoutMilliseconds);if($reply.Status -eq 'Success'){$samples.Add([double]$reply.RoundtripTime)}else{$failures++}}finally{$ping.Dispose()}}catch{$failures++}
        }
        $tcpMs=$null
        try{
            $client=[Net.Sockets.TcpClient]::new();$sw=[Diagnostics.Stopwatch]::StartNew();$task=$client.ConnectAsync($endpoint,$Port)
            if($task.Wait($TimeoutMilliseconds) -and $client.Connected){$sw.Stop();$tcpMs=[math]::Round($sw.Elapsed.TotalMilliseconds,2)}
            $client.Dispose()
        }catch{}
        [pscustomobject]@{Target=$endpoint;ResolvedAddresses=$resolved;SampleCount=$SampleCount;SuccessCount=$samples.Count;FailureCount=$failures;LatencyMs=if($samples.Count){[math]::Round(($samples|Measure-Object -Average).Average,2)}else{$null};MinimumLatencyMs=if($samples.Count){($samples|Measure-Object -Minimum).Minimum}else{$null};MaximumLatencyMs=if($samples.Count){($samples|Measure-Object -Maximum).Maximum}else{$null};TcpConnectMs=$tcpMs;Port=$Port;Reachable=($samples.Count -gt 0 -or $null -ne $tcpMs);CapturedAt=[datetime]::UtcNow.ToString('o');EvidenceType='DnsPingAndTcpObservation'}
    }
}
function Get-WinCareNetworkExperimentHistory {
    [CmdletBinding()]
    param([int]$Limit=100)
    $path=Join-Path (Get-WinCareBridgeStateRoot 'Network') 'experiments.json';$records=@(Read-WinCareBridgeJson -LiteralPath $path -Default @());return @($records|Sort-Object FinishedAt -Descending|Select-Object -First $Limit)
}
function New-WinCareNetworkExperimentPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Setting,[Parameter(Mandatory)][object]$Value,[Alias('TargetHost')][string[]]$TargetHosts,[int]$SampleCount=4,[int]$TimeoutMilliseconds=1500,[double]$MinimumImprovementPercent=0,[double]$MaximumRegressionPercent=10)
    $TargetHosts=@(Resolve-WinCareNetworkProbeTarget -Targets $TargetHosts)
    if($TargetHosts.Count -eq 0){throw 'Network experiments require explicit TargetHosts or configured NetworkExperimentTargets; WinCare will not silently probe public services.'}
    $current=Get-WinCareTcpGlobalState;$before=Get-WinCareBridgeProperty $current $Setting $null
    if($null -eq $before){throw "Unknown or unavailable TCP setting: $Setting"}
    $baseline=@($TargetHosts|ForEach-Object{Measure-WinCareNetworkEndpoint -Target $_ -SampleCount $SampleCount -TimeoutMilliseconds $TimeoutMilliseconds})
    $action=New-WinCareBridgeAction -Type 'RunNetworkExperiment' -Label "Experiment with TCP setting $Setting" -Risk High -RequiresAdmin $true -Reversible $true -Parameters @{Setting=$Setting;Value=$Value;BeforeHash=if(Get-Command Get-WinCareCanonicalObjectHash -ErrorAction SilentlyContinue){Get-WinCareCanonicalObjectHash $current}else{''};BeforeValue=$before;TargetHosts=@($TargetHosts);SampleCount=$SampleCount;TimeoutMilliseconds=$TimeoutMilliseconds;MinimumImprovementPercent=$MinimumImprovementPercent;MaximumRegressionPercent=$MaximumRegressionPercent;Baseline=$baseline} -Compensator @{Type='RestoreTcpGlobalSetting';Parameters=@{Setting=$Setting;Value=$before;ExpectedCurrentValue=$Value};RequiresAdmin=$true} -TimeoutSeconds 1800 -Verification 'The requested netsh TCP value must be observed and endpoint measurements must remain inside the declared regression budget.'
    New-WinCareBridgePlan -Title "TCP network experiment: $Setting" -Description 'Applies one bounded TCP setting, measures configured endpoints, and restores the prior value on excessive regression.' -Actions @($action)
}
function Invoke-WinCareRunNetworkExperimentAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action,[string]$OperationId='')
    $blocked=Test-WinCareWindowsRequired 'TCP network experiments';if($blocked){return $blocked}
    $p=Get-WinCareActionParameters $Action;$setting=[string]$p.Setting;$value=[string]$p.Value;$map=Get-WinCareTcpSettingTokenMap
    if(-not $map.Contains($setting)){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'UnsupportedTcpSetting' -Message "Unsupported TCP setting: $setting" -ExitCode 22}
    $allowed=@(Get-WinCareTcpAllowedValue $setting);if($allowed.Count -gt 0 -and $value -notin @($allowed|ForEach-Object{[string]$_})){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InvalidTcpValue' -Message "Invalid value '$value' for $setting." -ExitCode 22}
    $applied=$false
    $restoreResult=$null
    try{
        if($OperationId -and (Test-WinCareOperationCancellationRequested -OperationId $OperationId)){throw [OperationCanceledException]::new('Network experiment was cancelled before mutation.')}
        $apply=Invoke-WinCareBridgeProcess -FilePath 'netsh.exe' -ArgumentList @('interface','tcp','set','global',("{0}={1}" -f $map[$setting],$value)) -TimeoutSeconds 60 -RequireAdmin
        if(-not $apply.Success){return $apply}
        $applied=$true
        $observed=Get-WinCareTcpGlobalState;$actual=[string](Get-WinCareBridgeProperty $observed $setting '')
        if($actual -ne $value.ToLowerInvariant()){throw "TCP setting verification failed; observed '$actual'."}
        $after=[Collections.Generic.List[object]]::new()
        foreach($target in @($p.TargetHosts)){
            if($OperationId -and (Test-WinCareOperationCancellationRequested -OperationId $OperationId)){throw [OperationCanceledException]::new('Network experiment was cancelled during measurement.')}
            $after.Add((Measure-WinCareNetworkEndpoint -Target ([string]$target) -SampleCount ([int]$p.SampleCount) -TimeoutMilliseconds ([int]$p.TimeoutMilliseconds)))
        }
        $baseline=@($p.Baseline);$beforeValues=@($baseline|Where-Object{$null -ne $_.LatencyMs}|ForEach-Object{[double]$_.LatencyMs});$afterValues=@($after|Where-Object{$null -ne $_.LatencyMs}|ForEach-Object{[double]$_.LatencyMs})
        if($beforeValues.Count -eq 0 -or $afterValues.Count -eq 0){throw 'Network experiment was inconclusive because comparable endpoint measurements were unavailable.'}
        $beforeAvg=[double]($beforeValues|Measure-Object -Average).Average;$afterAvg=[double]($afterValues|Measure-Object -Average).Average
        if($beforeAvg -le 0){throw 'Network experiment baseline was invalid.'}
        $change=[math]::Round((($beforeAvg-$afterAvg)/$beforeAvg)*100,2)
        if($change -lt -[double]$p.MaximumRegressionPercent){throw "Network experiment regressed latency by $([math]::Abs($change)) percent."}
        if($change -lt [double]$p.MinimumImprovementPercent){throw "Network experiment improvement of $change percent did not meet the minimum of $($p.MinimumImprovementPercent) percent."}
        $record=[ordered]@{Id=[guid]::NewGuid().ToString('N');OperationId=$OperationId;Setting=$setting;BeforeValue=$p.BeforeValue;AppliedValue=$value;Baseline=$baseline;After=@($after);ImprovementPercent=$change;FinishedAt=[datetime]::UtcNow.ToString('o')}
        $historyPath=Join-Path (Get-WinCareBridgeStateRoot 'Network') 'experiments.json';$history=@(Read-WinCareBridgeJson -LiteralPath $historyPath -Default @());Write-WinCareBridgeJson -LiteralPath $historyPath -InputObject @($history+$record)
        return New-WinCareBridgeResult -Success $true -Code 'NetworkExperimentVerified' -Message 'TCP setting was applied and endpoint measurements met the declared improvement and regression budgets.' -Data @{Record=$record;ObservedState=$observed;EvidenceType='MeasuredNetworkExperiment';Compensator=@{Type='RestoreTcpGlobalSetting';Parameters=@{Setting=$setting;Value=[string]$p.BeforeValue;ExpectedCurrentValue=$value};RequiresAdmin=$true}}
    }catch{
        $error=$_.Exception
        if($applied){
            # Automatic rollback is mandatory for cancellation, failure, inconclusive evidence, regression, or insufficient improvement.
            $restoreResult=Invoke-WinCareRestoreTcpGlobalSettingAction -Action ([pscustomobject]@{Parameters=@{Setting=$setting;Value=[string]$p.BeforeValue;ExpectedCurrentValue=$value}})
        }
        $cancelled=$error -is [OperationCanceledException]
        $rollbackOk=(-not $applied) -or ($restoreResult -and $restoreResult.Success)
        $code=if($cancelled){if($rollbackOk){'NetworkExperimentCancelledRolledBack'}else{'NetworkExperimentCancellationRollbackFailed'}}elseif($rollbackOk){'NetworkExperimentFailedRolledBack'}else{'NetworkExperimentRollbackFailed'}
        New-WinCareBridgeResult -Success $false -Status $(if($cancelled){'Cancelled'}else{'Failed'}) -Code $code -Message $error.Message -ExitCode $(if($cancelled){1223}else{74}) -Data @{Restore=$restoreResult;EvidenceType='AutomaticRollback'}
    }
}
function Invoke-WinCareRestoreTcpGlobalSettingAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'TCP setting restore';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;$map=Get-WinCareTcpSettingTokenMap;$setting=[string]$p.Setting;$value=[string]$p.Value
    if(-not $map.Contains($setting)){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'UnsupportedTcpSetting' -Message "Unsupported TCP setting: $setting" -ExitCode 22}
    $result=Invoke-WinCareBridgeProcess -FilePath 'netsh.exe' -ArgumentList @('interface','tcp','set','global',("{0}={1}" -f $map[$setting],$value)) -TimeoutSeconds 60 -RequireAdmin;if(-not $result.Success){return $result}
    $state=Get-WinCareTcpGlobalState;$actual=[string](Get-WinCareBridgeProperty $state $setting '');$success=$actual -eq $value.ToLowerInvariant()
    New-WinCareBridgeResult -Success $success -Code $(if($success){'TcpSettingRestored'}else{'TcpSettingRestoreVerificationFailed'}) -Message $(if($success){'TCP setting was restored and verified.'}else{"TCP setting restore could not be verified; observed '$actual'."}) -Data @{Setting=$setting;Requested=$value;Observed=$actual;EvidenceType='ObservedTcpGlobalState'} -ExitCode $(if($success){0}else{74})
}
