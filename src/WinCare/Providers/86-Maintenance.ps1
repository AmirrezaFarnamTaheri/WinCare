function Get-WinCareMaintenanceStorePath {
    Join-Path (Join-Path $script:WinCareState.Root 'Maintenance') 'windows.json'
}

function Get-WinCareMaintenanceEventPath {
    Join-Path (Join-Path $script:WinCareState.Root 'Maintenance') 'events.jsonl'
}

function ConvertTo-WinCareMaintenanceMap {
    param([Parameter(Mandatory)][object]$Value)
    ConvertTo-WinCareParameterDictionary $Value
}

function Get-WinCareMaintenanceRecordHash {
    [CmdletBinding()]
    param([AllowNull()][object]$Record)
    if($null -eq $Record){return 'absent'}
    $map=ConvertTo-WinCareMaintenanceMap $Record
    $null=Test-WinCareMaintenanceRecord -Record $map
    Get-WinCareSha256Text -Text (ConvertTo-WinCareCanonicalJson -InputObject $map -Depth 40)
}

function Get-WinCareMaintenanceRecordFromStore {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Store,[Parameter(Mandatory)][string]$Id)
    @($Store.Windows|Where-Object{[string]$_.Id -eq $Id}|Select-Object -First 1)
}

function Test-WinCareMaintenanceRecord {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Record)
    $map=ConvertTo-WinCareMaintenanceMap $Record
    $allowed=@('SchemaVersion','Id','Name','Description','StartAt','EndAt','NoticeAt','State','CreatedAt','UpdatedAt','PlaybookId','Tags','RequiresRestart','Result','SourceRecords')
    $null=Test-WinCareStrictObjectKeys -InputObject $map -AllowedKeys $allowed -Context 'maintenance record'
    foreach($required in @('SchemaVersion','Id','Name','StartAt','EndAt','NoticeAt','State','CreatedAt','UpdatedAt','Tags','RequiresRestart','SourceRecords')){
        if(-not $map.ContainsKey($required)){throw "Maintenance record is missing $required."}
    }
    if([int]$map.SchemaVersion -ne 1){throw 'Unsupported maintenance record schema.'}
    if([string]$map.Id -notmatch '^[a-f0-9]{32}$'){throw 'Maintenance record ID is invalid.'}
    if([string]::IsNullOrWhiteSpace([string]$map.Name) -or ([string]$map.Name).Length -gt 160){throw 'Maintenance record name is invalid.'}
    if(([string]$map.Description).Length -gt 2000){throw 'Maintenance description is too long.'}
    $start=[datetimeoffset]::Parse([string]$map.StartAt,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind)
    $end=[datetimeoffset]::Parse([string]$map.EndAt,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind)
    $notice=[datetimeoffset]::Parse([string]$map.NoticeAt,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind)
    $created=[datetimeoffset]::Parse([string]$map.CreatedAt,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind)
    $updated=[datetimeoffset]::Parse([string]$map.UpdatedAt,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind)
    if($end -le $start){throw 'Maintenance end time must be later than start time.'}
    if(($end-$start).TotalDays -gt 31){throw 'Maintenance duration cannot exceed 31 days.'}
    if($notice -gt $start){throw 'Maintenance notice time cannot be after the start time.'}
    if($created -gt $updated.AddMinutes(1)){throw 'Maintenance timestamps are inconsistent.'}
    if([string]$map.State -notin @('Scheduled','Notice','Active','Completed','Cancelled')){throw 'Maintenance state is invalid.'}
    if($map.PlaybookId -and [string]$map.PlaybookId -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$'){throw 'Maintenance playbook ID is invalid.'}
    if($map.Tags -is [string] -or $map.Tags -isnot [Collections.IEnumerable]){throw 'Maintenance tags must be an array.'}
    $tags=@($map.Tags);if($tags.Count -gt 32 -or @($tags|Where-Object{$_ -isnot [string] -or ([string]$_).Length -gt 80}).Count){throw 'Maintenance tags are invalid.'}
    if($map.SourceRecords -is [string] -or $map.SourceRecords -isnot [Collections.IEnumerable]){throw 'Maintenance source records must be an array.'}
    if($map.RequiresRestart -isnot [bool]){throw 'Maintenance RequiresRestart must be Boolean.'}
    return $true
}

function Read-WinCareMaintenanceStore {
    [CmdletBinding()]
    param()
    $path=Get-WinCareMaintenanceStorePath
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){return [ordered]@{SchemaVersion=1;Revision=0;Windows=@()}}
    $document=Read-WinCareProtectedJson -LiteralPath $path -Purpose 'WinCare.MaintenanceStore' -AsHashtable
    $null=Test-WinCareStrictObjectKeys -InputObject $document -AllowedKeys @('SchemaVersion','Revision','Windows') -Context 'maintenance store'
    if([int]$document.SchemaVersion -ne 1 -or [long]$document.Revision -lt 0){throw 'Maintenance store schema or revision is invalid.'}
    if($document.Windows -is [string] -or $document.Windows -isnot [Collections.IEnumerable]){throw 'Maintenance store windows must be an array.'}
    if(@($document.Windows).Count -gt 10000){throw 'Maintenance store exceeds 10,000 windows.'}
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($record in @($document.Windows)){$null=Test-WinCareMaintenanceRecord $record;if(-not $ids.Add([string]$record.Id)){throw "Duplicate maintenance record: $($record.Id)"}}
    return $document
}

function Write-WinCareMaintenanceStore {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Store)
    $map=ConvertTo-WinCareMaintenanceMap $Store
    $null=Test-WinCareStrictObjectKeys -InputObject $map -AllowedKeys @('SchemaVersion','Revision','Windows') -Context 'maintenance store'
    foreach($required in @('SchemaVersion','Revision','Windows')){if(-not $map.ContainsKey($required)){throw "Maintenance store is missing $required."}}
    if([int]$map.SchemaVersion -ne 1 -or [long]$map.Revision -lt 0){throw 'Maintenance store schema or revision is invalid.'}
    if($map.Windows -is [string] -or $map.Windows -isnot [Collections.IEnumerable]){throw 'Maintenance store windows must be an array.'}
    $windows=@($map.Windows);if($windows.Count -gt 10000){throw 'Maintenance store exceeds 10,000 windows.'}
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($record in $windows){$null=Test-WinCareMaintenanceRecord $record;if(-not $ids.Add([string]$record.Id)){throw "Duplicate maintenance record: $($record.Id)"}}
    Write-WinCareProtectedJson -LiteralPath (Get-WinCareMaintenanceStorePath) -InputObject $map -Purpose 'WinCare.MaintenanceStore'
}

function Add-WinCareMaintenanceEvent {
    param([Parameter(Mandatory)][string]$Id,[Parameter(Mandatory)][string]$Event,[string]$FromState='',[string]$ToState='',[string]$Message='')
    if($Id -notmatch '^[a-f0-9]{32}$'){throw 'Maintenance event ID is invalid.'}
    if($Event -notmatch '^[A-Za-z][A-Za-z0-9.-]{0,79}$'){throw 'Maintenance event name is invalid.'}
    foreach($state in @($FromState,$ToState)){if($state -and $state -notin @('Scheduled','Notice','Active','Completed','Cancelled','Absent')){throw "Maintenance event state is invalid: $state"}}
    if($Message.Length -gt 2000){$Message=$Message.Substring(0,2000)}
    $path=Get-WinCareMaintenanceEventPath
    $parent=Split-Path -Parent $path;$null=New-Item -ItemType Directory -Path $parent -Force
    $record=[ordered]@{SchemaVersion=1;Timestamp=[datetime]::UtcNow.ToString('o');Id=$Id;Event=$Event;FromState=$FromState;ToState=$ToState;Message=$Message}
    $line=ConvertTo-WinCareCanonicalJson -InputObject (Protect-WinCareJsonRecord -InputObject $record -Purpose 'WinCare.MaintenanceEvent') -Depth 20
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes($line+"`n")
    $stream=[IO.File]::Open($path,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}

function Get-WinCareMaintenanceEffectiveState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Record,[datetimeoffset]$At=[datetimeoffset]::Now)
    $state=[string]$Record.State
    if($state -in @('Completed','Cancelled')){return $state}
    $start=[datetimeoffset]::Parse([string]$Record.StartAt)
    $end=[datetimeoffset]::Parse([string]$Record.EndAt)
    $notice=[datetimeoffset]::Parse([string]$Record.NoticeAt)
    if($At -ge $end){return 'Completed'}
    if($At -ge $start){return 'Active'}
    if($At -ge $notice){return 'Notice'}
    return 'Scheduled'
}

function Get-WinCareMaintenanceWindow {
    [CmdletBinding()]
    param([string]$Id,[switch]$IncludeTerminal)
    Ensure-WinCareState
    $store=Read-WinCareMaintenanceStore
    $records=@($store.Windows)
    if($Id){$records=@($records|Where-Object{[string]$_.Id -eq $Id})}
    if(-not $IncludeTerminal){$records=@($records|Where-Object{[string]$_.State -notin @('Completed','Cancelled')})}
    foreach($record in $records|Sort-Object StartAt){
        [pscustomobject]@{
            Id=[string]$record.Id;Name=[string]$record.Name;Description=[string]$record.Description
            StartAt=[datetimeoffset]$record.StartAt;EndAt=[datetimeoffset]$record.EndAt;NoticeAt=[datetimeoffset]$record.NoticeAt
            State=[string]$record.State;EffectiveState=Get-WinCareMaintenanceEffectiveState $record
            PlaybookId=[string]$record.PlaybookId;Tags=@($record.Tags);RequiresRestart=[bool]$record.RequiresRestart
            Result=$record.Result;SourceRecords=@($record.SourceRecords);UpdatedAt=[datetimeoffset]$record.UpdatedAt
        }
    }
}

function New-WinCareMaintenanceWindowRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][datetimeoffset]$StartAt,
        [Parameter(Mandatory)][datetimeoffset]$EndAt,
        [datetimeoffset]$NoticeAt,
        [string]$Description='',
        [string]$PlaybookId='',
        [string[]]$Tags=@(),
        [bool]$RequiresRestart=$false,
        [string[]]$SourceRecords=@('SRC:652ed31cf6')
    )
    if(-not $PSBoundParameters.ContainsKey('NoticeAt')){$NoticeAt=$StartAt.AddMinutes(-[int](Get-WinCareConfig 'MaintenanceNoticeMinutes'))}
    $now=[datetime]::UtcNow.ToString('o')
    $record=[ordered]@{SchemaVersion=1;Id=[guid]::NewGuid().ToString('N');Name=$Name.Trim();Description=$Description;StartAt=$StartAt.ToUniversalTime().ToString('o');EndAt=$EndAt.ToUniversalTime().ToString('o');NoticeAt=$NoticeAt.ToUniversalTime().ToString('o');State='Scheduled';CreatedAt=$now;UpdatedAt=$now;PlaybookId=$PlaybookId;Tags=@($Tags);RequiresRestart=$RequiresRestart;Result=$null;SourceRecords=@($SourceRecords)}
    $null=Test-WinCareMaintenanceRecord $record
    return $record
}

function Test-WinCareMaintenanceTransition {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$From,[Parameter(Mandatory)][string]$To)
    $allowed=@{
        Scheduled=@('Notice','Active','Cancelled')
        Notice=@('Active','Cancelled')
        Active=@('Completed','Cancelled')
        Completed=@()
        Cancelled=@()
    }
    return $allowed.ContainsKey($From) -and $To -in @($allowed[$From])
}

function New-WinCareMaintenanceUpsertPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Window)
    $map=ConvertTo-WinCareMaintenanceMap $Window;$null=Test-WinCareMaintenanceRecord $map
    $store=Read-WinCareMaintenanceStore
    $before=@(Get-WinCareMaintenanceRecordFromStore -Store $store -Id ([string]$map.Id))
    $expectedRecordHash=if($before.Count){Get-WinCareMaintenanceRecordHash -Record $before[0]}else{'absent'}
    $plan=New-WinCarePlan -Title "Schedule maintenance: $($map.Name)" -Description 'Create or replace a validated maintenance window using exact revision and record preconditions.' -SourceRecords @($map.SourceRecords) -Metadata @{MaintenanceId=$map.Id;ExpectedRevision=[long]$store.Revision;ExpectedRecordHash=$expectedRecordHash}
    $action=New-WinCareAction -Type UpsertMaintenanceWindow -Label "Save maintenance window: $($map.Name)" -Risk Moderate -Parameters @{Window=$map;ExpectedRevision=[long]$store.Revision;ExpectedRecordHash=$expectedRecordHash} -Reversible $true -Tags @('Maintenance','StateMachine','Idempotent','CompareAndSwap','DynamicCompensator') -SourceRecords @($map.SourceRecords) -RecoveryDescription 'Restore the previous exact maintenance record or remove the newly created record if the saved record remains unchanged.'
    $null=Add-WinCarePlanAction -Plan $plan -Action $action
    return $plan
}

function New-WinCareMaintenanceTransitionPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Id,[Parameter(Mandatory)][ValidateSet('Notice','Active','Completed','Cancelled')][string]$State,[string]$Message='')
    $store=Read-WinCareMaintenanceStore
    $raw=@(Get-WinCareMaintenanceRecordFromStore -Store $store -Id $Id)
    if(-not $raw.Count){throw "Maintenance window was not found: $Id"}
    $record=ConvertTo-WinCareMaintenanceMap $raw[0];$null=Test-WinCareMaintenanceRecord -Record $record
    if(-not(Test-WinCareMaintenanceTransition -From ([string]$record.State) -To $State)){throw "Maintenance transition is not allowed: $($record.State) -> $State"}
    $expectedRecordHash=Get-WinCareMaintenanceRecordHash -Record $record
    $plan=New-WinCarePlan -Title "Maintenance transition: $($record.Name) -> $State" -Description $Message -SourceRecords @($record.SourceRecords) -Metadata @{MaintenanceId=$Id;FromState=$record.State;ToState=$State;ExpectedRevision=[long]$store.Revision;ExpectedRecordHash=$expectedRecordHash}
    $action=New-WinCareAction -Type SetMaintenanceWindowState -Label "Set maintenance state to $State" -Risk Moderate -Parameters @{Id=$Id;State=$State;Message=$Message;ExpectedRevision=[long]$store.Revision;ExpectedRecordHash=$expectedRecordHash} -Reversible $true -Tags @('Maintenance','StateMachine','CompareAndSwap','DynamicCompensator') -SourceRecords @($record.SourceRecords) -RecoveryDescription "Restore the complete previous maintenance record in state $($record.State) if the transitioned record remains unchanged."
    $null=Add-WinCarePlanAction -Plan $plan -Action $action
    return $plan
}

function Invoke-WinCareUpsertMaintenanceWindowAction {
    param([Parameter(Mandatory)][object]$Action)
    $window=ConvertTo-WinCareMaintenanceMap $Action.Parameters.Window;$null=Test-WinCareMaintenanceRecord $window
    $store=Read-WinCareMaintenanceStore
    if([long]$store.Revision -ne [long]$Action.Parameters.ExpectedRevision){return New-WinCareResult -Success $false -Status Blocked -Code 'MaintenanceStoreChanged' -Message 'Maintenance store revision changed after preview.' -ExitCode 74}
    $before=@(Get-WinCareMaintenanceRecordFromStore -Store $store -Id ([string]$window.Id))
    $beforeRecord=if($before.Count){ConvertTo-WinCareMaintenanceMap $before[0]}else{$null}
    $beforeHash=Get-WinCareMaintenanceRecordHash -Record $beforeRecord
    if($beforeHash -ne [string]$Action.Parameters.ExpectedRecordHash){return New-WinCareResult -Success $false -Status Blocked -Code 'MaintenanceRecordChanged' -Message 'Maintenance record changed after preview.' -ExitCode 74}
    $list=[Collections.Generic.List[object]]::new();$replaced=$false
    foreach($record in @($store.Windows)){if([string]$record.Id -eq [string]$window.Id){$list.Add($window);$replaced=$true}else{$list.Add($record)}}
    if(-not $replaced){$list.Add($window)}
    $next=[ordered]@{SchemaVersion=1;Revision=([long]$store.Revision+1);Windows=@($list)}
    try{
        Write-WinCareMaintenanceStore $next
        $verify=Read-WinCareMaintenanceStore
        $saved=@(Get-WinCareMaintenanceRecordFromStore -Store $verify -Id ([string]$window.Id))
        if([long]$verify.Revision -ne [long]$next.Revision -or $saved.Count -ne 1){throw 'Maintenance upsert verification failed.'}
        $savedHash=Get-WinCareMaintenanceRecordHash -Record $saved[0]
        $expectedSavedHash=Get-WinCareMaintenanceRecordHash -Record $window
        if($savedHash -ne $expectedSavedHash){throw 'Saved maintenance record hash does not match the planned record.'}
        Add-WinCareMaintenanceEvent -Id ([string]$window.Id) -Event $(if($replaced){'Updated'}else{'Scheduled'}) -ToState ([string]$window.State) -Message ([string]$window.Name)
        $compensator=@{Type='RestoreMaintenanceWindowRecord';RequiresAdmin=$false;Parameters=@{Id=[string]$window.Id;Exists=($null -ne $beforeRecord);Record=$beforeRecord;ExpectedCurrentHash=$savedHash}}
        New-WinCareResult -Success $true -Code 'MaintenanceWindowSaved' -Message 'Maintenance window was saved and verified.' -Data @{Id=$window.Id;Revision=$next.Revision;RecordHash=$savedHash;Compensator=$compensator}
    }catch{
        $primary=$_.Exception.Message;$rollbackError=''
        try{Write-WinCareMaintenanceStore $store}catch{$rollbackError=$_.Exception.Message}
        $rolledBack=$false
        if(-not $rollbackError){
            try{$restored=Read-WinCareMaintenanceStore;$restoredRecord=@(Get-WinCareMaintenanceRecordFromStore -Store $restored -Id ([string]$window.Id));$restoredHash=if($restoredRecord.Count){Get-WinCareMaintenanceRecordHash -Record $restoredRecord[0]}else{'absent'};$rolledBack=([long]$restored.Revision -eq [long]$store.Revision -and $restoredHash -eq $beforeHash)}catch{$rollbackError=$_.Exception.Message}
        }
        try{Add-WinCareMaintenanceEvent -Id ([string]$window.Id) -Event 'UpsertRolledBack' -ToState ([string]$window.State) -Message $primary}catch{Write-Verbose "Maintenance rollback event could not be recorded: $($_.Exception.Message)"}
        New-WinCareResult -Success $false -Status Failed -Code $(if($rolledBack){'MaintenanceUpsertFailedRolledBack'}else{'MaintenanceUpsertRollbackFailed'}) -Message $(if($rolledBack){"$primary The previous store was restored."}else{"$primary Rollback failed: $rollbackError"}) -ExitCode 1 -Data @{Id=$window.Id;RolledBack=$rolledBack}
    }
}

function Invoke-WinCareSetMaintenanceWindowStateAction {
    param([Parameter(Mandatory)][object]$Action)
    $id=[string]$Action.Parameters.Id;$state=[string]$Action.Parameters.State
    $store=Read-WinCareMaintenanceStore
    if([long]$store.Revision -ne [long]$Action.Parameters.ExpectedRevision){return New-WinCareResult -Success $false -Status Blocked -Code 'MaintenanceStoreChanged' -Message 'Maintenance store revision changed after preview.' -ExitCode 74}
    $before=@(Get-WinCareMaintenanceRecordFromStore -Store $store -Id $id)
    if(-not $before.Count){return New-WinCareResult -Success $false -Status Blocked -Code 'MaintenanceWindowNotFound' -Message "Maintenance window was not found: $id" -ExitCode 2}
    $beforeRecord=ConvertTo-WinCareMaintenanceMap $before[0];$beforeHash=Get-WinCareMaintenanceRecordHash -Record $beforeRecord
    if($beforeHash -ne [string]$Action.Parameters.ExpectedRecordHash){return New-WinCareResult -Success $false -Status Blocked -Code 'MaintenanceRecordChanged' -Message 'Maintenance record changed after preview.' -ExitCode 74}
    $from=[string]$beforeRecord.State
    if($from -ne $state -and -not(Test-WinCareMaintenanceTransition -From $from -To $state)){return New-WinCareResult -Success $false -Status Blocked -Code 'InvalidMaintenanceTransition' -Message "Maintenance transition is not allowed: $from -> $state" -ExitCode 22}
    $list=[Collections.Generic.List[object]]::new();$afterRecord=$null
    foreach($item in @($store.Windows)){
        if([string]$item.Id -eq $id){
            $map=ConvertTo-WinCareMaintenanceMap $item;$map.State=$state;$map.UpdatedAt=[datetime]::UtcNow.ToString('o')
            if($state -eq 'Completed'){$map.Result=[ordered]@{CompletedAt=$map.UpdatedAt;Message=[string](Get-WinCarePropertyValue $Action.Parameters 'Message' '')}}
            $null=Test-WinCareMaintenanceRecord -Record $map;$afterRecord=$map;$list.Add($map)
        }else{$list.Add($item)}
    }
    $next=[ordered]@{SchemaVersion=1;Revision=([long]$store.Revision+1);Windows=@($list)}
    try{
        Write-WinCareMaintenanceStore $next
        $verify=Read-WinCareMaintenanceStore;$saved=@(Get-WinCareMaintenanceRecordFromStore -Store $verify -Id $id)
        if([long]$verify.Revision -ne [long]$next.Revision -or $saved.Count -ne 1){throw 'Maintenance transition verification failed.'}
        $savedHash=Get-WinCareMaintenanceRecordHash -Record $saved[0];$expectedSavedHash=Get-WinCareMaintenanceRecordHash -Record $afterRecord
        if($savedHash -ne $expectedSavedHash){throw 'Transitioned maintenance record hash does not match the planned state.'}
        Add-WinCareMaintenanceEvent -Id $id -Event 'StateChanged' -FromState $from -ToState $state -Message ([string](Get-WinCarePropertyValue $Action.Parameters 'Message' ''))
        $compensator=@{Type='RestoreMaintenanceWindowRecord';RequiresAdmin=$false;Parameters=@{Id=$id;Exists=$true;Record=$beforeRecord;ExpectedCurrentHash=$savedHash}}
        New-WinCareResult -Success $true -Code 'MaintenanceStateChanged' -Message "Maintenance state changed from $from to $state." -Data @{Id=$id;From=$from;To=$state;Revision=$next.Revision;RecordHash=$savedHash;Compensator=$compensator}
    }catch{
        $primary=$_.Exception.Message;$rollbackError=''
        try{Write-WinCareMaintenanceStore $store}catch{$rollbackError=$_.Exception.Message}
        $rolledBack=$false
        if(-not $rollbackError){try{$restored=Read-WinCareMaintenanceStore;$restoredRecord=@(Get-WinCareMaintenanceRecordFromStore -Store $restored -Id $id);$rolledBack=([long]$restored.Revision -eq [long]$store.Revision -and $restoredRecord.Count -eq 1 -and (Get-WinCareMaintenanceRecordHash -Record $restoredRecord[0]) -eq $beforeHash)}catch{$rollbackError=$_.Exception.Message}}
        try{Add-WinCareMaintenanceEvent -Id $id -Event 'TransitionRolledBack' -FromState $from -ToState $state -Message $primary}catch{Write-Verbose "Maintenance rollback event could not be recorded: $($_.Exception.Message)"}
        New-WinCareResult -Success $false -Status Failed -Code $(if($rolledBack){'MaintenanceTransitionFailedRolledBack'}else{'MaintenanceTransitionRollbackFailed'}) -Message $(if($rolledBack){"$primary The previous store was restored."}else{"$primary Rollback failed: $rollbackError"}) -ExitCode 1 -Data @{Id=$id;RolledBack=$rolledBack}
    }
}

function Invoke-WinCareRestoreMaintenanceWindowRecordAction {
    param([Parameter(Mandatory)][object]$Action)
    $id=[string]$Action.Parameters.Id;$exists=[bool]$Action.Parameters.Exists
    $store=Read-WinCareMaintenanceStore;$current=@(Get-WinCareMaintenanceRecordFromStore -Store $store -Id $id)
    $currentHash=if($current.Count){Get-WinCareMaintenanceRecordHash -Record $current[0]}else{'absent'}
    if($currentHash -ne [string]$Action.Parameters.ExpectedCurrentHash){return New-WinCareResult -Success $false -Status Blocked -Code 'MaintenanceRecoveryConflict' -Message 'Maintenance record changed after the original operation; recovery will not overwrite it.' -ExitCode 74}
    $targetRecord=$null
    if($exists){$targetRecord=ConvertTo-WinCareMaintenanceMap $Action.Parameters.Record;$null=Test-WinCareMaintenanceRecord -Record $targetRecord}
    $list=[Collections.Generic.List[object]]::new();foreach($item in @($store.Windows)){if([string]$item.Id -ne $id){$list.Add($item)}};if($exists){$list.Add($targetRecord)}
    $next=[ordered]@{SchemaVersion=1;Revision=([long]$store.Revision+1);Windows=@($list)}
    try{
        Write-WinCareMaintenanceStore $next
        $verify=Read-WinCareMaintenanceStore;$actual=@(Get-WinCareMaintenanceRecordFromStore -Store $verify -Id $id)
        $actualHash=if($actual.Count){Get-WinCareMaintenanceRecordHash -Record $actual[0]}else{'absent'}
        $expectedHash=if($exists){Get-WinCareMaintenanceRecordHash -Record $targetRecord}else{'absent'}
        if([long]$verify.Revision -ne [long]$next.Revision -or $actualHash -ne $expectedHash){throw 'Maintenance recovery verification failed.'}
        Add-WinCareMaintenanceEvent -Id $id -Event 'Restored' -ToState $(if($exists){[string]$targetRecord.State}else{'Absent'})
        New-WinCareResult -Success $true -Code 'MaintenanceRecordRestored' -Message 'Maintenance record rollback was applied and verified.' -Data @{Id=$id;Exists=$exists;Revision=$next.Revision;RecordHash=$actualHash}
    }catch{
        $primary=$_.Exception.Message;$rollbackError=''
        try{Write-WinCareMaintenanceStore $store}catch{$rollbackError=$_.Exception.Message}
        $rolledBack=$false
        if(-not $rollbackError){try{$restored=Read-WinCareMaintenanceStore;$restoredCurrent=@(Get-WinCareMaintenanceRecordFromStore -Store $restored -Id $id);$restoredHash=if($restoredCurrent.Count){Get-WinCareMaintenanceRecordHash -Record $restoredCurrent[0]}else{'absent'};$rolledBack=([long]$restored.Revision -eq [long]$store.Revision -and $restoredHash -eq $currentHash)}catch{$rollbackError=$_.Exception.Message}}
        New-WinCareResult -Success $false -Status Failed -Code $(if($rolledBack){'MaintenanceRestoreFailedRolledBack'}else{'MaintenanceRestoreRollbackFailed'}) -Message $(if($rolledBack){"$primary The pre-recovery store was restored."}else{"$primary Rollback failed: $rollbackError"}) -ExitCode 1 -Data @{Id=$id;RolledBack=$rolledBack}
    }
}

function Get-WinCareMaintenanceMetrics {
    [CmdletBinding()]
    param()
    $all=@(Get-WinCareMaintenanceWindow -IncludeTerminal)
    $counts=@{};foreach($state in @('Scheduled','Notice','Active','Completed','Cancelled')){$counts[$state]=@($all|Where-Object EffectiveState -eq $state).Count}
    $next=@($all|Where-Object{$_.EffectiveState -in @('Scheduled','Notice')}|Sort-Object StartAt|Select-Object -First 1)
    $active=@($all|Where-Object EffectiveState -eq 'Active')
    [pscustomobject]@{
        Timestamp=[datetime]::UtcNow;Total=$all.Count;Scheduled=$counts.Scheduled;Notice=$counts.Notice;Active=$counts.Active;Completed=$counts.Completed;Cancelled=$counts.Cancelled
        NextStartUnix=if($next.Count){$next[0].StartAt.ToUnixTimeSeconds()}else{0};ActiveIds=@($active.Id);UpcomingRestartRequired=@($all|Where-Object{$_.EffectiveState -in @('Scheduled','Notice','Active') -and $_.RequiresRestart}).Count
    }
}

function ConvertTo-WinCarePrometheusMaintenanceMetrics {
    [CmdletBinding()]
    param([object]$Metrics=(Get-WinCareMaintenanceMetrics))
    @(
        '# HELP wincare_maintenance_windows Number of maintenance windows by effective state.'
        '# TYPE wincare_maintenance_windows gauge'
        "wincare_maintenance_windows{state='scheduled'} $($Metrics.Scheduled)"
        "wincare_maintenance_windows{state='notice'} $($Metrics.Notice)"
        "wincare_maintenance_windows{state='active'} $($Metrics.Active)"
        "wincare_maintenance_windows{state='completed'} $($Metrics.Completed)"
        "wincare_maintenance_windows{state='cancelled'} $($Metrics.Cancelled)"
        '# HELP wincare_maintenance_next_start_seconds Unix timestamp of the next maintenance start.'
        '# TYPE wincare_maintenance_next_start_seconds gauge'
        "wincare_maintenance_next_start_seconds $($Metrics.NextStartUnix)"
        '# HELP wincare_maintenance_restart_required Upcoming or active windows that may require restart.'
        '# TYPE wincare_maintenance_restart_required gauge'
        "wincare_maintenance_restart_required $($Metrics.UpcomingRestartRequired)"
    ) -join "`n"
}

function New-WinCareMaintenanceMetricsExportPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Destination)
    $root=Join-Path $script:WinCareState.Root 'Metrics'
    $path=Assert-WinCareSafePath -LiteralPath $Destination -AllowedRoots @($root) -AllowMissing
    $content=[Text.Encoding]::UTF8.GetBytes((ConvertTo-WinCarePrometheusMaintenanceMetrics)+(if($IsWindows){"`r`n"}else{"`n"}))
    $action=New-WinCareManagedFileWriteAction -Path $path -Content $content -AllowedRoots @($root) -Label 'Export maintenance metrics' -SourceRecords @('SRC:652ed31cf6')
    New-WinCarePlan -Title 'Export maintenance metrics' -Description 'Write a local Prometheus text exposition file; no network listener or telemetry is created.' -Actions @($action) -SourceRecords @('SRC:652ed31cf6')
}



