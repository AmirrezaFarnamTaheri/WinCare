<#
.SYNOPSIS
Builds and executes bounded process working-set trim actions. It does not claim system RAM optimization.
#>
function Get-WinCareMemoryTrimCandidate {
    [CmdletBinding()]
    param(
        [ValidateRange(16,1048576)][int]$MinWorkingSetMB=256,
        [ValidateRange(1,128)][int]$MaximumProcesses=32,
        [int[]]$ProcessId=@()
    )

    $protected=@(
        'System','Idle','Registry','Memory Compression','csrss','wininit',
        'services','lsass','smss','winlogon','dwm'
    )
    $processes=if($ProcessId.Count) {
        @(foreach($id in $ProcessId) {
            Get-Process -Id $id -ErrorAction SilentlyContinue
        })
    } else {
        @(Get-Process -ErrorAction SilentlyContinue|Where-Object {
            $_.WorkingSet64 -ge ([long]$MinWorkingSetMB*1MB)
        }|Sort-Object WorkingSet64 -Descending|Select-Object -First $MaximumProcesses)
    }
    @($processes|Where-Object {
        $_.Id -ne $PID -and $_.ProcessName -notin $protected
    }|ForEach-Object {
        [pscustomobject]@{
            ProcessId=$_.Id
            ProcessName=$_.ProcessName
            StartTimeUtc=$(try{$_.StartTime.ToUniversalTime().ToString('o')}catch{$null})
            StartTimeUtcTicks=$(try{$_.StartTime.ToUniversalTime().Ticks}catch{0L})
            WorkingSetBytes=[long]$_.WorkingSet64
            SessionId=$(try{$_.SessionId}catch{$null})
            EvidenceType='ProcessObservation'
        }
    })
}

function New-WinCareMemoryOptimizationPlan {
    [CmdletBinding()]
    param(
        [ValidateRange(16,1048576)][int]$MinWorkingSetMB=256,
        [ValidateRange(1,128)][int]$MaximumProcesses=32,
        [int[]]$ProcessId=@()
    )

    if(-not $IsWindows) { throw 'Working-set trimming is supported only on Windows.' }
    $candidates=@(Get-WinCareMemoryTrimCandidate `
        -MinWorkingSetMB $MinWorkingSetMB `
        -MaximumProcesses $MaximumProcesses `
        -ProcessId $ProcessId)
    $actions=foreach($candidate in $candidates) {
        New-WinCareAction -Type 'TrimProcessWorkingSet' `
            -Label "Trim working set: $($candidate.ProcessName) [$($candidate.ProcessId)]" `
            -Risk Low -Parameters @{
                ProcessId=[int]$candidate.ProcessId
                ProcessStartTime=[string]$candidate.StartTimeUtc
                ProcessStartTimeUtcTicks=[long]$candidate.StartTimeUtcTicks
                MinimumWorkingSetBytes=[long]$candidate.WorkingSetBytes
            } -TimeoutSeconds 30 `
            -Verification 'The native call must target the same process instance and record before/after observations.'
    }
    New-WinCarePlan -Title 'Trim selected process working sets' `
        -Description 'This is a temporary working-set trim; Windows may repopulate pages immediately.' `
        -Actions @($actions) -RollbackOnFailure $false `
        -Metadata @{Candidates=$candidates;EvidenceType='ProcessIdentityAndWorkingSetObservation'}
}

function Invoke-WinCareMemoryOptimization {
    [CmdletBinding()]
    param(
        [ValidateRange(16,1048576)][int]$MinWorkingSetMB=256,
        [ValidateRange(1,128)][int]$MaximumProcesses=32,
        [int[]]$ProcessId=@(),
        [switch]$Apply,
        [switch]$Approved,
        [switch]$PreviewOnly
    )

    try {
        $plan=New-WinCareMemoryOptimizationPlan `
            -MinWorkingSetMB $MinWorkingSetMB `
            -MaximumProcesses $MaximumProcesses `
            -ProcessId $ProcessId
    } catch {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'WorkingSetTrimUnsupported' -Message $_.Exception.Message -ExitCode 78
    }
    if(-not $Apply -and -not $PreviewOnly) { return $plan }
    Invoke-WinCarePlan -Plan $plan -Approved:$Approved -PreviewOnly:$PreviewOnly
}

function Invoke-WinCareTrimProcessWorkingSetAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)

    if(-not $IsWindows) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'WorkingSetTrimUnsupported' `
            -Message 'Working-set trimming is supported only on Windows.' -ExitCode 78
    }
    $parameters=$Action.Parameters
    $process=Get-Process -Id ([int]$parameters.ProcessId) -ErrorAction SilentlyContinue
    if(-not $process) {
        return New-WinCareResult -Success $false -Status Failed `
            -Code 'ProcessMissing' -Message 'The process no longer exists.' -ExitCode 2
    }
    $start=try{$process.StartTime.ToUniversalTime().ToString('o')}catch{$null}
    if($start -ne [string]$parameters.ProcessStartTime) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'ProcessIdentityChanged' `
            -Message 'The process ID was reused after preview.' -ExitCode 74
    }
    if(-not (Initialize-WinCareNativeRuntime -RequiredTypes @('WinCare.Native.ProcessMemory'))) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'NativeProcessMemoryUnavailable' `
            -Message 'The source-built process-memory runtime is unavailable.' -ExitCode 78
    }
    # ponytail: WinCare.Native.ProcessMemory C# wrapper -> direct P/Invoke SetProcessWorkingSetSize
    $result=[WinCare.Native.ProcessMemory]::TrimWorkingSet(
        [int]$process.Id,
        [long]$parameters.ProcessStartTimeUtcTicks,
        [long]$parameters.MinimumWorkingSetBytes
    )
    $code=if(-not $result.IdentityMatched) {
        'ProcessIdentityChanged'
    }elseif($result.Success){'WorkingSetTrimRequested'}else{'WorkingSetTrimFailed'}
    $status=if($result.Success){'Succeeded'}elseif(-not $result.IdentityMatched){'Blocked'}else{'Failed'}
    $exitCode=if($result.Success){0}elseif($result.Win32Error -gt 0){[int]$result.Win32Error}else{74}
    New-WinCareResult -Success ([bool]$result.Success) -Status $status `
        -Code $code -Message ([string]$result.Message) -ExitCode $exitCode -Data @{
            ProcessId=$process.Id
            ProcessName=$process.ProcessName
            ProcessStartTime=$start
            ProcessStartTimeUtcTicks=[long]$result.ProcessStartTimeUtcTicks
            BeforeBytes=[long]$result.BeforeBytes
            AfterBytes=[long]$result.AfterBytes
            DeltaBytes=[long]$result.AfterBytes-[long]$result.BeforeBytes
            NativeCallSucceeded=[bool]$result.Success
            Win32Error=[int]$result.Win32Error
            EvidenceType='CompiledProcessIdentityNativeCallAndBeforeAfterWorkingSet'
        }
}
