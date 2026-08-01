#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: System toolkit, hardening, maintenance, downloads, display, WLAN, logs, tasks, and legacy compatibility.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function Get-WinCareSystemToolkitRoot {
    [CmdletBinding()]
    param()
    Get-WinCareBridgeStateRoot 'Toolkit'
}
function Get-WinCareSystemToolkitDiagnostic {
    [CmdletBinding()]
    param()
    $diagnostics=[Collections.Generic.List[object]]::new();$summary=Get-WinCareSystemSummary;$security=Get-WinCareSecuritySummary;$network=Get-WinCareNetworkSummary;$cpu=Measure-WinCareCpuDiagnostic -Samples 1;$memory=Get-WinCareMemoryInternals
    $diagnostics.Add([pscustomobject]@{Id='os';Category='System';Status='Observed';Title='Operating system';Value="$($summary.Windows) build $($summary.Build)";Evidence=$summary})
    $diagnostics.Add([pscustomobject]@{Id='cpu';Category='Performance';Status=if($cpu.UsagePercent -gt 90){'Warning'}else{'Healthy'};Title='CPU utilization';Value=$cpu.UsagePercent;Evidence=$cpu})
    $memoryPercent=if($memory.TotalBytes){[math]::Round((($memory.TotalBytes-$memory.FreeBytes)/$memory.TotalBytes)*100,2)}else{0};$diagnostics.Add([pscustomobject]@{Id='memory';Category='Performance';Status=if($memoryPercent -gt 90){'Warning'}else{'Healthy'};Title='Memory utilization';Value=$memoryPercent;Evidence=$memory})
    foreach($drive in @($summary.Drives)){$diagnostics.Add([pscustomobject]@{Id=('drive-'+$drive.DeviceId);Category='Storage';Status=if($drive.FreePercent -lt 10){'Warning'}else{'Healthy'};Title="Drive $($drive.DeviceId) free space";Value=$drive.FreePercent;Evidence=$drive})}
    $diagnostics.Add([pscustomobject]@{Id='defender';Category='Security';Status=if($security.DefenderState -eq 'Enabled'){'Healthy'}elseif($security.DefenderState -eq 'Unknown'){'Unknown'}else{'Warning'};Title='Microsoft Defender';Value=$security.DefenderState;Evidence=$security})
    $diagnostics.Add([pscustomobject]@{Id='firewall';Category='Security';Status=if($security.FirewallState -eq 'Enabled'){'Healthy'}elseif($security.FirewallState -eq 'Unknown'){'Unknown'}else{'Warning'};Title='Windows Firewall';Value=$security.FirewallState;Evidence=$security})
    $diagnostics.Add([pscustomobject]@{Id='network';Category='Network';Status=if($network.InternetReachable){'Healthy'}else{'Warning'};Title='Connectivity';Value=$network.InternetReachable;Evidence=$network})
    $diagnostics.Add([pscustomobject]@{Id='restart';Category='Maintenance';Status=if($summary.PendingRestart){'Info'}else{'Healthy'};Title='Pending restart';Value=$summary.PendingRestart;Evidence=@{PendingRestart=$summary.PendingRestart}})
    return @($diagnostics)
}
function Get-WinCareWin32ErrorMessage {
    [CmdletBinding()]
    param([Alias('Code')][int]$ErrorCode=0)
    $message=[ComponentModel.Win32Exception]::new($ErrorCode).Message;[pscustomobject]@{ErrorCode=$ErrorCode;Hex=('0x{0:X8}' -f ([uint32]$ErrorCode));Message=$message;EvidenceType='SystemWin32MessageTable'}
}
function Get-WinCareMsiMetadata {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('LiteralPath')][string]$MsiPath)
    $path=(Resolve-Path -LiteralPath $MsiPath -ErrorAction Stop).Path;if(-not $IsWindows){return [pscustomobject]@{Path=$path;Sha256=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant();Supported=$false;EvidenceType='FileHashOnly'}}
    $properties=@('ProductCode','ProductName','ProductVersion','Manufacturer','UpgradeCode','ALLUSERS','InstallScope','Template');$result=[ordered]@{Path=$path;Sha256=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant();Supported=$true;EvidenceType='WindowsInstallerDatabase'}
    $installer=$null;$database=$null;$view=$null;try{$installer=New-Object -ComObject WindowsInstaller.Installer;$database=$installer.GetType().InvokeMember('OpenDatabase','InvokeMethod',$null,$installer,@($path,0));foreach($name in $properties){$query="SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$name'";$view=$database.GetType().InvokeMember('OpenView','InvokeMethod',$null,$database,@($query));$view.GetType().InvokeMember('Execute','InvokeMethod',$null,$view,$null)|Out-Null;$record=$view.GetType().InvokeMember('Fetch','InvokeMethod',$null,$view,$null);if($record){$value=$record.GetType().InvokeMember('StringData','GetProperty',$null,$record,1);$result[$name]=$value};try{$view.GetType().InvokeMember('Close','InvokeMethod',$null,$view,$null)|Out-Null}catch{}};[pscustomobject]$result}catch{throw "MSI metadata query failed: $($_.Exception.Message)"}finally{foreach($object in @($view,$database,$installer)){if($object){try{[Runtime.InteropServices.Marshal]::FinalReleaseComObject($object)|Out-Null}catch{}}}}
}
function Get-WinCareHardeningProfile {
    [CmdletBinding()]
    param([string]$Id='')
    $profiles=@(
        [pscustomobject]@{Id='balanced';Title='Balanced workstation hardening';Description='Enables firewall, UAC, Defender cloud protection, and PowerShell script-block logging without disabling common workstation workflows.';Risk='High';Actions=@(
            @{Type='SetFirewallProfileState';Label='Enable all firewall profiles';Risk='High';RequiresAdmin=$true;Parameters=@{Profiles=@('Domain','Private','Public');Enabled=$true;Before=@{}}},
            @{Type='SetRegistryValue';Label='Enable UAC';Risk='High';RequiresAdmin=$true;Parameters=@{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System';Name='EnableLUA';Value=1;ValueType='DWord';Before=@{}}},
            @{Type='SetDefenderPreference';Label='Enable Defender cloud protection';Risk='Low';RequiresAdmin=$true;Parameters=@{Name='MAPSReporting';Value=2;Before=$null}},
            @{Type='SetRegistryValue';Label='Enable PowerShell script block logging';Risk='Moderate';RequiresAdmin=$true;Parameters=@{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging';Name='EnableScriptBlockLogging';Value=1;ValueType='DWord';Before=@{}}}
        )},
        [pscustomobject]@{Id='strict';Title='Strict workstation hardening';Description='Balanced controls plus stricter Defender, SMB, and credential protections. Compatibility testing is required.';Risk='Critical';Actions=@(
            @{Type='SetFirewallProfileState';Label='Enable all firewall profiles';Risk='High';RequiresAdmin=$true;Parameters=@{Profiles=@('Domain','Private','Public');Enabled=$true;Before=@{}}},
            @{Type='SetDefenderPreference';Label='Enable PUA protection';Risk='Low';RequiresAdmin=$true;Parameters=@{Name='PUAProtection';Value=1;Before=$null}},
            @{Type='SetDefenderPreference';Label='Enable network protection';Risk='High';RequiresAdmin=$true;Parameters=@{Name='EnableNetworkProtection';Value=1;Before=$null}},
            @{Type='SetRegistryValue';Label='Disable SMB1 server';Risk='High';RequiresAdmin=$true;Parameters=@{Path='HKLM:\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters';Name='SMB1';Value=0;ValueType='DWord';Before=@{}}},
            @{Type='SetRegistryValue';Label='Enable LSA protection';Risk='Critical';RequiresAdmin=$true;Parameters=@{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Lsa';Name='RunAsPPL';Value=1;ValueType='DWord';Before=@{}}}
        )},
        [pscustomobject]@{Id='hailmary';Title='Emergency containment hardening';Description='Emergency-only containment: enables firewall, blocks inbound by default, enables Defender controls, and disables remote desktop. Requires explicit critical authorization.';Risk='Critical';Actions=@(
            @{Type='SetFirewallProfileState';Label='Enable firewall containment';Risk='Critical';RequiresAdmin=$true;Parameters=@{Profiles=@('Domain','Private','Public');Enabled=$true;Before=@{}}},
            @{Type='SetDefenderPreference';Label='Enable Defender network protection';Risk='Critical';RequiresAdmin=$true;Parameters=@{Name='EnableNetworkProtection';Value=1;Before=$null}},
            @{Type='SetRegistryValue';Label='Disable Remote Desktop';Risk='Critical';RequiresAdmin=$true;Parameters=@{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server';Name='fDenyTSConnections';Value=1;ValueType='DWord';Before=@{}}}
        )}
    )
    if($Id){return @($profiles|Where-Object{$_.Id -eq $Id})};return $profiles
}
function Get-WinCareHardeningAssessment {
    [CmdletBinding()]
    param([string]$ProfileId='balanced')
    $profile=Get-WinCareHardeningProfile -Id $ProfileId|Select-Object -First 1;if(-not $profile){throw "Unknown hardening profile: $ProfileId"};$findings=[Collections.Generic.List[object]]::new()
    foreach($definition in @($profile.Actions)){$p=ConvertTo-WinCareBridgeHashtable $definition.Parameters;$compliant=$false;$observed=$null;switch([string]$definition.Type){'SetFirewallProfileState'{$profiles=@(Get-NetFirewallProfile -ErrorAction SilentlyContinue|Where-Object{$_.Name -in @($p.Profiles)});$observed=@($profiles|Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction);$compliant=$profiles.Count -eq @($p.Profiles).Count -and @($profiles|Where-Object{$_.Enabled -ne [bool]$p.Enabled}).Count -eq 0}'SetRegistryValue'{try{$observed=(Get-ItemProperty -LiteralPath $p.Path -Name $p.Name -ErrorAction Stop).$($p.Name);$compliant=$observed -eq $p.Value}catch{$observed=$null;$compliant=$false}}'SetDefenderPreference'{try{$pref=Get-MpPreference -ErrorAction Stop;$observed=$pref.$($p.Name);$compliant=$observed -eq $p.Value}catch{$observed=$null;$compliant=$false}}};$findings.Add([pscustomobject]@{ProfileId=$ProfileId;Control=$definition.Label;Type=$definition.Type;Compliant=$compliant;Expected=$p;Observed=$observed;Severity=if($compliant){'Info'}else{$definition.Risk}})};[pscustomobject]@{Profile=$profile;Compliant=@($findings|Where-Object{$_.Compliant}).Count;NonCompliant=@($findings|Where-Object{-not $_.Compliant}).Count;Findings=@($findings);AssessedAt=[datetime]::UtcNow.ToString('o')}
}
function New-WinCareHardeningProfilePlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProfileId)
    $profile=Get-WinCareHardeningProfile -Id $ProfileId|Select-Object -First 1;if(-not $profile){throw "Unknown hardening profile: $ProfileId"};$actions=[Collections.Generic.List[object]]::new()
    foreach($definition in @($profile.Actions)){$parameters=ConvertTo-WinCareBridgeHashtable $definition.Parameters;$compensator=@{};switch([string]$definition.Type){'SetRegistryValue'{$exists=$false;$value=$null;$kind=$null;try{$key=Get-Item -LiteralPath $parameters.Path -ErrorAction Stop;$value=(Get-ItemProperty -LiteralPath $parameters.Path -Name $parameters.Name -ErrorAction Stop).$($parameters.Name);$kind=$key.GetValueKind($parameters.Name).ToString();$exists=$true}catch{};$parameters.Before=@{Exists=$exists;Value=$value;ValueType=$kind};$compensator=@{Type='RestoreRegistryBackup';Parameters=@{Path=$parameters.Path;Name=$parameters.Name;Exists=$exists;Value=$value;ValueType=$kind};RequiresAdmin=$true}}'SetFirewallProfileState'{$before=@{};foreach($profileState in @(Get-NetFirewallProfile -ErrorAction SilentlyContinue|Where-Object{$_.Name -in @($parameters.Profiles)})){$before[$profileState.Name]=[bool]$profileState.Enabled};$parameters.Before=$before;$compensator=@{Type='SetFirewallProfileState';Parameters=@{Profiles=@($before.Keys);Enabled=$true;Before=$before};RequiresAdmin=$true}}'SetDefenderPreference'{try{$pref=Get-MpPreference -ErrorAction Stop;$parameters.Before=$pref.$($parameters.Name)}catch{};$compensator=@{Type='SetDefenderPreference';Parameters=@{Name=$parameters.Name;Value=$parameters.Before;Before=$parameters.Value};RequiresAdmin=$true}}};$actions.Add((New-WinCareBridgeAction -Type $definition.Type -Label $definition.Label -Risk $definition.Risk -Parameters $parameters -RequiresAdmin ([bool]$definition.RequiresAdmin) -Reversible $true -Compensator $compensator -Verification 'The target control must be re-read and equal the requested value.'))}
    New-WinCareBridgePlan -Title $profile.Title -Description $profile.Description -Actions @($actions) -Metadata @{ProfileId=$ProfileId;Assessment=Get-WinCareHardeningAssessment -ProfileId $ProfileId}
}
function Get-WinCareMaintenanceTemplate {
    [CmdletBinding()]
    param([Alias('TemplateId')][string]$Id='')
    $templates=@(
        [pscustomobject]@{Id='weekly-health';Title='Weekly workstation health';Description='Bounded weekly health and telemetry window.';DurationMinutes=90;RequiresRestart=$false;PlaybookId='';Tags=@('Health','Weekly')},
        [pscustomobject]@{Id='monthly-deep-health';Title='Monthly deep health';Description='Longer monthly health and Defender maintenance window.';DurationMinutes=240;RequiresRestart=$false;PlaybookId='';Tags=@('Health','Security','Monthly')}
    )
    if($Id){return @($templates|Where-Object{$_.Id -eq $Id})}
    return $templates
}
function New-WinCareMaintenanceTemplatePlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('Id')][string]$TemplateId,[Parameter(Mandatory)][datetimeoffset]$StartAt,[string]$Name='')
    $template=Get-WinCareMaintenanceTemplate -TemplateId $TemplateId|Select-Object -First 1
    if(-not $template){throw "Unknown maintenance template: $TemplateId"}
    if(-not $Name){$Name=[string]$template.Title}
    $window=New-WinCareMaintenanceWindowRecord -Name $Name -Description ([string]$template.Description) -StartAt $StartAt -EndAt $StartAt.AddMinutes([int]$template.DurationMinutes) -PlaybookId ([string]$template.PlaybookId) -Tags @($template.Tags) -RequiresRestart ([bool]$template.RequiresRestart) -SourceRecords @('SRC:1c7d9e2add')
    New-WinCareMaintenanceUpsertPlan -Window $window
}
function Get-WinCareSystemShortcutCatalog {
    [CmdletBinding()]
    param([string]$Query='',[ValidateRange(1,500)][int]$Limit=250)
    $catalog=@(
        [pscustomobject]@{Id='event-viewer';Title='Event Viewer';Target='eventvwr.msc';Arguments='';Category='Administration'},
        [pscustomobject]@{Id='services';Title='Services';Target='services.msc';Arguments='';Category='Administration'},
        [pscustomobject]@{Id='task-scheduler';Title='Task Scheduler';Target='taskschd.msc';Arguments='';Category='Administration'},
        [pscustomobject]@{Id='device-manager';Title='Device Manager';Target='devmgmt.msc';Arguments='';Category='Hardware'},
        [pscustomobject]@{Id='disk-management';Title='Disk Management';Target='diskmgmt.msc';Arguments='';Category='Storage'},
        [pscustomobject]@{Id='windows-security';Title='Windows Security';Target='windowsdefender:';Arguments='';Category='Security'},
        [pscustomobject]@{Id='network-settings';Title='Network settings';Target='ms-settings:network';Arguments='';Category='Network'},
        [pscustomobject]@{Id='storage-settings';Title='Storage settings';Target='ms-settings:storagesense';Arguments='';Category='Storage'}
    )
    if($Query){$catalog=@($catalog|Where-Object{$_.Id -like "*$Query*" -or $_.Title -like "*$Query*" -or $_.Category -like "*$Query*"})}
    @($catalog|Select-Object -First $Limit)
}
function New-WinCareSystemShortcutExportPlan {
    [CmdletBinding()]
    param([string]$Name='system-shortcuts.json',[string]$Query='')
    if($Name -notmatch '^[A-Za-z0-9._-]{1,120}\.json$'){throw 'Shortcut export name must be a simple .json file name.'}
    $catalog=@(Get-WinCareSystemShortcutCatalog -Query $Query -Limit 500)
    $manifest=[ordered]@{SchemaVersion=2;GeneratedAt=[datetime]::UtcNow.ToString('o');Query=$Query;Shortcuts=$catalog}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($manifest|ConvertTo-Json -Depth 10))
    if($bytes.Length -gt 1MB){throw 'Shortcut export exceeds 1 MiB.'}
    $root=Get-WinCareSystemToolkitRoot;$path=Join-Path $root $Name
    $action=New-WinCareManagedFileWriteAction -Path $path -Content ([Text.Encoding]::UTF8.GetString($bytes)) -AllowedRoots @($root)
    New-WinCareBridgePlan -Title 'Export system shortcuts' -Description 'Writes a bounded shortcut manifest under the WinCare toolkit state root.' -Actions @($action)
}
function Import-WinCareDownloadBatchDefinition {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('Path')][string]$LiteralPath)
    $resolved=(Resolve-Path -LiteralPath $LiteralPath -ErrorAction Stop).Path;$item=Get-Item -LiteralPath $resolved -Force
    if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Download batch file cannot be a reparse point.'}
    if($item.Length -gt 256KB){throw 'Download batch exceeds 256 KiB.'}
    $definition=Read-WinCareBoundedJson -LiteralPath $resolved -MaximumBytes 262144 -Depth 40
    $null=Test-WinCareStrictObjectKeys -InputObject $definition -AllowedKeys @('SchemaVersion','Jobs') -Context 'download batch'
    if([int](Get-WinCareBridgeProperty $definition 'SchemaVersion' 1) -ne 1){throw 'Download batch schema is unsupported.'}
    $jobs=@(Get-WinCareBridgeProperty $definition 'Jobs' @())
    if($jobs.Count -lt 1 -or $jobs.Count -gt 100){throw 'Download batch must contain 1..100 jobs.'}
    $validated=[Collections.Generic.List[object]]::new();$destinations=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($job in $jobs){
        $null=Test-WinCareStrictObjectKeys -InputObject $job -AllowedKeys @('Id','Name','Urls','Uri','Destination','Sha256','Headers','CollisionPolicy','Priority','MaxRetries','ScheduledAt') -Context 'download batch job'
        $urls=@(Get-WinCareBridgeProperty $job 'Urls' @());if(-not $urls.Count){$uri=[string](Get-WinCareBridgeProperty $job 'Uri' '');if($uri){$urls=@($uri)}}
        if($urls.Count -lt 1 -or $urls.Count -gt 8){throw 'Each batch job must provide 1..8 URLs.'}
        $destination=[IO.Path]::GetFullPath([string](Get-WinCareBridgeProperty $job 'Destination' ''))
        if(-not $destinations.Add($destination)){throw "Duplicate download destination: $destination"}
        $sha=[string](Get-WinCareBridgeProperty $job 'Sha256' '');if($sha -and $sha -notmatch '^[a-fA-F0-9]{64}$'){throw 'Download SHA-256 is invalid.'}
        $headers=ConvertTo-WinCareBridgeHashtable (Get-WinCareBridgeProperty $job 'Headers' @{})
        $parameters=@{Url=@($urls);Destination=$destination;Name=[string](Get-WinCareBridgeProperty $job 'Name' ([IO.Path]::GetFileName($destination)));CollisionPolicy=[string](Get-WinCareBridgeProperty $job 'CollisionPolicy' 'Fail');HashAlgorithm=if($sha){'SHA256'}else{'None'};ExpectedHash=$sha;Headers=$headers;Priority=[string](Get-WinCareBridgeProperty $job 'Priority' 'Normal');MaxRetries=[int](Get-WinCareBridgeProperty $job 'MaxRetries' 3);SourceRecords=@('SRC:1c7d9e2add')}
        $scheduled=[string](Get-WinCareBridgeProperty $job 'ScheduledAt' '');if($scheduled){$parameters.ScheduledAt=[datetimeoffset]::Parse($scheduled)}
        $record=New-WinCareDownloadJobRecord @parameters
        $providedId=[string](Get-WinCareBridgeProperty $job 'Id' '');if($providedId){if($providedId -notmatch '^[a-fA-F0-9]{32}$'){throw 'Download batch job Id must be 32 hexadecimal characters.'};$record.Id=$providedId.ToLowerInvariant();$record.TemporaryPath=Join-Path (Join-Path (Get-WinCareDownloadRoot) 'Partial') ($record.Id+'.part')}
        $validated.Add($record)
    }
    [pscustomobject]@{SchemaVersion=1;SourcePath=$resolved;SourceSha256=Get-WinCareSha256 -LiteralPath $resolved;Jobs=@($validated)}
}
function New-WinCareDownloadBatchPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('Jobs')][object]$Batch)
    $jobs=if($Batch -is [Collections.IEnumerable] -and $Batch -isnot [string] -and $null -eq (Get-WinCareBridgeProperty $Batch 'Jobs' $null)){@($Batch)}else{@(Get-WinCareBridgeProperty $Batch 'Jobs' @())}
    if($jobs.Count -lt 1 -or $jobs.Count -gt 100){throw 'Download batch must contain 1..100 jobs.'}
    $store=Get-WinCareDownloadStore -Strict;$next=ConvertTo-WinCareParameterDictionary $store
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($existing in @($store.Jobs)){$null=$ids.Add([string]$existing.Id)}
    $destinations=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($existing in @($store.Jobs)){$null=$destinations.Add([string]$existing.Destination)}
    $records=[Collections.Generic.List[object]]::new()
    foreach($job in $jobs){$map=ConvertTo-WinCareParameterDictionary $job;$null=Test-WinCareDownloadJobRecord $map;if(-not $ids.Add([string]$map.Id)){throw "Duplicate download job Id: $($map.Id)"};if(-not $destinations.Add([string]$map.Destination)){throw "Duplicate download destination: $($map.Destination)"};$records.Add($map)}
    if(@($store.Jobs).Count+$records.Count -gt [int](Get-WinCarePolicy 'MaximumDownloadJobs')){throw 'Download batch would exceed MaximumDownloadJobs.'}
    $next.Revision=[long]$store.Revision+1;$next.UpdatedAt=[datetime]::UtcNow.ToString('o');$next.Jobs=@($store.Jobs)+@($records)
    New-WinCareDownloadStoreReplacementPlan -Current $store -Next $next -Title "Queue download batch ($($records.Count) jobs)" -SourceRecords @('SRC:1c7d9e2add')
}
function Read-WinCareBencodeValue {
    [CmdletBinding()]
    param([Parameter(Mandatory)][byte[]]$Bytes,[Parameter(Mandatory)][ref]$Index,[ValidateRange(0,16)][int]$Depth=0)
    if($Depth -gt 16){throw 'Torrent metadata nesting exceeds 16 levels.'}
    if($Index.Value -lt 0 -or $Index.Value -ge $Bytes.Length){throw 'Unexpected end of bencoded data.'}
    $token=[char]$Bytes[$Index.Value]
    if($token -eq 'i'){
        $Index.Value++;$start=$Index.Value
        while($Index.Value -lt $Bytes.Length -and [char]$Bytes[$Index.Value] -ne 'e'){$Index.Value++}
        if($Index.Value -ge $Bytes.Length){throw 'Unterminated bencoded integer.'}
        $raw=[Text.Encoding]::ASCII.GetString($Bytes,$start,$Index.Value-$start);$Index.Value++
        if($raw -notmatch '^(0|-?[1-9][0-9]*)$'){throw 'Invalid bencoded integer.'}
        $number=[int64]0;if(-not [int64]::TryParse($raw,[ref]$number)){throw 'Bencoded integer is out of range.'};return $number
    }
    if($token -eq 'l'){
        $Index.Value++;$list=[Collections.Generic.List[object]]::new()
        while($true){if($Index.Value -ge $Bytes.Length){throw 'Unterminated bencoded list.'};if([char]$Bytes[$Index.Value] -eq 'e'){$Index.Value++;break};$list.Add((Read-WinCareBencodeValue -Bytes $Bytes -Index $Index -Depth ($Depth+1)))}
        return @($list)
    }
    if($token -eq 'd'){
        $Index.Value++;$dict=[ordered]@{}
        while($true){if($Index.Value -ge $Bytes.Length){throw 'Unterminated bencoded dictionary.'};if([char]$Bytes[$Index.Value] -eq 'e'){$Index.Value++;break};$keyBytes=Read-WinCareBencodeValue -Bytes $Bytes -Index $Index -Depth ($Depth+1);if($keyBytes -isnot [byte[]]){throw 'Bencoded dictionary key must be a byte string.'};$key=[Text.Encoding]::UTF8.GetString($keyBytes);if($dict.Contains($key)){throw "Duplicate bencoded dictionary key: $key"};$dict[$key]=Read-WinCareBencodeValue -Bytes $Bytes -Index $Index -Depth ($Depth+1)}
        return $dict
    }
    if([char]::IsDigit($token)){
        $start=$Index.Value
        while($Index.Value -lt $Bytes.Length -and [char]$Bytes[$Index.Value] -ne ':'){$Index.Value++}
        if($Index.Value -ge $Bytes.Length){throw 'Unterminated bencoded byte-string length.'}
        $raw=[Text.Encoding]::ASCII.GetString($Bytes,$start,$Index.Value-$start);if($raw -notmatch '^(0|[1-9][0-9]*)$'){throw 'Invalid bencoded byte-string length.'}
        $length=[int]0;if(-not [int]::TryParse($raw,[ref]$length)){throw 'Bencoded byte-string length is out of range.'};$Index.Value++
        if($length -gt $Bytes.Length-$Index.Value){throw 'Bencoded byte string exceeds remaining input.'}
        $data=[byte[]]::new($length);[Array]::Copy($Bytes,$Index.Value,$data,0,$length);$Index.Value+=$length;return $data
    }
    throw "Invalid bencode token '$token' at offset $($Index.Value)."
}
function ConvertFrom-WinCareBencodeText {
    param([AllowNull()][object]$Value)
    if($Value -is [byte[]]){try{return [Text.Encoding]::UTF8.GetString($Value)}catch{return [Convert]::ToBase64String($Value)}};if($Value -is [Collections.IDictionary]){$result=[ordered]@{};foreach($key in $Value.Keys){$result[$key]=ConvertFrom-WinCareBencodeText $Value[$key]};return $result};if($Value -is [Collections.IEnumerable] -and $Value -isnot [string]){return @($Value|ForEach-Object{ConvertFrom-WinCareBencodeText $_})};return $Value
}
function Get-WinCareTorrentMetadata {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('TorrentPath')][string]$LiteralPath)
    $path=(Resolve-Path -LiteralPath $LiteralPath -ErrorAction Stop).Path;$item=Get-Item -LiteralPath $path -Force
    if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Torrent metadata file cannot be a reparse point.'}
    if($item.Length -lt 1 -or $item.Length -gt 16MB){throw 'Torrent metadata must be between 1 byte and 16 MiB.'}
    $bytes=Read-WinCareBoundedFileBytes -LiteralPath $path -MaximumBytes 16777216;$index=0;$root=Read-WinCareBencodeValue -Bytes $bytes -Index ([ref]$index) -Depth 0
    if($index -ne $bytes.Length){throw 'Torrent metadata contains trailing bytes.'}
    if($root -isnot [Collections.IDictionary] -or -not $root.Contains('info')){throw 'Torrent metadata has no info dictionary.'}
    $text=ConvertFrom-WinCareBencodeText $root;$info=$text.info;$files=[Collections.Generic.List[object]]::new()
    if($info.files){foreach($file in @($info.files)){$parts=@($file.path);if($parts.Count -eq 0 -or @($parts|Where-Object{$_ -in @('','..','.') -or $_ -match '[\\/:*?"<>|]'}).Count){throw 'Torrent file path is unsafe.'};$length=[long]$file.length;if($length -lt 0){throw 'Torrent file length is invalid.'};$files.Add([pscustomobject]@{Path=($parts-join '/');Length=$length})}}
    else{$length=[long]$info.length;if($length -lt 0){throw 'Torrent file length is invalid.'};$files.Add([pscustomobject]@{Path=[string]$info.name;Length=$length})}
    [pscustomobject]@{Path=$path;Sha256=Get-WinCareSha256 -LiteralPath $path;Name=[string]$info.name;PieceLength=[long]$info.'piece length';PieceCount=if($info.pieces -is [byte[]]){[math]::Floor($info.pieces.Length/20)}else{$null};Private=[bool]$info.private;CreatedBy=[string]$text.'created by';CreationDate=if($text.'creation date'){[DateTimeOffset]::FromUnixTimeSeconds([long]$text.'creation date').UtcDateTime}else{$null};Announce=[string]$text.announce;AnnounceList=@($text.'announce-list');Files=@($files);FileCount=$files.Count;TotalBytes=[long](($files|Measure-Object Length -Sum).Sum);TotalLength=[long](($files|Measure-Object Length -Sum).Sum);NetworkExecution='Not implemented';EvidenceType='LocalBoundedBencodeParse'}
}
function Get-WinCareDisplayOverrideState {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return [pscustomobject]@{Supported=$false;Overrides=@();Hash=''}};$roots=@('HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration','HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Connectivity','HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\ScaleFactors');$snapshots=[Collections.Generic.List[object]]::new();foreach($root in $roots){if(Test-Path $root){$snapshots.Add([pscustomobject]@{Path=$root;Snapshot=Get-WinCareRegistryTreeSnapshot $root})}};$record=[ordered]@{Supported=$true;Overrides=@($snapshots);CapturedAt=[datetime]::UtcNow.ToString('o')};$record.Hash=if(Get-Command Get-WinCareCanonicalObjectHash -ErrorAction SilentlyContinue){Get-WinCareCanonicalObjectHash $record.Overrides}else{''};[pscustomobject]$record
}
function Test-WinCareDisplayOverrideSnapshot {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Snapshot)
    return $null -ne (Get-WinCareBridgeProperty $Snapshot 'Overrides' $null) -and [string](Get-WinCareBridgeProperty $Snapshot 'Hash' '') -match '^[a-f0-9]{64}$'
}
function New-WinCareDisplayOverrideResetPlan {
    [CmdletBinding()]
    param()
    $before=Get-WinCareDisplayOverrideState;if(-not (Test-WinCareDisplayOverrideSnapshot $before)){throw 'Display override snapshot could not be established.'};$paths=@($before.Overrides|ForEach-Object{$_.Path});$action=New-WinCareBridgeAction -Type 'ResetDisplayOverrides' -Label 'Reset display topology overrides' -Risk Critical -RequiresAdmin $true -Reversible $true -Parameters @{BeforeHash=$before.Hash;DevicePaths=$paths} -Compensator @{Type='RestoreDisplayOverrides';Parameters=@{Snapshot=$before;ExpectedCurrentHash=''};RequiresAdmin=$true} -TimeoutSeconds 600 -Verification 'Configured display override keys must be absent and a recovery snapshot must be available.';New-WinCareBridgePlan -Title 'Reset display overrides' -Description 'Backs up and removes GraphicsDrivers configuration/connectivity/scale-factor keys. Windows will rebuild them after sign-out or restart.' -Actions @($action)
}
function Invoke-WinCareResetDisplayOverridesAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Display override reset';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;$before=Get-WinCareDisplayOverrideState;if($before.Hash -ne [string]$p.BeforeHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'DisplayOverrideStateChanged' -Message 'Display override state changed after preview.' -ExitCode 74};try{$recovery=Join-Path (Get-WinCareBridgeStateRoot 'Recovery') ('display-overrides-'+[guid]::NewGuid().ToString('N')+'.json');Write-WinCareBridgeJson $recovery $before;foreach($path in @($p.DevicePaths)){if(Test-Path $path){Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop}};$after=Get-WinCareDisplayOverrideState;$remaining=@($p.DevicePaths|Where-Object{Test-Path $_});$success=$remaining.Count -eq 0;New-WinCareBridgeResult -Success $success -Code $(if($success){'DisplayOverridesReset'}else{'DisplayOverrideResetVerificationFailed'}) -Message $(if($success){'Display override keys were removed and recovery data was saved.'}else{'One or more display override keys remain.'}) -Data @{Before=$before;After=$after;RecoveryPath=$recovery;Remaining=$remaining;EvidenceType='RegistryKeyAbsence';Compensator=@{Type='RestoreDisplayOverrides';Parameters=@{Snapshot=$before;ExpectedCurrentHash=$after.Hash};RequiresAdmin=$true}} -ExitCode $(if($success){0}else{74}) -RestartRequired $true}catch{New-WinCareBridgeResult -Success $false -Code 'DisplayOverrideResetFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareRestoreDisplayOverridesAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Display override restore';if($blocked){return $blocked}
    $p=Get-WinCareActionParameters $Action;$snapshot=$p.Snapshot
    if(-not (Test-WinCareDisplayOverrideSnapshot $snapshot)){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InvalidDisplaySnapshot' -Message 'Display snapshot is invalid.' -ExitCode 22}
    $current=Get-WinCareDisplayOverrideState
    if($p.ExpectedCurrentHash -and $current.Hash -ne [string]$p.ExpectedCurrentHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'DisplayRecoveryConflict' -Message 'Display override state changed after reset; recovery was refused.' -ExitCode 74 -Data @{ExpectedCurrentHash=$p.ExpectedCurrentHash;ActualCurrentHash=$current.Hash}}
    try{
        foreach($entry in @($snapshot.Overrides)){Invoke-WinCareRestoreRegistrySnapshotDirect -Path $entry.Path -Snapshot $entry.Snapshot}
        $after=Get-WinCareDisplayOverrideState;$success=$after.Hash -eq [string]$snapshot.Hash
        New-WinCareBridgeResult -Success $success -Code $(if($success){'DisplayOverridesRestored'}else{'DisplayOverrideRestoreVerificationFailed'}) -Message $(if($success){'Display overrides were restored and hash-verified.'}else{'Display override restore hash did not match.'}) -Data @{BeforeRecovery=$current;After=$after;ExpectedHash=$snapshot.Hash;ExpectedCurrentHash=$p.ExpectedCurrentHash;EvidenceType='RegistryTreeHash'} -ExitCode $(if($success){0}else{74}) -RestartRequired $true
    }catch{New-WinCareBridgeResult -Success $false -Code 'DisplayOverrideRestoreFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Get-WinCareWlanSurvey {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return @()};$result=Invoke-WinCareBridgeProcess -FilePath 'netsh.exe' -ArgumentList @('wlan','show','networks','mode=bssid') -TimeoutSeconds 30;if(-not $result.Success){return @()};$records=[Collections.Generic.List[object]]::new();$current=$null;$bssid=$null;foreach($line in $result.Data.StdOut -split "`r?`n"){
        if($line -match '^\s*SSID\s+\d+\s*:\s*(.*)$'){if($current){$records.Add([pscustomobject]$current)};$current=[ordered]@{Ssid=$Matches[1].Trim();Authentication='';Encryption='';Bssids=@()}}
        elseif($current -and $line -match '^\s*Authentication\s*:\s*(.*)$'){$current.Authentication=$Matches[1].Trim()}
        elseif($current -and $line -match '^\s*Encryption\s*:\s*(.*)$'){$current.Encryption=$Matches[1].Trim()}
        elseif($current -and $line -match '^\s*BSSID\s+\d+\s*:\s*(.*)$'){$bssid=[ordered]@{Address=$Matches[1].Trim();SignalPercent=$null;RadioType='';Channel=$null};$current.Bssids+=@([pscustomobject]$bssid)}
        elseif($bssid -and $line -match '^\s*Signal\s*:\s*(\d+)%'){$bssid.SignalPercent=[int]$Matches[1]}
        elseif($bssid -and $line -match '^\s*Radio type\s*:\s*(.*)$'){$bssid.RadioType=$Matches[1].Trim()}
        elseif($bssid -and $line -match '^\s*Channel\s*:\s*(\d+)'){$bssid.Channel=[int]$Matches[1]}
    };if($current){$records.Add([pscustomobject]$current)};return @($records)
}
function Get-WinCareLogSnapshot {
    [CmdletBinding(DefaultParameterSetName='EventLog')]
    param(
        [Parameter(ParameterSetName='EventLog')][string]$LogName='Application',
        [Parameter(ParameterSetName='File',Mandatory)][string]$LiteralPath,
        [Alias('Pattern')][string]$FilterRegex='',
        [datetime]$Since=[datetime]::Now.AddHours(-24),
        [Alias('Limit')][ValidateRange(1,5000)][int]$MaxEvents=500
    )
    if($FilterRegex){try{$null=[regex]::new($FilterRegex)}catch{throw "Invalid log filter regex: $($_.Exception.Message)"}}
    if($PSCmdlet.ParameterSetName -eq 'File'){
        $path=(Resolve-Path -LiteralPath $LiteralPath -ErrorAction Stop).Path;$item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if(-not $item.PSIsContainer -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0){
            $allLines=@(Read-WinCareBoundedLines -LiteralPath $path -MaximumBytes 33554432 -MaximumLines 250000 -MaximumLineCharacters 262144)
            $lines=@($allLines | Select-Object -Last $MaxEvents);$index=0
            foreach($line in $lines){$index++;if($FilterRegex -and $line -notmatch $FilterRegex){continue};[pscustomobject]@{Timestamp=$item.LastWriteTimeUtc;Level='Line';Source=$item.Name;Message=$line;Path=$path;LineFromTail=$index;EvidenceType='BoundedFileTail'}}
            return
        }
        throw 'Log snapshot path must be a non-reparse-point file.'
    }
    if(-not $IsWindows){return @()};$events=@(Get-WinEvent -FilterHashtable @{LogName=$LogName;StartTime=$Since} -MaxEvents $MaxEvents -ErrorAction SilentlyContinue);if($FilterRegex){$events=@($events|Where-Object{($_.ProviderName+' '+$_.Message) -match $FilterRegex})};return @($events|ForEach-Object{[pscustomobject]@{Timestamp=$_.TimeCreated;Level=$_.LevelDisplayName;Source=$_.ProviderName;Message=$_.Message;TimeCreated=$_.TimeCreated;Id=$_.Id;LevelCode=$_.Level;ProviderName=$_.ProviderName;MachineName=$_.MachineName;RecordId=$_.RecordId;TaskDisplayName=$_.TaskDisplayName;EvidenceType='WindowsEventLog'}})
}
function Get-WinCarePeMetadata {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('LiteralPath')][string]$Path)
    $resolved=(Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path;$stream=[IO.File]::OpenRead($resolved);$reader=[IO.BinaryReader]::new($stream);try{if($reader.ReadUInt16() -ne 0x5A4D){throw 'Not an MZ executable.'};$stream.Position=0x3C;$peOffset=$reader.ReadInt32();if($peOffset -lt 64 -or $peOffset -gt $stream.Length-24){throw 'Invalid PE header offset.'};$stream.Position=$peOffset;if($reader.ReadUInt32() -ne 0x00004550){throw 'PE signature not found.'};$machine=$reader.ReadUInt16();$sections=$reader.ReadUInt16();$timestamp=$reader.ReadUInt32();$stream.Position+=8;$optionalSize=$reader.ReadUInt16();$characteristics=$reader.ReadUInt16();$magic=$reader.ReadUInt16();$architecture=switch($machine){0x14c{'x86'}0x8664{'x64'}0xAA64{'arm64'}0x1c4{'arm'}default{'unknown'}};$item=Get-Item $resolved;$signature=if($IsWindows -and (Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue)){Get-AuthenticodeSignature -LiteralPath $resolved -ErrorAction SilentlyContinue}else{$null};[pscustomobject]@{Path=$resolved;FileName=$item.Name;Length=$item.Length;Sha256=(Get-FileHash $resolved -Algorithm SHA256).Hash.ToLowerInvariant();Architecture=$architecture;Machine=('0x{0:X4}' -f $machine);SectionCount=$sections;Sections=$sections;Timestamp=[DateTimeOffset]::FromUnixTimeSeconds($timestamp).UtcDateTime;TimestampUtc=[DateTimeOffset]::FromUnixTimeSeconds($timestamp).UtcDateTime;Pe32Plus=$magic -eq 0x20b;PeKind=if($magic -eq 0x20b){'PE32+'}elseif($magic -eq 0x10b){'PE32'}else{'Unknown'};Characteristics=('0x{0:X4}' -f $characteristics);OptionalHeaderSize=$optionalSize;FileVersion=$item.VersionInfo.FileVersion;ProductVersion=$item.VersionInfo.ProductVersion;CompanyName=$item.VersionInfo.CompanyName;SignatureStatus=if($signature){$signature.Status.ToString()}else{'NotChecked'};Signer=if($signature -and $signature.SignerCertificate){$signature.SignerCertificate.Subject}else{$null};EvidenceType='ParsedPeHeaderHashAndSignature'}}finally{$reader.Dispose();$stream.Dispose()}
}
function Get-WinCareContainerLogMonitorConfiguration {
    [CmdletBinding()]
    param()
    $path=Join-Path (Get-WinCareBridgeStateRoot 'Telemetry') 'container-log-monitor.json';$config=Read-WinCareBridgeJson -LiteralPath $path -Default ([pscustomobject]@{SchemaVersion=1;Enabled=$false;Sources=@();Destination='';UpdatedAt=$null});$config|Add-Member -NotePropertyName Path -NotePropertyValue $path -Force;return $config
}
function New-WinCareContainerLogMonitorPlan {
    [CmdletBinding()]
    param([bool]$Enabled=$true,[string[]]$Sources=@(),[string]$Destination='',[string[]]$EventLog=@(),[string[]]$FilePath=@(),[string[]]$EtwProvider=@(),[string]$Name='container-log-monitor.json')
    if($Name -notmatch '^[A-Za-z0-9._-]{1,120}$'){throw 'Container log monitor configuration name is invalid.'};if(-not $Name.EndsWith('.json',[StringComparison]::OrdinalIgnoreCase)){$Name+='.json'}
    $files=[Collections.Generic.List[string]]::new();foreach($source in @($Sources)+@($FilePath)){if([string]::IsNullOrWhiteSpace($source)){continue};$full=(Resolve-Path -LiteralPath $source -ErrorAction Stop).Path;$item=Get-Item -LiteralPath $full -Force -ErrorAction Stop;if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "Container log source cannot be a reparse point: $full"};$files.Add($full)}
    foreach($nameValue in @($EventLog)){if($nameValue -notmatch '^[A-Za-z0-9._/ -]{1,200}$'){throw "Invalid event log name: $nameValue"}}
    foreach($provider in @($EtwProvider)){if($provider -notmatch '^[A-Za-z0-9._{}-]{1,200}$'){throw "Invalid ETW provider identity: $provider"}}
    if(-not $EventLog.Count -and -not $files.Count -and -not $EtwProvider.Count){$EventLog=@('System','Application')}
    if($Destination){$Destination=[IO.Path]::GetFullPath($Destination)}else{$Destination=Join-Path (Get-WinCareBridgeStateRoot 'Telemetry') 'container-logs'}
    $record=[ordered]@{SchemaVersion=2;Enabled=$Enabled;EventLogs=@($EventLog|Sort-Object -Unique);Files=@($files|Sort-Object -Unique);EtwProviders=@($EtwProvider|Sort-Object -Unique);Destination=$Destination;UpdatedAt=[datetime]::UtcNow.ToString('o');Mode='ConfigurationOnly';EvidenceType='PersistedMonitorConfiguration'}
    $path=Join-Path (Get-WinCareBridgeStateRoot 'Telemetry') $Name;$content=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($record|ConvertTo-Json -Depth 10)))
    $action=New-WinCareBridgeAction -Type 'WriteManagedFile' -Label 'Configure container log monitor' -Risk Moderate -Parameters @{Path=$path;ContentBase64=$content;ExpectedBeforeHash=if(Test-Path $path){(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()}else{''};AllowedRoots=@((Get-WinCareBridgeStateRoot 'Telemetry'))} -Reversible $true -Verification 'The persisted monitor configuration must equal the planned document.'
    New-WinCareBridgePlan -Title 'Configure container log monitor' -Description 'Persists validated local event-log, file, and ETW source definitions. Collection is performed only by an explicitly started monitor runtime.' -Actions @($action)
}
function Get-WinCareTaskBoard {
    [CmdletBinding()]
    param([Alias('Status')][string]$State='All')
    $normalized=switch -Regex ($State){'^(All)$'{'All';break}'^(Open|T[o]do|TaskT[o]do)$'{'Open';break}'^(InProgress|Doing|TaskDoing)$'{'InProgress';break}'^(Blocked|TaskBlocked)$'{'Blocked';break}'^(Done|Completed|TaskDone)$'{'Done';break}default{throw "Unsupported task state: $State"}}
    $path=Join-Path (Get-WinCareBridgeStateRoot 'Tasks') 'taskboard.json';$tasks=@(Read-WinCareBridgeJson -LiteralPath $path -Default @());if($normalized -ne 'All'){return @($tasks|Where-Object{$_.State -eq $normalized})};return $tasks
}
function New-WinCareTaskPlan {
    [CmdletBinding()]
    param([string]$Id=([guid]::NewGuid().ToString('N')),[Parameter(Mandatory)][string]$Title,[Alias('Body')][string]$Description='',[Alias('Status')][string]$State='Open',[string[]]$Tags=@(),[Nullable[datetime]]$DueAt,[switch]$Pinned)
    if([string]::IsNullOrWhiteSpace($Id)){$Id=[guid]::NewGuid().ToString('N')};if($Id -notmatch '^[A-Za-z0-9._-]{1,100}$'){throw 'Task ID is invalid.'}
    $normalized=switch -Regex ($State){'^(Open|T[o]do|TaskT[o]do)$'{'Open';break}'^(InProgress|Doing|TaskDoing)$'{'InProgress';break}'^(Blocked|TaskBlocked)$'{'Blocked';break}'^(Done|Completed|TaskDone)$'{'Done';break}default{throw "Unsupported task state: $State"}}
    $tasks=[Collections.Generic.List[object]]::new();foreach($task in Get-WinCareTaskBoard){if($task.Id -ne $Id){$tasks.Add($task)}};$record=[ordered]@{Id=$Id;Title=$Title;Description=$Description;State=$normalized;Pinned=[bool]$Pinned;Tags=@($Tags);DueAt=if($DueAt){$DueAt.Value.ToUniversalTime().ToString('o')}else{$null};UpdatedAt=[datetime]::UtcNow.ToString('o')};$tasks.Add([pscustomobject]$record);$path=Join-Path (Get-WinCareBridgeStateRoot 'Tasks') 'taskboard.json';$content=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((@($tasks)|ConvertTo-Json -Depth 10)));$action=New-WinCareBridgeAction -Type 'WriteManagedFile' -Label "Save task $Id" -Risk Low -Parameters @{Path=$path;ContentBase64=$content;ExpectedBeforeHash=if(Test-Path $path){(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()}else{''};AllowedRoots=@((Get-WinCareBridgeStateRoot 'Tasks'))} -Reversible $true -Verification 'The task board must contain the requested record after write.';New-WinCareBridgePlan -Title "Save task: $Title" -Actions @($action)
}

function Get-WinCareLegacySecurityControlSnapshot {
    param([Parameter(Mandatory)][string]$Control)
    switch($Control){
        'RealTimeProtection'{try{$value=-not (Get-MpPreference).DisableRealtimeMonitoring}catch{$value=$null};$record=[ordered]@{Control=$Control;Enabled=$value}}
        'FirewallPublic'{try{$value=[bool](Get-NetFirewallProfile -Name Public).Enabled}catch{$value=$null};$record=[ordered]@{Control=$Control;Enabled=$value}}
        'AdvertisingId'{$path='HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo';try{$value=(Get-ItemProperty $path -Name Enabled -ErrorAction Stop).Enabled}catch{$value=$null};$record=[ordered]@{Control=$Control;Enabled=([int]$value -ne 0);Path=$path;Name='Enabled';Raw=$value}}
        'TailoredExperiences'{$path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy';try{$value=(Get-ItemProperty $path -Name TailoredExperiencesWithDiagnosticDataEnabled -ErrorAction Stop).TailoredExperiencesWithDiagnosticDataEnabled}catch{$value=$null};$record=[ordered]@{Control=$Control;Enabled=([int]$value -ne 0);Path=$path;Name='TailoredExperiencesWithDiagnosticDataEnabled';Raw=$value}}
        'ConsumerFeatures'{$path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent';try{$value=(Get-ItemProperty $path -Name DisableWindowsConsumerFeatures -ErrorAction Stop).DisableWindowsConsumerFeatures}catch{$value=$null};$record=[ordered]@{Control=$Control;Enabled=([int]$value -ne 1);Path=$path;Name='DisableWindowsConsumerFeatures';Raw=$value}}
        default{throw "Unsupported legacy control: $Control"}
    };$record.Hash=if(Get-Command Get-WinCareCanonicalObjectHash -ErrorAction SilentlyContinue){Get-WinCareCanonicalObjectHash $record}else{''};[pscustomobject]$record
}
function Invoke-WinCareSetLegacySecurityControlStateAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$before=Get-WinCareLegacySecurityControlSnapshot -Control $p.Control;if($p.BeforeHash -and $before.Hash -ne [string]$p.BeforeHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'LegacyControlChanged' -Message 'Security control changed after preview.' -ExitCode 74};try{switch([string]$p.Control){'RealTimeProtection'{Set-MpPreference -DisableRealtimeMonitoring (-not [bool]$p.Enabled) -ErrorAction Stop}'FirewallPublic'{Set-NetFirewallProfile -Name Public -Enabled ([bool]$p.Enabled) -ErrorAction Stop}'AdvertisingId'{$null=New-Item $before.Path -Force;New-ItemProperty $before.Path -Name $before.Name -Value $(if($p.Enabled){1}else{0}) -PropertyType DWord -Force|Out-Null}'TailoredExperiences'{$null=New-Item $before.Path -Force;New-ItemProperty $before.Path -Name $before.Name -Value $(if($p.Enabled){1}else{0}) -PropertyType DWord -Force|Out-Null}'ConsumerFeatures'{$null=New-Item $before.Path -Force;New-ItemProperty $before.Path -Name $before.Name -Value $(if($p.Enabled){0}else{1}) -PropertyType DWord -Force|Out-Null}};$after=Get-WinCareLegacySecurityControlSnapshot -Control $p.Control;$success=$after.Enabled -eq [bool]$p.Enabled;New-WinCareBridgeResult -Success $success -Code $(if($success){'LegacyControlChanged'}else{'LegacyControlVerificationFailed'}) -Message $(if($success){'Legacy control state was changed and observed.'}else{'Legacy control state did not match the request.'}) -Data @{Before=$before;After=$after;EvidenceType='ObservedSecurityControl';Compensator=@{Type='RestoreLegacySecurityControlState';Parameters=@{Control=$p.Control;Snapshot=$before;ExpectedCurrentHash=$after.Hash};RequiresAdmin=$true}} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'LegacyControlChangeFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareRestoreLegacySecurityControlStateAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$current=Get-WinCareLegacySecurityControlSnapshot -Control $p.Control
    if($p.ExpectedCurrentHash -and $current.Hash -ne [string]$p.ExpectedCurrentHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'LegacySecurityRecoveryConflict' -Message 'Legacy security control changed after the original mutation; recovery was refused.' -ExitCode 74 -Data @{ExpectedCurrentHash=$p.ExpectedCurrentHash;ActualCurrentHash=$current.Hash}}
    $snapshot=$p.Snapshot;$synthetic=[pscustomobject]@{Parameters=@{Control=$p.Control;Enabled=[bool]$snapshot.Enabled;BeforeHash=$current.Hash}}
    $result=Invoke-WinCareSetLegacySecurityControlStateAction $synthetic
    if(-not $result.Success){return $result}
    $after=Get-WinCareLegacySecurityControlSnapshot -Control $p.Control;$success=[bool]$after.Enabled -eq [bool]$snapshot.Enabled
    New-WinCareBridgeResult -Success $success -Code $(if($success){'LegacySecurityControlRestored'}else{'LegacySecurityControlRestoreVerificationFailed'}) -Message $(if($success){'Legacy security control was restored and observed.'}else{'Legacy security control restore could not be verified.'}) -Data @{BeforeRecovery=$current;After=$after;ExpectedCurrentHash=$p.ExpectedCurrentHash;EvidenceType='ObservedSecurityControl'} -ExitCode $(if($success){0}else{74})
}
function Get-WinCareHibernateState {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return [pscustomobject]@{Supported=$false;Enabled=$false}};$enabled=Test-Path (Join-Path $env:SystemDrive 'hiberfil.sys');$reg=$null;try{$reg=(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Power' -Name HibernateEnabled -ErrorAction Stop).HibernateEnabled}catch{};[pscustomobject]@{Supported=$true;Enabled=$enabled -or [int]$reg -eq 1;HiberFile=Join-Path $env:SystemDrive 'hiberfil.sys';RegistryValue=$reg}
}
function Invoke-WinCareSetHibernateStateAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Hibernate configuration';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;$result=Invoke-WinCareBridgeProcess -FilePath 'powercfg.exe' -ArgumentList @('/hibernate',$(if([bool]$p.Enabled){'on'}else{'off'})) -TimeoutSeconds 120 -RequireAdmin;if(-not $result.Success){return $result};$after=Get-WinCareHibernateState;$success=$after.Enabled -eq [bool]$p.Enabled;New-WinCareBridgeResult -Success $success -Code $(if($success){'HibernateStateChanged'}else{'HibernateVerificationFailed'}) -Message $(if($success){'Hibernate state was changed and observed.'}else{'Hibernate state did not match the request.'}) -Data @{Before=$p.Before;After=$after;EvidenceType='HiberfileAndRegistry'} -ExitCode $(if($success){0}else{74})
}
