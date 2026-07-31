#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: Injection-surface, ETW, process, memory, licensing, and CPU diagnostics.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function Get-WinCareInjectionSurface {
    [CmdletBinding()]
    param([ValidateRange(0,10000)][int]$Limit=0)
    $items=[Collections.Generic.List[object]]::new();if(-not $IsWindows){return @($items)}
    $definitions=@(
        @{Id='appinit-64';Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows';Name='AppInit_DLLs';Kind='AppInitDll'},
        @{Id='appinit-32';Path='HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Windows';Name='AppInit_DLLs';Kind='AppInitDll'},
        @{Id='appcert';Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\AppCertDlls';Name='*';Kind='AppCertDll'}
    )
    foreach($definition in $definitions){
        if(-not (Test-Path -LiteralPath $definition.Path)){continue}
        if($definition.Name -eq '*'){
            $property=Get-ItemProperty -LiteralPath $definition.Path -ErrorAction SilentlyContinue
            foreach($p in @($property.PSObject.Properties|Where-Object{$_.Name -notmatch '^PS'})){$items.Add([pscustomobject]@{Id="$($definition.Id)-$($p.Name)";Kind=$definition.Kind;Path=$definition.Path;Name=$p.Name;Value=$p.Value;Exists=$true;Hash=Get-WinCareInjectionSurfaceValueStateHash -Path $definition.Path -Name $p.Name;EvidenceType='RegistryObservation'})}
        }else{
            $value=$null;$exists=$false;try{$value=(Get-ItemProperty -LiteralPath $definition.Path -Name $definition.Name -ErrorAction Stop).$($definition.Name);$exists=$true}catch{}
            if($exists -and -not [string]::IsNullOrWhiteSpace([string]$value)){$items.Add([pscustomobject]@{Id=$definition.Id;Kind=$definition.Kind;Path=$definition.Path;Name=$definition.Name;Value=$value;Exists=$true;Hash=Get-WinCareInjectionSurfaceValueStateHash -Path $definition.Path -Name $definition.Name;EvidenceType='RegistryObservation'})}
        }
    }
    foreach($root in @('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options')){
        if(-not (Test-Path $root)){continue};foreach($key in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue){try{$debugger=(Get-ItemProperty -LiteralPath $key.PSPath -Name Debugger -ErrorAction Stop).Debugger;if($debugger){$items.Add([pscustomobject]@{Id=('ifeo-'+$key.PSChildName);Kind='IfeoDebugger';Path=$key.PSPath;Name='Debugger';Value=$debugger;Exists=$true;Hash=Get-WinCareInjectionSurfaceValueStateHash -Path $key.PSPath -Name 'Debugger';EvidenceType='RegistryObservation'})}}catch{}}
    }
    $result=@($items|Sort-Object Kind,Path,Name)
    if($Limit -gt 0){return @($result|Select-Object -First $Limit)}
    return $result
}
function Get-WinCareInjectionSurfaceValueStateHash {
    [CmdletBinding()]
    param([string]$Path,[string]$Name,[object]$Surface)
    if($Surface){$record=ConvertTo-WinCareBridgeHashtable $Surface}else{$exists=$false;$value=$null;$kind=$null;try{$item=Get-Item -LiteralPath $Path -ErrorAction Stop;$value=(Get-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction Stop).$Name;$kind=$item.GetValueKind($Name).ToString();$exists=$true}catch{};$record=[ordered]@{Path=$Path;Name=$Name;Exists=$exists;Value=$value;ValueKind=$kind}}
    if(Get-Command Get-WinCareCanonicalObjectHash -ErrorAction SilentlyContinue){return Get-WinCareCanonicalObjectHash $record}
    $json=$record|ConvertTo-Json -Compress -Depth 20;return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($json))).ToLowerInvariant()
}
function Get-WinCareInjectionQuarantineRecord {
    [CmdletBinding()]
    param([string]$Id='')
    $path=Join-Path (Get-WinCareBridgeStateRoot 'Security') 'injection-quarantine.protected.json'
    if(-not (Test-Path -LiteralPath $path)){return @()}
    $records=@(Read-WinCareProtectedJson -LiteralPath $path -Purpose 'WinCare.InjectionQuarantine')
    if($Id){return @($records|Where-Object{$_.Id -eq $Id})}
    return $records
}
function Test-WinCareInjectionSurfaceSnapshot {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Snapshot)
    $map=ConvertTo-WinCareParameterDictionary $Snapshot
    $surface=if($map.Contains('Surface')){ConvertTo-WinCareParameterDictionary $map.Surface}else{$map}
    $path=[string](Get-WinCarePropertyValue $surface 'Path' '')
    $name=[string](Get-WinCarePropertyValue $surface 'Name' '')
    if([string]::IsNullOrWhiteSpace($path) -or [string]::IsNullOrWhiteSpace($name)){throw 'Injection-surface snapshot is missing its target identity.'}
    $exact=@('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Windows',
        'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\AppCertDlls')
    $ifeo=@('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\')
    $allowed=($path -in $exact) -or [bool](@($ifeo|Where-Object{$path.StartsWith($_,[StringComparison]::OrdinalIgnoreCase)})|Select-Object -First 1)
    if(-not $allowed){throw 'Injection-surface snapshot targets an unapproved registry root.'}
    if($name -notmatch '^[^\\/:*?"<>|]{1,255}$'){throw 'Injection-surface value name is invalid.'}
    if($map.Contains('Values')){
        foreach($value in @($map.Values)){
            $entry=ConvertTo-WinCareParameterDictionary $value
            $entryPath=[string](Get-WinCarePropertyValue $entry 'Path' '')
            $entryName=[string](Get-WinCarePropertyValue $entry 'Name' '')
            if($entryPath -ne $path -or $entryName -ne $name){
                throw 'Injection-surface recovery values do not match the declared target.'
            }
        }
    }elseif($null -eq (Get-WinCarePropertyValue $surface 'Exists' $null)){throw 'Injection-surface snapshot is missing its existence state.'}
    $true
}

function New-WinCareInjectionSurfaceQuarantinePlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$SurfaceId)
    $catalog=@(Get-WinCareInjectionSurface);$actions=[Collections.Generic.List[object]]::new()
    foreach($id in $SurfaceId){$surface=$catalog|Where-Object{$_.Id -eq $id}|Select-Object -First 1;if(-not $surface){throw "Injection surface not found: $id"};$actions.Add((New-WinCareBridgeAction -Type 'QuarantineInjectionSurface' -Label "Quarantine injection surface $id" -Risk Critical -RequiresAdmin $true -Reversible $true -Parameters @{SurfaceId=$id;ExpectedHash=$surface.Hash} -Verification 'The registry value must be absent after quarantine and a protected recovery record must exist.'))}
    New-WinCareBridgePlan -Title 'Quarantine process-injection surfaces' -Description 'Removes selected AppInit, AppCert, or IFEO debugger values after exact-state validation.' -Actions @($actions)
}
function Invoke-WinCareQuarantineInjectionSurfaceAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Injection-surface quarantine';if($blocked){return $blocked}
    $p=Get-WinCareActionParameters $Action;$surface=Get-WinCareInjectionSurface|Where-Object{$_.Id -eq [string]$p.SurfaceId}|Select-Object -First 1
    if(-not $surface){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InjectionSurfaceNotFound' -Message 'The selected injection surface no longer exists.' -ExitCode 2}
    if(-not (Test-WinCareInjectionSurfaceSnapshot $surface)){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InjectionSurfaceOutsideAllowedRoots' -Message 'The selected registry value is outside the remediable injection-surface roots.' -ExitCode 5}
    if($p.ExpectedHash -and $surface.Hash -ne [string]$p.ExpectedHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InjectionSurfaceChanged' -Message 'The selected registry value changed after preview.' -ExitCode 74}
    $record=[ordered]@{Id=[guid]::NewGuid().ToString('N');SurfaceId=$surface.Id;Path=$surface.Path;Name=$surface.Name;Value=$surface.Value;ValueKind=try{(Get-Item -LiteralPath $surface.Path).GetValueKind($surface.Name).ToString()}catch{'String'};BeforeHash=$surface.Hash;QuarantinedAt=[datetime]::UtcNow.ToString('o')}
    $record.IdentityHash=Get-WinCareCanonicalObjectHash $record
    $removed=$false
    try{
        Remove-ItemProperty -LiteralPath $surface.Path -Name $surface.Name -ErrorAction Stop;$removed=$true
        $remaining=Get-WinCareInjectionSurface|Where-Object{$_.Id -eq $surface.Id};if($remaining){throw 'Registry value remained present after removal.'}
        $storePath=Join-Path (Get-WinCareBridgeStateRoot 'Security') 'injection-quarantine.protected.json'
        $records=@();if(Test-Path -LiteralPath $storePath){$records=@(Read-WinCareProtectedJson -LiteralPath $storePath -Purpose 'WinCare.InjectionQuarantine')}
        Write-WinCareProtectedJson -LiteralPath $storePath -InputObject @($records+$record) -Purpose 'WinCare.InjectionQuarantine'
        New-WinCareBridgeResult -Success $true -Code 'InjectionSurfaceQuarantined' -Message 'Injection surface was removed and an authenticated recovery record was persisted.' -Data @{RecordId=$record.Id;RecordIdentityHash=$record.IdentityHash;EvidenceType='RegistryValueAbsenceAndProtectedRecord';Compensator=@{Type='RestoreInjectionSurface';Parameters=@{RecordId=$record.Id;ExpectedCurrentHash=Get-WinCareInjectionSurfaceValueStateHash -Path $surface.Path -Name $surface.Name};RequiresAdmin=$true}}
    }catch{
        $primary=$_.Exception.Message
        if($removed){
            try{$null=New-Item -ItemType Directory -Path $surface.Path -Force;$kind=[Microsoft.Win32.RegistryValueKind]::$($record.ValueKind);New-ItemProperty -LiteralPath $surface.Path -Name $surface.Name -Value $surface.Value -PropertyType $kind -Force -ErrorAction Stop|Out-Null}catch{return New-WinCareBridgeResult -Success $false -Code 'InjectionSurfaceQuarantineRollbackFailed' -Message "$primary Recovery failed: $($_.Exception.Message)" -ExitCode 1}
        }
        New-WinCareBridgeResult -Success $false -Code 'InjectionSurfaceQuarantineFailedRolledBack' -Message $primary -ExitCode 1 -Data @{Surface=$surface}
    }
}
function Invoke-WinCareRestoreInjectionSurfaceAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Injection-surface restore';if($blocked){return $blocked}
    $p=Get-WinCareActionParameters $Action
    try{$record=Get-WinCareInjectionQuarantineRecord -Id ([string]$p.RecordId)|Select-Object -First 1}catch{return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InjectionSurfaceProtectedRecordInvalid' -Message $_.Exception.Message -ExitCode 74}
    if(-not $record){return New-WinCareBridgeResult -Success $false -Code 'QuarantineRecordNotFound' -Message 'Recovery record was not found.' -ExitCode 2}
    $copy=ConvertTo-WinCareBridgeHashtable $record;$identity=[string]$copy.IdentityHash;$copy.Remove('IdentityHash')
    if($identity -notmatch '^[a-f0-9]{64}$' -or (Get-WinCareCanonicalObjectHash $copy) -ne $identity){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InjectionSurfaceRecordIdentityMismatch' -Message 'Authenticated recovery record identity does not match its payload.' -ExitCode 74}
    if(-not (Test-WinCareInjectionSurfaceSnapshot ([pscustomobject]@{Path=$record.Path;Name=$record.Name;Exists=$true}))){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InjectionSurfaceOutsideAllowedRoots' -Message 'Recovery target is outside the remediable injection-surface roots.' -ExitCode 5}
    $current=Get-WinCareInjectionSurfaceValueStateHash -Path $record.Path -Name $record.Name
    if($p.ExpectedCurrentHash -and $current -ne [string]$p.ExpectedCurrentHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InjectionSurfaceRestoreConflict' -Message 'The target value changed after quarantine.' -ExitCode 74}
    try{
        $null=New-Item -ItemType Directory -Path $record.Path -Force
        $kind=[Microsoft.Win32.RegistryValueKind]::$($record.ValueKind)
        New-ItemProperty -LiteralPath $record.Path -Name $record.Name -Value $record.Value -PropertyType $kind -Force -ErrorAction Stop|Out-Null
        $after=Get-WinCareInjectionSurfaceValueStateHash -Path $record.Path -Name $record.Name;$success=$after -eq $record.BeforeHash
        New-WinCareBridgeResult -Success $success -Code $(if($success){'InjectionSurfaceRestored'}else{'InjectionSurfaceRestoreVerificationFailed'}) -Message $(if($success){'Injection surface was restored and hash-verified.'}else{'Restored value did not match the recorded state.'}) -Data @{RecordId=$record.Id;RecordIdentityHash=$identity;ObservedHash=$after;EvidenceType='RegistryValueHash'} -ExitCode $(if($success){0}else{74})
    }catch{New-WinCareBridgeResult -Success $false -Code 'InjectionSurfaceRestoreFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Get-WinCareProcessModuleInventory {
    [CmdletBinding()]
    param([int]$ProcessId=0,[ValidateRange(0,100000)][int]$Limit=0)
    $processes=if($ProcessId -gt 0){@(Get-Process -Id $ProcessId -ErrorAction Stop)}else{@(Get-Process -ErrorAction SilentlyContinue)};$result=[Collections.Generic.List[object]]::new()
    foreach($process in $processes){
        try{foreach($module in @($process.Modules)){
            if($Limit -gt 0 -and $result.Count -ge $Limit){break}
            $hash=$null;try{$hash=(Get-FileHash -LiteralPath $module.FileName -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()}catch{}
            $result.Add([pscustomobject]@{ProcessId=$process.Id;ProcessName=$process.ProcessName;ProcessStartTime=try{$process.StartTime.ToUniversalTime().ToString('o')}catch{$null};ModuleName=$module.ModuleName;FileName=$module.FileName;BaseAddress=('0x{0:x}' -f $module.BaseAddress.ToInt64());ModuleMemorySize=$module.ModuleMemorySize;FileVersion=$module.FileVersionInfo.FileVersion;CompanyName=$module.FileVersionInfo.CompanyName;Sha256=$hash;Accessible=$true;EvidenceType='LiveProcessModuleObservation'})
        }}catch{if($Limit -eq 0 -or $result.Count -lt $Limit){$result.Add([pscustomobject]@{ProcessId=$process.Id;ProcessName=$process.ProcessName;Error=$_.Exception.Message;Accessible=$false;EvidenceType='ProcessAccessFailure'})}}
        if($Limit -gt 0 -and $result.Count -ge $Limit){break}
    }
    return @($result)
}
function Get-WinCareRemoteThreadEvent {
    [CmdletBinding()]
    param([datetime]$Since=[datetime]::Now.AddHours(-24),[int]$MaxEvents=500)
    if(-not $IsWindows){return @()};$events=[Collections.Generic.List[object]]::new();try{foreach($event in @(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Sysmon/Operational';Id=8;StartTime=$Since} -MaxEvents $MaxEvents -ErrorAction Stop)){[xml]$xml=$event.ToXml();$data=@{};foreach($node in $xml.Event.EventData.Data){$data[[string]$node.Name]=[string]$node.'#text'};$events.Add([pscustomobject]@{TimeCreated=$event.TimeCreated;RecordId=$event.RecordId;SourceProcessId=$data.SourceProcessId;SourceImage=$data.SourceImage;TargetProcessId=$data.TargetProcessId;TargetImage=$data.TargetImage;StartAddress=$data.StartAddress;StartModule=$data.StartModule;StartFunction=$data.StartFunction})}}catch{}
    return @($events)
}
function Get-WinCareEtwSession {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return @()};$result=Invoke-WinCareBridgeProcess -FilePath 'logman.exe' -ArgumentList @('query','-ets') -TimeoutSeconds 30;if(-not $result.Success){return @()};$sessions=[Collections.Generic.List[object]]::new();foreach($line in $result.Data.StdOut -split "`r?`n"){if($line -match '^\s*([^\s].*?)\s{2,}(Running|Stopped)\s*$'){$sessions.Add([pscustomobject]@{Name=$Matches[1].Trim();Status=$Matches[2];Source='logman'})}};return @($sessions)
}
function New-WinCareEtwCapturePlan {
    [CmdletBinding()]
    param([ValidateSet('General','CPU','FileIO','Network')][string]$Profile='General',[ValidateRange(1,3600)][int]$DurationSeconds=30,[Parameter(Mandatory)][string]$Destination,[int]$ProcessId=0)
    $modules=if($ProcessId -gt 0){@(Get-WinCareProcessModuleInventory -ProcessId $ProcessId)}else{@()};$action=New-WinCareBridgeAction -Type 'CaptureEtwTrace' -Label "Capture ETW trace: $Profile" -Risk Moderate -RequiresAdmin $true -Parameters @{Profile=$Profile;DurationSeconds=$DurationSeconds;Destination=[IO.Path]::GetFullPath($Destination);ProcessId=$ProcessId;ProcessModules=$modules} -TimeoutSeconds ($DurationSeconds+300) -Verification 'The ETL file must exist, be non-empty, and have a SHA-256 digest.';New-WinCareBridgePlan -Title 'Capture ETW trace' -Description 'Uses Windows Performance Recorder when available and logman as a bounded fallback.' -Actions @($action) -RollbackOnFailure $false
}
function Invoke-WinCareCaptureEtwTraceAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action,[string]$OperationId='')
    $blocked=Test-WinCareWindowsRequired 'ETW capture';if($blocked){return $blocked}
    $p=Get-WinCareActionParameters $Action;$destination=[IO.Path]::GetFullPath([string]$p.Destination);$parent=Split-Path -Parent $destination;$null=New-Item -ItemType Directory -Path $parent -Force
    $duration=[math]::Min(3600,[math]::Max(1,[int]$p.DurationSeconds));$profile=[string]$p.Profile;$wpr=Get-Command wpr.exe -ErrorAction SilentlyContinue;$name='';$started=$false;$cancelled=$false
    try{
        if($wpr){
            $preset=switch($profile){'CPU'{'CPU'}'FileIO'{'FileIO'}'Network'{'Network'}default{'GeneralProfile'}}
            $start=Invoke-WinCareBridgeProcess -FilePath $wpr.Source -ArgumentList @('-start',$preset,'-filemode') -TimeoutSeconds 60 -RequireAdmin;if(-not $start.Success){return $start};$started=$true
        }else{
            $name='WinCare-'+[guid]::NewGuid().ToString('N');$provider='{9e814aad-3204-11d2-9a82-006008a86939}'
            $start=Invoke-WinCareBridgeProcess -FilePath 'logman.exe' -ArgumentList @('start',$name,'-p',$provider,'0xffffffffffffffff','5','-o',$destination,'-ets') -TimeoutSeconds 60 -RequireAdmin;if(-not $start.Success){return $start};$started=$true
        }
        for($second=0;$second -lt $duration;$second++){
            if($OperationId -and (Test-WinCareOperationCancellationRequested -OperationId $OperationId)){$cancelled=$true;break}
            Start-Sleep -Seconds 1
        }
        if($cancelled){
            if($wpr){$null=Invoke-WinCareBridgeProcess -FilePath $wpr.Source -ArgumentList @('-cancel') -TimeoutSeconds 120 -RequireAdmin}else{$null=Invoke-WinCareBridgeProcess -FilePath 'logman.exe' -ArgumentList @('stop',$name,'-ets') -TimeoutSeconds 60 -RequireAdmin}
            $started=$false
            if(Test-Path -LiteralPath $destination){Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue}
            return New-WinCareBridgeResult -Success $false -Status Cancelled -Code 'EtwCaptureCancelled' -Message 'ETW capture was cancelled and the active session was stopped.' -ExitCode 1223 -Data @{Destination=$destination;EvidenceType='EtwSessionCancellation'}
        }
        if($wpr){$stop=Invoke-WinCareBridgeProcess -FilePath $wpr.Source -ArgumentList @('-stop',$destination) -TimeoutSeconds 300 -RequireAdmin}else{$stop=Invoke-WinCareBridgeProcess -FilePath 'logman.exe' -ArgumentList @('stop',$name,'-ets') -TimeoutSeconds 60 -RequireAdmin}
        $started=$false;if(-not $stop.Success){return $stop}
        $success=Test-Path -LiteralPath $destination -PathType Leaf;if($success){$item=Get-Item -LiteralPath $destination;$success=$item.Length -gt 0}
        New-WinCareBridgeResult -Success $success -Code $(if($success){'EtwTraceCaptured'}else{'EtwTraceVerificationFailed'}) -Message $(if($success){'ETW trace was captured and file-verified.'}else{'ETW trace file was not created or was empty.'}) -Data @{Destination=$destination;Bytes=if($success){$item.Length}else{0};Sha256=if($success){Get-WinCareSha256 -LiteralPath $destination}else{$null};Profile=$profile;DurationSeconds=$duration;EvidenceType='EtwFile'} -ExitCode $(if($success){0}else{74})
    }catch{
        if($started){if($wpr){$null=Invoke-WinCareBridgeProcess -FilePath $wpr.Source -ArgumentList @('-cancel') -TimeoutSeconds 120 -RequireAdmin}else{$null=Invoke-WinCareBridgeProcess -FilePath 'logman.exe' -ArgumentList @('stop',$name,'-ets') -TimeoutSeconds 60 -RequireAdmin}}
        New-WinCareBridgeResult -Success $false -Code 'EtwCaptureFailed' -Message $_.Exception.Message -ExitCode 1
    }
}
function Get-WinCareProcessInternals {
    [CmdletBinding()]
    param([int]$ProcessId=0,[ValidateRange(0,10000)][int]$Top=0)
    $processes=if($ProcessId){@(Get-Process -Id $ProcessId -ErrorAction Stop)}else{@(Get-Process -ErrorAction SilentlyContinue|Sort-Object WorkingSet64 -Descending)}
    if(-not $ProcessId -and $Top -gt 0){$processes=@($processes|Select-Object -First $Top)}
    foreach($p in $processes){[pscustomobject]@{Id=$p.Id;Name=$p.ProcessName;Path=try{$p.Path}catch{$null};StartTime=try{$p.StartTime}catch{$null};CpuSeconds=try{[double]$p.CPU}catch{0};WorkingSetBytes=[long]$p.WorkingSet64;PrivateMemoryBytes=[long]$p.PrivateMemorySize64;VirtualMemoryBytes=[long]$p.VirtualMemorySize64;HandleCount=$p.HandleCount;ThreadCount=@($p.Threads).Count;PriorityClass=try{$p.PriorityClass.ToString()}catch{'Unknown'};Responding=try{$p.Responding}catch{$null};SessionId=$p.SessionId;EvidenceType='LiveProcessObservation'}}
}
function Get-WinCareMemoryInternals {
    [CmdletBinding()]
    param()
    if($IsWindows){$os=Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue;$page=@(Get-CimInstance Win32_PageFileUsage -ErrorAction SilentlyContinue);$total=if($os){[long]$os.TotalVisibleMemorySize*1KB}else{[GC]::GetGCMemoryInfo().TotalAvailableMemoryBytes};$free=if($os){[long]$os.FreePhysicalMemory*1KB}else{[math]::Max(0,$total-[GC]::GetTotalMemory($false))};return [pscustomobject]@{TotalBytes=$total;FreeBytes=$free;UsedBytes=$total-$free;CommittedBytes=if($os){[long]($os.TotalVirtualMemorySize-$os.FreeVirtualMemory)*1KB}else{$null};PageFiles=@($page|ForEach-Object{[pscustomobject]@{Name=$_.Name;AllocatedBaseSizeMB=$_.AllocatedBaseSize;CurrentUsageMB=$_.CurrentUsage;PeakUsageMB=$_.PeakUsage}});CapturedAt=[datetime]::UtcNow.ToString('o')}}
    $info=[GC]::GetGCMemoryInfo();[pscustomobject]@{TotalBytes=[long]$info.TotalAvailableMemoryBytes;FreeBytes=[math]::Max(0,[long]$info.TotalAvailableMemoryBytes-[GC]::GetTotalMemory($false));UsedBytes=[GC]::GetTotalMemory($false);CommittedBytes=$null;PageFiles=@();CapturedAt=[datetime]::UtcNow.ToString('o')}
}
function Get-WinCareProcessorTopology {
    [CmdletBinding()]
    param()
    if($IsWindows){$cpus=@(Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue);$computer=Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue;return [pscustomobject]@{Sockets=$cpus.Count;PhysicalCores=[int](($cpus|Measure-Object NumberOfCores -Sum).Sum);LogicalProcessors=if($computer){[int]$computer.NumberOfLogicalProcessors}else{[Environment]::ProcessorCount};NumaNodes=@(Get-CimInstance Win32_NumaNode -ErrorAction SilentlyContinue).Count;Processors=@($cpus|Select-Object DeviceID,Name,Manufacturer,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,VirtualizationFirmwareEnabled);Architecture=[Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()}}
    [pscustomobject]@{Sockets=$null;PhysicalCores=$null;LogicalProcessors=[Environment]::ProcessorCount;NumaNodes=$null;Processors=@();Architecture=[Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()}
}
function Get-WinCareCredentialProvider {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return @()};$root='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers';$items=[Collections.Generic.List[object]]::new();foreach($key in @(Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue)){try{$clsid=$key.PSChildName;$name=(Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction Stop).'(default)';$disabled=try{[bool](Get-ItemProperty -LiteralPath $key.PSPath -Name Disabled -ErrorAction Stop).Disabled}catch{$false};$items.Add([pscustomobject]@{Clsid=$clsid;Name=$name;Disabled=$disabled;RegistryPath=$key.PSPath})}catch{}};return @($items)
}
function Get-WinCareAppContainerCapability {
    [CmdletBinding()]
    param([string]$PackageName='')
    if(-not $IsWindows -or -not (Get-Command Get-AppxPackage -ErrorAction SilentlyContinue)){return @()}
    $packages=if([string]::IsNullOrWhiteSpace($PackageName)){@(Get-AppxPackage -ErrorAction SilentlyContinue)}else{@(Get-AppxPackage -Name "*$PackageName*" -ErrorAction SilentlyContinue)}
    $result=[Collections.Generic.List[object]]::new()
    foreach($package in $packages){
        $capabilities=[Collections.Generic.List[string]]::new();$manifestPath='';$manifestHash=''
        try{
            $manifestPath=Join-Path $package.InstallLocation 'AppxManifest.xml'
            if(Test-Path -LiteralPath $manifestPath -PathType Leaf){
                $manifestItem=Get-Item -LiteralPath $manifestPath -Force -ErrorAction Stop
                if($manifestItem.Attributes -band [IO.FileAttributes]::ReparsePoint -or [long]$manifestItem.Length -gt 4194304){throw 'AppX manifest is unsafe or exceeds 4 MiB.'}
                $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=4194304
                $reader=[Xml.XmlReader]::Create($manifestPath,$settings);$manifest=[Xml.XmlDocument]::new()
                try{$manifest.XmlResolver=$null;$manifest.Load($reader)}finally{$reader.Dispose()}
                foreach($node in @($manifest.SelectNodes("//*[local-name()='Capabilities']/*"))){
                    $name=$node.GetAttribute('Name');if(-not $name){$name=$node.Name};if($name -and -not $capabilities.Contains($name)){$capabilities.Add($name)}
                }
                $manifestHash=Get-WinCareSha256 -LiteralPath $manifestPath
            }
        }catch{}
        $result.Add([pscustomobject]@{Name=$package.Name;PackageFullName=$package.PackageFullName;PackageFamilyName=$package.PackageFamilyName;Publisher=$package.Publisher;Version=[string]$package.Version;Architecture=[string]$package.Architecture;SignatureKind=[string]$package.SignatureKind;Capabilities=@($capabilities|Sort-Object);InstallLocation=$package.InstallLocation;ManifestPath=$manifestPath;ManifestSha256=$manifestHash;EvidenceType='InstalledAppxManifest'})
    }
    return @($result|Sort-Object Name,Version)
}
function New-WinCareProcessTuningPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$ProcessId,[ValidateSet('Idle','BelowNormal','Normal','AboveNormal','High','RealTime')][string]$Priority='Normal',[Nullable[long]]$AffinityMask)
    $process=Get-Process -Id $ProcessId -ErrorAction Stop;$parameters=@{ProcessId=$ProcessId;Priority=$Priority;ProcessStartTime=$process.StartTime.ToUniversalTime().ToString('o')};if($null -ne $AffinityMask){$parameters.AffinityMask=[long]$AffinityMask};$action=New-WinCareBridgeAction -Type 'SetProcessTuning' -Label "Tune process $($process.ProcessName)" -Risk Moderate -Parameters $parameters -Reversible $true -Compensator @{Type='SetProcessTuning';Parameters=@{ProcessId=$ProcessId;Priority=$process.PriorityClass.ToString();AffinityMask=[long]$process.ProcessorAffinity;ProcessStartTime=$process.StartTime.ToUniversalTime().ToString('o')};RequiresAdmin=$false} -Verification 'Process identity, priority, and optional affinity must match after mutation.';New-WinCareBridgePlan -Title 'Tune process' -Actions @($action)
}
function Invoke-WinCareProcessTuningAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;try{$process=Get-Process -Id ([int]$p.ProcessId) -ErrorAction Stop;$start=$process.StartTime.ToUniversalTime().ToString('o');if($p.ProcessStartTime -and $start -ne [string]$p.ProcessStartTime){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'ProcessIdentityChanged' -Message 'Process ID was reused after preview.' -ExitCode 74};$before=[ordered]@{Priority=$process.PriorityClass.ToString();AffinityMask=[long]$process.ProcessorAffinity};$process.PriorityClass=[Diagnostics.ProcessPriorityClass]::$($p.Priority);if($p.Contains('AffinityMask')){$process.ProcessorAffinity=[intptr][long]$p.AffinityMask};$process.Refresh();$success=$process.PriorityClass.ToString() -eq [string]$p.Priority -and (-not $p.Contains('AffinityMask') -or [long]$process.ProcessorAffinity -eq [long]$p.AffinityMask);New-WinCareBridgeResult -Success $success -Code $(if($success){'ProcessTuningApplied'}else{'ProcessTuningVerificationFailed'}) -Message $(if($success){'Process tuning was applied and observed.'}else{'Process tuning could not be verified.'}) -Data @{ProcessId=$process.Id;ProcessName=$process.ProcessName;Before=$before;After=@{Priority=$process.PriorityClass.ToString();AffinityMask=[long]$process.ProcessorAffinity};EvidenceType='LiveProcessState';Compensator=@{Type='SetProcessTuning';Parameters=@{ProcessId=$process.Id;Priority=$before.Priority;AffinityMask=$before.AffinityMask;ProcessStartTime=$start};RequiresAdmin=$false}} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'ProcessTuningFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Get-WinCareLicensingSummary {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return [pscustomobject]@{Supported=$false;Products=@()}};$products=@(Get-CimInstance SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL" -ErrorAction SilentlyContinue|Where-Object{$_.Name -match 'Windows'}|Select-Object Name,Description,LicenseStatus,PartialProductKey,GracePeriodRemaining,EvaluationEndDate);[pscustomobject]@{Supported=$true;Products=$products;Licensed=(@($products|Where-Object{$_.LicenseStatus -eq 1}).Count -gt 0);CapturedAt=[datetime]::UtcNow.ToString('o')}
}
function Measure-WinCareCpuDiagnostic {
    [CmdletBinding()]
    param(
        [Alias('Samples')][ValidateRange(1,60)][int]$Seconds=5,
        [Alias('IntervalSeconds')][ValidateRange(1,64)][int]$Threads=[math]::Min(2,[Environment]::ProcessorCount)
    )
    $started=[datetime]::UtcNow;$jobs=@();$workUnits=[long]0;$completed=$false;$mode='SingleRunspaceFallback'
    try{
        if(Get-Command Start-ThreadJob -ErrorAction SilentlyContinue){
            $mode='ThreadJob';$deadline=$started.AddSeconds($Seconds).ToString('o')
            foreach($index in 1..$Threads){
                $jobs+=Start-ThreadJob -ArgumentList $deadline,$index -ScriptBlock {
                    param($Deadline,$WorkerId)
                    $end=[datetime]::Parse($Deadline).ToUniversalTime();[long]$count=0;$value=[double]($WorkerId+1)
                    while([datetime]::UtcNow -lt $end){for($i=0;$i -lt 25000;$i++){$value=[math]::Sqrt(($value*$value)+$i+1);if($value -gt 1000000){$value=$WorkerId+1}};$count+=25000}
                    [pscustomobject]@{WorkerId=$WorkerId;WorkUnits=$count;Checksum=$value}
                }
            }
            $null=Wait-Job -Job $jobs -Timeout ($Seconds+30)
            $records=@(Receive-Job -Job $jobs -ErrorAction SilentlyContinue)
            $workUnits=[long](($records|Measure-Object WorkUnits -Sum).Sum)
            $completed=@($jobs|Where-Object State -ne 'Completed').Count -eq 0
        }else{
            $deadline=$started.AddSeconds($Seconds);$value=[double]1
            while([datetime]::UtcNow -lt $deadline){for($i=0;$i -lt 25000;$i++){$value=[math]::Sqrt(($value*$value)+$i+1);if($value -gt 1000000){$value=1}};$workUnits+=25000}
            $completed=$true
        }
    }finally{
        foreach($job in @($jobs)){if($job.State -notin @('Completed','Failed','Stopped')){Stop-Job $job -ErrorAction SilentlyContinue};Remove-Job $job -Force -ErrorAction SilentlyContinue}
    }
    $finished=[datetime]::UtcNow
    [pscustomobject]@{DurationSeconds=[math]::Round(($finished-$started).TotalSeconds,2);RequestedSeconds=$Seconds;Threads=$Threads;Mode=$mode;Completed=$completed;WorkUnits=$workUnits;StartedAt=$started.ToString('o');FinishedAt=$finished.ToString('o');Purpose='Bounded CPU scheduler and arithmetic diagnostic; this is not a benchmark.';EvidenceType='TimedExecutedWorkload'}
}
