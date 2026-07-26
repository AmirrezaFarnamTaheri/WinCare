function Get-WinCareAutomationRoot {
    Ensure-WinCareState
    Get-WinCareBridgeStateRoot 'Automation'
}

function Test-WinCareAutomationProfile {
    param([Parameter(Mandatory)][Collections.IDictionary]$Profile)
    $allowed=@('schemaVersion','id','title','command','arguments','apply','schedule','maximumRuntimeMinutes','batteryAllowed','networkRequired','notification','sourceRecords')
    $null=Test-WinCareStrictObjectKeys -InputObject $Profile -AllowedKeys $allowed -Context 'automation profile'
    if([int]$Profile.schemaVersion -ne 1){throw 'Unsupported automation profile schema.'}
    if([string]$Profile.id -notmatch '^[A-Za-z0-9._-]{3,80}$'){throw 'Invalid automation profile ID.'}
    if([string]::IsNullOrWhiteSpace([string]$Profile.title) -or ([string]$Profile.title).Length -gt 160){throw 'Automation profile title is invalid.'}
    if([int]$Profile.maximumRuntimeMinutes -lt 1 -or [int]$Profile.maximumRuntimeMinutes -gt 1440){throw 'Automation runtime is outside 1..1440 minutes.'}
    $commands=@(Get-WinCareHeadlessCommandName|Where-Object{$_ -ne 'run-automation'})
    if([string]$Profile.command -notin $commands){throw "Unsupported automation command: $($Profile.command)"}
    if([string]$Profile.schedule -notmatch '^(daily@(?:[01]\d|2[0-3]):[0-5]\d|weekly:(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)@(?:[01]\d|2[0-3]):[0-5]\d)$'){throw 'Schedule must be daily@HH:mm or weekly:Day@HH:mm.'}
    if([string]$Profile.notification -notin @('None','Console','EventLog')){throw 'Automation notification must be None, Console, or EventLog.'}
    if($null -eq $Profile.arguments){$Profile.arguments=@{}}
    if($Profile.arguments -isnot [Collections.IDictionary]){throw 'Automation arguments must be a JSON object.'}
    $argumentsJson=$Profile.arguments|ConvertTo-Json -Compress -Depth 30
    if([Text.Encoding]::UTF8.GetByteCount($argumentsJson)-gt 65536){throw 'Automation arguments exceed the 64 KiB limit.'}
    if($argumentsJson -match '(?i)(password|secret|token|api[_-]?key|credential)'){throw 'Automation profiles cannot contain apparent secrets or credentials.'}
    return $true
}

function Save-WinCareAutomationProfile {
    [CmdletBinding()]param([Parameter(Mandatory)][Collections.IDictionary]$Profile)
    $null=Test-WinCareAutomationProfile -Profile $Profile
    $path=Join-Path (Get-WinCareAutomationRoot) (([string]$Profile.id)+'.json')
    Write-WinCareProtectedJson -LiteralPath $path -InputObject $Profile -Purpose 'WinCare.AutomationProfile'
    return $path
}

function Get-WinCareAutomationProfile {
    [CmdletBinding()]param([string]$Id)
    $root=Get-WinCareAutomationRoot
    if($Id -and $Id -notmatch '^[A-Za-z0-9._-]{3,80}$'){throw 'Invalid automation profile ID.'}
    $files=if($Id){@(Join-Path $root ($Id+'.json'))}else{@(Get-ChildItem -LiteralPath $root -File -Filter '*.json' -ErrorAction SilentlyContinue|Select-Object -ExpandProperty FullName)}
    $profiles=[Collections.Generic.List[object]]::new()
    foreach($file in $files){
        if(-not(Test-Path -LiteralPath $file -PathType Leaf)){continue}
        try{$profile=Read-WinCareProtectedJson -LiteralPath $file -Purpose 'WinCare.AutomationProfile' -AsHashtable;$null=Test-WinCareAutomationProfile -Profile $profile;$profiles.Add($profile)}
        catch{Write-WinCareLog -Level Warning -Message 'Ignoring an invalid automation profile.' -Data @{path=$file;error=$_.Exception.Message}}
    }
    @($profiles)
}

function Test-WinCareAutomationRuntimeCondition {
    param([Parameter(Mandatory)][Collections.IDictionary]$Profile)
    if([bool]$Profile.networkRequired -and -not[Net.NetworkInformation.NetworkInterface]::GetIsNetworkAvailable()){
        return New-WinCareResult -Success $false -Status Blocked -Code 'NetworkUnavailable' -Message 'Automation requires an active network connection.' -ExitCode 50
    }
    if(-not[bool]$Profile.batteryAllowed -and $IsWindows){
        $battery=@(Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue|Select-Object -First 1)
        if($battery.Count -and [int]$battery[0].BatteryStatus -in @(1,4,5,11)){
            return New-WinCareResult -Success $false -Status Blocked -Code 'BatteryRestricted' -Message 'Automation is blocked while the device is running on battery.' -ExitCode 50
        }
    }
    New-WinCareResult -Success $true -Code 'AutomationConditionsPassed' -Message 'Automation runtime conditions passed.'
}

function Write-WinCareAutomationNotification {
    param([Collections.IDictionary]$Profile,[object]$Result)
    $message="WinCare automation '$($Profile.title)' completed with status $($Result.Status): $($Result.Message)"
    switch([string]$Profile.notification){
        'Console'{Write-Host $message}
        'EventLog'{
            if($IsWindows){
                try{
                    if(-not[Diagnostics.EventLog]::SourceExists('WinCare')){New-EventLog -LogName Application -Source WinCare -ErrorAction Stop}
                    Write-EventLog -LogName Application -Source WinCare -EntryType $(if($Result.Success){'Information'}else{'Error'}) -EventId $(if($Result.Success){3000}else{3001}) -Message $message
                }catch{Write-WinCareLog -Level Warning -Message 'Automation event-log notification failed.' -Data @{error=$_.Exception.Message}}
            }
        }
    }
}

function Invoke-WinCareAutomationProfile {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $profile=@(Get-WinCareAutomationProfile -Id $Id)|Select-Object -First 1
    if(-not $profile){return New-WinCareResult -Success $false -Status Failed -Code 'AutomationProfileNotFound' -Message "Automation profile not found: $Id" -ExitCode 2}
    $condition=Test-WinCareAutomationRuntimeCondition -Profile $profile
    if(-not $condition.Success){Write-WinCareAutomationNotification -Profile $profile -Result $condition;return $condition}
    $started=[datetime]::UtcNow
    try{
        $result=Invoke-WinCareHeadlessCommand -Command ([string]$profile.command) -Arguments ([hashtable]$profile.arguments) -Apply:([bool]$profile.apply) -JsonObject
        if($null -eq $result){$result=New-WinCareResult -Success $true -Message 'Automation command completed without a structured result.' -Data @{} }
        $duration=([datetime]::UtcNow-$started).TotalSeconds
        if($result.PSObject.Properties['Data']){$result.Data=[ordered]@{AutomationId=$Id;DurationSeconds=[math]::Round($duration,3);CommandResult=$result.Data}}
        Write-WinCareAutomationNotification -Profile $profile -Result $result
        return $result
    }catch{
        $result=New-WinCareResult -Success $false -Status Failed -Code 'AutomationExecutionFailed' -Message $_.Exception.Message -ExitCode 1 -Data @{AutomationId=$Id;DurationSeconds=[math]::Round(([datetime]::UtcNow-$started).TotalSeconds,3)}
        Write-WinCareAutomationNotification -Profile $profile -Result $result
        return $result
    }
}

function New-WinCareAutomationTaskPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][Collections.IDictionary]$Profile,[switch]$Remove)
    $null=Test-WinCareAutomationProfile -Profile $Profile
    $type=if($Remove){'RemoveAutomationTask'}else{'RegisterAutomationTask'}
    $action=New-WinCareAction -Type $type -Label "$(if($Remove){'Remove'}else{'Register'}) automation $($Profile.title)" -Risk Moderate -Parameters @{Profile=$Profile} -RequiresAdmin $false -Reversible (-not $Remove) -Compensator $(if(-not $Remove){@{Type='RemoveAutomationTask';Parameters=@{Profile=$Profile}}}else{@{}}) -Tags @('Automation','Idempotent') -SourceRecords @('SRC:1e40d19709')
    New-WinCarePlan -Title 'Configure scheduled maintenance' -Actions @($action) -SourceRecords @('SRC:1e40d19709')
}

function Invoke-WinCareRegisterAutomationTaskAction {
    param([object]$Action)
    if(-not $IsWindows -or -not(Get-Command Register-ScheduledTask -ErrorAction SilentlyContinue)){return New-WinCareResult -Success $false -Status Blocked -Code 'TaskSchedulerUnavailable' -Message 'Windows Task Scheduler cmdlets are unavailable.' -ExitCode 50}
    $profile=ConvertTo-WinCareParameterDictionary $Action.Parameters.Profile;$null=Test-WinCareAutomationProfile -Profile $profile
    $path=Save-WinCareAutomationProfile -Profile $profile
    $entry=Join-Path (Split-Path (Split-Path $script:WinCareModuleRoot -Parent) -Parent) 'WinCare.ps1'
    $entry=Assert-WinCareSafePath -LiteralPath $entry
    $pwsh=(Get-Process -Id $PID).Path
    $arguments=@('-NoProfile','-NonInteractive','-File',$entry,'-Command','run-automation','-ArgumentsJson',("{`"Id`":`"$($profile.id)`"}"),'-Apply','-Json')
    $quoted=@($arguments|ForEach-Object{if($_ -match '[\s"]'){ '"'+($_ -replace '(\\*)"','$1$1\"' -replace '(\\+)$','$1$1')+'"'}else{$_}})
    $argumentLine=$quoted -join ' '
    $taskName='WinCare-'+$profile.id
    $actionObj=New-ScheduledTaskAction -Execute $pwsh -Argument $argumentLine
    $schedule=[string]$profile.schedule
    if($schedule -match '^daily@(?<time>\d{2}:\d{2})$'){$trigger=New-ScheduledTaskTrigger -Daily -At ([datetime]::Today.Add([timespan]::ParseExact($Matches.time,'hh\:mm',$null)))}
    elseif($schedule -match '^weekly:(?<day>Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)@(?<time>\d{2}:\d{2})$'){$trigger=New-ScheduledTaskTrigger -Weekly -DaysOfWeek $Matches.day -At ([datetime]::Today.Add([timespan]::ParseExact($Matches.time,'hh\:mm',$null)))}
    else{return New-WinCareResult -Success $false -Message 'Schedule must be daily@HH:mm or weekly:Day@HH:mm.' -ExitCode 22}
    $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes ([int]$profile.maximumRuntimeMinutes)) -AllowStartIfOnBatteries:([bool]$profile.batteryAllowed) -DontStopIfGoingOnBatteries:([bool]$profile.batteryAllowed) -StartWhenAvailable
    Register-ScheduledTask -TaskName $taskName -Action $actionObj -Trigger $trigger -Settings $settings -Description ([string]$profile.title) -Force|Out-Null
    $registered=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if(-not $registered){Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue;return New-WinCareResult -Success $false -Message 'Task Scheduler did not return the registered task.' -ExitCode 74}
    New-WinCareResult -Success $true -Message 'Automation task registered and profile integrity-protected.' -Data @{TaskName=$taskName;ProfilePath=$path;Schedule=$profile.schedule;Command=$profile.command}
}

function Invoke-WinCareRemoveAutomationTaskAction {
    param([object]$Action)
    if(-not $IsWindows -or -not(Get-Command Unregister-ScheduledTask -ErrorAction SilentlyContinue)){return New-WinCareResult -Success $false -Status Blocked -Code 'TaskSchedulerUnavailable' -Message 'Windows Task Scheduler cmdlets are unavailable.' -ExitCode 50}
    $profile=ConvertTo-WinCareParameterDictionary $Action.Parameters.Profile;$null=Test-WinCareAutomationProfile -Profile $profile
    $taskName='WinCare-'+$profile.id
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    if(Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue){return New-WinCareResult -Success $false -Message 'Automation task still exists after removal.' -ExitCode 74}
    $path=Join-Path (Get-WinCareAutomationRoot) (([string]$profile.id)+'.json')
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    New-WinCareResult -Success (-not(Test-Path -LiteralPath $path)) -Message 'Automation task and protected profile removed.' -Data @{TaskName=$taskName}
}


