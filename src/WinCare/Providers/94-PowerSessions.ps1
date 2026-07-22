function Initialize-WinCareProductivityInterop {
    if('WinCare.Native.Productivity' -as [type]){return $true}
    $path=Join-Path $script:WinCareModuleRoot 'Native\WinCare.Productivity.cs'
    try{Add-Type -Path $path -ErrorAction Stop;return $true}
    catch{Write-WinCareLog -Level Warning -Message 'Productivity interop could not be loaded.' -Data @{error=$_.Exception.Message};return $false}
}

function Get-WinCarePowerSessionRoot {
    Ensure-WinCareState
    $root=Join-Path $script:WinCareState.Root 'PowerSessions'
    $null=New-Item -ItemType Directory -Path $root -Force
    $root
}

function Get-WinCarePowerSessionPath {
    param([Parameter(Mandatory)][string]$Id)
    if($Id -notmatch '^[a-f0-9]{32}$'){throw 'Power-session ID is invalid.'}
    Assert-WinCareSafePath -LiteralPath (Join-Path (Get-WinCarePowerSessionRoot) "$Id.json") -AllowedRoots @((Get-WinCarePowerSessionRoot)) -AllowMissing
}

function Invoke-WinCarePowerSessionLocked {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][scriptblock]$ScriptBlock,
        [ValidateRange(1,60)][int]$TimeoutSeconds=15
    )
    if($Id -notmatch '^[a-f0-9]{32}$'){throw 'Power-session lock identity is invalid.'}
    $mutex=$null;$acquired=$false
    try{
        $mutex=[Threading.Mutex]::new($false,"Local\WinCare.PowerSession.$Id")
        $acquired=$mutex.WaitOne([timespan]::FromSeconds($TimeoutSeconds))
        if(-not $acquired){throw 'Timed out waiting for the power-session state lock.'}
        & $ScriptBlock
    }finally{
        if($acquired){try{$mutex.ReleaseMutex()}catch{Write-Verbose 'Power-session mutex was already released.'}}
        if($mutex){$mutex.Dispose()}
    }
}

function Test-WinCarePowerSessionRecord {
    param([Parameter(Mandatory)][Collections.IDictionary]$Record,[string]$ExpectedId='')
    $allowed=@('SchemaVersion','Revision','Id','Mode','PreventDisplay','AwayMode','Status','CreatedAt','StartedAt','ExpiresAt','EndedAt','RefreshSeconds','HostProcessId','HostProcessStartTime','HeartbeatAt','Reason','SourceRecords')
    $null=Test-WinCareStrictObjectKeys -InputObject $Record -AllowedKeys $allowed -Context 'power-session record'
    if([int]$Record.SchemaVersion -ne 2 -or [long]$Record.Revision -lt 0){throw 'Power-session schema or revision is invalid.'}
    if([string]$Record.Id -notmatch '^[a-f0-9]{32}$' -or ($ExpectedId -and [string]$Record.Id -ne $ExpectedId)){throw 'Power-session record identity is invalid.'}
    if([string]$Record.Mode -notin @('System','Display','Away')){throw 'Power-session mode is invalid.'}
    if([string]$Record.Status -notin @('Prepared','Active','StopRequested','Stopped','Expired','Failed')){throw 'Power-session status is invalid.'}
    if($Record.PreventDisplay -isnot [bool] -or $Record.AwayMode -isnot [bool]){throw 'Power-session flags must be Boolean.'}
    if([int]$Record.RefreshSeconds -lt 5 -or [int]$Record.RefreshSeconds -gt 300){throw 'Power-session refresh interval is invalid.'}
    if([int]$Record.HostProcessId -lt 0){throw 'Power-session host process ID is invalid.'}
    if(([string]$Record.Reason).Length -gt 1000){throw 'Power-session reason is too long.'}
    if(@($Record.SourceRecords).Count -gt 64 -or @($Record.SourceRecords|Where-Object{$_ -isnot [string] -or ([string]$_).Length -gt 160}).Count){throw 'Power-session source records are invalid.'}
    foreach($field in @('CreatedAt','StartedAt','ExpiresAt','EndedAt','HostProcessStartTime','HeartbeatAt')){
        $value=[string]$Record[$field]
        if($value){$parsed=[datetime]::MinValue;if(-not[datetime]::TryParse($value,[ref]$parsed)){throw "Power-session timestamp is invalid: $field"}}
    }
    $true
}

function Read-WinCarePowerSessionRecord {
    param([Parameter(Mandatory)][string]$Id)
    $path=Get-WinCarePowerSessionPath -Id $Id
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){return $null}
    $record=Read-WinCareProtectedJson -LiteralPath $path -Purpose 'WinCare.PowerSession'
    $null=Test-WinCarePowerSessionRecord -Record $record -ExpectedId $Id
    $record
}

function Write-WinCarePowerSessionRecord {
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Record,
        [long]$ExpectedRevision=-2
    )
    $id=[string]$Record.Id
    Invoke-WinCarePowerSessionLocked -Id $id -ScriptBlock {
        $current=Read-WinCarePowerSessionRecord -Id $id
        $actualRevision=if($current){[long]$current.Revision}else{-1L}
        if($ExpectedRevision -ne -2 -and $actualRevision -ne $ExpectedRevision){throw "Power-session state changed concurrently. Expected revision $ExpectedRevision; actual revision $actualRevision."}
        $copy=[ordered]@{}
        foreach($key in $Record.Keys){$copy[$key]=$Record[$key]}
        $copy.SchemaVersion=2
        $copy.Revision=$actualRevision+1
        $null=Test-WinCarePowerSessionRecord -Record $copy -ExpectedId $id
        $path=Get-WinCarePowerSessionPath -Id $id
        Write-WinCareProtectedJson -LiteralPath $path -InputObject $copy -Purpose 'WinCare.PowerSession'
        $saved=Read-WinCarePowerSessionRecord -Id $id
        if((Get-WinCareCanonicalObjectHash $saved) -ne (Get-WinCareCanonicalObjectHash $copy)){throw 'Power-session record verification failed.'}
        $saved
    }
}

function Get-WinCarePowerSession {
    [CmdletBinding()]param([string]$Id,[switch]$IncludeTerminal)
    $paths=if($Id){@(Get-WinCarePowerSessionPath -Id $Id)}else{@(Get-ChildItem -LiteralPath (Get-WinCarePowerSessionRoot) -Filter '*.json' -File -ErrorAction SilentlyContinue|Select-Object -ExpandProperty FullName)}
    $items=[Collections.Generic.List[object]]::new()
    foreach($path in $paths){
        try{
            $record=Read-WinCarePowerSessionRecord -Id ([IO.Path]::GetFileNameWithoutExtension($path))
            if(-not $record){continue}
            $alive=$false
            if([int]$record.HostProcessId -gt 0){
                try{$process=Get-Process -Id ([int]$record.HostProcessId) -ErrorAction Stop;$alive=$process.StartTime.ToUniversalTime().ToString('o') -eq [string]$record.HostProcessStartTime}
                catch{Write-Verbose 'Power-session host process is not running.'}
            }
            $effectiveStatus=[string]$record.Status
            if($effectiveStatus -eq 'Active' -and -not $alive){$effectiveStatus='Failed'}
            if($effectiveStatus -in @('Prepared','Active','StopRequested') -and [string]$record.ExpiresAt){
                $expiry=[datetime]::Parse([string]$record.ExpiresAt).ToUniversalTime()
                if([datetime]::UtcNow -ge $expiry){$effectiveStatus='Expired'}
            }
            if($IncludeTerminal -or [string]$record.Status -in @('Prepared','Active','StopRequested')){
                $items.Add([pscustomobject]@{Id=$record.Id;Revision=[long]$record.Revision;Mode=$record.Mode;PreventDisplay=[bool]$record.PreventDisplay;AwayMode=[bool]$record.AwayMode;Status=$effectiveStatus;StoredStatus=[string]$record.Status;CreatedAt=$record.CreatedAt;StartedAt=$record.StartedAt;ExpiresAt=$record.ExpiresAt;EndedAt=$record.EndedAt;HeartbeatAt=$record.HeartbeatAt;HostProcessId=[int]$record.HostProcessId;HostAlive=$alive;Reason=$record.Reason})
            }
        }catch{Write-WinCareLog -Level Warning -Message 'Ignoring an invalid power-session record.' -Data @{path=$path;error=$_.Exception.Message}}
    }
    @($items|Sort-Object CreatedAt -Descending)
}

function New-WinCarePowerSessionPlan {
    [CmdletBinding()]
    param(
        [ValidateSet('System','Display','Away')][string]$Mode='System',
        [ValidateRange(0,10080)][int]$DurationMinutes=60,
        [ValidateRange(5,300)][int]$RefreshSeconds=30,
        [ValidateLength(1,1000)][string]$Reason='Operator-requested awake session'
    )
    if(-not $IsWindows){throw 'Power sessions require Windows.'}
    $id=[guid]::NewGuid().ToString('N')
    $preventDisplay=$Mode -in @('Display','Away');$away=$Mode -eq 'Away'
    $expires=if($DurationMinutes -gt 0){[datetime]::UtcNow.AddMinutes($DurationMinutes).ToString('o')}else{''}
    $action=New-WinCareAction -Type 'StartPowerSession' -Label "Start $Mode awake session" -Risk $(if($away){'High'}elseif($preventDisplay){'Moderate'}else{'Low'}) -Parameters @{Id=$id;Mode=$Mode;PreventDisplay=$preventDisplay;AwayMode=$away;ExpiresAt=$expires;RefreshSeconds=$RefreshSeconds;Reason=$Reason} -RequiresAdmin $false -Reversible $true -TimeoutSeconds 60 -Tags @('Power','Awake','Session') -SourceRecords @('W3-AWAKE-EXECUTION-STATE','W3-AWAKE-TIMED-LIFECYCLE') -RecoveryDescription 'Stop the exact WinCare power-session host and clear its execution-state request.'
    New-WinCarePlan -Title 'Start WinCare power session' -Description 'Runs a dedicated integrity-checked local host that renews the supported Windows execution-state request until expiry or explicit stop.' -Actions @($action) -SourceRecords @('W3-AWAKE-EXECUTION-STATE','W3-AWAKE-TIMED-LIFECYCLE')
}

function New-WinCarePowerSessionStopPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $record=Read-WinCarePowerSessionRecord -Id $Id
    if(-not $record){throw 'Power session does not exist.'}
    if([string]$record.Status -notin @('Prepared','Active','StopRequested')){throw 'Power session is already terminal.'}
    $action=New-WinCareAction -Type 'StopPowerSession' -Label "Stop $($record.Mode) awake session" -Risk Low -Parameters @{Id=$Id;ExpectedHostProcessId=[int]$record.HostProcessId;ExpectedHostProcessStartTime=[string]$record.HostProcessStartTime} -RequiresAdmin $false -Reversible $false -TimeoutSeconds 30 -Tags @('Power','Awake','Session') -SourceRecords @('W3-AWAKE-TIMED-LIFECYCLE') -RecoveryDescription 'A stopped awake session is not restarted automatically.'
    New-WinCarePlan -Title 'Stop WinCare power session' -Actions @($action) -RollbackOnFailure $false -SourceRecords @('W3-AWAKE-TIMED-LIFECYCLE')
}

function Invoke-WinCareStartPowerSessionAction {
    param([object]$Action)
    if(-not(Initialize-WinCareProductivityInterop)){return New-WinCareResult -Success $false -Message 'Windows productivity interop is unavailable.' -ExitCode 2}
    $p=$Action.Parameters;$id=[string]$p.Id;$started=$null
    if(Read-WinCarePowerSessionRecord -Id $id){return New-WinCareResult -Success $false -Status Blocked -Code 'PowerSessionExists' -Message 'Power-session record already exists.' -ExitCode 17}
    $record=[ordered]@{SchemaVersion=2;Revision=-1;Id=$id;Mode=[string]$p.Mode;PreventDisplay=[bool]$p.PreventDisplay;AwayMode=[bool]$p.AwayMode;Status='Prepared';CreatedAt=[datetime]::UtcNow.ToString('o');StartedAt='';ExpiresAt=[string]$p.ExpiresAt;EndedAt='';RefreshSeconds=[int]$p.RefreshSeconds;HostProcessId=0;HostProcessStartTime='';HeartbeatAt='';Reason=[string]$p.Reason;SourceRecords=@($Action.SourceRecords)}
    try{
        $record=Write-WinCarePowerSessionRecord -Record $record -ExpectedRevision -1
        $host=Assert-WinCareSafePath -LiteralPath (Join-Path $script:WinCareModuleRoot 'Host\PowerSessionHost.ps1') -AllowedRoots @($script:WinCareModuleRoot)
        $manifest=Assert-WinCareSafePath -LiteralPath (Join-Path $script:WinCareModuleRoot 'WinCare.psd1') -AllowedRoots @($script:WinCareModuleRoot)
        $treeHash=Get-WinCareModuleTreeHash
        $pwsh=(Get-Process -Id $PID -ErrorAction Stop).Path
        $started=Start-WinCareDetachedProcess -FilePath $pwsh -ArgumentList @('-NoLogo','-NoProfile','-NonInteractive','-File',$host,'-ModulePath',$manifest,'-SessionId',$id,'-ExpectedModuleTreeHash',$treeHash)
        if(-not $started.Success){throw $started.Message}
        $record=Read-WinCarePowerSessionRecord -Id $id
        $copy=ConvertTo-WinCareParameterDictionary $record;$copy.HostProcessId=[int]$started.Data.ProcessId;$copy.HostProcessStartTime=[string]$started.Data.ProcessStartTime;$copy.Status='Prepared';$copy.StartedAt='';$copy.HeartbeatAt=''
        $record=Write-WinCarePowerSessionRecord -Record $copy -ExpectedRevision ([long]$record.Revision)
        $deadline=[datetime]::UtcNow.AddSeconds(10);$active=$null
        while([datetime]::UtcNow -lt $deadline){
            $process=Get-Process -Id ([int]$record.HostProcessId) -ErrorAction SilentlyContinue
            if(-not $process -or $process.StartTime.ToUniversalTime().ToString('o') -ne [string]$record.HostProcessStartTime){throw 'Power-session host exited or changed identity before activation.'}
            $candidate=Read-WinCarePowerSessionRecord -Id $id
            if($candidate -and [string]$candidate.Status -eq 'Active' -and [int]$candidate.HostProcessId -eq [int]$record.HostProcessId -and [string]$candidate.HostProcessStartTime -eq [string]$record.HostProcessStartTime -and [string]$candidate.HeartbeatAt){$active=$candidate;break}
            if($candidate -and [string]$candidate.Status -in @('Stopped','Expired','Failed')){throw "Power-session host entered terminal state during startup: $($candidate.Status)"}
            Start-Sleep -Milliseconds 100
        }
        if(-not $active){throw 'Power-session host did not complete its execution-state handshake within 10 seconds.'}
        $record=$active
        $compensator=[ordered]@{Type='StopPowerSession';Parameters=@{Id=$id;ExpectedHostProcessId=[int]$record.HostProcessId;ExpectedHostProcessStartTime=[string]$record.HostProcessStartTime};RequiresAdmin=$false}
        New-WinCareResult -Success $true -Message 'Power session started and execution-state handshake verified.' -Data @{Session=$record;Compensator=$compensator}
    }catch{
        $failure=$_.Exception.Message
        if($started -and [int]$started.Data.ProcessId -gt 0){
            try{$process=Get-Process -Id ([int]$started.Data.ProcessId) -ErrorAction SilentlyContinue;if($process -and $process.StartTime.ToUniversalTime().ToString('o') -eq [string]$started.Data.ProcessStartTime){$process.Kill($true);$process.WaitForExit(5000)}}
            catch{Write-WinCareLog -Level Warning -Message 'Failed power-session host could not be terminated cleanly.' -Data @{id=$id;error=$_.Exception.Message}}
        }
        try{$current=Read-WinCarePowerSessionRecord -Id $id;if($current){$copy=ConvertTo-WinCareParameterDictionary $current;$copy.Status='Failed';$copy.EndedAt=[datetime]::UtcNow.ToString('o');$copy.HeartbeatAt=$copy.EndedAt;$null=Write-WinCarePowerSessionRecord -Record $copy -ExpectedRevision ([long]$current.Revision)}}
        catch{Write-WinCareLog -Level Warning -Message 'Failed power-session terminal state could not be recorded.' -Data @{id=$id;error=$_.Exception.Message}}
        New-WinCareResult -Success $false -Message "Power session failed to start: $failure" -ExitCode 1
    }
}

function Invoke-WinCareStopPowerSessionAction {
    param([object]$Action)
    $p=$Action.Parameters;$record=Read-WinCarePowerSessionRecord -Id ([string]$p.Id)
    if(-not $record){return New-WinCareResult -Success $true -Message 'Power-session record is already absent.' -Code 'AlreadyStopped'}
    if([string]$record.Status -in @('Stopped','Expired','Failed')){return New-WinCareResult -Success $true -Message 'Power session is already terminal.' -Data $record}
    if([int]$p.ExpectedHostProcessId -gt 0 -and [int]$record.HostProcessId -ne [int]$p.ExpectedHostProcessId){return New-WinCareResult -Success $false -Status Blocked -Code 'PowerSessionProcessChanged' -Message 'Power-session host identity changed after preview.' -ExitCode 74}
    if([string]$p.ExpectedHostProcessStartTime -and [string]$record.HostProcessStartTime -ne [string]$p.ExpectedHostProcessStartTime){return New-WinCareResult -Success $false -Status Blocked -Code 'PowerSessionProcessChanged' -Message 'Power-session host start time changed after preview.' -ExitCode 74}
    try{$copy=ConvertTo-WinCareParameterDictionary $record;$copy.Status='StopRequested';$record=Write-WinCarePowerSessionRecord -Record $copy -ExpectedRevision ([long]$record.Revision)}
    catch{return New-WinCareResult -Success $false -Status Blocked -Code 'PowerSessionChanged' -Message $_.Exception.Message -ExitCode 74}
    $deadline=[datetime]::UtcNow.AddSeconds(10)
    while([datetime]::UtcNow -lt $deadline){if(-not(Get-Process -Id ([int]$record.HostProcessId) -ErrorAction SilentlyContinue)){break};Start-Sleep -Milliseconds 250}
    $process=Get-Process -Id ([int]$record.HostProcessId) -ErrorAction SilentlyContinue
    if($process){
        try{if($process.StartTime.ToUniversalTime().ToString('o') -eq [string]$record.HostProcessStartTime){$process.Kill($true);$process.WaitForExit(5000)}}
        catch{Write-WinCareLog -Level Warning -Message 'Power-session host required forced termination.' -Data @{id=$record.Id;error=$_.Exception.Message}}
    }
    try{
        $current=Read-WinCarePowerSessionRecord -Id ([string]$p.Id)
        if($current -and [string]$current.Status -notin @('Stopped','Expired','Failed')){$copy=ConvertTo-WinCareParameterDictionary $current;$copy.Status='Stopped';$copy.EndedAt=[datetime]::UtcNow.ToString('o');$copy.HeartbeatAt=$copy.EndedAt;$current=Write-WinCarePowerSessionRecord -Record $copy -ExpectedRevision ([long]$current.Revision)}
        New-WinCareResult -Success $true -Message 'Power session stopped and execution-state request released.' -Data $current
    }catch{New-WinCareResult -Success $false -Message "Power session stopped, but its terminal record could not be verified: $($_.Exception.Message)" -ExitCode 1}
}

function Invoke-WinCarePowerSessionHost {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$SessionId)
    Ensure-WinCareState
    if(-not(Initialize-WinCareProductivityInterop)){throw 'Windows productivity interop is unavailable.'}
    $record=Read-WinCarePowerSessionRecord -Id $SessionId
    if(-not $record -or [string]$record.Status -notin @('Prepared','Active')){throw 'Power-session record is not runnable.'}
    $lastHeartbeat=[datetime]::MinValue
    $self=Get-Process -Id $PID -ErrorAction Stop;$selfStart=$self.StartTime.ToUniversalTime().ToString('o')
    try{
        while($true){
            $record=Read-WinCarePowerSessionRecord -Id $SessionId
            if(-not $record -or [string]$record.Status -in @('StopRequested','Stopped','Expired','Failed')){break}
            if([int]$record.HostProcessId -le 0 -or [string]::IsNullOrWhiteSpace([string]$record.HostProcessStartTime)){Start-Sleep -Milliseconds 100;continue}
            if([int]$record.HostProcessId -ne $PID -or [string]$record.HostProcessStartTime -ne $selfStart){throw 'Power-session host identity does not match its authorized record.'}
            if([string]$record.ExpiresAt){
                $expiry=[datetime]::Parse([string]$record.ExpiresAt).ToUniversalTime()
                if([datetime]::UtcNow -ge $expiry){
                    try{$copy=ConvertTo-WinCareParameterDictionary $record;$copy.Status='Expired';$copy.EndedAt=[datetime]::UtcNow.ToString('o');$copy.HeartbeatAt=$copy.EndedAt;$null=Write-WinCarePowerSessionRecord -Record $copy -ExpectedRevision ([long]$record.Revision)}
                    catch{Write-Verbose 'Power-session expiry raced with another state transition.'}
                    break
                }
            }
            [WinCare.Native.Productivity]::AssertExecutionState([bool]$record.PreventDisplay,[bool]$record.AwayMode)
            $now=[datetime]::UtcNow
            if([string]$record.Status -eq 'Prepared' -or ($now-$lastHeartbeat).TotalSeconds -ge 15){
                try{
                    $copy=ConvertTo-WinCareParameterDictionary $record
                    if([string]$copy.Status -eq 'Prepared'){$copy.Status='Active';$copy.StartedAt=$now.ToString('o')}
                    $copy.HeartbeatAt=$now.ToString('o');$null=Write-WinCarePowerSessionRecord -Record $copy -ExpectedRevision ([long]$record.Revision);$lastHeartbeat=$now
                }catch{Write-Verbose 'Power-session activation or heartbeat raced with another state transition.'}
            }
            Start-Sleep -Seconds ([math]::Max(5,[int]$record.RefreshSeconds))
        }
    }finally{
        try{[WinCare.Native.Productivity]::ClearExecutionState()}catch{Write-WinCareLog -Level Warning -Message 'Power execution state could not be cleared cleanly.' -Data @{id=$SessionId;error=$_.Exception.Message}}
        try{$record=Read-WinCarePowerSessionRecord -Id $SessionId;if($record -and [string]$record.Status -notin @('Stopped','Expired','Failed')){$copy=ConvertTo-WinCareParameterDictionary $record;$copy.Status='Stopped';$copy.EndedAt=[datetime]::UtcNow.ToString('o');$copy.HeartbeatAt=$copy.EndedAt;$null=Write-WinCarePowerSessionRecord -Record $copy -ExpectedRevision ([long]$record.Revision)}}
        catch{Write-WinCareLog -Level Warning -Message 'Power-session terminal state could not be recorded.' -Data @{id=$SessionId;error=$_.Exception.Message}}
    }
}


