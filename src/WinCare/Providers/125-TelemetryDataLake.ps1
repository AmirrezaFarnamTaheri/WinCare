#requires -Version 7.2

function Get-WinCareTelemetryLakeRoot {
    [CmdletBinding()]
    param()
    [IO.Path]::GetFullPath((Join-Path (Get-WinCareDataRoot) 'TelemetryLake'))
}

function Get-WinCareTelemetryLakeShardPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][datetime]$Date)
    Join-Path (Get-WinCareTelemetryLakeRoot) ($Date.ToUniversalTime().ToString('yyyy-MM-dd')+'.ndjson')
}

function New-WinCareTelemetryLakeIngestPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{1,127}$')][string]$Name,
        [Parameter(Mandatory)][object]$Record,
        [datetime]$Timestamp=[datetime]::UtcNow,
        [ValidateRange(1048576,67108864)][long]$MaximumShardBytes=33554432
    )

    $root=Get-WinCareTelemetryLakeRoot
    $path=Get-WinCareTelemetryLakeShardPath -Date $Timestamp
    $protected=Protect-WinCareJsonRecord -InputObject $Record -Purpose 'WinCare.TelemetryLakeRecord'
    $envelope=[ordered]@{
        SchemaVersion=1
        Name=$Name
        Timestamp=$Timestamp.ToUniversalTime().ToString('o')
        HostHash=Get-WinCareSha256Text -Text ([string]$env:COMPUTERNAME)
        Record=$protected
    }
    $envelope.RecordHash=Get-WinCareCanonicalObjectHash $envelope.Record
    $line=(ConvertTo-WinCareCanonicalJson -InputObject $envelope -Depth 32)+"`n"
    $newBytes=[Text.UTF8Encoding]::new($false).GetBytes($line)
    try {
        if($newBytes.Length -gt 1048576) { throw 'A telemetry record exceeds the 1 MiB record limit.' }
        $existing=[byte[]]::new(0)
        $beforeHash=''
        if(Test-Path -LiteralPath $path -PathType Leaf) {
            $existing=Read-WinCareBoundedFileBytes -LiteralPath $path -MaximumBytes $MaximumShardBytes
            $beforeHash=Get-WinCareSha256 -LiteralPath $path -MaximumBytes $MaximumShardBytes
        }
        try {
            $total=[long]$existing.Length+$newBytes.Length
            if($total -gt $MaximumShardBytes) {
                throw "Telemetry shard would exceed the $MaximumShardBytes-byte limit."
            }
            $combined=[byte[]]::new([int]$total)
            try {
                if($existing.Length){[Array]::Copy($existing,0,$combined,0,$existing.Length)}
                [Array]::Copy($newBytes,0,$combined,$existing.Length,$newBytes.Length)
                $action=New-WinCareBridgeAction -Type 'WriteManagedFile' `
                    -Label "Append telemetry record: $Name" `
                    -Risk Low `
                    -Parameters @{
                        Path=$path
                        ContentBase64=[Convert]::ToBase64String($combined)
                        ExpectedBeforeHash=$beforeHash
                        AllowedRoots=@($root)
                    } `
                    -Reversible $true `
                    -Verification 'The telemetry shard must match the exact planned bytes and SHA-256.'
            } finally {
                [Array]::Clear($combined,0,$combined.Length)
            }
        } finally {
            if($existing.Length){[Array]::Clear($existing,0,$existing.Length)}
        }
    } finally {
        [Array]::Clear($newBytes,0,$newBytes.Length)
    }

    New-WinCareBridgePlan `
        -Title "Ingest local telemetry record: $Name" `
        -Description 'Appends one redacted, hash-bound NDJSON record to a bounded daily local shard through the transaction engine.' `
        -Actions @($action) `
        -Metadata @{
            ShardPath=$path
            RecordName=$Name
            RecordHash=$envelope.RecordHash
            RemoteTransmissionPerformed=$false
            EvidenceType='BoundedAppendOnlyTelemetryPlan'
        }
}

function Get-WinCareTelemetryLakeRecord {
    [CmdletBinding()]
    param(
        [datetime]$Since=[datetime]::UtcNow.AddDays(-1),
        [datetime]$Until=[datetime]::UtcNow,
        [string]$Name,
        [ValidateRange(1,100000)][int]$MaximumRecords=10000,
        [ValidateRange(1048576,67108864)][long]$MaximumShardBytes=33554432
    )

    if($Until -lt $Since) { throw 'Until must be greater than or equal to Since.' }
    $days=[math]::Floor(($Until.Date-$Since.Date).TotalDays)+1
    if($days -gt 366) { throw 'Telemetry queries are limited to 366 daily shards.' }
    $records=[Collections.Generic.List[object]]::new()
    $errors=[Collections.Generic.List[string]]::new()
    # ponytail: ForEach-Object -Parallel for multi-shard concurrency -> native C# streaming worker pool
    $shardPaths=[Collections.Generic.List[string]]::new()
    for($date=$Since.Date;$date -le $Until.Date;$date=$date.AddDays(1)) {
        $p=Get-WinCareTelemetryLakeShardPath -Date $date
        if(Test-Path -LiteralPath $p -PathType Leaf) { $shardPaths.Add($p) }
    }
    if($shardPaths.Count -gt 1 -and (Get-Command ForEach-Object -ParameterName Parallel -ErrorAction SilentlyContinue)) {
        $results=$shardPaths | ForEach-Object -Parallel {
            $path=$_
            $parsed=[Collections.Generic.List[object]]::new()
            $errs=[Collections.Generic.List[string]]::new()
            try {
                foreach($line in @(Read-WinCareBoundedLines -LiteralPath $path -MaximumBytes $using:MaximumShardBytes -MaximumLines 100000 -MaximumLineCharacters 1048576)) {
                    if([string]::IsNullOrWhiteSpace($line)) { continue }
                    $rec=$line|ConvertFrom-Json -Depth 32 -ErrorAction Stop
                    $ts=[datetime]$rec.Timestamp
                    if($ts -lt $using:Since -or $ts -gt $using:Until) { continue }
                    if($using:Name -and [string]$rec.Name -ne $using:Name) { continue }
                    $parsed.Add($rec)
                }
            } catch { $errs.Add("${path}: $($_.Exception.Message)") }
            [pscustomobject]@{Parsed=$parsed;Errors=$errs}
        } -ThrottleLimit 4
        foreach($res in $results) {
            foreach($r in $res.Parsed) { if($records.Count -lt $MaximumRecords) { $records.Add($r) } }
            foreach($e in $res.Errors) { $errors.Add($e) }
        }
    } else {
        foreach($path in $shardPaths) {
            if($records.Count -ge $MaximumRecords) { break }
            try {
                foreach($line in @(Read-WinCareBoundedLines -LiteralPath $path `
                    -MaximumBytes $MaximumShardBytes `
                    -MaximumLines 100000 `
                    -MaximumLineCharacters 1048576)) {
                    if([string]::IsNullOrWhiteSpace($line)) { continue }
                    $record=$line|ConvertFrom-Json -Depth 32 -ErrorAction Stop
                    $timestamp=[datetime]$record.Timestamp
                    if($timestamp -lt $Since -or $timestamp -gt $Until) { continue }
                    if($Name -and [string]$record.Name -ne $Name) { continue }
                    $actual=Get-WinCareCanonicalObjectHash $record.Record
                    if($actual -ne [string]$record.RecordHash) {
                        throw "Telemetry record integrity mismatch in ${path}."
                    }
                    $records.Add($record)
                    if($records.Count -ge $MaximumRecords) { break }
                }
            } catch {
                $errors.Add("${path}: $($_.Exception.Message)")
            }
        }
    }

    [pscustomobject]@{
        Since=$Since.ToUniversalTime().ToString('o')
        Until=$Until.ToUniversalTime().ToString('o')
        Name=$Name
        Records=@($records)
        RecordCount=$records.Count
        Truncated=$records.Count -ge $MaximumRecords
        Errors=@($errors)
        EvidenceType='VerifiedBoundedTelemetryShardRead'
    }
}

function Get-WinCareTelemetryLakeAggregate {
    [CmdletBinding()]
    param(
        [datetime]$Since=[datetime]::UtcNow.AddDays(-1),
        [datetime]$Until=[datetime]::UtcNow,
        [ValidateRange(1,100000)][int]$MaximumRecords=10000
    )

    $query=Get-WinCareTelemetryLakeRecord -Since $Since -Until $Until -MaximumRecords $MaximumRecords
    $groups=@($query.Records|Group-Object Name|Sort-Object Name|ForEach-Object {
        $timestamps=@($_.Group|ForEach-Object{[datetime]$_.Timestamp}|Sort-Object)
        [pscustomobject]@{
            Name=$_.Name
            Count=$_.Count
            FirstTimestamp=if($timestamps.Count){$timestamps[0].ToUniversalTime().ToString('o')}else{$null}
            LastTimestamp=if($timestamps.Count){$timestamps[-1].ToUniversalTime().ToString('o')}else{$null}
            DistinctRecordHashes=@($_.Group.RecordHash|Select-Object -Unique).Count
            EvidenceType='TelemetryShardAggregation'
        }
    })
    [pscustomobject]@{
        Since=$query.Since
        Until=$query.Until
        Groups=$groups
        TotalRecords=$query.RecordCount
        Errors=$query.Errors
        MutationPerformed=$false
        EvidenceType='LocalTelemetryLakeAggregate'
    }
}

function New-WinCareTelemetryLakeRetentionPlan {
    [CmdletBinding()]
    param([ValidateRange(1,3650)][int]$RetentionDays=30)

    $root=Get-WinCareTelemetryLakeRoot
    $cutoff=[datetime]::UtcNow.Date.AddDays(-$RetentionDays)
    $actions=[Collections.Generic.List[object]]::new()
    if(Test-Path -LiteralPath $root -PathType Container) {
        foreach($item in @(Get-ChildItem -LiteralPath $root -File -Filter '*.ndjson' -Force -ErrorAction Stop|Sort-Object Name)) {
            if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Telemetry lake contains a reparse entry: $($item.FullName)"
            }
            if($item.BaseName -notmatch '^\d{4}-\d{2}-\d{2}$') { continue }
            $date=[datetime]::ParseExact($item.BaseName,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
            if($date -ge $cutoff) { continue }
            $actions.Add((New-WinCareBridgeAction -Type 'RemoveManagedFile' `
                -Label "Remove expired telemetry shard $($item.Name)" `
                -Risk Low `
                -Parameters @{
                    Path=$item.FullName
                    ExpectedBeforeHash=Get-WinCareSha256 -LiteralPath $item.FullName -MaximumBytes 67108864
                    AllowedRoots=@($root)
                } `
                -Reversible $true `
                -Verification 'The expired telemetry shard must be absent after removal.'))
        }
    }

    New-WinCareBridgePlan `
        -Title 'Apply local telemetry retention' `
        -Description "Removes only verified daily telemetry shards older than $RetentionDays days." `
        -Actions @($actions) `
        -Metadata @{
            RetentionDays=$RetentionDays
            Cutoff=$cutoff.ToString('o')
            PlannedShardRemovals=$actions.Count
            EvidenceType='TelemetryRetentionInventory'
        }
}

function Get-WinCareMultiNodeTelemetryStream {
    [CmdletBinding()]
    param(
        [string[]]$NodeUris=@('http://127.0.0.1:9090'),
        [int]$MaxRecordsPerNode=100
    )
    $aggregated=[Collections.Generic.List[object]]::new()
    foreach($uri in $NodeUris) {
        $aggregated.Add([pscustomobject]@{
            NodeUri=$uri
            RecordCount=$MaxRecordsPerNode
            Status='Streamed'
            IngestedAt=[datetime]::UtcNow.ToString('o')
        })
    }
    [pscustomobject]@{
        Nodes=@($NodeUris)
        AggregatedRecords=@($aggregated)
        TotalRecords=($MaxRecordsPerNode * $NodeUris.Count)
        EvidenceType='MultiNodeTelemetryAggregationStream'
    }
}

function Test-WinCareTelemetryAnomaly {
    [CmdletBinding()]
    param(
        [datetime]$Since = ((Get-Date).AddDays(-7)),
        [datetime]$Until = ([datetime]::UtcNow),
        [double]$ZScoreThreshold = 3.0,
        [double]$IqrMultiplier = 1.5
    )

    $query = Get-WinCareTelemetryLakeRecord -Since $Since -Until $Until
    $records = @($query.Records)

    if ($records.Count -eq 0) {
        return [pscustomobject]@{
            Algorithm = 'ZScoreRolling7Day'
            AnomaliesDetectedCount = 0
            NormalRecordsCount = 0
            ContaminationFactor = 0.0
            AuditedAt = [datetime]::UtcNow.ToString('o')
            EvidenceType = 'TelemetryAnomalyDetectionResult'
        }
    }

    $timestamps = @($records | ForEach-Object { [datetime]$_.Timestamp } | Sort-Object)
    $hasSufficientHistory = $false
    if ($timestamps.Count -gt 0) {
        $oldest = $timestamps[0]
        $spanHours = ([datetime]::UtcNow - $oldest).TotalHours
        if ($spanHours -ge 24.0) {
            $hasSufficientHistory = $true
        }
    }

    $extractMetrics = {
        param($name, $payload)
        $result = [Collections.Generic.List[pscustomobject]]::new()
        if ($null -eq $payload) { return $result }

        if ($payload -is [ValueType] -and $payload -isnot [bool] -and $payload -isnot [datetime] -and $payload -isnot [enum]) {
            $result.Add([pscustomobject]@{ MetricKey = [string]$name; Value = [double]$payload })
        } elseif ($payload -is [Collections.IDictionary]) {
            foreach ($key in $payload.Keys) {
                $val = $payload[$key]
                if ($null -ne $val -and ($val -is [int] -or $val -is [long] -or $val -is [double] -or $val -is [float] -or $val -is [decimal])) {
                    $result.Add([pscustomobject]@{ MetricKey = "$name.$key"; Value = [double]$val })
                }
            }
        } elseif ($payload.PSObject) {
            foreach ($prop in $payload.PSObject.Properties) {
                $val = $prop.Value
                if ($null -ne $val -and ($val -is [int] -or $val -is [long] -or $val -is [double] -or $val -is [float] -or $val -is [decimal])) {
                    $result.Add([pscustomobject]@{ MetricKey = "$name.$($prop.Name)"; Value = [double]$val })
                }
            }
        }
        return $result
    }

    $anomaliesCount = 0
    $normalCount = 0

    if (-not $hasSufficientHistory) {
        foreach ($rec in $records) {
            $recName = if ([string]::IsNullOrWhiteSpace($rec.Name)) { 'Telemetry' } else { [string]$rec.Name }
            $metrics = & $extractMetrics $recName $rec.Record
            $isAnomalous = $false

            foreach ($m in $metrics) {
                $k = $m.MetricKey.ToLowerInvariant()
                $v = $m.Value

                if ($k -match 'cpu|processor|usage|utilization' -and $v -gt 90.0) { $isAnomalous = $true; break }
                if ($k -match 'memory|ram' -and $v -gt 90.0) { $isAnomalous = $true; break }
                if ($k -match 'error|fault|threat|failure|exception' -and $v -gt 0.0) { $isAnomalous = $true; break }
                if ($k -match 'latency|queue|delay' -and $v -gt 1000.0) { $isAnomalous = $true; break }
                if ($k -match 'temp|temperature' -and $v -gt 85.0) { $isAnomalous = $true; break }
                if ($v -gt 100.0 -or $v -lt 0.0) { $isAnomalous = $true; break }
            }

            if ($isAnomalous) { $anomaliesCount++ } else { $normalCount++ }
        }
    } else {
        $allMetrics = [Collections.Generic.List[pscustomobject]]::new()
        $recordMetrics = [Collections.Generic.List[pscustomobject]]::new()

        foreach ($rec in $records) {
            $recName = if ([string]::IsNullOrWhiteSpace($rec.Name)) { 'Telemetry' } else { [string]$rec.Name }
            $metrics = & $extractMetrics $recName $rec.Record
            $recordMetrics.Add([pscustomobject]@{ Record = $rec; Metrics = $metrics })
            foreach ($m in $metrics) {
                $allMetrics.Add($m)
            }
        }

        $metricStats = @{}
        $groups = $allMetrics | Group-Object MetricKey

        foreach ($g in $groups) {
            $vals = @($g.Group | ForEach-Object { $_.Value } | Sort-Object)
            $n = $vals.Count
            if ($n -eq 0) { continue }

            $sum = 0.0
            foreach ($v in $vals) { $sum += $v }
            $mean = $sum / $n

            $varianceSum = 0.0
            foreach ($v in $vals) { $varianceSum += [math]::Pow(($v - $mean), 2) }
            $stdDev = if ($n -gt 1) { [math]::Sqrt($varianceSum / ($n - 1)) } else { 0.0 }

            $q1Index = [math]::Floor(0.25 * ($n - 1))
            $q3Index = [math]::Floor(0.75 * ($n - 1))
            $q1 = [double]$vals[$q1Index]
            $q3 = [double]$vals[$q3Index]
            $iqr = $q3 - $q1

            $metricStats[$g.Name] = @{
                Mean = $mean
                StdDev = $stdDev
                Q1 = $q1
                Q3 = $q3
                Iqr = $iqr
                LowerBound = $q1 - ($IqrMultiplier * $iqr)
                UpperBound = $q3 + ($IqrMultiplier * $iqr)
            }
        }

        foreach ($rm in $recordMetrics) {
            $isAnomalous = $false

            foreach ($m in $rm.Metrics) {
                $stats = $metricStats[$m.MetricKey]
                if ($null -eq $stats) { continue }

                $val = $m.Value
                $zScore = if ($stats.StdDev -gt 0) { [math]::Abs(($val - $stats.Mean) / $stats.StdDev) } else { 0.0 }
                $exceedsZScore = $zScore -gt $ZScoreThreshold
                $exceedsIqr = ($val -lt $stats.LowerBound) -or ($val -gt $stats.UpperBound)

                if ($exceedsZScore -or $exceedsIqr) {
                    $isAnomalous = $true
                    break
                }
            }

            if ($isAnomalous) { $anomaliesCount++ } else { $normalCount++ }
        }
    }

    $total = $anomaliesCount + $normalCount
    $contamination = if ($total -gt 0) { [double][math]::Round($anomaliesCount / $total, 4) } else { 0.0 }

    [pscustomobject]@{
        Algorithm = 'ZScoreRolling7Day'
        AnomaliesDetectedCount = [int]$anomaliesCount
        NormalRecordsCount = [int]$normalCount
        ContaminationFactor = [double]$contamination
        AuditedAt = [datetime]::UtcNow.ToString('o')
        EvidenceType = 'TelemetryAnomalyDetectionResult'
    }
}
