#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: Local telemetry capture, validation, history, and export planning.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function Get-WinCareTelemetryRoot {
    [CmdletBinding()]
    param()
    Get-WinCareBridgeStateRoot 'Telemetry'
}
function Get-WinCareTelemetrySnapshot {
    [CmdletBinding()]
    param([int]$TopProcessCount=5)
    $cpu=0.0
    if($IsWindows -and (Get-Command Get-Counter -ErrorAction SilentlyContinue)){try{$cpu=[math]::Round((Get-Counter '\Processor(_Total)\% Processor Time' -MaxSamples 1 -ErrorAction Stop).CounterSamples[0].CookedValue,2)}catch{}}
    if($cpu -eq 0){try{$cpu=[math]::Round((Get-CimInstance Win32_Processor -ErrorAction Stop|Measure-Object LoadPercentage -Average).Average,2)}catch{}}
    $memory=Get-WinCareMemoryInternals;$disk=Get-WinCareDiskByteCounters
    $top=@(Get-Process -ErrorAction SilentlyContinue|Sort-Object CPU -Descending|Select-Object -First $TopProcessCount Id,ProcessName,CPU,WorkingSet64,StartTime)
    [pscustomobject]@{Timestamp=[datetime]::UtcNow.ToString('o');CpuUsagePercent=$cpu;MemoryTotalBytes=[long]$memory.TotalBytes;MemoryUsedBytes=[long]($memory.TotalBytes-$memory.FreeBytes);MemoryFreeBytes=[long]$memory.FreeBytes;DiskReadBytesPerSec=[double]$disk.ReadBytesPerSec;DiskWriteBytesPerSec=[double]$disk.WriteBytesPerSec;TopProcesses=$top;PendingRestart=Get-WinCarePendingRestart}
}
function Get-WinCareTelemetrySeries {
    [CmdletBinding()]
    param(
        [string]$Name='',
        [ValidateRange(1,3600)][int]$DurationSeconds=30,
        [ValidateRange(1,60)][int]$IntervalSeconds=2,
        [ValidateRange(0,50)][int]$TopProcessCount=5,
        [string]$OperationId='',
        [switch]$Capture
    )
    if(-not $Capture){return @(Get-WinCareTelemetryHistory -Name $Name -Limit 1000)}
    $samples=[Collections.Generic.List[object]]::new()
    $deadline=[datetime]::UtcNow.AddSeconds([math]::Min(3600,$DurationSeconds))
    do{
        if($OperationId -and (Test-WinCareOperationCancellationRequested -OperationId $OperationId)){throw [OperationCanceledException]::new('Telemetry capture was cancelled.')}
        $samples.Add((Get-WinCareTelemetrySnapshot -TopProcessCount $TopProcessCount))
        if([datetime]::UtcNow -lt $deadline){
            $remaining=[math]::Max(0,[math]::Ceiling(($deadline-[datetime]::UtcNow).TotalSeconds))
            for($i=0;$i -lt [math]::Min($IntervalSeconds,$remaining);$i++){
                if($OperationId -and (Test-WinCareOperationCancellationRequested -OperationId $OperationId)){throw [OperationCanceledException]::new('Telemetry capture was cancelled.')}
                Start-Sleep -Seconds 1
            }
        }
    }while([datetime]::UtcNow -lt $deadline)
    return @($samples)
}
function Get-WinCareTelemetryHistory {
    [CmdletBinding()]
    param([string]$Name='',[int]$Limit=1000)
    $root=Get-WinCareTelemetryRoot;$files=if($Name){@(Get-Item -LiteralPath (Join-Path $root ($Name+'.json')) -ErrorAction SilentlyContinue)}else{@(Get-ChildItem -LiteralPath $root -Filter '*.json' -File -ErrorAction SilentlyContinue|Sort-Object LastWriteTimeUtc -Descending)}
    $output=[Collections.Generic.List[object]]::new()
    foreach($file in $files){$record=Read-WinCareBridgeJson -LiteralPath $file.FullName;if($record){foreach($sample in @($record.Samples)){$output.Add((ConvertTo-WinCarePublicTelemetrySample $sample))}}}
    return @($output|Sort-Object Timestamp -Descending|Select-Object -First $Limit)
}
function ConvertTo-WinCarePublicTelemetrySample {
    [CmdletBinding()]
    param([Parameter(Mandatory,ValueFromPipeline)][object]$InputObject)
    process{[pscustomobject]@{Timestamp=[string](Get-WinCareBridgeProperty $InputObject 'Timestamp' '');CpuUsagePercent=[double](Get-WinCareBridgeProperty $InputObject 'CpuUsagePercent' 0);MemoryTotalBytes=[long](Get-WinCareBridgeProperty $InputObject 'MemoryTotalBytes' 0);MemoryUsedBytes=[long](Get-WinCareBridgeProperty $InputObject 'MemoryUsedBytes' 0);MemoryFreeBytes=[long](Get-WinCareBridgeProperty $InputObject 'MemoryFreeBytes' 0);DiskReadBytesPerSec=[double](Get-WinCareBridgeProperty $InputObject 'DiskReadBytesPerSec' 0);DiskWriteBytesPerSec=[double](Get-WinCareBridgeProperty $InputObject 'DiskWriteBytesPerSec' 0);TopProcesses=@(Get-WinCareBridgeProperty $InputObject 'TopProcesses' @())}}
}
function Test-WinCareTelemetrySeries {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Series)
    $samples=@(Get-WinCareBridgeProperty $Series 'Samples' $Series)
    if($samples.Count -eq 0){return $false}
    foreach($sample in $samples){$stamp=[string](Get-WinCareBridgeProperty $sample 'Timestamp' '');$parsed=[datetime]::MinValue;if(-not [datetime]::TryParse($stamp,[ref]$parsed)){return $false};$cpu=[double](Get-WinCareBridgeProperty $sample 'CpuUsagePercent' -1);if($cpu -lt 0 -or $cpu -gt 100){return $false}}
    return $true
}
function New-WinCareTelemetryCapturePlan {
    [CmdletBinding()]
    param([string]$Name=('capture-'+[datetime]::UtcNow.ToString('yyyyMMdd-HHmmss')),[ValidateRange(1,3600)][int]$DurationSeconds=30,[ValidateRange(1,60)][int]$IntervalSeconds=2,[ValidateRange(0,50)][int]$TopProcessCount=5)
    $historyHash='';$historyPath=Join-Path (Get-WinCareTelemetryRoot) ($Name+'.json');if(Test-Path $historyPath){$historyHash=(Get-FileHash -LiteralPath $historyPath -Algorithm SHA256).Hash.ToLowerInvariant()}
    $action=New-WinCareBridgeAction -Type 'CaptureTelemetrySeries' -Label "Capture telemetry series $Name" -Risk ReadOnly -Parameters @{Name=$Name;DurationSeconds=$DurationSeconds;IntervalSeconds=$IntervalSeconds;TopProcessCount=$TopProcessCount;ExpectedHistoryHash=$historyHash} -TimeoutSeconds ($DurationSeconds+60) -Verification 'A schema-valid telemetry series must be written and re-read from the telemetry store.'
    New-WinCareBridgePlan -Title 'Capture telemetry' -Description 'Samples CPU, memory, disk, process, and restart state using local operating-system counters.' -Actions @($action) -RollbackOnFailure $false
}
function New-WinCareTelemetryExportPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Destination,[Alias('SeriesId')][string]$Name='')
    $history=@(Get-WinCareTelemetryHistory -Name $Name -Limit 100000)
    $json=$history|ConvertTo-Json -Depth 20
    $bytes=[Text.Encoding]::UTF8.GetBytes($json)
    if($bytes.Length -gt 1MB){throw 'Telemetry export exceeds the one-MiB limit.'}
    $content=[Convert]::ToBase64String($bytes)
    $allowed=@((Split-Path -Parent ([IO.Path]::GetFullPath($Destination))))
    $beforeHash=if(Test-Path -LiteralPath $Destination){Get-WinCareSha256 -LiteralPath $Destination}else{''}
    $action=New-WinCareBridgeAction -Type 'WriteManagedFile' -Label 'Export telemetry history' -Risk Low -Parameters @{Path=[IO.Path]::GetFullPath($Destination);ContentBase64=$content;ExpectedBeforeHash=$beforeHash;AllowedRoots=$allowed} -Reversible $true -Verification 'Exported telemetry JSON must be byte-identical to the planned payload.'
    New-WinCareBridgePlan -Title 'Export telemetry' -Actions @($action)
}
function Invoke-WinCareCaptureTelemetrySeriesAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action,[string]$OperationId='')
    $p=Get-WinCareActionParameters $Action
    $name=[string]$p.Name;$duration=[int]$p.DurationSeconds;$interval=[int]$p.IntervalSeconds;$count=[int]$p.TopProcessCount
    if($name -notmatch '^[A-Za-z0-9._-]{1,80}$'){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InvalidTelemetryName' -Message 'Telemetry capture name is invalid.' -ExitCode 22}
    if($duration -lt 1 -or $duration -gt 3600){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'TelemetryDurationOutOfRange' -Message 'Telemetry duration must be within 1..3600 seconds.' -ExitCode 22}
    $path=Join-Path (Get-WinCareTelemetryRoot) ($name+'.json')
    $existingHash=if(Test-Path -LiteralPath $path){Get-WinCareSha256 -LiteralPath $path}else{''}
    if([string]$p.ExpectedHistoryHash -ne $existingHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'TelemetryHistoryChanged' -Message 'Telemetry history changed after preview.' -ExitCode 74}
    try{
        $samples=@(Get-WinCareTelemetrySeries -Name $name -DurationSeconds $duration -IntervalSeconds $interval -TopProcessCount $count -OperationId $OperationId -Capture)
        $series=[ordered]@{SchemaVersion=1;Name=$name;CapturedAt=[datetime]::UtcNow.ToString('o');DurationSeconds=$duration;IntervalSeconds=$interval;Samples=@($samples)}
        if(-not (Test-WinCareTelemetrySeries $series)){return New-WinCareBridgeResult -Success $false -Code 'TelemetryValidationFailed' -Message 'Captured telemetry failed schema validation.' -ExitCode 74}
        $encoded=[Text.Encoding]::UTF8.GetBytes(($series|ConvertTo-Json -Depth 20 -Compress))
        if($encoded.Length -gt 1MB){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'TelemetrySeriesTooLarge' -Message 'Telemetry series exceeds the one-MiB limit.' -ExitCode 27 -Data @{Bytes=$encoded.Length;LimitBytes=1MB}}
        Write-WinCareBridgeJson -LiteralPath $path -InputObject $series
        $roundtrip=Read-WinCareBridgeJson -LiteralPath $path
        $success=$null -ne $roundtrip -and (Test-WinCareTelemetrySeries $roundtrip)
        $historyLimit=[int](Get-WinCareConfig 'TelemetryHistoryLimit')
        $all=@(Get-ChildItem -LiteralPath (Get-WinCareTelemetryRoot) -Filter '*.json' -File -ErrorAction SilentlyContinue|Sort-Object LastWriteTimeUtc -Descending)
        foreach($stale in @($all|Select-Object -Skip $historyLimit)){Remove-Item -LiteralPath $stale.FullName -Force -ErrorAction SilentlyContinue}
        New-WinCareBridgeResult -Success $success -Code $(if($success){'TelemetryCaptured'}else{'TelemetryPersistenceFailed'}) -Message $(if($success){'Telemetry was captured, persisted, bounded, and verified.'}else{'Telemetry persistence verification failed.'}) -Data @{Path=$path;SampleCount=$samples.Count;Sha256=if($success){Get-WinCareSha256 -LiteralPath $path}else{$null};HistoryLimit=$historyLimit;EvidenceType='PersistedTelemetrySeries'} -ExitCode $(if($success){0}else{74})
    }catch [OperationCanceledException]{
        New-WinCareBridgeResult -Success $false -Status Cancelled -Code 'TelemetryCaptureCancelled' -Message $_.Exception.Message -ExitCode 1223 -Data @{Path=$path;EvidenceType='CancellationObservation'}
    }catch{
        New-WinCareBridgeResult -Success $false -Code 'TelemetryCaptureFailed' -Message $_.Exception.Message -ExitCode 1
    }
}
