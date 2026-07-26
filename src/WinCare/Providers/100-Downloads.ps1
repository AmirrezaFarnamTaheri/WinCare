function Get-WinCareDownloadRoot {
    Ensure-WinCareState
    $root=Get-WinCareBridgeStateRoot 'Downloads'
    foreach($name in @('Partial','Completed','Exports')) {
        $null=Get-WinCareBridgeStateRoot (Join-Path 'Downloads' $name)
    }
    return $root
}

function Get-WinCareDownloadStorePath { Join-Path (Get-WinCareDownloadRoot) 'jobs.json' }
function Get-WinCareDefaultDownloadStore { [ordered]@{SchemaVersion=1;Revision=0;UpdatedAt='1970-01-01T00:00:00.0000000Z';Jobs=@()} }
function Get-WinCareDownloadStoreHash { param([Collections.IDictionary]$Store);Get-WinCareCanonicalObjectHash -InputObject $Store }

function Get-WinCareDownloadByteLimit {
    $configLimit=[long](Get-WinCareConfig 'DownloadMaximumBytes')
    $policyLimit=[long](Get-WinCarePolicy 'MaximumDownloadBytes')
    [long][math]::Min($configLimit,$policyLimit)
}

function Get-WinCareActiveDownloadCount {
    $store=Get-WinCareDownloadStore -Strict
    @($store.Jobs|Where-Object{[string]$_.Status -eq 'Transferring'}).Count
}

function Get-WinCareDownloadDestinationRoots {
    $roots=[Collections.Generic.List[string]]::new()
    $roots.Add((Join-Path (Get-WinCareDownloadRoot) 'Completed'))
    $downloads=if($HOME){Join-Path $HOME 'Downloads'}else{''}
    if($downloads){$roots.Add((Resolve-WinCareCanonicalPath -LiteralPath $downloads -AllowMissing))}
    @($roots|Sort-Object -Unique)
}

function Test-WinCareDownloadHeaderMap {
    param([AllowNull()][object]$Headers)
    if($null -eq $Headers){return $true}
    $map=ConvertTo-WinCareParameterDictionary $Headers
    if($map.Count -gt 16){throw 'Download headers exceed the 16-header limit.'}
    foreach($key in @($map.Keys)){
        if([string]$key -notmatch '^[A-Za-z][A-Za-z0-9-]{0,63}$'){throw "Download header name is invalid: $key"}
        if([string]$key -match '^(?i:Authorization|Proxy-Authorization|Cookie|Set-Cookie)$'){throw "Sensitive download header is not permitted in durable state: $key"}
        $value=[string]$map[$key]
        if($value.Length -gt 2048 -or $value -match "[\r\n]"){throw "Download header value is invalid: $key"}
    }
    $true
}

function Test-WinCareDownloadUri {
    param([Parameter(Mandatory)][string]$Uri)
    if($Uri.Length -gt 4096){throw 'Download URI is too long.'}
    $parsed=$null
    if(-not[uri]::TryCreate($Uri,[UriKind]::Absolute,[ref]$parsed)){throw 'Download URI is not absolute.'}
    if($parsed.Scheme -notin @('https','http')){throw 'Only HTTP and HTTPS downloads are supported.'}
    $allowHttp=[bool](Get-WinCarePolicy 'AllowInsecureDownloads')
    if($parsed.Scheme -eq 'http' -and -not $allowHttp){throw 'Insecure HTTP downloads are disabled by policy.'}
    if([string]::IsNullOrWhiteSpace($parsed.Host)){throw 'Download URI must contain a host.'}
    $allowPrivate=[bool](Get-WinCarePolicy 'AllowPrivateDownloadTargets')
    $admission=Assert-WinCareServiceUri -Uri $parsed `
        -AllowPrivateNetwork:$allowPrivate -AllowExplicitHttp:$allowHttp
    ([uri]$admission.Uri).AbsoluteUri
}

function Test-WinCareDownloadJobRecord {
    param([Parameter(Mandatory)][object]$Record)
    $map=ConvertTo-WinCareParameterDictionary $Record
    $allowed=@('SchemaVersion','Id','Revision','Name','Urls','SourceIndex','Destination','TemporaryPath','CollisionPolicy','ExpectedHash','HashAlgorithm','Headers','Priority','MaxRetries','RetryCount','ScheduledAt','Status','BitsJobId','BytesTotal','BytesTransferred','CreatedAt','UpdatedAt','CompletedAt','LastError','SourceRecords')
    $null=Test-WinCareStrictObjectKeys -InputObject $map -AllowedKeys $allowed -Context 'download job'
    foreach($required in @('SchemaVersion','Id','Revision','Name','Urls','SourceIndex','Destination','TemporaryPath','CollisionPolicy','ExpectedHash','HashAlgorithm','Headers','Priority','MaxRetries','RetryCount','ScheduledAt','Status','BitsJobId','BytesTotal','BytesTransferred','CreatedAt','UpdatedAt','CompletedAt','LastError','SourceRecords')){if(-not $map.ContainsKey($required)){throw "Download job is missing $required."}}
    if([int]$map.SchemaVersion -ne 1 -or [long]$map.Revision -lt 0){throw 'Download job schema or revision is invalid.'}
    if([string]$map.Id -notmatch '^[a-f0-9]{32}$'){throw 'Download job ID is invalid.'}
    if([string]::IsNullOrWhiteSpace([string]$map.Name) -or ([string]$map.Name).Length -gt 240){throw 'Download job name is invalid.'}
    if($map.Urls -is [string] -or $map.Urls -isnot [Collections.IEnumerable]){throw 'Download URLs must be an array.'}
    $urls=@($map.Urls);if($urls.Count -lt 1 -or $urls.Count -gt 8){throw 'Download jobs require 1..8 source URLs.'}
    foreach($url in $urls){$null=Test-WinCareDownloadUri ([string]$url)}
    if([int]$map.SourceIndex -lt 0 -or [int]$map.SourceIndex -ge $urls.Count){throw 'Download source index is invalid.'}
    $roots=Get-WinCareDownloadDestinationRoots
    $destination=Assert-WinCareSafePath -LiteralPath ([string]$map.Destination) -AllowedRoots $roots -AllowMissing
    $partialRoot=Join-Path (Get-WinCareDownloadRoot) 'Partial'
    $temporary=Assert-WinCareSafePath -LiteralPath ([string]$map.TemporaryPath) -AllowedRoots @($partialRoot) -AllowMissing
    if($destination -eq $temporary){throw 'Download destination and temporary path cannot be the same.'}
    if([string]$map.CollisionPolicy -notin @('Fail','Replace')){throw 'Download collision policy is invalid.'}
    if([string]$map.HashAlgorithm -notin @('None','SHA256','SHA512')){throw 'Download hash algorithm is invalid.'}
    if([string]$map.HashAlgorithm -eq 'None'){
        if([string]$map.ExpectedHash){throw 'ExpectedHash must be empty when no hash algorithm is selected.'}
    }else{
        $length=if([string]$map.HashAlgorithm -eq 'SHA256'){64}else{128}
        if([string]$map.ExpectedHash -notmatch "^[a-fA-F0-9]{$length}$"){throw 'Expected download hash is invalid.'}
    }
    $null=Test-WinCareDownloadHeaderMap $map.Headers
    if([string]$map.Priority -notin @('Foreground','High','Normal','Low')){throw 'Download priority is invalid.'}
    if([int]$map.MaxRetries -lt 0 -or [int]$map.MaxRetries -gt 20 -or [int]$map.RetryCount -lt 0 -or [int]$map.RetryCount -gt [int]$map.MaxRetries){throw 'Download retry counters are invalid.'}
    if([string]$map.Status -notin @('Queued','Scheduled','Transferring','Suspended','Transferred','Completed','Failed','Cancelled')){throw 'Download status is invalid.'}
    if([string]$map.BitsJobId -and [string]$map.BitsJobId -notmatch '^[a-fA-F0-9-]{36}$'){throw 'BITS job ID is invalid.'}
    if([long]$map.BytesTotal -lt 0 -or [long]$map.BytesTransferred -lt 0){throw 'Download byte counters are invalid.'}
    foreach($field in @('ScheduledAt','CreatedAt','UpdatedAt','CompletedAt')){if([string]$map[$field]){$dt=[datetimeoffset]::MinValue;if(-not[datetimeoffset]::TryParse([string]$map[$field],[ref]$dt)){throw "Download timestamp is invalid: $field"}}}
    if(([string]$map.LastError).Length -gt 4000){throw 'Download error text is too long.'}
    if($map.SourceRecords -is [string] -or $map.SourceRecords -isnot [Collections.IEnumerable] -or @($map.SourceRecords).Count -gt 64){throw 'Download source records are invalid.'}
    $true
}

function Test-WinCareDownloadStore {
    param([Parameter(Mandatory)][object]$Store)
    $map=ConvertTo-WinCareParameterDictionary $Store
    $null=Test-WinCareStrictObjectKeys -InputObject $map -AllowedKeys @('SchemaVersion','Revision','UpdatedAt','Jobs') -Context 'download store'
    if([int]$map.SchemaVersion -ne 1 -or [long]$map.Revision -lt 0){throw 'Download store schema or revision is invalid.'}
    if($map.Jobs -is [string] -or $map.Jobs -isnot [Collections.IEnumerable]){throw 'Download store Jobs must be an array.'}
    $jobs=@($map.Jobs);$maximumJobs=[int](Get-WinCarePolicy 'MaximumDownloadJobs');if($jobs.Count -gt $maximumJobs){throw "Download store exceeds the policy limit of $maximumJobs jobs."}
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($job in $jobs){$null=Test-WinCareDownloadJobRecord $job;if(-not $ids.Add([string]$job.Id)){throw "Duplicate download job: $($job.Id)"}}
    $true
}

function Get-WinCareDownloadStore {
    [CmdletBinding()]param([switch]$Strict)
    $path=Get-WinCareDownloadStorePath
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){return Get-WinCareDefaultDownloadStore}
    try{$store=Read-WinCareProtectedJson -LiteralPath $path -Purpose 'WinCare.DownloadStore' -AsHashtable;$null=Test-WinCareDownloadStore $store;$store}
    catch{if($Strict){throw};Write-WinCareLog -Level Warning -Message 'Download store is invalid.' -Data @{error=$_.Exception.Message};Get-WinCareDefaultDownloadStore}
}

function Set-WinCareDownloadStoreInternal {
    param([Parameter(Mandatory)][object]$Store)
    $map=ConvertTo-WinCareParameterDictionary $Store;$null=Test-WinCareDownloadStore $map
    Write-WinCareProtectedJson -LiteralPath (Get-WinCareDownloadStorePath) -InputObject $map -Purpose 'WinCare.DownloadStore'
    $map
}

function Get-WinCareDownloadJobRecordFromStore {
    param([Parameter(Mandatory)][object]$Store,[Parameter(Mandatory)][string]$Id)
    @($Store.Jobs|Where-Object{[string]$_.Id -eq $Id}|Select-Object -First 1)
}

function Get-WinCareDownloadRecordHash {
    param([AllowNull()][object]$Record)
    if($null -eq $Record){return 'absent'}
    $map=ConvertTo-WinCareParameterDictionary $Record;$null=Test-WinCareDownloadJobRecord $map;Get-WinCareCanonicalObjectHash $map
}

function New-WinCareDownloadJobRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Url,
        [Parameter(Mandatory)][string]$Destination,
        [string]$Name='',
        [ValidateSet('Fail','Replace')][string]$CollisionPolicy='Fail',
        [ValidateSet('None','SHA256','SHA512')][string]$HashAlgorithm='None',
        [string]$ExpectedHash='',
        [hashtable]$Headers=@{},
        [ValidateSet('Foreground','High','Normal','Low')][string]$Priority='Normal',
        [ValidateRange(0,20)][int]$MaxRetries=3,
        [datetimeoffset]$ScheduledAt,
        [string[]]$SourceRecords=@('SRC:fe3a7d1145')
    )
    Ensure-WinCareState
    $urls=@($Url|ForEach-Object{Test-WinCareDownloadUri ([string]$_)}|Select-Object -Unique)
    if($urls.Count -lt 1 -or $urls.Count -gt 8){throw 'Provide 1..8 unique download URLs.'}
    $roots=Get-WinCareDownloadDestinationRoots;$destinationPath=Assert-WinCareSafePath -LiteralPath $Destination -AllowedRoots $roots -AllowMissing
    if(-not $Name){$Name=[IO.Path]::GetFileName($destinationPath)}
    if(-not $PSBoundParameters.ContainsKey('ScheduledAt')){$ScheduledAt=[datetimeoffset]::MinValue}
    $id=[guid]::NewGuid().ToString('N');$partial=Join-Path (Join-Path (Get-WinCareDownloadRoot) 'Partial') "$id.part"
    $now=[datetime]::UtcNow.ToString('o');$scheduled=if($ScheduledAt -gt [datetimeoffset]::MinValue){$ScheduledAt.ToUniversalTime().ToString('o')}else{''}
    $status=if($scheduled -and [datetimeoffset]::Parse($scheduled) -gt [datetimeoffset]::UtcNow){'Scheduled'}else{'Queued'}
    $record=[ordered]@{SchemaVersion=1;Id=$id;Revision=0;Name=$Name;Urls=$urls;SourceIndex=0;Destination=$destinationPath;TemporaryPath=$partial;CollisionPolicy=$CollisionPolicy;ExpectedHash=$ExpectedHash.ToLowerInvariant();HashAlgorithm=$HashAlgorithm;Headers=$Headers;Priority=$Priority;MaxRetries=$MaxRetries;RetryCount=0;ScheduledAt=$scheduled;Status=$status;BitsJobId='';BytesTotal=0;BytesTransferred=0;CreatedAt=$now;UpdatedAt=$now;CompletedAt='';LastError='';SourceRecords=@($SourceRecords)}
    $null=Test-WinCareDownloadJobRecord $record
    $record
}

function Get-WinCareDownloadJob {
    [CmdletBinding()]param([string]$Id,[switch]$IncludeTerminal)
    Ensure-WinCareState;$store=Get-WinCareDownloadStore -Strict
    $jobs=@($store.Jobs);if($Id){$jobs=@($jobs|Where-Object{[string]$_.Id -eq $Id})};if(-not $IncludeTerminal){$jobs=@($jobs|Where-Object{[string]$_.Status -notin @('Completed','Cancelled')})}
    foreach($job in $jobs|Sort-Object CreatedAt -Descending){
        $map=ConvertTo-WinCareParameterDictionary $job;$liveState='';$liveError=''
        if([string]$map.BitsJobId -and $IsWindows -and (Get-Command Get-BitsTransfer -ErrorAction SilentlyContinue)){
            try{$bits=Get-BitsTransfer -Id ([guid]$map.BitsJobId) -ErrorAction Stop;$liveState=[string]$bits.JobState;$map.BytesTotal=[long]$bits.BytesTotal;$map.BytesTransferred=[long]$bits.BytesTransferred}
            catch{$liveError=$_.Exception.Message}
        }
        [pscustomobject]@{Id=$map.Id;Name=$map.Name;Status=$map.Status;LiveState=$liveState;Source=[string]$map.Urls[[int]$map.SourceIndex];Destination=$map.Destination;ExpectedHash=$map.ExpectedHash;HashAlgorithm=$map.HashAlgorithm;Priority=$map.Priority;RetryCount=[int]$map.RetryCount;MaxRetries=[int]$map.MaxRetries;BytesTotal=[long]$map.BytesTotal;BytesTransferred=[long]$map.BytesTransferred;ProgressPercent=if([long]$map.BytesTotal -gt 0){[math]::Round(([long]$map.BytesTransferred/[long]$map.BytesTotal)*100,1)}else{0};ScheduledAt=$map.ScheduledAt;LastError=if($liveError){$liveError}else{$map.LastError};RecordHash=Get-WinCareDownloadRecordHash $map;Revision=[long]$map.Revision}
    }
}

function New-WinCareDownloadStoreReplacementPlan {
    param([Parameter(Mandatory)][object]$Current,[Parameter(Mandatory)][object]$Next,[Parameter(Mandatory)][string]$Title,[string[]]$SourceRecords=@())
    $currentMap=ConvertTo-WinCareParameterDictionary $Current;$nextMap=ConvertTo-WinCareParameterDictionary $Next
    $null=Test-WinCareDownloadStore $currentMap;$null=Test-WinCareDownloadStore $nextMap
    $beforeHash=Get-WinCareDownloadStoreHash $currentMap;$afterHash=Get-WinCareDownloadStoreHash $nextMap
    $action=New-WinCareAction -Type ReplaceDownloadStore -Label $Title -Risk Low -Parameters @{Store=$nextMap;ExpectedHash=$beforeHash} -Reversible $true -Compensator @{Type='ReplaceDownloadStore';RequiresAdmin=$false;Parameters=@{Store=$currentMap;ExpectedHash=$afterHash}} -Tags @('Downloads','Store','Idempotent') -SourceRecords $SourceRecords -RecoveryDescription 'Restore the exact previous protected download queue store.'
    New-WinCarePlan -Title $Title -Description 'Update the protected download queue using compare-and-swap.' -Actions @($action) -SourceRecords $SourceRecords
}

function New-WinCareDownloadJobPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Job)
    $jobMap=ConvertTo-WinCareParameterDictionary $Job;$null=Test-WinCareDownloadJobRecord $jobMap
    $store=Get-WinCareDownloadStore -Strict;if(@(Get-WinCareDownloadJobRecordFromStore $store $jobMap.Id).Count){throw 'Download job already exists.'}
    $next=ConvertTo-WinCareParameterDictionary $store;$next.Revision=[long]$store.Revision+1;$next.UpdatedAt=[datetime]::UtcNow.ToString('o');$next.Jobs=@($store.Jobs)+@($jobMap)
    New-WinCareDownloadStoreReplacementPlan -Current $store -Next $next -Title "Queue download: $($jobMap.Name)" -SourceRecords @($jobMap.SourceRecords)
}

function New-WinCareDownloadJobRemovePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $store=Get-WinCareDownloadStore -Strict;$record=@(Get-WinCareDownloadJobRecordFromStore $store $Id)
    if(-not $record.Count){throw 'Download job was not found.'};if([string]$record[0].Status -in @('Transferring','Suspended','Transferred')){throw 'Active download jobs must be cancelled before removal.'}
    $next=ConvertTo-WinCareParameterDictionary $store;$next.Revision=[long]$store.Revision+1;$next.UpdatedAt=[datetime]::UtcNow.ToString('o');$next.Jobs=@($store.Jobs|Where-Object{[string]$_.Id -ne $Id})
    New-WinCareDownloadStoreReplacementPlan -Current $store -Next $next -Title "Remove download record: $($record[0].Name)" -SourceRecords @($record[0].SourceRecords)
}

function New-WinCareDownloadStartPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $store=Get-WinCareDownloadStore -Strict;$record=@(Get-WinCareDownloadJobRecordFromStore $store $Id)|Select-Object -First 1
    if(-not $record){throw 'Download job was not found.'};if([string]$record.Status -notin @('Queued','Scheduled','Failed')){throw "Download cannot start from state $($record.Status)."}
    if([string]$record.ScheduledAt -and [datetimeoffset]::Parse([string]$record.ScheduledAt) -gt [datetimeoffset]::UtcNow){throw 'Download is scheduled for a future time.'}
    $action=New-WinCareAction -Type StartDownloadTransfer -Label "Start download: $($record.Name)" -Risk Low -Parameters @{Id=$Id;ExpectedRecordHash=Get-WinCareDownloadRecordHash $record} -Reversible $true -Tags @('Downloads','BITS','Network') -SourceRecords @($record.SourceRecords) -RecoveryDescription 'Cancel the exact BITS transfer and remove its private partial file.' -TimeoutSeconds 120
    New-WinCarePlan -Title "Start download: $($record.Name)" -Description 'Create an asynchronous BITS transfer using the exact queued source and protected temporary path.' -Actions @($action) -SourceRecords @($record.SourceRecords)
}

function New-WinCareDownloadStatePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id,[Parameter(Mandatory)][ValidateSet('Suspended','Transferring')][string]$State)
    $record=Get-WinCareDownloadStore -Strict|ForEach-Object{Get-WinCareDownloadJobRecordFromStore $_ $Id}|Select-Object -First 1
    if(-not $record){throw 'Download job was not found.'}
    $allowed=if($State -eq 'Suspended'){@('Transferring')}else{@('Suspended')};if([string]$record.Status -notin $allowed){throw "Download cannot enter $State from $($record.Status)."}
    $action=New-WinCareAction -Type SetDownloadTransferState -Label "$State download: $($record.Name)" -Risk Low -Parameters @{Id=$Id;State=$State;ExpectedRecordHash=Get-WinCareDownloadRecordHash $record} -Tags @('Downloads','BITS') -SourceRecords @($record.SourceRecords) -TimeoutSeconds 120
    New-WinCarePlan -Title "$State download" -Actions @($action) -SourceRecords @($record.SourceRecords)
}

function New-WinCareDownloadReconcilePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $record=Get-WinCareDownloadStore -Strict|ForEach-Object{Get-WinCareDownloadJobRecordFromStore $_ $Id}|Select-Object -First 1
    if(-not $record){throw 'Download job was not found.'};if([string]$record.Status -notin @('Transferring','Suspended','Transferred','Failed')){throw 'Only active or failed download jobs can be reconciled.'}
    $action=New-WinCareAction -Type ReconcileDownloadTransfer -Label "Reconcile download: $($record.Name)" -Risk Low -Parameters @{Id=$Id;ExpectedRecordHash=Get-WinCareDownloadRecordHash $record} -Tags @('Downloads','BITS','Verification') -SourceRecords @($record.SourceRecords) -TimeoutSeconds 600
    New-WinCarePlan -Title "Reconcile download: $($record.Name)" -Description 'Refresh BITS state and atomically finalize a transferred file after integrity verification.' -Actions @($action) -SourceRecords @($record.SourceRecords)
}

function New-WinCareDownloadCancelPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $record=Get-WinCareDownloadStore -Strict|ForEach-Object{Get-WinCareDownloadJobRecordFromStore $_ $Id}|Select-Object -First 1
    if(-not $record){throw 'Download job was not found.'};if([string]$record.Status -in @('Completed','Cancelled')){throw 'Download is already terminal.'}
    $action=New-WinCareAction -Type CancelDownloadTransfer -Label "Cancel download: $($record.Name)" -Risk Moderate -Parameters @{Id=$Id;ExpectedRecordHash=Get-WinCareDownloadRecordHash $record} -Tags @('Downloads','BITS','Destructive') -SourceRecords @($record.SourceRecords) -RecoveryDescription 'Cancellation removes the partial transfer and is not automatically reversible.' -TimeoutSeconds 120
    New-WinCarePlan -Title "Cancel download: $($record.Name)" -Description 'Cancel the exact BITS job and delete only its WinCare-owned partial file.' -Actions @($action) -RollbackOnFailure $false -SourceRecords @($record.SourceRecords)
}

function Set-WinCareDownloadRecordInStore {
    param([Parameter(Mandatory)][object]$Store,[Parameter(Mandatory)][object]$Record)
    $map=ConvertTo-WinCareParameterDictionary $Record;$null=Test-WinCareDownloadJobRecord $map
    $jobs=[Collections.Generic.List[object]]::new();$found=$false
    foreach($job in @($Store.Jobs)){if([string]$job.Id -eq [string]$map.Id){$jobs.Add($map);$found=$true}else{$jobs.Add($job)}}
    if(-not $found){throw 'Download record disappeared during update.'}
    [ordered]@{SchemaVersion=1;Revision=([long]$Store.Revision+1);UpdatedAt=[datetime]::UtcNow.ToString('o');Jobs=@($jobs)}
}

function ConvertTo-WinCareBitsHeaders {
    param([AllowNull()][object]$Headers)
    $null=Test-WinCareDownloadHeaderMap $Headers;$map=ConvertTo-WinCareParameterDictionary $Headers
    @($map.Keys|Sort-Object|ForEach-Object{"$_`: $($map[$_])"})
}

function Get-WinCareBitsPriority {
    param([string]$Priority)
    switch($Priority){'Foreground'{'Foreground'};'High'{'High'};'Low'{'Low'};default{'Normal'}}
}

function Invoke-WinCareReplaceDownloadStoreAction {
    param([Parameter(Mandatory)][object]$Action)
    $current=Get-WinCareDownloadStore -Strict;$currentHash=Get-WinCareDownloadStoreHash $current
    if($currentHash -ne [string]$Action.Parameters.ExpectedHash){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadStoreChanged' -Message 'Download queue changed after preview.' -ExitCode 74}
    $next=ConvertTo-WinCareParameterDictionary $Action.Parameters.Store;$null=Test-WinCareDownloadStore $next
    try{$null=Set-WinCareDownloadStoreInternal $next;$verify=Get-WinCareDownloadStore -Strict;if((Get-WinCareDownloadStoreHash $verify) -ne (Get-WinCareDownloadStoreHash $next)){throw 'Download store verification failed.'};New-WinCareResult -Success $true -Code 'DownloadStoreReplaced' -Message 'Download queue store updated and verified.' -Data @{Revision=$verify.Revision;Jobs=@($verify.Jobs).Count}}
    catch{$primary=$_.Exception.Message;$rollbackError='';try{$null=Set-WinCareDownloadStoreInternal $current}catch{$rollbackError=$_.Exception.Message};New-WinCareResult -Success $false -Code $(if($rollbackError){'DownloadStoreRollbackFailed'}else{'DownloadStoreFailedRolledBack'}) -Message $(if($rollbackError){"$primary Rollback failed: $rollbackError"}else{"$primary The previous store was restored."}) -ExitCode 1}
}

function Invoke-WinCareStartDownloadTransferAction {
    param([Parameter(Mandatory)][object]$Action)
    if(-not $IsWindows){return New-WinCareResult -Success $false -Status Blocked -Code 'WindowsRequired' -Message 'BITS downloads require Windows.' -ExitCode 3}
    if(-not(Get-Command Start-BitsTransfer -ErrorAction SilentlyContinue)){try{Import-Module BitsTransfer -ErrorAction Stop}catch{return New-WinCareResult -Success $false -Status Blocked -Code 'BitsUnavailable' -Message $_.Exception.Message -ExitCode 3}}
    $store=Get-WinCareDownloadStore -Strict;$record=@(Get-WinCareDownloadJobRecordFromStore $store ([string]$Action.Parameters.Id))|Select-Object -First 1
    if(-not $record){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadNotFound' -Message 'Download job was not found.' -ExitCode 2}
    if((Get-WinCareDownloadRecordHash $record) -ne [string]$Action.Parameters.ExpectedRecordHash){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadChanged' -Message 'Download job changed after preview.' -ExitCode 74}
    if([string]$record.Status -notin @('Queued','Scheduled','Failed')){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadStateInvalid' -Message "Download cannot start from state $($record.Status)." -ExitCode 22}
    $maximumConcurrent=[int](Get-WinCareConfig 'DownloadMaximumConcurrent')
    if((Get-WinCareActiveDownloadCount) -ge $maximumConcurrent){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadConcurrencyLimit' -Message "The configured limit of $maximumConcurrent concurrent downloads has been reached." -ExitCode 75}
    $maximumDownloadBytes=[long](Get-WinCarePolicy 'MaximumDownloadBytes');$effectiveByteLimit=[long](Get-WinCareDownloadByteLimit)
    if($effectiveByteLimit -gt $maximumDownloadBytes){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadPolicyLimitInvalid' -Message 'Effective download limit exceeds policy MaximumDownloadBytes.' -ExitCode 74}
    $sourceIndex=[int]$record.SourceIndex;if([string]$record.Status -eq 'Failed'){$sourceIndex=([int]$record.SourceIndex+1)%@($record.Urls).Count}
    $source=[string]$record.Urls[$sourceIndex]
    $source=Test-WinCareDownloadUri $source
    $temp=Assert-WinCareSafePath -LiteralPath ([string]$record.TemporaryPath) `
        -AllowedRoots @((Join-Path (Get-WinCareDownloadRoot) 'Partial')) -AllowMissing
    if(Test-Path -LiteralPath $temp){$existing=Get-Item -LiteralPath $temp -Force;if($existing.Attributes -band [IO.FileAttributes]::ReparsePoint){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadPartialReparsePoint' -Message 'Existing download partial is a reparse point.' -ExitCode 5};Remove-Item -LiteralPath $temp -Force -ErrorAction Stop}
    $bits=$null
    try{
        $params=@{Source=$source;Destination=$temp;Asynchronous=$true;DisplayName="WinCare:$($record.Id)";Description="WinCare verified download: $($record.Name)";Priority=(Get-WinCareBitsPriority $record.Priority);ErrorAction='Stop'}
        $headers=@(ConvertTo-WinCareBitsHeaders $record.Headers);if($headers.Count){$params.CustomHeaders=$headers}
        $bits=Start-BitsTransfer @params
        if([long]$bits.BytesTotal -gt 0 -and [long]$bits.BytesTotal -gt $effectiveByteLimit){try{Remove-BitsTransfer -BitsJob $bits -Confirm:$false -ErrorAction Stop}catch{Write-WinCareLog -Level Warning -Message 'Oversized BITS job could not be removed immediately.' -Data @{error=$_.Exception.Message}};throw "Download size exceeds policy MaximumDownloadBytes/effective limit of $effectiveByteLimit bytes."}
        $copy=ConvertTo-WinCareParameterDictionary $record;$copy.SourceIndex=$sourceIndex;$copy.Status='Transferring';$copy.BitsJobId=[string]$bits.JobId;$copy.BytesTotal=[long]$bits.BytesTotal;$copy.BytesTransferred=[long]$bits.BytesTransferred;$copy.UpdatedAt=[datetime]::UtcNow.ToString('o');$copy.LastError='';$copy.Revision=[long]$copy.Revision+1
        $next=Set-WinCareDownloadRecordInStore -Store $store -Record $copy;$null=Set-WinCareDownloadStoreInternal $next
        $saved=Get-WinCareDownloadStore -Strict|ForEach-Object{Get-WinCareDownloadJobRecordFromStore $_ $copy.Id}|Select-Object -First 1
        if(-not $saved -or [string]$saved.BitsJobId -ne [string]$bits.JobId){throw 'BITS transfer state could not be verified in the protected store.'}
        $compensator=@{Type='CancelDownloadTransfer';RequiresAdmin=$false;Parameters=@{Id=[string]$copy.Id;ExpectedRecordHash=Get-WinCareDownloadRecordHash $saved}}
        New-WinCareResult -Success $true -Code 'DownloadTransferStarted' -Message 'BITS transfer started and bound to the protected queue record.' -Data @{Job=$saved;MaximumDownloadBytes=$maximumDownloadBytes;EffectiveByteLimit=$effectiveByteLimit;Compensator=$compensator;EvidenceType='BitsJobAndProtectedRecord'}
    }catch{
        $primary=$_.Exception.Message
        if($bits){try{Remove-BitsTransfer -BitsJob $bits -Confirm:$false -ErrorAction Stop}catch{Write-WinCareLog -Level Warning -Message 'Failed BITS transfer could not be removed.' -Data @{error=$_.Exception.Message}}}
        try{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force -ErrorAction Stop}}catch{Write-WinCareLog -Level Warning -Message 'Failed download partial could not be removed.' -Data @{error=$_.Exception.Message}}
        New-WinCareResult -Success $false -Code 'DownloadStartFailed' -Message $primary -ExitCode 1
    }
}

function Invoke-WinCareSetDownloadTransferStateAction {
    param([Parameter(Mandatory)][object]$Action)
    $store=Get-WinCareDownloadStore -Strict;$record=@(Get-WinCareDownloadJobRecordFromStore $store ([string]$Action.Parameters.Id))|Select-Object -First 1
    if(-not $record){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadNotFound' -Message 'Download job was not found.' -ExitCode 2}
    if((Get-WinCareDownloadRecordHash $record) -ne [string]$Action.Parameters.ExpectedRecordHash){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadChanged' -Message 'Download job changed after preview.' -ExitCode 74}
    try{
        $bits=Get-BitsTransfer -Id ([guid]$record.BitsJobId) -ErrorAction Stop;$state=[string]$Action.Parameters.State
        if($state -eq 'Suspended'){Suspend-BitsTransfer -BitsJob $bits -ErrorAction Stop}else{Resume-BitsTransfer -BitsJob $bits -Asynchronous -ErrorAction Stop}
        $copy=ConvertTo-WinCareParameterDictionary $record;$copy.Status=$state;$copy.UpdatedAt=[datetime]::UtcNow.ToString('o');$copy.Revision=[long]$copy.Revision+1
        $next=Set-WinCareDownloadRecordInStore $store $copy;$null=Set-WinCareDownloadStoreInternal $next
        New-WinCareResult -Success $true -Code 'DownloadStateChanged' -Message "Download state changed to $state." -Data $copy
    }catch{New-WinCareResult -Success $false -Code 'DownloadStateChangeFailed' -Message $_.Exception.Message -ExitCode 1}
}

function Complete-WinCareDownloadedFile {
    param([Parameter(Mandatory)][object]$Record)
    $temp=Assert-WinCareSafePath -LiteralPath ([string]$Record.TemporaryPath) -AllowedRoots @((Join-Path (Get-WinCareDownloadRoot) 'Partial'))
    if(-not(Test-Path -LiteralPath $temp -PathType Leaf)){throw 'BITS completed without the expected partial file.'}
    $tempItem=Get-Item -LiteralPath $temp -Force -ErrorAction Stop
    if($tempItem.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Download partial file is a reparse point.'}
    $byteLimit=Get-WinCareDownloadByteLimit
    if([long]$tempItem.Length -gt $byteLimit){throw "Downloaded file exceeds the effective byte limit of $byteLimit bytes."}
    $expectedHash=''
    if([string]$Record.HashAlgorithm -ne 'None'){
        $expectedHash=[string]$Record.ExpectedHash
        $actual=(Get-FileHash -LiteralPath $temp -Algorithm ([string]$Record.HashAlgorithm) -ErrorAction Stop).Hash.ToLowerInvariant()
        if($actual -ne $expectedHash){throw "Download hash mismatch. Expected $expectedHash; actual $actual."}
    }
    $destination=Assert-WinCareSafePath -LiteralPath ([string]$Record.Destination) -AllowedRoots (Get-WinCareDownloadDestinationRoots) -AllowMissing
    $parent=Split-Path -Parent $destination
    $null=New-Item -ItemType Directory -Path $parent -Force
    $parent=Assert-WinCareSafePath -LiteralPath $parent -AllowedRoots (Get-WinCareDownloadDestinationRoots)
    $leaf=[IO.Path]::GetFileName($destination)
    $staging=Join-Path $parent ".$leaf.wincare-stage-$([guid]::NewGuid().ToString('N'))"
    $tombstone=Join-Path $parent ".$leaf.wincare-old-$([guid]::NewGuid().ToString('N'))"
    $hadExisting=$false;$committed=$false
    try{
        Copy-Item -LiteralPath $temp -Destination $staging -Force:$false -ErrorAction Stop
        $stageItem=Get-Item -LiteralPath $staging -Force -ErrorAction Stop
        if($stageItem.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Download staging file is a reparse point.'}
        if([long]$stageItem.Length -ne [long]$tempItem.Length){throw 'Download staging length verification failed.'}
        if($expectedHash){
            $stageHash=(Get-FileHash -LiteralPath $staging -Algorithm ([string]$Record.HashAlgorithm) -ErrorAction Stop).Hash.ToLowerInvariant()
            if($stageHash -ne $expectedHash){throw 'Download staging hash verification failed.'}
        }
        if(Test-Path -LiteralPath $destination){
            if([string]$Record.CollisionPolicy -ne 'Replace'){throw 'Download destination already exists.'}
            $existing=Get-Item -LiteralPath $destination -Force -ErrorAction Stop
            if($existing.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Download destination is a reparse point.'}
            if($existing.PSIsContainer){throw 'Download destination is a directory.'}
            [IO.File]::Move($destination,$tombstone);$hadExisting=$true
        }
        [IO.File]::Move($staging,$destination);$committed=$true
        $final=Get-Item -LiteralPath $destination -Force -ErrorAction Stop
        if($final.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Final download is a reparse point.'}
        if([long]$final.Length -ne [long]$tempItem.Length){throw 'Final download length verification failed.'}
        if($expectedHash){
            $finalHash=(Get-FileHash -LiteralPath $destination -Algorithm ([string]$Record.HashAlgorithm) -ErrorAction Stop).Hash.ToLowerInvariant()
            if($finalHash -ne $expectedHash){throw 'Final download hash verification failed.'}
        }
        Remove-Item -LiteralPath $temp -Force -ErrorAction Stop
        if($hadExisting -and (Test-Path -LiteralPath $tombstone)){Remove-Item -LiteralPath $tombstone -Force -ErrorAction Stop}
        [pscustomobject]@{Destination=$destination;Bytes=[long]$final.Length;Hash=$expectedHash;Replaced=$hadExisting}
    }catch{
        $primary=$_.Exception.Message
        if($committed -and (Test-Path -LiteralPath $destination)){try{Remove-Item -LiteralPath $destination -Force -ErrorAction Stop}catch{Write-WinCareLog -Level Warning -Message 'Failed final download could not be removed during rollback.' -Data @{error=$_.Exception.Message}}}
        if($hadExisting -and (Test-Path -LiteralPath $tombstone) -and -not(Test-Path -LiteralPath $destination)){try{[IO.File]::Move($tombstone,$destination)}catch{throw "$primary Existing destination rollback failed: $($_.Exception.Message)"}}
        if(Test-Path -LiteralPath $staging){try{Remove-Item -LiteralPath $staging -Force -ErrorAction Stop}catch{Write-WinCareLog -Level Warning -Message 'Download staging file could not be removed.' -Data @{error=$_.Exception.Message}}}
        throw $primary
    }
}

function Get-WinCareDueDownloadJob {
    [CmdletBinding()]param()
    $now=[datetimeoffset]::UtcNow
    @(Get-WinCareDownloadJob -IncludeTerminal|Where-Object{
        [string]$_.Status -eq 'Queued' -or
        ([string]$_.Status -eq 'Scheduled' -and [string]$_.ScheduledAt -and [datetimeoffset]::Parse([string]$_.ScheduledAt) -le $now)
    }|Sort-Object ScheduledAt,Name)
}

function New-WinCareDueDownloadStartPlan {
    [CmdletBinding()]param()
    $available=[math]::Max(0,[int](Get-WinCareConfig 'DownloadMaximumConcurrent')-(Get-WinCareActiveDownloadCount))
    if($available -le 0){throw 'No download concurrency slots are available.'}
    $due=@(Get-WinCareDueDownloadJob|Select-Object -First $available)
    if(-not $due.Count){throw 'No queued or due scheduled downloads are ready to start.'}
    $actions=[Collections.Generic.List[object]]::new();$sources=[Collections.Generic.List[string]]::new()
    $store=Get-WinCareDownloadStore -Strict
    foreach($job in $due){
        $record=Get-WinCareDownloadJobRecordFromStore $store ([string]$job.Id)|Select-Object -First 1
        $actions.Add((New-WinCareAction -Type StartDownloadTransfer -Label "Start due download: $($record.Name)" -Risk Low -Parameters @{Id=[string]$record.Id;ExpectedRecordHash=Get-WinCareDownloadRecordHash $record} -Reversible $true -Tags @('Downloads','BITS','Scheduled') -SourceRecords @($record.SourceRecords) -RecoveryDescription 'Cancel the exact BITS transfer and remove its private partial file.' -TimeoutSeconds 120))
        foreach($source in @($record.SourceRecords)){if(-not $sources.Contains([string]$source)){$sources.Add([string]$source)}}
    }
    New-WinCarePlan -Title 'Start queued and due downloads' -Description "Start up to $available downloads without exceeding the configured concurrency limit." -Actions @($actions) -SourceRecords @($sources)
}

function Invoke-WinCareReconcileDownloadTransferAction {
    param([Parameter(Mandatory)][object]$Action)
    $store=Get-WinCareDownloadStore -Strict;$record=@(Get-WinCareDownloadJobRecordFromStore $store ([string]$Action.Parameters.Id))|Select-Object -First 1
    if(-not $record){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadNotFound' -Message 'Download job was not found.' -ExitCode 2}
    if((Get-WinCareDownloadRecordHash $record) -ne [string]$Action.Parameters.ExpectedRecordHash){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadChanged' -Message 'Download job changed after preview.' -ExitCode 74}
    $copy=ConvertTo-WinCareParameterDictionary $record;$maximumDownloadBytes=[long](Get-WinCarePolicy 'MaximumDownloadBytes');$effectiveByteLimit=[long](Get-WinCareDownloadByteLimit)
    try{
        $bits=Get-BitsTransfer -Id ([guid]$record.BitsJobId) -ErrorAction Stop;$copy.BytesTotal=[long]$bits.BytesTotal;$copy.BytesTransferred=[long]$bits.BytesTransferred
        if([long]$bits.BytesTotal -gt 0 -and [long]$bits.BytesTotal -gt $effectiveByteLimit){try{Remove-BitsTransfer -BitsJob $bits -Confirm:$false -ErrorAction Stop}catch{Write-WinCareLog -Level Warning -Message 'Oversized BITS job could not be removed during reconciliation.' -Data @{error=$_.Exception.Message}};throw "Download size exceeds policy MaximumDownloadBytes/effective limit of $effectiveByteLimit bytes."}
        switch([string]$bits.JobState){
            'Transferred'{
                Complete-BitsTransfer -BitsJob $bits -ErrorAction Stop
                $partial=Get-Item -LiteralPath ([string]$record.TemporaryPath) -Force -ErrorAction Stop
                if($partial.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Downloaded partial is a ReparsePoint.'}
                if([long]$partial.Length -gt $effectiveByteLimit){throw "Downloaded file exceeds MaximumDownloadBytes/effective limit of $effectiveByteLimit bytes."}
                if([string]$record.HashAlgorithm -ne 'None'){$actual=Get-FileHash -LiteralPath $partial.FullName -Algorithm ([string]$record.HashAlgorithm) -ErrorAction Stop;if($actual.Hash.ToLowerInvariant() -ne [string]$record.ExpectedHash){throw 'Download hash mismatch before commit.'}}
                $completion=Complete-WinCareDownloadedFile -Record $record
                $copy.Status='Completed';$copy.CompletedAt=[datetime]::UtcNow.ToString('o');$copy.BitsJobId='';$copy.LastError='';$copy.BytesTotal=[long]$completion.Bytes;$copy.BytesTransferred=[long]$completion.Bytes
            }
            'Suspended'{$copy.Status='Suspended'}
            'Error'{$copy.Status='Failed';$copy.LastError=[string]$bits.ErrorDescription;$copy.RetryCount=[math]::Min([int]$copy.MaxRetries,[int]$copy.RetryCount+1)}
            'TransientError'{$copy.Status='Transferring';$copy.LastError=[string]$bits.ErrorDescription}
            'Cancelled'{$copy.Status='Cancelled';$copy.BitsJobId=''}
            default{$copy.Status='Transferring'}
        }
        $copy.UpdatedAt=[datetime]::UtcNow.ToString('o');$copy.Revision=[long]$copy.Revision+1
        $next=Set-WinCareDownloadRecordInStore $store $copy;$null=Set-WinCareDownloadStoreInternal $next
        New-WinCareResult -Success $true -Code 'DownloadReconciled' -Message "Download reconciled to $($copy.Status)." -Data @{Job=$copy;MaximumDownloadBytes=$maximumDownloadBytes;EffectiveByteLimit=$effectiveByteLimit;EvidenceType='BitsStateAndVerifiedFile'}
    }catch{
        $copy.Status='Failed';$copy.LastError=$_.Exception.Message;$copy.UpdatedAt=[datetime]::UtcNow.ToString('o');$copy.Revision=[long]$copy.Revision+1
        try{$next=Set-WinCareDownloadRecordInStore $store $copy;$null=Set-WinCareDownloadStoreInternal $next}catch{Write-WinCareLog -Level Warning -Message 'Download failure state could not be persisted.' -Data @{error=$_.Exception.Message}}
        New-WinCareResult -Success $false -Code 'DownloadReconcileFailed' -Message $copy.LastError -ExitCode 1 -Data @{Job=$copy;MaximumDownloadBytes=$maximumDownloadBytes;EffectiveByteLimit=$effectiveByteLimit}
    }
}

function Invoke-WinCareCancelDownloadTransferAction {
    param([Parameter(Mandatory)][object]$Action)
    $store=Get-WinCareDownloadStore -Strict;$record=@(Get-WinCareDownloadJobRecordFromStore $store ([string]$Action.Parameters.Id))|Select-Object -First 1
    if(-not $record){return New-WinCareResult -Success $true -Code 'DownloadAlreadyAbsent' -Message 'Download job is already absent.'}
    if((Get-WinCareDownloadRecordHash $record) -ne [string]$Action.Parameters.ExpectedRecordHash){return New-WinCareResult -Success $false -Status Blocked -Code 'DownloadChanged' -Message 'Download job changed after preview.' -ExitCode 74}
    $warnings=[Collections.Generic.List[string]]::new()
    if([string]$record.BitsJobId){try{$bits=Get-BitsTransfer -Id ([guid]$record.BitsJobId) -ErrorAction Stop;Remove-BitsTransfer -BitsJob $bits -Confirm:$false -ErrorAction Stop}catch{$warnings.Add("BITS removal: $($_.Exception.Message)")}}
    try{$temp=Assert-WinCareSafePath -LiteralPath ([string]$record.TemporaryPath) -AllowedRoots @((Join-Path (Get-WinCareDownloadRoot) 'Partial')) -AllowMissing;if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force -ErrorAction Stop}}catch{$warnings.Add("Partial removal: $($_.Exception.Message)")}
    $copy=ConvertTo-WinCareParameterDictionary $record;$copy.Status='Cancelled';$copy.BitsJobId='';$copy.UpdatedAt=[datetime]::UtcNow.ToString('o');$copy.Revision=[long]$copy.Revision+1
    try{$next=Set-WinCareDownloadRecordInStore $store $copy;$null=Set-WinCareDownloadStoreInternal $next}catch{return New-WinCareResult -Success $false -Code 'DownloadCancelStoreFailed' -Message $_.Exception.Message -ExitCode 1 -Warnings @($warnings)}
    New-WinCareResult -Success $true -Status $(if($warnings.Count){'SucceededWithWarnings'}else{'Succeeded'}) -Code 'DownloadCancelled' -Message 'Download transfer was cancelled and its private partial file was removed.' -Data $copy -Warnings @($warnings)
}


