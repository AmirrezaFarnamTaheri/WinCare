#requires -Version 7.2

function Predict-WinCareKernelBsodFault {
    [CmdletBinding()]
    param([ValidateRange(1,90)][int]$LookbackDays=7)

    if(-not $IsWindows) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'WindowsRequired' `
            -Message 'Kernel reliability evidence requires Windows.' -ExitCode 78
    }
    try {
        $start=(Get-Date).AddDays(-$LookbackDays)
        $bugchecks=@(Get-WinEvent -FilterHashtable @{
            LogName='System';Id=1001;StartTime=$start
        } -MaxEvents 200 -ErrorAction SilentlyContinue)
        $unexpected=@(Get-WinEvent -FilterHashtable @{
            LogName='System';Id=41;StartTime=$start
        } -MaxEvents 200 -ErrorAction SilentlyContinue)
        $driverErrors=@(Get-WinEvent -FilterHashtable @{
            LogName='System';Level=2;StartTime=$start
        } -MaxEvents 500 -ErrorAction SilentlyContinue|Where-Object {
            $_.ProviderName -match 'Kernel|WHEA|Display|stor|disk|nvme'
        })
        $score=[math]::Min(
            1.0,
            ($bugchecks.Count*0.30)+($unexpected.Count*0.08)+($driverErrors.Count*0.01)
        )
        $level=if($score -ge .7){'High'}elseif($score -ge .3){'Elevated'}else{'Low'}
        $top=@($driverErrors|Group-Object ProviderName|Sort-Object Count -Descending|
            Select-Object -First 10 Name,Count)
        New-WinCareResult -Success $true `
            -Status $(if($level -in @('High','Elevated')){'SucceededWithWarnings'}else{'Succeeded'}) `
            -Code 'KernelRiskSignalsAssessed' `
            -Message 'A transparent event-weighted reliability score was calculated; this is not a neural prediction.' `
            -Warnings @('The deterministic score is not a failure probability.') -Data @{
                Engine='DeterministicEventHeuristic'
                LookbackDays=$LookbackDays
                RiskScore=$score
                RiskLevel=$level
                BugcheckEvents=$bugchecks.Count
                UnexpectedShutdownEvents=$unexpected.Count
                KernelDriverErrors=$driverErrors.Count
                TopProviders=$top
                EvidenceType='WindowsEventLogSignals'
            }
    } catch {
        New-WinCareResult -Success $false -Code 'KernelRiskAssessmentFailed' `
            -Message $_.Exception.Message -ExitCode 1
    }
}

function Invoke-WinCarePageTableOptimization {
    [CmdletBinding(SupportsShouldProcess,ConfirmImpact='Medium')]
    param(
        [string[]]$ProcessName,
        [ValidateRange(1,100)][int]$MaximumProcesses=20
    )

    if(-not $IsWindows) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'WindowsRequired' -Message 'Working-set trimming requires Windows.' -ExitCode 78
    }
    if(-not (Initialize-WinCareNativeRuntime -RequiredTypes @('WinCare.Native.ProcessMemory'))) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'NativeProcessMemoryUnavailable' `
            -Message 'The source-built process-memory runtime is unavailable.' -ExitCode 78
    }
    $critical=@('Idle','System','Registry','smss','csrss','wininit','services','lsass','winlogon','dwm')
    $targets=if($ProcessName) {
        Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    } else {
        Get-Process|Where-Object WorkingSet64 -ge 256MB
    }
    $targets=@($targets|Where-Object {
        $_.Name -notin $critical -and $_.Id -ne $PID
    }|Sort-Object WorkingSet64 -Descending|Select-Object -First $MaximumProcesses)
    if(-not $targets.Count) {
        return New-WinCareResult -Success $true -Code 'NoWorkingSetTargets' `
            -Message 'No eligible high-working-set processes were found.' `
            -Data @{Processes=@();EvidenceType='ProcessInventory'}
    }
    if(-not $PSCmdlet.ShouldProcess(($targets.Name-join ', '),'Trim process working sets')) {
        return New-WinCareResult -Success $false -Status Cancelled `
            -Code 'Cancelled' -Message 'Working-set trimming was cancelled.' -ExitCode 125
    }
    $records=foreach($process in $targets) {
        $startTicks=$process.StartTime.ToUniversalTime().Ticks
        $result=[WinCare.Native.ProcessMemory]::TrimWorkingSet(
            [int]$process.Id,
            [long]$startTicks,
            [long]$process.WorkingSet64
        )
        [pscustomobject]@{
            Name=$process.Name
            Id=$process.Id
            BeforeBytes=[long]$result.BeforeBytes
            AfterBytes=[long]$result.AfterBytes
            ReducedBytes=[math]::Max(0,[long]$result.BeforeBytes-[long]$result.AfterBytes)
            Success=[bool]$result.Success
            Error=if($result.Success){$null}else{[string]$result.Message}
            Win32Error=[int]$result.Win32Error
        }
    }
    $success=@($records|Where-Object Success).Count -gt 0
    New-WinCareResult -Success $success `
        -Status $(if(@($records|Where-Object { -not $_.Success }).Count){
            'SucceededWithWarnings'
        }elseif($success){'Succeeded'}else{'Failed'}) `
        -Code $(if($success){'WorkingSetsTrimmed'}else{'WorkingSetTrimFailed'}) `
        -Message $(if($success){
            'Eligible process working sets were trimmed and re-measured.'
        }else{'No process working set could be trimmed.'}) `
        -Warnings @('This trims working sets; it does not optimize page tables.') `
        -ExitCode $(if($success){0}else{1}) -Data @{
            Processes=@($records)
            TotalReducedBytes=($records|Measure-Object ReducedBytes -Sum).Sum
            EvidenceType='BeforeAfterWorkingSetMeasurement'
        }
}
