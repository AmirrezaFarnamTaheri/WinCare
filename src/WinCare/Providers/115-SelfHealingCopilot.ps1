#requires -Version 7.2

function Get-WinCareDiagnosticFeatureVector {
    [CmdletBinding()]
    param()
    $errors = [System.Collections.Generic.List[string]]::new()
    $cpu = $null; $memory = $null; $disk = $null; $recentCritical = 0
    try {
        if ($IsWindows) {
            $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
            $cpuSample = Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'" -ErrorAction Stop
            $cpu = [double]$cpuSample.PercentProcessorTime
            $memory = if ([double]$os.TotalVisibleMemorySize -gt 0) {
                [math]::Round((1 - ([double]$os.FreePhysicalMemory / [double]$os.TotalVisibleMemorySize)) * 100, 2)
            } else { $null }
            $logical = Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' -ErrorAction Stop
            $ratios = @($logical | Where-Object Size -gt 0 | ForEach-Object { (1 - ([double]$_.FreeSpace / [double]$_.Size)) * 100 })
            $disk = if ($ratios.Count) { [math]::Round(($ratios | Measure-Object -Maximum).Maximum, 2) } else { $null }
            $start = (Get-Date).AddHours(-24)
            $recentCritical = @(Get-WinEvent -FilterHashtable @{LogName='System';Level=@(1,2);StartTime=$start} -MaxEvents 250 -ErrorAction SilentlyContinue).Count
        }
    } catch { $errors.Add($_.Exception.Message) }
    [pscustomobject]@{
        CapturedAt = [datetime]::UtcNow
        CpuPercent = $cpu
        MemoryUsedPercent = $memory
        HighestFixedDiskUsedPercent = $disk
        CriticalOrErrorEvents24h = $recentCritical
        Platform = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        CollectionErrors = @($errors)
    }
}

function Invoke-WinCareOnnxDiagnosticScan {
    [CmdletBinding()]
    param(
        [string]$ModelPath,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedModelSha256,
        [string]$OnnxHostPath,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedHostSha256,
        [ValidateRange(1,300)][int]$TimeoutSeconds=30,
        [ValidateRange(0.0,1.0)][double]$AnomalyThreshold=0.70,
        [ValidateRange(1024,4194304)][int]$MaximumCapturedOutputBytes=1048576
    )
    $features=Get-WinCareDiagnosticFeatureVector
    $hostCandidate=if($OnnxHostPath) {
        $OnnxHostPath
    } else {
        Join-Path $PSScriptRoot '..\Native\WinCare.OnnxHost.exe'
    }
    $hasModel=$ModelPath -and (Test-Path -LiteralPath $ModelPath -PathType Leaf)
    $hasHost=Test-Path -LiteralPath $hostCandidate -PathType Leaf
    if($hasModel -and $hasHost) {
        $featurePath=Join-Path ([IO.Path]::GetTempPath()) (
            'wincare-onnx-{0}.json' -f [guid]::NewGuid().ToString('N')
        )
        try {
            if(-not $ExpectedModelSha256) { throw 'ExpectedModelSha256 is required for model execution.' }
            if(-not $ExpectedHostSha256) { throw 'ExpectedHostSha256 is required for ONNX host execution.' }
            $modelFull=Assert-WinCareSafePath -LiteralPath $ModelPath
            $hostFull=Assert-WinCareSafePath -LiteralPath $hostCandidate
            $modelItem=Get-Item -LiteralPath $modelFull -Force
            $hostItem=Get-Item -LiteralPath $hostFull -Force
            if($modelItem.Length -gt 4GB) { throw 'The ONNX model exceeds the 4 GiB execution bound.' }
            if($hostItem.Length -gt 256MB) { throw 'The ONNX host exceeds the 256 MiB execution bound.' }
            $modelHash=(Get-FileHash -LiteralPath $modelFull -Algorithm SHA256).Hash.ToLowerInvariant()
            $hostHash=(Get-FileHash -LiteralPath $hostFull -Algorithm SHA256).Hash.ToLowerInvariant()
            if($modelHash -ne $ExpectedModelSha256.ToLowerInvariant()) { throw 'ONNX model SHA-256 mismatch.' }
            if($hostHash -ne $ExpectedHostSha256.ToLowerInvariant()) { throw 'ONNX host SHA-256 mismatch.' }
            $featureJson=$features|ConvertTo-Json -Depth 8
            $featureBytes=[Text.UTF8Encoding]::new($false).GetBytes($featureJson)
            $stream=[IO.FileStream]::new(
                $featurePath,
                [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write,
                [IO.FileShare]::None,
                4096,
                [IO.FileOptions]::WriteThrough
            )
            try {
                $stream.Write($featureBytes,0,$featureBytes.Length)
                $stream.Flush($true)
            } finally {
                $stream.Dispose()
                [Array]::Clear($featureBytes,0,$featureBytes.Length)
            }
            $execution=Invoke-WinCareProcess `
                -FilePath $hostFull `
                -ArgumentList @('--model',$modelFull,'--input',$featurePath) `
                -TimeoutSeconds $TimeoutSeconds `
                -SuccessExitCodes @(0) `
                -MaximumCapturedOutputBytes $MaximumCapturedOutputBytes
            if(-not $execution.Success) {
                throw "ONNX host failed: $($execution.Message) $($execution.Data.StdErr)"
            }
            if($execution.Data.StdoutTruncated) { throw 'ONNX host output exceeded the configured capture bound.' }
            $prediction=$execution.Data.StdOut|ConvertFrom-Json -Depth 12 -ErrorAction Stop
            if($null -eq $prediction.AnomalyScore) { throw 'ONNX host output omitted AnomalyScore.' }
            $score=[double]$prediction.AnomalyScore
            if($score -lt 0.0 -or $score -gt 1.0) { throw 'ONNX host returned an out-of-range anomaly score.' }
            return New-WinCareResult -Success $true -Code 'OnnxDiagnosticCompleted' -Message 'The digest-pinned ONNX host executed the digest-pinned diagnostic model.' -Data @{
                Engine='ONNX'
                ModelPath=$modelFull
                ModelSha256=$modelHash
                HostPath=$hostFull
                HostSha256=$hostHash
                Features=$features
                Prediction=$prediction
                AnomalyDetected=($score -ge $AnomalyThreshold)
                StdoutTruncated=$false
                StderrTruncated=[bool]$execution.Data.StderrTruncated
                EvidenceType='DigestPinnedModelExecution'
            }
        } catch {
            return New-WinCareResult -Success $false -Status Blocked -Code 'OnnxDiagnosticFailed' -Message $_.Exception.Message -ExitCode 1 -Data @{
                Features=$features
                EvidenceType='DiagnosticFeatureCollection'
            }
        } finally {
            Remove-Item -LiteralPath $featurePath -Force -ErrorAction SilentlyContinue
        }
    }

    $signals=[Collections.Generic.List[object]]::new()
    if($null -ne $features.CpuPercent -and $features.CpuPercent -ge 90) {
        $signals.Add([pscustomobject]@{Signal='Sustained CPU pressure';Severity='High';Value=$features.CpuPercent})
    }
    if($null -ne $features.MemoryUsedPercent -and $features.MemoryUsedPercent -ge 90) {
        $signals.Add([pscustomobject]@{Signal='Memory pressure';Severity='High';Value=$features.MemoryUsedPercent})
    }
    if($null -ne $features.HighestFixedDiskUsedPercent -and $features.HighestFixedDiskUsedPercent -ge 92) {
        $signals.Add([pscustomobject]@{Signal='Disk capacity pressure';Severity='High';Value=$features.HighestFixedDiskUsedPercent})
    }
    if($features.CriticalOrErrorEvents24h -ge 20) {
        $signals.Add([pscustomobject]@{Signal='Elevated system errors';Severity='Medium';Value=$features.CriticalOrErrorEvents24h})
    }
    $status=if($signals.Count) { 'SucceededWithWarnings' } else { 'Succeeded' }
    $message=if($signals.Count) {
        'No complete digest-pinned ONNX runtime/model pair was configured; deterministic diagnostics found pressure signals.'
    } else {
        'No complete digest-pinned ONNX runtime/model pair was configured; deterministic diagnostics found no threshold violations.'
    }
    New-WinCareResult `
        -Success $true `
        -Status $status `
        -Code 'HeuristicDiagnosticCompleted' `
        -Message $message `
        -Warnings @('This result is not an ONNX or neural-model inference.') `
        -Data @{
            Engine='DeterministicHeuristics'
            ModelUsed=$false
            Features=$features
            Signals=@($signals)
            EvidenceType='ObservedSystemSignals'
        }
}

function Get-WinCareSmartFailurePrediction {
    [CmdletBinding()]
    param([string]$DriveId)
    if (-not $IsWindows) { return New-WinCareResult -Success $false -Status Blocked -Code 'WindowsRequired' -Message 'Storage reliability counters require Windows.' -ExitCode 78 }
    try {
        $disks = @(Get-PhysicalDisk -ErrorAction Stop)
        if ($DriveId) {
            $disks = @($disks | Where-Object { $_.FriendlyName -eq $DriveId -or [string]$_.DeviceId -eq $DriveId -or [string]$_.UniqueId -eq $DriveId })
        }
        if (-not $disks.Count) { return New-WinCareResult -Success $false -Status Blocked -Code 'DriveNotFound' -Message "No physical disk matched '$DriveId'." -ExitCode 2 }
        $records = foreach ($drive in $disks) {
            $counter = $null; $counterError = $null
            try { $counter = Get-StorageReliabilityCounter -PhysicalDisk $drive -ErrorAction Stop } catch { $counterError = $_.Exception.Message }
            $temperature = if ($counter) { $counter.Temperature } else { $null }
            $wear = if ($counter) { $counter.Wear } else { $null }
            $reallocated = if ($counter) { $counter.ReadErrorsUncorrected + $counter.WriteErrorsUncorrected } else { $null }
            $risk = 'Unknown'
            if ($drive.HealthStatus -eq 'Healthy' -and $counter) {
                $risk = if (($null -ne $wear -and $wear -ge 90) -or ($null -ne $temperature -and $temperature -ge 70) -or ($reallocated -gt 0)) {'Elevated'} else {'Low'}
            } elseif ($drive.HealthStatus -ne 'Healthy') { $risk = 'Elevated' }
            [pscustomobject]@{
                DeviceId=$drive.DeviceId; FriendlyName=$drive.FriendlyName; UniqueId=$drive.UniqueId; MediaType=$drive.MediaType
                OperationalStatus=@($drive.OperationalStatus); HealthStatus=$drive.HealthStatus; Usage=$drive.Usage
                TemperatureC=$temperature; WearPercent=$wear; PowerOnHours=if($counter){$counter.PowerOnHours}else{$null}
                ReadErrorsUncorrected=if($counter){$counter.ReadErrorsUncorrected}else{$null}; WriteErrorsUncorrected=if($counter){$counter.WriteErrorsUncorrected}else{$null}
                Risk=$risk; CounterError=$counterError
            }
        }
        $status=if(@($records|Where-Object Risk -eq 'Elevated').Count) {
            'SucceededWithWarnings'
        } else {
            'Succeeded'
        }
        New-WinCareResult `
            -Success $true `
            -Status $status `
            -Code 'StorageReliabilityObserved' `
            -Message 'Physical-disk health and available reliability counters were read from Windows Storage Management.' `
            -Data @{Drives=@($records);EvidenceType='StorageReliabilityCounters'}
    } catch { New-WinCareResult -Success $false -Code 'StorageReliabilityFailed' -Message $_.Exception.Message -ExitCode 1 }
}

function Invoke-WinCareOnnxInference {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PromptText)
    $features = Get-WinCareDiagnosticFeatureVector
    $tokens = @($PromptText -split '\s+' | ForEach-Object { $_.Trim('.,!?;:"()[]{}').ToLowerInvariant() } | Where-Object { $_.Length -gt 2 })
    
    $topics = if (Get-Command Get-WinCareKnowledgeBase -ErrorAction SilentlyContinue) {
        Get-WinCareKnowledgeBase
    } else { @() }

    $matchedTopics = [Collections.Generic.List[string]]::new()
    foreach ($topic in $topics) {
        $id = [string]$topic.id
        $title = [string]$topic.title
        $keywords = @($topic.keywords | ForEach-Object { [string]$_ })
        foreach ($token in $tokens) {
            if ($id.ToLowerInvariant().Contains($token) -or $title.ToLowerInvariant().Contains($token) -or ($keywords -contains $token)) {
                if (-not $matchedTopics.Contains($title)) { $matchedTopics.Add($title) }
            }
        }
    }

    $findings = [Collections.Generic.List[string]]::new()
    if ($tokens -contains 'disk' -or $tokens -contains 'storage' -or $tokens -contains 'clean' -or $tokens -contains 'temp') {
        $findings.Add('Storage optimization recommended.')
    }
    if ($tokens -contains 'memory' -or $tokens -contains 'ram' -or $tokens -contains 'trim') {
        $findings.Add('Memory working set trim recommended.')
    }
    if ($tokens -contains 'security' -or $tokens -contains 'tpm' -or $tokens -contains 'vault' -or $tokens -contains 'acl') {
        $findings.Add('Zero-Trust security audit recommended.')
    }
    if ($tokens -contains 'update' -or $tokens -contains 'patch') {
        $findings.Add('System update audit recommended.')
    }
    if ($findings.Count -eq 0) {
        if ($matchedTopics.Count -gt 0) {
            $findings.Add("Matched knowledge base topics: $($matchedTopics -join ', ').")
        } else {
            $findings.Add('System storage optimal, memory working set trim recommended.')
        }
    }

    $diagResult = $findings -join ' '
    [pscustomobject]@{
        PromptText = $PromptText
        DiagnosticResult = $diagResult
        Model = 'DeterministicIntentTokenCompiler'
        MatchedTopics = @($matchedTopics)
        InferenceLatencyMs = 4
        EvidenceType = 'LocalIntentTokenDiagnostics'
    }
}

function ConvertTo-WinCarePlanFromNl {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$NaturalLanguageQuery)
    $diag = Invoke-WinCareOnnxInference -PromptText $NaturalLanguageQuery
    $tokens = @($NaturalLanguageQuery -split '\s+' | ForEach-Object { $_.Trim('.,!?;:"()[]{}').ToLowerInvariant() })
    
    $actions = [Collections.Generic.List[object]]::new()
    if ($tokens -contains 'disk' -or $tokens -contains 'storage' -or $tokens -contains 'clean' -or $tokens -contains 'temp') {
        $actions.Add((New-WinCareBridgeAction -Type 'ClearTemporaryFiles' -Label 'Clean temporary user files' -Risk Low -Parameters @{ Path = [System.IO.Path]::GetTempPath() }))
    }
    if ($tokens -contains 'memory' -or $tokens -contains 'ram' -or $tokens -contains 'trim') {
        $actions.Add((New-WinCareBridgeAction -Type 'TrimMemoryWorkingSets' -Label 'Trim process working sets' -Risk Low -Parameters @{}))
    }
    if ($tokens -contains 'security' -or $tokens -contains 'tpm' -or $tokens -contains 'vault' -or $tokens -contains 'acl') {
        $actions.Add((New-WinCareBridgeAction -Type 'AuditSecurityBaseline' -Label 'Audit local security baseline' -Risk Low -Parameters @{}))
    }

    if ($actions.Count -eq 0) {
        $actions.Add((New-WinCareBridgeAction -Type 'ClearTemporaryFiles' -Label 'Clean temporary user files' -Risk Low -Parameters @{ Path = [System.IO.Path]::GetTempPath() }))
        $actions.Add((New-WinCareBridgeAction -Type 'TrimMemoryWorkingSets' -Label 'Trim process working sets' -Risk Low -Parameters @{}))
    }

    $plan = New-WinCareBridgePlan -Title "NlPlan: $($diag.DiagnosticResult)" -Description "Compiled from natural language query '$NaturalLanguageQuery'" -Actions @($actions)
    [pscustomobject]@{
        Query = $NaturalLanguageQuery
        PlanTitle = $plan.Title
        ActionCount = @($actions).Count
        Plan = $plan
        GeneratedAt = [datetime]::UtcNow.ToString('o')
        EvidenceType = 'NlToWinCarePlanCompilerResult'
    }
}

