function Get-WinCareBoundedRemoteSupportConfigFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [ValidateRange(1,5000)][int]$MaximumFiles=500,
        [ValidateRange(1,32)][int]$MaximumDepth=8,
        [ValidateRange(1,10000)][int]$MaximumDirectories=1000
    )
    if(-not(Test-Path -LiteralPath $Root -PathType Container)){return @()}
    $safeRoot=Assert-WinCareSafePath -LiteralPath $Root -AllowedRoots @($Root)
    $rootItem=Get-Item -LiteralPath $safeRoot -Force -ErrorAction Stop
    if(($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw "Remote-support configuration root is a reparse point: $safeRoot"}
    $queue=[Collections.Generic.Queue[object]]::new()
    $queue.Enqueue([pscustomobject]@{Path=$safeRoot;Depth=0})
    $files=[Collections.Generic.List[object]]::new();$visitedDirectories=0
    while($queue.Count -gt 0 -and $files.Count -lt $MaximumFiles){
        $entry=$queue.Dequeue();$visitedDirectories++
        if($visitedDirectories -gt $MaximumDirectories){
            Write-WinCareLog -Level Warning -Message 'Remote-support configuration traversal reached its directory bound.' -Data @{root=$safeRoot;maximumDirectories=$MaximumDirectories}
            break
        }
        try{$children=@(Get-ChildItem -LiteralPath $entry.Path -Force -ErrorAction Stop)}
        catch{Write-WinCareLog -Level Warning -Message 'A remote-support configuration directory could not be enumerated.' -Data @{path=$entry.Path;error=$_.Exception.Message};continue}
        foreach($child in $children|Sort-Object FullName){
            if(($child.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){
                Write-WinCareLog -Level Warning -Message 'Skipping a reparse point in a remote-support configuration tree.' -Data @{path=$child.FullName}
                continue
            }
            if($child.PSIsContainer){
                if([int]$entry.Depth -lt $MaximumDepth){
                    try{$directoryPath=Assert-WinCareSafePath -LiteralPath $child.FullName -AllowedRoots @($safeRoot);$queue.Enqueue([pscustomobject]@{Path=$directoryPath;Depth=[int]$entry.Depth+1})}
                    catch{Write-WinCareLog -Level Warning -Message 'Skipping an unsafe remote-support configuration directory.' -Data @{path=$child.FullName;error=$_.Exception.Message}}
                }
                continue
            }
            try{
                $filePath=Assert-WinCareSafePath -LiteralPath $child.FullName -AllowedRoots @($safeRoot)
                $files.Add([pscustomobject]@{Path=$filePath;Length=[long]$child.Length;Extension=[string]$child.Extension})
            }catch{Write-WinCareLog -Level Warning -Message 'Skipping an unsafe remote-support configuration file.' -Data @{path=$child.FullName;error=$_.Exception.Message}}
            if($files.Count -ge $MaximumFiles){break}
        }
    }
    @($files)
}

function Get-WinCareRemoteSupportCatalog {
    @(
        [pscustomobject]@{Id='rustdesk';Name='RustDesk';ProcessNames=@('rustdesk');ServicePatterns=@('RustDesk*');AppPattern='(?i)rustdesk';ConfigRoots=@("${env}:APPDATA\RustDesk","${env}:ProgramData\RustDesk")},
        [pscustomobject]@{Id='mousekeyproxy';Name='MouseKeyProxy';ProcessNames=@('MouseKeyProxy.Agent','MouseKeyProxy.Service','mkp');ServicePatterns=@('MouseKeyProxy*');AppPattern='(?i)mousekeyproxy';ConfigRoots=@("${env}:LOCALAPPDATA\MouseKeyProxy","${env}:ProgramData\MouseKeyProxy")},
        [pscustomobject]@{Id='anydesk';Name='AnyDesk';ProcessNames=@('AnyDesk','AnyDeskMSI');ServicePatterns=@('AnyDesk*');AppPattern='(?i)anydesk';ConfigRoots=@("${env}:APPDATA\AnyDesk","${env}:ProgramData\AnyDesk")},
        [pscustomobject]@{Id='teamviewer';Name='TeamViewer';ProcessNames=@('TeamViewer','TeamViewer_Service','TeamViewer_Desktop');ServicePatterns=@('TeamViewer*');AppPattern='(?i)teamviewer';ConfigRoots=@("${env}:APPDATA\TeamViewer","${env}:ProgramData\TeamViewer")},
        [pscustomobject]@{Id='quickassist';Name='Quick Assist';ProcessNames=@('QuickAssist');ServicePatterns=@();AppPattern='(?i)quick assist';ConfigRoots=@()},
        [pscustomobject]@{Id='remoteassistance';Name='Windows Remote Assistance';ProcessNames=@('msra');ServicePatterns=@();AppPattern='(?i)remote assistance';ConfigRoots=@()},
        [pscustomobject]@{Id='remotedesktop';Name='Remote Desktop';ProcessNames=@('mstsc','msrdc','RdClient.Windows');ServicePatterns=@('TermService');AppPattern='(?i)remote desktop';ConfigRoots=@()},
        [pscustomobject]@{Id='chromeremotedesktop';Name='Chrome Remote Desktop';ProcessNames=@('remoting_host','remote_assistance_host');ServicePatterns=@('chromoting*');AppPattern='(?i)chrome remote desktop';ConfigRoots=@("${env}:ProgramData\Google\Chrome Remote Desktop")},
        [pscustomobject]@{Id='parsec';Name='Parsec';ProcessNames=@('parsecd','pservice');ServicePatterns=@('Parsec*');AppPattern='(?i)parsec';ConfigRoots=@("${env}:APPDATA\Parsec","${env}:ProgramData\Parsec")},
        [pscustomobject]@{Id='splashtop';Name='Splashtop';ProcessNames=@('SRServer','SRManager','SplashtopRemoteService');ServicePatterns=@('Splashtop*');AppPattern='(?i)splashtop';ConfigRoots=@("${env}:ProgramData\Splashtop")}
    )
}

function Get-WinCareRemoteSupportSurface {
    [CmdletBinding()]param()
    $catalog=@(Get-WinCareRemoteSupportCatalog);$apps=@(Get-WinCareInstalledApplication -IncludeStoreApps)
    $processes=@(Get-Process -ErrorAction SilentlyContinue);$services=@();try{$services=@(Get-CimInstance Win32_Service -ErrorAction Stop)}catch{Write-WinCareLog -Level Warning -Message 'Remote-support service inventory was unavailable.' -Data @{error=$_.Exception.Message}}
    $listeners=@();if(Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue){try{$listeners=@(Get-NetTCPConnection -State Listen -ErrorAction Stop)}catch{Write-WinCareLog -Level Warning -Message 'Remote-support listener inventory was unavailable.' -Data @{error=$_.Exception.Message}}}
    $results=[Collections.Generic.List[object]]::new()
    foreach($tool in $catalog){
        $matchedApps=@($apps|Where-Object{[string]$_.Name -match [string]$tool.AppPattern})
        $matchedProcesses=[Collections.Generic.List[object]]::new()
        foreach($process in $processes|Where-Object{$_.ProcessName -in @($tool.ProcessNames)}){
            $path='';$start='';$hash='';$signature='Unknown'
            try{$path=$process.Path;$start=$process.StartTime.ToUniversalTime().ToString('o');if($path){$hash=Get-WinCareSha256 $path;$signature=(Get-AuthenticodeSignature -LiteralPath $path -ErrorAction SilentlyContinue).Status.ToString()}}catch{Write-Verbose 'Remote-support process metadata was unavailable.'}
            $ports=@($listeners|Where-Object OwningProcess -eq $process.Id|ForEach-Object{"$($_.LocalAddress):$($_.LocalPort)"})
            $matchedProcesses.Add([pscustomobject]@{ProcessId=[int]$process.Id;Name=$process.ProcessName;StartTime=$start;Path=$path;Sha256=$hash;Signature=$signature;ListeningEndpoints=$ports})
        }
        $matchedServices=@($services|Where-Object{$service=$_;[bool](@($tool.ServicePatterns)|Where-Object{$service.Name -like $_}|Select-Object -First 1)}|ForEach-Object{[pscustomobject]@{Name=$_.Name;DisplayName=$_.DisplayName;State=$_.State;Status=$_.State;StartMode=$_.StartMode;StartType=$_.StartMode;PathName=$_.PathName;ProcessId=[int]$_.ProcessId}})
        $configs=[Collections.Generic.List[object]]::new()
        foreach($root in @($tool.ConfigRoots|Where-Object{$_})){
            foreach($file in @(Get-WinCareBoundedRemoteSupportConfigFile -Root $root -MaximumFiles ([math]::Max(1,500-$configs.Count)) -MaximumDepth 8 -MaximumDirectories 1000)){
                $safe=[string]$file.Path;$keys=@()
                if([long]$file.Length -le 1MB -and [string]$file.Extension -in @('.toml','.ini','.conf','.json','.xml','.txt')){
                    try{$keys=@(Read-WinCareBoundedLines -LiteralPath $safe -MaximumBytes 1048576 -MaximumLines 500 -MaximumLineCharacters 65536 | ForEach-Object{if($_ -match '^\s*([A-Za-z0-9_.-]{1,80})\s*[:=]'){$Matches[1]}} | Where-Object{$_} | Sort-Object -Unique | Select-Object -First 100)}catch{Write-WinCareLog -Level Warning -Message 'Remote-support configuration keys were unavailable.' -Data @{path=$safe;error=$_.Exception.Message}}
                }
                try{$hash=Get-WinCareSha256 $safe;$configs.Add([pscustomobject]@{Path=$safe;Bytes=[long]$file.Length;Sha256=$hash;KeyNames=$keys;ValuesCollected=$false})}
                catch{Write-WinCareLog -Level Warning -Message 'Remote-support configuration file hashing failed.' -Data @{path=$safe;error=$_.Exception.Message}}
                if($configs.Count -ge 500){break}
            }
            if($configs.Count -ge 500){break}
        }
        $processIds=@($matchedProcesses|ForEach-Object{[int]$_.ProcessId});$matchedListeners=@($listeners|Where-Object{[int]$_.OwningProcess -in $processIds}|ForEach-Object{[pscustomobject]@{LocalAddress=[string]$_.LocalAddress;LocalPort=[int]$_.LocalPort;OwningProcess=[int]$_.OwningProcess}})
        $publishers=@($matchedApps|ForEach-Object{[string](Get-WinCarePropertyValue $_ 'Publisher' '')}|Where-Object{$_}|Sort-Object -Unique);$installPaths=@($matchedApps|ForEach-Object{[string](Get-WinCarePropertyValue $_ 'InstallLocation' '')}|Where-Object{$_}|Sort-Object -Unique);$configKeys=@($configs|ForEach-Object{@($_.KeyNames)}|Where-Object{$_}|Sort-Object -Unique|Select-Object -First 500)
        $installed=$matchedApps.Count -gt 0 -or $matchedProcesses.Count -gt 0 -or $matchedServices.Count -gt 0
        if($installed -or $configs.Count -gt 0){$results.Add([pscustomobject]@{Id=$tool.Id;Name=$tool.Name;Publisher=($publishers -join '; ');Installed=$installed;Applications=$matchedApps;InstallPaths=$installPaths;Processes=@($matchedProcesses);ProcessNames=@($matchedProcesses.Name|Sort-Object -Unique);Services=$matchedServices;ServiceNames=@($matchedServices.Name|Sort-Object -Unique);Listeners=$matchedListeners;ConfigFiles=@($configs);ConfigKeys=$configKeys;SecretsCollected=$false;Active=($matchedProcesses.Count -gt 0 -or @($matchedServices|Where-Object State -eq 'Running').Count -gt 0)})}
    }
    @($results)
}

function Get-WinCareRemoteConsentPath {Ensure-WinCareState;Join-Path $script:WinCareState.Root 'RemoteSupport\consent.json'}
function Get-WinCareDefaultRemoteConsentStore {[ordered]@{SchemaVersion=1;Revision=0;UpdatedAt='1970-01-01T00:00:00.0000000Z';Records=@()}}
function Test-WinCareRemoteConsentStore {
    param([Parameter(Mandatory)][Collections.IDictionary]$Store)
    $null=Test-WinCareStrictObjectKeys -InputObject $Store -AllowedKeys @('SchemaVersion','Revision','UpdatedAt','Records') -Context 'remote-support consent store'
    if([int]$Store.SchemaVersion -ne 1 -or [long]$Store.Revision -lt 0){throw 'Remote-support consent store schema is invalid.'}
    $updated=[datetime]::MinValue;if(-not[datetime]::TryParse([string]$Store.UpdatedAt,[ref]$updated)){throw 'Remote-support consent store timestamp is invalid.'}
    $records=@($Store.Records);if($records.Count -gt 500){throw 'Remote-support consent store exceeds 500 records.'}
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($record in $records){
        $null=Test-WinCareStrictObjectKeys -InputObject $record -AllowedKeys @('Id','ToolId','Operator','Reason','Status','CreatedAt','ExpiresAt','ClosedAt','DeviceScope','AllowClipboard','AllowFileTransfer','SourceRecords') -Context 'remote-support consent record'
        if([string]$record.Id -notmatch '^[a-f0-9]{32}$' -or -not $ids.Add([string]$record.Id)){throw 'Remote-support consent ID is invalid or duplicated.'}
        if([string]$record.ToolId -notmatch '^[a-z0-9][a-z0-9.-]{1,80}$'){throw 'Remote-support tool ID is invalid.'}
        if(([string]$record.Operator).Length -gt 160){throw 'Remote-support operator is too long.'}
        if([string]::IsNullOrWhiteSpace([string]$record.Reason) -or ([string]$record.Reason).Length -gt 1000){throw 'Remote-support reason is invalid.'}
        if([string]$record.Status -notin @('Prepared','Active','Closed','Revoked','Expired')){throw 'Remote-support consent status is invalid.'}
        if([string]$record.DeviceScope -notin @('ThisDevice','TwoDevicePair')){throw 'Remote-support device scope is invalid.'}
        if($record.AllowClipboard -isnot [bool] -or $record.AllowFileTransfer -isnot [bool]){throw 'Remote-support data-sharing flags must be Boolean.'}
        $created=[datetime]::MinValue;$expires=[datetime]::MinValue
        if(-not[datetime]::TryParse([string]$record.CreatedAt,[ref]$created) -or -not[datetime]::TryParse([string]$record.ExpiresAt,[ref]$expires) -or $expires.ToUniversalTime() -le $created.ToUniversalTime()){throw 'Remote-support consent time range is invalid.'}
        if([string]$record.ClosedAt){$closed=[datetime]::MinValue;if(-not[datetime]::TryParse([string]$record.ClosedAt,[ref]$closed)){throw 'Remote-support consent close time is invalid.'}}
        if([string]$record.Status -in @('Closed','Revoked','Expired') -and [string]::IsNullOrWhiteSpace([string]$record.ClosedAt)){throw 'Terminal remote-support consent records require a close time.'}
        if([string]$record.Status -in @('Prepared','Active') -and [string]$record.ClosedAt){throw 'Open remote-support consent records cannot have a close time.'}
        if(@($record.SourceRecords).Count -gt 64 -or @($record.SourceRecords|Where-Object{$_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) -or ([string]$_).Length -gt 160}).Count){throw 'Remote-support consent source records are invalid.'}
    }
    $true
}
function Get-WinCareRemoteConsentStore { [CmdletBinding()]param([switch]$Strict);$path=Get-WinCareRemoteConsentPath;if(-not(Test-Path -LiteralPath $path)){return Get-WinCareDefaultRemoteConsentStore};try{$store=Read-WinCareProtectedJson -LiteralPath $path -Purpose 'WinCare.RemoteConsent';$null=Test-WinCareRemoteConsentStore $store;$store}catch{if($Strict){throw};Write-WinCareLog -Level Warning -Message 'Remote-support consent store is invalid.' -Data @{error=$_.Exception.Message};Get-WinCareDefaultRemoteConsentStore} }
function Get-WinCareRemoteConsentStoreHash {param([Collections.IDictionary]$Store);Get-WinCareCanonicalObjectHash $Store}
function Get-WinCareRemoteSupportConsent {
    [CmdletBinding()]param([string]$Id,[switch]$IncludeTerminal)
    $records=[Collections.Generic.List[object]]::new();$now=[datetime]::UtcNow
    foreach($source in @((Get-WinCareRemoteConsentStore).Records)){
        $copy=[ordered]@{};foreach($key in $source.Keys){$copy[$key]=$source[$key]}
        $copy.StoredStatus=[string]$source.Status
        if([string]$source.Status -in @('Prepared','Active') -and [datetime]::Parse([string]$source.ExpiresAt).ToUniversalTime() -le $now){$copy.Status='Expired'}
        $records.Add([pscustomobject]$copy)
    }
    $items=@($records)
    if($Id){$items=@($items|Where-Object Id -eq $Id)}
    if(-not $IncludeTerminal){$items=@($items|Where-Object Status -in @('Prepared','Active'))}
    @($items|Sort-Object CreatedAt -Descending)
}

function New-WinCareRemoteSupportConsentPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ToolId,[Parameter(Mandatory)][string]$Reason,[string]$Operator='',[ValidateRange(5,1440)][int]$DurationMinutes=60,[ValidateSet('ThisDevice','TwoDevicePair')][string]$DeviceScope='ThisDevice',[bool]$AllowClipboard=$false,[bool]$AllowFileTransfer=$false)
    if(-not(Get-WinCareRemoteSupportCatalog|Where-Object Id -eq $ToolId)){throw 'Remote-support tool ID is not recognized.'}
    $before=Get-WinCareRemoteConsentStore -Strict;$beforeHash=Get-WinCareRemoteConsentStoreHash $before;$id=[guid]::NewGuid().ToString('N')
    $record=[ordered]@{Id=$id;ToolId=$ToolId;Operator=$Operator;Reason=$Reason;Status='Prepared';CreatedAt=[datetime]::UtcNow.ToString('o');ExpiresAt=[datetime]::UtcNow.AddMinutes($DurationMinutes).ToString('o');ClosedAt='';DeviceScope=$DeviceScope;AllowClipboard=$AllowClipboard;AllowFileTransfer=$AllowFileTransfer;SourceRecords=@('SRC:2441cc19b2')}
    $after=[ordered]@{SchemaVersion=1;Revision=[long]$before.Revision+1;UpdatedAt=[datetime]::UtcNow.ToString('o');Records=@($before.Records)+@($record)};$afterHash=Get-WinCareRemoteConsentStoreHash $after
    $action=New-WinCareAction -Type 'ReplaceRemoteConsentStore' -Label "Create $ToolId support consent" -Risk Moderate -Parameters @{Store=$after;ExpectedCurrentHash=$beforeHash} -RequiresAdmin $false -Reversible $true -Compensator @{Type='ReplaceRemoteConsentStore';Parameters=@{Store=$before;ExpectedCurrentHash=$afterHash}} -TimeoutSeconds 30 -Tags @('RemoteSupport','Consent') -SourceRecords @($record.SourceRecords) -RecoveryDescription 'Restore the exact prior local consent-record store.'
    New-WinCarePlan -Title 'Create remote-support consent record' -Actions @($action) -SourceRecords @($record.SourceRecords)
}

function New-WinCareRemoteSupportExpiryReconciliationPlan {
    [CmdletBinding()]param()
    $before=Get-WinCareRemoteConsentStore -Strict;$beforeHash=Get-WinCareRemoteConsentStoreHash $before;$now=[datetime]::UtcNow;$changed=0
    $records=@($before.Records|ForEach-Object{
        $copy=[ordered]@{};foreach($key in $_.Keys){$copy[$key]=$_[$key]}
        if([string]$copy.Status -in @('Prepared','Active') -and [datetime]::Parse([string]$copy.ExpiresAt).ToUniversalTime() -le $now){$copy.Status='Expired';$copy.ClosedAt=$now.ToString('o');$changed++}
        $copy
    })
    if($changed -eq 0){throw 'No expired remote-support consent records require reconciliation.'}
    $after=[ordered]@{SchemaVersion=1;Revision=[long]$before.Revision+1;UpdatedAt=$now.ToString('o');Records=$records};$afterHash=Get-WinCareRemoteConsentStoreHash $after
    $action=New-WinCareAction -Type 'ReplaceRemoteConsentStore' -Label "Reconcile $changed expired remote-support consent record(s)" -Risk Moderate -Parameters @{Store=$after;ExpectedCurrentHash=$beforeHash} -RequiresAdmin $false -Reversible $true -Compensator @{Type='ReplaceRemoteConsentStore';Parameters=@{Store=$before;ExpectedCurrentHash=$afterHash}} -TimeoutSeconds 30 -Tags @('RemoteSupport','Consent','Reconciliation') -SourceRecords @('SRC:2441cc19b2') -RecoveryDescription 'Restore the exact prior consent-record store.'
    New-WinCarePlan -Title 'Reconcile expired remote-support consent records' -Actions @($action) -SourceRecords @('SRC:2441cc19b2')
}

function New-WinCareRemoteSupportConsentStatePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id,[ValidateSet('Active','Closed','Revoked')][string]$State)
    $before=Get-WinCareRemoteConsentStore -Strict;$beforeHash=Get-WinCareRemoteConsentStoreHash $before;$record=@($before.Records|Where-Object Id -eq $Id)|Select-Object -First 1;if(-not $record){throw 'Remote-support consent record was not found.'}
    if([string]$record.Status -in @('Prepared','Active') -and [datetime]::Parse([string]$record.ExpiresAt).ToUniversalTime() -le [datetime]::UtcNow){throw 'Expired consent must be reconciled and cannot transition to another active state.'}
    $allowed=@{Prepared=@('Active','Closed','Revoked');Active=@('Closed','Revoked');Closed=@();Revoked=@();Expired=@()};if($State -notin @($allowed[[string]$record.Status])){throw "Transition $($record.Status) -> $State is not allowed."}
    $records=@($before.Records|ForEach-Object{if($_.Id -eq $Id){$copy=[ordered]@{};foreach($key in $_.Keys){$copy[$key]=$_[$key]};$copy.Status=$State;if($State -in @('Closed','Revoked')){$copy.ClosedAt=[datetime]::UtcNow.ToString('o')};$copy}else{$_}})
    $after=[ordered]@{SchemaVersion=1;Revision=[long]$before.Revision+1;UpdatedAt=[datetime]::UtcNow.ToString('o');Records=$records};$afterHash=Get-WinCareRemoteConsentStoreHash $after
    $action=New-WinCareAction -Type 'ReplaceRemoteConsentStore' -Label "Set remote-support consent to $State" -Risk Moderate -Parameters @{Store=$after;ExpectedCurrentHash=$beforeHash} -RequiresAdmin $false -Reversible $true -Compensator @{Type='ReplaceRemoteConsentStore';Parameters=@{Store=$before;ExpectedCurrentHash=$afterHash}} -TimeoutSeconds 30 -Tags @('RemoteSupport','Consent') -SourceRecords @('SRC:2441cc19b2') -RecoveryDescription 'Restore the exact prior local consent-record store.'
    New-WinCarePlan -Title 'Change remote-support consent state' -Actions @($action) -SourceRecords @('SRC:2441cc19b2')
}

function Set-WinCareRemoteConsentStoreInternal {
    param([Parameter(Mandatory)][Collections.IDictionary]$Store)
    $null=Test-WinCareRemoteConsentStore $Store
    $path=Get-WinCareRemoteConsentPath;$null=New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force
    Write-WinCareProtectedJson -LiteralPath $path -InputObject $Store -Purpose 'WinCare.RemoteConsent'
    $actual=Get-WinCareRemoteConsentStore -Strict
    if((Get-WinCareRemoteConsentStoreHash $actual) -ne (Get-WinCareRemoteConsentStoreHash $Store)){throw 'Remote-support consent-store verification failed.'}
    $actual
}

function Invoke-WinCareReplaceRemoteConsentStoreAction {
    param([object]$Action)
    $store=ConvertTo-WinCareParameterDictionary $Action.Parameters.Store;$null=Test-WinCareRemoteConsentStore $store;$current=Get-WinCareRemoteConsentStore -Strict
    if((Get-WinCareRemoteConsentStoreHash $current) -ne [string]$Action.Parameters.ExpectedCurrentHash){return New-WinCareResult -Success $false -Status Blocked -Code 'RemoteConsentChanged' -Message 'Remote-support consent records changed after preview.' -ExitCode 74}
    try{$actual=Set-WinCareRemoteConsentStoreInternal -Store $store;New-WinCareResult -Success $true -Message 'Remote-support consent store updated and verified.' -Data @{Revision=$actual.Revision;Records=@($actual.Records).Count}}
    catch{
        $failure=$_.Exception.Message;$warnings=@()
        try{$null=Set-WinCareRemoteConsentStoreInternal -Store $current;$warnings+='The prior remote-support consent store was restored after the failed update.'}
        catch{$warnings+="The failed consent-store update could not be fully restored: $($_.Exception.Message)"}
        New-WinCareResult -Success $false -Message "Remote-support consent-store update failed: $failure" -Warnings $warnings -ExitCode 1
    }
}

function New-WinCareRemoteSupportEmergencyPlan {
    [CmdletBinding()]param([int[]]$ProcessId=@(),[switch]$TerminateKnownProcesses)
    $actions=[Collections.Generic.List[object]]::new();$actions.Add((New-WinCareAction -Type 'ReleaseLocalInput' -Label 'Release local cursor and modifier state' -Risk Low -Parameters @{} -RequiresAdmin $false -Reversible $false -TimeoutSeconds 15 -Tags @('RemoteSupport','EmergencyRelease') -SourceRecords @('SRC:2441cc19b2')))
    if($TerminateKnownProcesses){
        $surfaces=@(Get-WinCareRemoteSupportSurface);$known=@($surfaces.Processes)
        foreach($id in @($ProcessId|Sort-Object -Unique)){
            $process=@($known|Where-Object ProcessId -eq $id)|Select-Object -First 1;if(-not $process){throw "Process $id is not an inventoried remote-support process."}
            $actions.Add((New-WinCareAction -Type 'TerminateRemoteSupportProcess' -Label "Terminate remote-support process $($process.Name) ($id)" -Risk High -Parameters @{ProcessId=[int]$id;Name=[string]$process.Name;StartTime=[string]$process.StartTime;Path=[string]$process.Path;Sha256=[string]$process.Sha256} -RequiresAdmin $false -Reversible $false -TimeoutSeconds 30 -Tags @('RemoteSupport','EmergencyRelease') -SourceRecords @('SRC:2441cc19b2') -RecoveryDescription 'A terminated remote-support process is not restarted automatically.'))
        }
    }
    New-WinCarePlan -Title 'Emergency remote-support release' -Description 'Releases local input constraints first, then optionally terminates only exact inventoried remote-support processes.' -Actions @($actions) -RollbackOnFailure $false -SourceRecords @('SRC:2441cc19b2')
}

function Invoke-WinCareTerminateRemoteSupportProcessAction {
    param([object]$Action)
    $p=$Action.Parameters;$id=[int]$p.ProcessId;$process=Get-Process -Id $id -ErrorAction SilentlyContinue;if(-not $process){return New-WinCareResult -Success $true -Message 'Remote-support process is already stopped.' -Code 'AlreadyStopped'}
    $allowed=@(Get-WinCareRemoteSupportCatalog|ForEach-Object{@($_.ProcessNames)})
    if($process.ProcessName -notin $allowed -or $process.ProcessName -ne [string]$p.Name){return New-WinCareResult -Success $false -Status Blocked -Code 'RemoteProcessNotAllowlisted' -Message 'Process is not on the remote-support allowlist.' -ExitCode 5}
    $start='';$path='';try{$start=$process.StartTime.ToUniversalTime().ToString('o');$path=$process.Path}catch{return New-WinCareResult -Success $false -Message 'Process identity could not be established.' -ExitCode 5}
    if($start -ne [string]$p.StartTime -or $path -ne [string]$p.Path -or (Get-WinCareSha256 $path) -ne [string]$p.Sha256){return New-WinCareResult -Success $false -Status Blocked -Code 'RemoteProcessChanged' -Message 'Remote-support process identity changed after preview.' -ExitCode 74}
    $process.Kill($true);$process.WaitForExit(10000)
    New-WinCareResult -Success (-not(Get-Process -Id $id -ErrorAction SilentlyContinue)) -Message 'Remote-support process terminated.' -Data @{ProcessId=$id;Name=$p.Name}
}

