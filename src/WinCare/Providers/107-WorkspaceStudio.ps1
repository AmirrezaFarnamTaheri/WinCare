function Get-WinCareWorkspaceStudioRoot {
    [CmdletBinding()]
    param()
    Ensure-WinCareState
    Get-WinCareBridgeStateRoot 'WorkspaceStudio'
}

function Get-WinCareWorkspaceStudioStorePath {
    param([ValidateSet('FileWorkspaces','BrightnessSchedules')][string]$Kind)
    $name=if($Kind -eq 'FileWorkspaces'){'file-workspaces.json'}else{'brightness-schedules.json'}
    Join-Path (Get-WinCareWorkspaceStudioRoot) $name
}

function Get-WinCareWorkspaceStudioStore {
    param([ValidateSet('FileWorkspaces','BrightnessSchedules')][string]$Kind)
    $path=Get-WinCareWorkspaceStudioStorePath -Kind $Kind
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        return [ordered]@{SchemaVersion=1;Revision=0;UpdatedAt=[datetime]::UtcNow.ToString('o');Records=@();EvidenceType='WorkspaceStudioStore'}
    }
    $store=Read-WinCareJsonHashtable -LiteralPath $path
    $null=Test-WinCareStrictObjectKeys -InputObject $store -AllowedKeys @('SchemaVersion','Revision','UpdatedAt','Records') -Context "$Kind store"
    if([int]$store.SchemaVersion -ne 1 -or [long]$store.Revision -lt 0){throw "$Kind store schema is invalid."}
    $records=@($store.Records)
    $limit=if($Kind -eq 'FileWorkspaces'){[int](Get-WinCareConfig 'FileWorkspaceItemLimit')}else{[int](Get-WinCareConfig 'BrightnessScheduleLimit')}
    if($records.Count -gt $limit){throw "$Kind store exceeds its configured record limit."}
    return $store
}

function New-WinCareWorkspaceStudioStorePlan {
    param([ValidateSet('FileWorkspaces','BrightnessSchedules')][string]$Kind,[Parameter(Mandatory)][object[]]$Records,[string[]]$SourceRecords)
    $before=Get-WinCareWorkspaceStudioStore -Kind $Kind
    $after=[ordered]@{SchemaVersion=1;Revision=[long]$before.Revision+1;UpdatedAt=[datetime]::UtcNow.ToString('o');Records=@($Records)}
    $path=Get-WinCareWorkspaceStudioStorePath -Kind $Kind
    $bytes=[Text.Encoding]::UTF8.GetBytes((ConvertTo-WinCareCanonicalJson -InputObject $after -Depth 40)+"`n")
    $action=New-WinCareManagedFileWriteAction -Path $path -Content $bytes -AllowedRoots @((Get-WinCareWorkspaceStudioRoot)) -Label "Replace $Kind catalog" -SourceRecords $SourceRecords
    New-WinCarePlan -Title "Update $Kind catalog" -Description 'Replace the bounded local catalog atomically with an exact-content recovery action.' -Actions @($action) -SourceRecords $SourceRecords
}

function Get-WinCareFileWorkspace {
    [CmdletBinding()]
    param([string]$Id='')
    $records=@((Get-WinCareWorkspaceStudioStore -Kind FileWorkspaces).Records)
    if($Id){$records=@($records|Where-Object{[string]$_.Id -eq $Id})}
    return $records
}

function New-WinCareFileWorkspacePlan {
    [CmdletBinding()]
    param(
        [string]$Id='',
        [Parameter(Mandatory)][ValidateLength(1,120)][string]$Name,
        [Parameter(Mandatory)][string[]]$Path,
        [string[]]$Bookmark=@(),
        [string]$SearchPattern=''
    )
    $limit=[int](Get-WinCareConfig 'FileWorkspaceItemLimit')
    if($Path.Count -lt 1 -or $Path.Count -gt 64 -or $Bookmark.Count -gt 256){throw 'File workspace path or bookmark count is outside supported bounds.'}
    $normalized=[Collections.Generic.List[string]]::new()
    foreach($item in $Path){
        if([string]::IsNullOrWhiteSpace($item) -or $item.Length -gt 32760){throw 'File workspace contains an invalid path.'}
        $full=[IO.Path]::GetFullPath($item)
        if($full -match "`0"){throw 'File workspace path contains a null byte.'}
        $normalized.Add($full)
    }
    $bookmarks=[Collections.Generic.List[string]]::new()
    foreach($item in $Bookmark){if([string]::IsNullOrWhiteSpace($item) -or $item.Length -gt 32760){throw 'File workspace contains an invalid bookmark.'};$bookmarks.Add([IO.Path]::GetFullPath($item))}
    if($SearchPattern.Length -gt 256){throw 'File workspace search pattern exceeds 256 characters.'}
    if(-not $Id){$Id=[guid]::NewGuid().ToString('N')}
    if($Id -notmatch '^[a-zA-Z0-9._-]{8,80}$'){throw 'File workspace identifier is invalid.'}
    $store=Get-WinCareWorkspaceStudioStore -Kind FileWorkspaces
    $records=[Collections.Generic.List[object]]::new();$replaced=$false
    foreach($record in @($store.Records)){
        if([string]$record.Id -eq $Id){$records.Add([ordered]@{SchemaVersion=1;Id=$Id;Name=$Name;Paths=@($normalized|Sort-Object -Unique);Bookmarks=@($bookmarks|Sort-Object -Unique);SearchPattern=$SearchPattern;CreatedAt=[string]$record.CreatedAt;UpdatedAt=[datetime]::UtcNow.ToString('o');SourceRecords=@('SRC:f1b9beff21')});$replaced=$true}else{$records.Add($record)}
    }
    if(-not $replaced){$records.Add([ordered]@{SchemaVersion=1;Id=$Id;Name=$Name;Paths=@($normalized|Sort-Object -Unique);Bookmarks=@($bookmarks|Sort-Object -Unique);SearchPattern=$SearchPattern;CreatedAt=[datetime]::UtcNow.ToString('o');UpdatedAt=[datetime]::UtcNow.ToString('o');SourceRecords=@('SRC:f1b9beff21')})}
    if($records.Count -gt $limit){throw "File workspace catalog exceeds the configured limit of $limit."}
    New-WinCareWorkspaceStudioStorePlan -Kind FileWorkspaces -Records @($records) -SourceRecords @('SRC:f1b9beff21')
}

function Get-WinCareWorkspaceStudioProfile {
    [CmdletBinding()]
    param([string]$Id='')
    $profiles=@(
        [pscustomobject]@{Id='columns-2';Name='Two equal columns';Ratios=@(@(0,0,0.5,1),@(0.5,0,0.5,1));SourceRecords=@('SRC:f1b9beff21')},
        [pscustomobject]@{Id='columns-3';Name='Three equal columns';Ratios=@(@(0,0,0.3333,1),@(0.3333,0,0.3334,1),@(0.6667,0,0.3333,1));SourceRecords=@('SRC:f1b9beff21')},
        [pscustomobject]@{Id='main-stack';Name='Main plus vertical stack';Ratios=@(@(0,0,0.67,1),@(0.67,0,0.33,0.5),@(0.67,0.5,0.33,0.5));SourceRecords=@('SRC:f1b9beff21')},
        [pscustomobject]@{Id='docked-side';Name='Docked side utility';Ratios=@(@(0,0,0.78,1),@(0.78,0,0.22,1));SourceRecords=@('SRC:f1b9beff21')},
        [pscustomobject]@{Id='rows-2';Name='Two equal rows';Ratios=@(@(0,0,1,0.5),@(0,0.5,1,0.5));SourceRecords=@('SRC:f1b9beff21')}
    )
    if($Id){return @($profiles|Where-Object Id -eq $Id)}
    return $profiles
}

function New-WinCareWorkspaceStudioLayoutPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProfileId,[Parameter(Mandatory)][string[]]$ProcessName,[string[]]$TitlePattern=@(),[string]$Name='')
    $profile=Get-WinCareWorkspaceStudioProfile -Id $ProfileId|Select-Object -First 1
    if(-not $profile){throw 'Workspace Studio profile was not found.'}
    if($ProcessName.Count -ne @($profile.Ratios).Count){throw 'ProcessName count must match the selected profile slot count.'}
    $slots=[Collections.Generic.List[object]]::new()
    for($i=0;$i -lt $ProcessName.Count;$i++){
        if($ProcessName[$i] -notmatch '^[A-Za-z0-9._-]{1,120}$'){throw 'Workspace process name is invalid.'}
        $r=@($profile.Ratios[$i]);$title=if($i -lt $TitlePattern.Count){[string]$TitlePattern[$i]}else{''}
        $slots.Add([ordered]@{ProcessName=$ProcessName[$i];TitlePattern=$title;LeftRatio=[double]$r[0];TopRatio=[double]$r[1];WidthRatio=[double]$r[2];HeightRatio=[double]$r[3];Required=$true})
    }
    if(-not $Name){$Name=$profile.Name}
    $record=New-WinCareWorkspaceLayoutRecord -Name $Name -Description 'Workspace Studio governed layout.' -Slot @($slots)
    $record.SourceRecords=@($profile.SourceRecords)
    New-WinCareWorkspaceLayoutSavePlan -Layout $record
}

function Get-WinCareUnifiedSystemSnapshot {
    [CmdletBinding()]
    param([ValidateRange(1,250)][int]$ProcessLimit=40)
    $telemetry=try{Get-WinCareTelemetrySnapshot}catch{$null}
    $processes=if($IsWindows){@(Get-Process -ErrorAction SilentlyContinue|Sort-Object CPU -Descending|Select-Object -First $ProcessLimit|ForEach-Object{[pscustomobject]@{Id=$_.Id;Name=$_.ProcessName;CpuSeconds=[math]::Round([double]$_.CPU,2);WorkingSetBytes=[long]$_.WorkingSet64;Threads=$_.Threads.Count;StartTime=try{$_.StartTime.ToUniversalTime()}catch{$null}}})}else{@()}
    $services=if($IsWindows){@(Get-CimInstance Win32_Service -ErrorAction SilentlyContinue|Group-Object State|ForEach-Object{[pscustomobject]@{State=$_.Name;Count=$_.Count}})}else{@()}
    $connections=if($IsWindows -and (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)){@(Get-NetTCPConnection -ErrorAction SilentlyContinue|Group-Object State|ForEach-Object{[pscustomobject]@{State=$_.Name;Count=$_.Count}})}else{@()}
    [pscustomobject]@{SchemaVersion=1;CapturedAt=[datetime]::UtcNow.ToString('o');Telemetry=$telemetry;Processes=$processes;Services=$services;TcpConnections=$connections;SourceRecords=@('SRC:f1b9beff21')}
}

function New-WinCareMonitoringExportPlan {
    [CmdletBinding()]
    param([ValidateLength(3,80)][string]$Name='wincare-local')
    if($Name -notmatch '^[A-Za-z0-9._-]+$'){throw 'Monitoring export name is invalid.'}
    $root=Join-Path (Get-WinCareWorkspaceStudioRoot) 'Monitoring'
    $telegraf=@'
# WinCare generated local-only Telegraf input profile.
[agent]
  interval = "15s"
  flush_interval = "15s"
  omit_hostname = false

[[inputs.cpu]]
  percpu = true
  totalcpu = true
  collect_cpu_time = false
[[inputs.mem]]
[[inputs.disk]]
  ignore_fs = ["tmpfs", "devtmpfs"]
[[inputs.diskio]]
[[inputs.net]]
[[inputs.processes]]
[[inputs.system]]

# No output is configured deliberately. Add an authenticated destination outside WinCare.
'@
    $dashboard=[ordered]@{title='WinCare Local Operations';schemaVersion=39;version=1;refresh='15s';tags=@('wincare','windows','local');time=[ordered]@{from='now-6h';to='now'};panels=@(
        [ordered]@{id=1;title='CPU utilization';type='timeseries';targets=@([ordered]@{refId='A';measurement='cpu';field='usage_active'})},
        [ordered]@{id=2;title='Memory utilization';type='timeseries';targets=@([ordered]@{refId='A';measurement='mem';field='used_percent'})},
        [ordered]@{id=3;title='Disk utilization';type='timeseries';targets=@([ordered]@{refId='A';measurement='disk';field='used_percent'})},
        [ordered]@{id=4;title='Processes';type='stat';targets=@([ordered]@{refId='A';measurement='processes';field='total'})}
    );annotations=[ordered]@{list=@()};templating=[ordered]@{list=@()}}
    $actions=@(
        (New-WinCareManagedFileWriteAction -Path (Join-Path $root "$Name-telegraf.conf") -Content ([Text.Encoding]::UTF8.GetBytes($telegraf)) -AllowedRoots @($root) -Label 'Write local-only Telegraf input profile' -SourceRecords @('SRC:f1b9beff21')),
        (New-WinCareManagedFileWriteAction -Path (Join-Path $root "$Name-grafana.json") -Content ([Text.Encoding]::UTF8.GetBytes((ConvertTo-WinCareCanonicalJson -InputObject $dashboard -Depth 30)+"`n")) -AllowedRoots @($root) -Label 'Write Grafana dashboard template' -SourceRecords @('SRC:f1b9beff21'))
    )
    New-WinCarePlan -Title 'Export local monitoring templates' -Description 'Creates bounded local input and dashboard templates without credentials, listeners, or network destinations.' -Actions $actions -SourceRecords @('SRC:f1b9beff21')
}

function Get-WinCarePackageArchiveMetadata {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath)
    if(-not(Test-Path -LiteralPath $LiteralPath -PathType Leaf)){throw 'Package archive does not exist.'}
    $item=Get-Item -LiteralPath $LiteralPath -Force
    if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw 'Package archive reparse points are not admitted.'}
    if($item.Length -gt 20GB){throw 'Package archive exceeds the 20 GiB inspection bound.'}
    $limit=[int](Get-WinCareConfig 'PackageArchiveEntryLimit')
    $archive=[IO.Compression.ZipFile]::OpenRead($item.FullName)
    try{
        if($archive.Entries.Count -gt $limit){throw "Package archive exceeds the configured $limit entry limit."}
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$entries=[Collections.Generic.List[object]]::new();[long]$expanded=0
        foreach($entry in $archive.Entries){
            $name=([string]$entry.FullName).Replace('\','/')
            if([string]::IsNullOrWhiteSpace($name)){continue}
            if($name.Length -gt 32760 -or $name -match '[\x00-\x1f]' -or $name.StartsWith('/') -or $name -match '^[A-Za-z]:' -or @($name.Split('/')|Where-Object{$_ -eq '..'}).Count){throw "Archive contains an unsafe member path: $name"}
            if(-not $seen.Add($name)){throw "Archive contains a duplicate or case-colliding member: $name"}
            if([long]$entry.CompressedLength -gt 0 -and [long]$entry.Length -gt 1MB -and ([double]$entry.Length/[double]$entry.CompressedLength) -gt 1000){throw "Archive member has a suspicious compression ratio: $name"}
            $expanded+=[long]$entry.Length;if($expanded -gt 80GB){throw 'Package archive expanded-size bound was exceeded.'}
            $entries.Add([pscustomobject]@{Path=$name;Length=[long]$entry.Length;CompressedLength=[long]$entry.CompressedLength})
        }
        $kind=switch -Regex ($item.Extension.ToLowerInvariant()){ '\.apk'{'APK'} '\.ipa'{'IPA'} '\.(appx|msix|appxbundle|msixbundle)'{'AppX'} default{'ZIP'} }
        $manifestNames=switch($kind){'APK'{@('AndroidManifest.xml')} 'IPA'{@($entries.Path|Where-Object{$_ -match '^Payload/[^/]+\.app/Info\.plist$'}|Select-Object -First 1)} 'AppX'{@('AppxManifest.xml','AppxMetadata/AppxBundleManifest.xml')} default{@()}}
        $manifest=[ordered]@{}
        foreach($manifestName in @($manifestNames|Where-Object{$_})){
            $entry=$archive.GetEntry($manifestName);if(-not $entry -or $entry.Length -gt 2MB){continue}
            $stream=$entry.Open()
            try{$text=Read-WinCareBoundedStreamText -Stream $stream -MaximumBytes 2097152 -ExpectedBytes ([long]$entry.Length)}finally{$stream.Dispose()}
            if($text -notmatch "`0"){$manifest[$manifestName]=if($text.Length -gt 65536){$text.Substring(0,65536)}else{$text}}
        }
        [pscustomobject]@{SchemaVersion=1;Path=$item.FullName;Kind=$kind;Sha256=Get-WinCareSha256 -LiteralPath $item.FullName;ArchiveBytes=[long]$item.Length;ExpandedBytes=$expanded;EntryCount=$entries.Count;CompressionRatio=if($item.Length){[math]::Round($expanded/[double]$item.Length,2)}else{0};Manifests=$manifest;Entries=@($entries|Select-Object -First 500);EntriesTruncated=$entries.Count -gt 500;Executed=$false;Extracted=$false;SourceRecords=@('SRC:f1b9beff21')}
    }finally{$archive.Dispose()}
}

function Test-WinCareKanataConfiguration {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath)
    if(-not(Test-Path -LiteralPath $LiteralPath -PathType Leaf)){throw 'Kanata configuration does not exist.'}
    $item=Get-Item -LiteralPath $LiteralPath -Force;if($item.Length -gt 1MB){throw 'Kanata configuration exceeds 1 MiB.'}
    $text=Read-WinCareBoundedText -LiteralPath $item.FullName -MaximumBytes 1048576
    if($text -match "`0"){throw 'Kanata configuration contains a null byte.'}
    $forms=[regex]::Matches($text,'(?m)^\s*\((defcfg|defsrc|deflayer|defalias|defvar|defoverrides)\b')|ForEach-Object{$_.Groups[1].Value}
    $includes=[regex]::Matches($text,'(?m)^\s*\(include\s+"([^"]+)"')|ForEach-Object{$_.Groups[1].Value}
    $root=Split-Path -Parent $item.FullName;$unsafe=[Collections.Generic.List[string]]::new()
    foreach($include in $includes){$candidate=[IO.Path]::GetFullPath((Join-Path $root $include));if(-not $candidate.StartsWith(([IO.Path]::GetFullPath($root)+[IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)){$unsafe.Add($include)}}
    [pscustomobject]@{Path=$item.FullName;Sha256=Get-WinCareSha256 -LiteralPath $item.FullName;Forms=@($forms);HasDefcfg='defcfg' -in $forms;HasDefsrc='defsrc' -in $forms;LayerCount=@($forms|Where-Object{$_ -eq 'deflayer'}).Count;Includes=@($includes);UnsafeIncludes=@($unsafe);Admitted=($unsafe.Count -eq 0 -and 'defsrc' -in $forms -and 'deflayer' -in $forms);Executed=$false;SourceRecords=@('SRC:f1b9beff21')}
}

function Get-WinCareSyncthingState {
    [CmdletBinding()]
    param([string]$ConfigPath='')
    if(-not $ConfigPath){
        if([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)){return [pscustomobject]@{ConfigPath='';ConfigPresent=$false;Processes=@();ApiKeyRedacted=$true;Reason='LOCALAPPDATA is unavailable.';SourceRecords=@('SRC:f1b9beff21')}}
        $ConfigPath=Join-Path $env:LOCALAPPDATA 'Syncthing\config.xml'
    }
    $processes=if($IsWindows){@(Get-Process -Name syncthing,SyncTrayzor -ErrorAction SilentlyContinue|Select-Object Id,ProcessName,StartTime,Path)}else{@()}
    if(-not(Test-Path -LiteralPath $ConfigPath -PathType Leaf)){return [pscustomobject]@{ConfigPath=$ConfigPath;ConfigPresent=$false;Processes=$processes;ApiKeyRedacted=$true;SourceRecords=@('SRC:f1b9beff21')}}
    $item=Get-Item -LiteralPath $ConfigPath -Force
    if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw 'Syncthing configuration reparse points are not admitted.'}
    if($item.Length -gt 10MB){throw 'Syncthing configuration exceeds 10 MiB.'}
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=10MB
    $reader=[Xml.XmlReader]::Create($item.FullName,$settings);$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null
    try{$xml.Load($reader)}finally{$reader.Dispose()}
    $folders=@($xml.configuration.folder|ForEach-Object{[pscustomobject]@{Id=[string]$_.id;Label=[string]$_.label;Path=[string]$_.path;Type=[string]$_.type;Paused=[string]$_.paused -eq 'true'}})
    $devices=@($xml.configuration.device|ForEach-Object{[pscustomobject]@{Id=[string]$_.id;Name=[string]$_.name;Introducer=[string]$_.introducer -eq 'true'}})
    [pscustomobject]@{ConfigPath=$ConfigPath;ConfigPresent=$true;Sha256=Get-WinCareSha256 -LiteralPath $ConfigPath;Folders=$folders;Devices=$devices;Processes=$processes;GuiAddress=[string]$xml.configuration.gui.address;ApiKeyRedacted=$true;SourceRecords=@('SRC:f1b9beff21')}
}

function New-WinCareWezTermStatusBarPlan {
    [CmdletBinding()]
    param([ValidateLength(3,80)][string]$Name='wincare-status.lua')
    if($Name -notmatch '^[A-Za-z0-9._-]+\.lua$'){throw 'WezTerm module name is invalid.'}
    $root=Join-Path (Get-WinCareWorkspaceStudioRoot) 'WezTerm'
    $lua=@'
local wezterm = require 'wezterm'
local M = {}
function M.apply(config)
  wezterm.on('update-status', function(window, pane)
    local cwd = pane:get_current_working_dir()
    local cwd_text = cwd and cwd.file_path or ''
    local workspace = window:active_workspace()
    local host = wezterm.hostname()
    window:set_right_status(wezterm.format({
      { Foreground = { AnsiColor = 'Aqua' } }, { Text = ' ' .. workspace .. ' ' },
      { Foreground = { AnsiColor = 'Fuchsia' } }, { Text = host .. ' ' },
      { Foreground = { AnsiColor = 'Grey' } }, { Text = cwd_text .. ' ' },
      { Text = wezterm.strftime('%Y-%m-%d %H:%M') .. ' ' },
    }))
  end)
  return config
end
return M
'@
    $action=New-WinCareManagedFileWriteAction -Path (Join-Path $root $Name) -Content ([Text.Encoding]::UTF8.GetBytes($lua)) -AllowedRoots @($root) -Label 'Write local WezTerm status module' -SourceRecords @('SRC:f1b9beff21')
    New-WinCarePlan -Title 'Create WezTerm status module' -Description 'Creates an offline local module for workspace, host, current directory, and time status.' -Actions @($action) -SourceRecords @('SRC:f1b9beff21')
}

function Get-WinCarePlaybookGraph {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Plan)
    $nodes=[Collections.Generic.List[object]]::new();$edges=[Collections.Generic.List[object]]::new();$index=0
    foreach($action in @($Plan.Actions)){
        $nodes.Add([pscustomobject]@{Id=[string]$action.Id;Index=$index;Type=[string]$action.Type;Label=[string]$action.Label;Risk=[string]$action.Risk;Reversible=[bool]$action.Reversible;SourceRecords=@($action.SourceRecords)})
        if($index -gt 0){$edges.Add([pscustomobject]@{From=[string]$Plan.Actions[$index-1].Id;To=[string]$action.Id;Kind='next-on-success'})}
        if($action.Compensator -and $action.Compensator.Count){$edges.Add([pscustomobject]@{From=[string]$action.Id;To="compensator:$($action.Id)";Kind='rollback';Type=[string]$action.Compensator.Type})}
        $index++
    }
    [pscustomobject]@{SchemaVersion=1;PlanId=[string]$Plan.Id;Title=[string]$Plan.Title;Nodes=@($nodes);Edges=@($edges);StopOnFailure=[bool]$Plan.StopOnFailure;RollbackOnFailure=[bool]$Plan.RollbackOnFailure;SourceRecords=@('SRC:f1b9beff21')}
}

function Get-WinCareBrightnessSchedule {
    [CmdletBinding()]
    param([string]$Id='')
    $records=@((Get-WinCareWorkspaceStudioStore -Kind BrightnessSchedules).Records)
    if($Id){$records=@($records|Where-Object{[string]$_.Id -eq $Id})}
    return $records
}

function New-WinCareBrightnessSchedulePlan {
    [CmdletBinding()]
    param([string]$Id='',[Parameter(Mandatory)][ValidateLength(1,100)][string]$Name,[Parameter(Mandatory)][object[]]$Step)
    if($Step.Count -lt 1 -or $Step.Count -gt 48){throw 'Brightness schedule must contain 1..48 steps.'}
    $steps=[Collections.Generic.List[object]]::new();$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($entry in $Step){$m=ConvertTo-WinCareParameterDictionary $entry;$time=[string]$m.Time;$percent=[int]$m.Percent;if($time -notmatch '^(?:[01]\d|2[0-3]):[0-5]\d$' -or $percent -lt 0 -or $percent -gt 100 -or -not $seen.Add($time)){throw 'Brightness schedule step is invalid or duplicated.'};$steps.Add([ordered]@{Time=$time;Percent=$percent})}
    if(-not $Id){$Id=[guid]::NewGuid().ToString('N')};if($Id -notmatch '^[A-Za-z0-9._-]{8,80}$'){throw 'Brightness schedule identifier is invalid.'}
    $store=Get-WinCareWorkspaceStudioStore -Kind BrightnessSchedules;$records=[Collections.Generic.List[object]]::new();$replaced=$false
    foreach($record in @($store.Records)){if([string]$record.Id -eq $Id){$records.Add([ordered]@{SchemaVersion=1;Id=$Id;Name=$Name;Steps=@($steps|Sort-Object Time);CreatedAt=[string]$record.CreatedAt;UpdatedAt=[datetime]::UtcNow.ToString('o');SourceRecords=@('SRC:f1b9beff21')});$replaced=$true}else{$records.Add($record)}}
    if(-not $replaced){$records.Add([ordered]@{SchemaVersion=1;Id=$Id;Name=$Name;Steps=@($steps|Sort-Object Time);CreatedAt=[datetime]::UtcNow.ToString('o');UpdatedAt=[datetime]::UtcNow.ToString('o');SourceRecords=@('SRC:f1b9beff21')})}
    if($records.Count -gt [int](Get-WinCareConfig 'BrightnessScheduleLimit')){throw 'Brightness schedule catalog exceeds its configured limit.'}
    New-WinCareWorkspaceStudioStorePlan -Kind BrightnessSchedules -Records @($records) -SourceRecords @('SRC:f1b9beff21')
}

function New-WinCareBrightnessScheduleApplyPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Id,[datetime]$At=[datetime]::Now)
    $schedule=Get-WinCareBrightnessSchedule -Id $Id|Select-Object -First 1;if(-not $schedule){throw 'Brightness schedule was not found.'}
    $minute=$At.Hour*60+$At.Minute;$selected=@($schedule.Steps|Where-Object{([int]([string]$_.Time).Substring(0,2)*60+[int]([string]$_.Time).Substring(3,2)) -le $minute}|Sort-Object Time|Select-Object -Last 1)
    if(-not $selected){$selected=@($schedule.Steps|Sort-Object Time|Select-Object -Last 1)}
    $plan=New-WinCareBrightnessPlan -Percent ([int]$selected[0].Percent);$plan.Title="Apply brightness schedule: $($schedule.Name)";$plan.SourceRecords=@('SRC:f1b9beff21');foreach($action in $plan.Actions){$action.SourceRecords=@('SRC:f1b9beff21')};$plan
}

function Get-WinCareFolderAppearanceState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath)
    if(-not(Test-Path -LiteralPath $LiteralPath -PathType Container)){throw 'Folder does not exist.'}
    $folder=Get-Item -LiteralPath $LiteralPath -Force
    if($folder.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Folder appearance does not operate on reparse points.'}
    $ini=Join-Path $folder.FullName 'desktop.ini';$exists=Test-Path -LiteralPath $ini -PathType Leaf
    $payload=[ordered]@{SchemaVersion=1;Path=$folder.FullName;FolderAttributes=[int]$folder.Attributes;DesktopIniExists=$exists;DesktopIniContentBase64=if($exists){[Convert]::ToBase64String((Read-WinCareBoundedFileBytes -LiteralPath $ini -MaximumBytes 1048576))}else{''};DesktopIniAttributes=if($exists){[int](Get-Item -LiteralPath $ini -Force).Attributes}else{0}}
    $payload.Hash=Get-WinCareCanonicalObjectHash -InputObject $payload
    [pscustomobject]$payload
}

function Restore-WinCareFolderAppearanceSnapshotInternal {
    param([Parameter(Mandatory)][object]$Snapshot)
    $s=ConvertTo-WinCareParameterDictionary $Snapshot;$path=[string]$s.Path;$folder=Get-Item -LiteralPath $path -Force;$ini=Join-Path $path 'desktop.ini'
    if([bool]$s.DesktopIniExists){[IO.File]::WriteAllBytes($ini,[Convert]::FromBase64String([string]$s.DesktopIniContentBase64));[IO.File]::SetAttributes($ini,[IO.FileAttributes][int]$s.DesktopIniAttributes)}elseif(Test-Path -LiteralPath $ini -PathType Leaf){Remove-Item -LiteralPath $ini -Force}
    [IO.File]::SetAttributes($folder.FullName,[IO.FileAttributes][int]$s.FolderAttributes)
}

function Resolve-WinCareFolderIconResource {
    param([Parameter(Mandatory)][string]$IconResource)
    if($IconResource.Length -gt 32760 -or $IconResource -match "[`r`n`0]" -or $IconResource -match '^(?:\\\\|//|[A-Za-z](?!:[\\/])[A-Za-z0-9+.-]*:)'){throw 'Folder icon resource must be a local file path, not a UNC path or URI.'}
    $pathText=$IconResource.Trim();$index=$null
    if($pathText -match '^(.*),(-?\d{1,6})$'){$pathText=$Matches[1].Trim();$index=[int]$Matches[2]}
    $expanded=[Environment]::ExpandEnvironmentVariables($pathText)
    if(-not [IO.Path]::IsPathFullyQualified($expanded)){throw 'Folder icon resource must be an absolute local path.'}
    $full=Assert-WinCareSafePath -LiteralPath $expanded
    if([IO.Path]::GetExtension($full) -notin @('.ico','.dll','.exe')){throw 'Folder icon resource must be an ICO, DLL, or EXE file.'}
    $value=if($null -ne $index){"$full,$index"}else{$full}
    [pscustomobject]@{Value=$value;Path=$full;Index=$index;Sha256=Get-WinCareSha256 -LiteralPath $full}
}

function New-WinCareFolderAppearancePlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath,[Parameter(Mandatory)][string]$IconResource,[string]$LocalizedName='')
    $before=Get-WinCareFolderAppearanceState -LiteralPath $LiteralPath
    $icon=Resolve-WinCareFolderIconResource -IconResource $IconResource
    if($LocalizedName.Length -gt 260 -or $LocalizedName -match "[`r`n`0]"){throw 'Localized folder name is invalid.'}
    $lines=@('[.ShellClassInfo]',"IconResource=$($icon.Value)",'ConfirmFileOp=0');if($LocalizedName){$lines+="LocalizedResourceName=$LocalizedName"};$content=[Text.Encoding]::Unicode.GetBytes(($lines -join "`r`n")+"`r`n")
    $afterPayload=[ordered]@{SchemaVersion=1;Path=[string]$before.Path;FolderAttributes=([int]$before.FolderAttributes -bor [int][IO.FileAttributes]::ReadOnly -bor [int][IO.FileAttributes]::System);DesktopIniExists=$true;DesktopIniContentBase64=[Convert]::ToBase64String($content);DesktopIniAttributes=([int][IO.FileAttributes]::Hidden -bor [int][IO.FileAttributes]::System -bor [int][IO.FileAttributes]::Archive)}
    $expectedAfterHash=Get-WinCareCanonicalObjectHash -InputObject $afterPayload
    $action=New-WinCareAction -Type SetFolderAppearance -Label "Set folder appearance: $($before.Path)" -Risk Moderate -Parameters @{Path=[string]$before.Path;IconResource=[string]$icon.Value;IconResourceSha256=[string]$icon.Sha256;LocalizedName=$LocalizedName;Before=$before;ExpectedBeforeHash=[string]$before.Hash;ExpectedAfterHash=$expectedAfterHash} -Reversible $true -Compensator @{Type='RestoreFolderAppearance';Parameters=@{Snapshot=$before;ExpectedCurrentHash=$expectedAfterHash}} -Tags @('Folder','Explorer','Appearance') -SourceRecords @('SRC:f1b9beff21') -RecoveryDescription 'Restore the exact prior desktop.ini bytes and folder/file attributes.'
    New-WinCarePlan -Title 'Set folder appearance' -Description 'Writes desktop.ini without installing a shell extension and preserves an exact recovery snapshot.' -Actions @($action) -SourceRecords @('SRC:f1b9beff21')
}

function Invoke-WinCareSetFolderAppearanceAction {
    param([object]$Action)
    $p=$Action.Parameters
    try{$icon=Resolve-WinCareFolderIconResource -IconResource ([string]$p.IconResource)}catch{return New-WinCareResult -Success $false -Status Blocked -Code 'FolderIconResourceDenied' -Message $_.Exception.Message -ExitCode 5}
    if([string]$icon.Sha256 -ne [string]$p.IconResourceSha256){return New-WinCareResult -Success $false -Status Blocked -Code 'FolderIconResourceChanged' -Message 'Folder icon resource changed after preview.' -ExitCode 74}
    $current=Get-WinCareFolderAppearanceState -LiteralPath ([string]$p.Path)
    if([string]$current.Hash -ne [string]$p.ExpectedBeforeHash){return New-WinCareResult -Success $false -Status Blocked -Code 'FolderAppearanceChanged' -Message 'Folder appearance changed after preview.' -ExitCode 74}
    $path=[string]$p.Path;$ini=Join-Path $path 'desktop.ini';$lines=@('[.ShellClassInfo]',"IconResource=$([string]$p.IconResource)",'ConfirmFileOp=0');if([string]$p.LocalizedName){$lines+="LocalizedResourceName=$([string]$p.LocalizedName)"};$bytes=[Text.Encoding]::Unicode.GetBytes(($lines -join "`r`n")+"`r`n")
    try{
        [IO.File]::WriteAllBytes($ini,$bytes);[IO.File]::SetAttributes($ini,[IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System -bor [IO.FileAttributes]::Archive)
        $folder=Get-Item -LiteralPath $path -Force;[IO.File]::SetAttributes($folder.FullName,$folder.Attributes -bor [IO.FileAttributes]::ReadOnly -bor [IO.FileAttributes]::System)
        $after=Get-WinCareFolderAppearanceState -LiteralPath $path
        if([string]$after.Hash -ne [string]$p.ExpectedAfterHash){throw 'Folder appearance post-state verification failed.'}
        New-WinCareResult -Success $true -Code 'FolderAppearanceSet' -Message 'Folder appearance metadata was written and verified.' -Data @{Before=$current;After=$after}
    }catch{try{Restore-WinCareFolderAppearanceSnapshotInternal -Snapshot $p.Before}catch{};New-WinCareResult -Success $false -Status Failed -Code 'FolderAppearanceFailed' -Message $_.Exception.Message -ExitCode 1}
}

function Invoke-WinCareRestoreFolderAppearanceAction {
    param([object]$Action)
    $s=$Action.Parameters.Snapshot;$current=Get-WinCareFolderAppearanceState -LiteralPath ([string]$s.Path);$expected=[string]$Action.Parameters.ExpectedCurrentHash
    if([string]$current.Hash -ne $expected){return New-WinCareResult -Success $false -Status Blocked -Code 'FolderAppearanceRecoveryConflict' -Message 'Folder appearance changed after the original operation.' -ExitCode 74}
    try{Restore-WinCareFolderAppearanceSnapshotInternal -Snapshot $s;$after=Get-WinCareFolderAppearanceState -LiteralPath ([string]$s.Path);New-WinCareResult -Success ([string]$after.Hash -eq [string]$s.Hash) -Code 'FolderAppearanceRestored' -Message 'Folder appearance snapshot was restored.' -Data $after}catch{New-WinCareResult -Success $false -Code 'FolderAppearanceRestoreFailed' -Message $_.Exception.Message -ExitCode 1}
}

function Get-WinCareAdbCommandShape {
    param([Parameter(Mandatory)][string[]]$Arguments)
    if($Arguments.Count -lt 1){throw 'ADB arguments are missing.'}
    $verbIndex=0;$serial=''
    if($Arguments[0] -eq '-s'){
        if($Arguments.Count -lt 3 -or [string]$Arguments[1] -notmatch '^[A-Za-z0-9._:-]{1,128}$'){throw 'ADB serial selector is invalid.'}
        $serial=[string]$Arguments[1];$verbIndex=2
    }
    $tail=if(($verbIndex+1) -lt $Arguments.Count){@($Arguments[($verbIndex+1)..($Arguments.Count-1)])}else{@()}
    [pscustomobject]@{Serial=$serial;VerbIndex=$verbIndex;Verb=[string]$Arguments[$verbIndex];Tail=$tail}
}

function Test-WinCareAdbRemotePath {
    param([Parameter(Mandatory)][string]$Path)
    if($Path.Length -gt 4096 -or $Path -notmatch '^/[A-Za-z0-9._/@+,:=-]*(?:/[A-Za-z0-9._@+,:=-]+)*/?$'){throw 'ADB remote path is outside the safe absolute-path grammar.'}
    return $true
}

function Test-WinCareVerifiedPeerToolArguments {
    param([Parameter(Mandatory)][string]$ToolId,[Parameter(Mandatory)][string[]]$Arguments,[bool]$Mutation)
    if($Arguments.Count -gt 64 -or @($Arguments|Where-Object{$_ -isnot [string] -or ([string]$_).Length -gt 32760 -or [string]$_ -match "`0"}).Count){throw 'Verified peer-tool arguments are invalid.'}
    switch($ToolId){
        'ViVeTool'{
            if($Arguments.Count -ne 2 -or $Arguments[0] -notin @('/enable','/disable') -or $Arguments[1] -notin @('/id:52580392,50902630','/id:52580392,50902630,59765208')){throw 'ViVeTool arguments are outside the exact Xbox Full Screen Experience allowlist.'}
            if(-not $Mutation){throw 'ViVeTool action must be marked as a mutation.'}
        }
        'Adb'{
            $shape=Get-WinCareAdbCommandShape -Arguments $Arguments;$verb=$shape.Verb;$tail=@($shape.Tail)
            if($verb -notin @('devices','version','get-state','get-serialno','shell','push','install','uninstall')){throw 'ADB command is outside the WinCare allowlist.'}
            switch($verb){
                'devices'{if($shape.Serial -or $tail.Count -gt 1 -or ($tail.Count -eq 1 -and $tail[0] -ne '-l')){throw 'ADB devices accepts only the optional -l flag and no serial selector.'}}
                'version'{if($shape.Serial -or $tail.Count){throw 'ADB version accepts no additional arguments.'}}
                'get-state'{if($tail.Count){throw 'ADB get-state accepts no additional arguments.'}}
                'get-serialno'{if($tail.Count){throw 'ADB get-serialno accepts no additional arguments.'}}
                'shell'{
                    if($tail.Count -lt 1){throw 'ADB shell requires an allowlisted read-only command.'}
                    $command=[string]$tail[0];$rest=@($tail|Select-Object -Skip 1)
                    switch($command){
                        'pwd'{if($rest.Count){throw 'ADB shell pwd accepts no arguments.'}}
                        'df'{if($rest.Count -gt 1 -or ($rest.Count -eq 1 -and $rest[0] -ne '-h')){throw 'ADB shell df accepts only -h.'}}
                        'getprop'{if($rest.Count -gt 1 -or ($rest.Count -eq 1 -and [string]$rest[0] -notmatch '^[A-Za-z0-9._-]{1,128}$')){throw 'ADB shell getprop property is invalid.'}}
                        'ls'{if($rest.Count -lt 1 -or $rest.Count -gt 2){throw 'ADB shell ls requires one safe path and an optional -l/-a/-la flag.'};$paths=@($rest|Where-Object{$_ -notin @('-l','-a','-la')});if($paths.Count -ne 1){throw 'ADB shell ls path/flag combination is invalid.'};$null=Test-WinCareAdbRemotePath -Path ([string]$paths[0])}
                        'stat'{if($rest.Count -ne 1){throw 'ADB shell stat requires exactly one safe path.'};$null=Test-WinCareAdbRemotePath -Path ([string]$rest[0])}
                        default{throw 'ADB shell command is outside the read-only allowlist.'}
                    }
                }
                'push'{
                    if($tail.Count -ne 2){throw 'ADB push requires one local source and one remote destination.'}
                    $transferRoot=Join-Path (Get-WinCareWorkspaceStudioRoot) 'AdbTransfers';$null=New-Item -ItemType Directory -Path $transferRoot -Force
                    $null=Assert-WinCareSafePath -LiteralPath ([string]$tail[0]) -AllowedRoots @($transferRoot)
                    $null=Test-WinCareAdbRemotePath -Path ([string]$tail[1])
                }
                'install'{
                    if($tail.Count -ne 1 -or [IO.Path]::GetExtension([string]$tail[0]) -ne '.apk'){throw 'ADB install requires exactly one APK path.'}
                    $transferRoot=Join-Path (Get-WinCareWorkspaceStudioRoot) 'AdbTransfers';$null=New-Item -ItemType Directory -Path $transferRoot -Force
                    $null=Assert-WinCareSafePath -LiteralPath ([string]$tail[0]) -AllowedRoots @($transferRoot)
                }
                'uninstall'{if($tail.Count -ne 1 -or [string]$tail[0] -notmatch '^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+$'){throw 'ADB uninstall package identifier is invalid.'}}
            }
            $readOnly=$verb -in @('devices','version','get-state','get-serialno','shell')
            if($Mutation -eq $readOnly){throw $(if($readOnly){'Read-only ADB command was incorrectly marked as a mutation.'}else{'Mutating ADB command must be marked as a mutation.'})}
        }
        default{throw 'Unknown verified peer tool identifier.'}
    }
    return $true
}

function New-WinCareVerifiedPeerToolPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('ViVeTool','Adb')][string]$ToolId,[Parameter(Mandatory)][string]$FilePath,[Parameter(Mandatory)][string]$Sha256,[Parameter(Mandatory)][string[]]$Arguments,[switch]$Mutation,[int[]]$SuccessExitCodes=@(0),[ValidateRange(1,3600)][int]$TimeoutSeconds=120)
    if($Sha256 -notmatch '^[a-fA-F0-9]{64}$'){throw 'Verified peer tool SHA-256 is invalid.'}
    $verifiedPath=Assert-WinCareSafePath -LiteralPath $FilePath
    if((Get-WinCareSha256 -LiteralPath $verifiedPath) -ne $Sha256.ToLowerInvariant()){throw 'Verified peer tool hash does not match the supplied SHA-256.'}
    $null=Test-WinCareVerifiedPeerToolArguments -ToolId $ToolId -Arguments $Arguments -Mutation ([bool]$Mutation)
    $parameters=@{ToolId=$ToolId;FilePath=$verifiedPath;Sha256=$Sha256.ToLowerInvariant();Arguments=@($Arguments);Mutation=[bool]$Mutation;SuccessExitCodes=@($SuccessExitCodes)}
    if($ToolId -eq 'Adb'){
        $shape=Get-WinCareAdbCommandShape -Arguments $Arguments
        if($shape.Verb -in @('push','install')){$parameters.ExpectedInputSha256=Get-WinCareSha256 -LiteralPath ([string]$shape.Tail[0])}
    }
    $risk=if($ToolId -eq 'ViVeTool'){'Critical'}elseif($Mutation){'High'}else{'Moderate'}
    $record=if($ToolId -eq 'ViVeTool'){'SRC:f1b9beff21'}else{'SRC:f1b9beff21'}
    $action=New-WinCareAction -Type RunVerifiedPeerTool -Label "Run verified $ToolId command" -Risk $risk -Parameters $parameters -RequiresAdmin ($ToolId -eq 'ViVeTool') -Reversible $false -TimeoutSeconds $TimeoutSeconds -Tags @('VerifiedTool',$ToolId) -SourceRecords @($record) -RecoveryDescription 'External tool behavior is bounded by exact hash and argument admission; recovery depends on the specific command.'
    New-WinCarePlan -Title "Run verified $ToolId command" -Actions @($action) -RollbackOnFailure $false -SourceRecords @($record)
}

function Invoke-WinCareRunVerifiedPeerToolAction {
    param([object]$Action)
    $p=$Action.Parameters;$path=[string]$p.FilePath
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){return New-WinCareResult -Success $false -Code 'VerifiedToolMissing' -Message 'Verified peer tool is missing.' -ExitCode 2}
    $toolItem=Get-Item -LiteralPath $path -Force;if(($toolItem.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){return New-WinCareResult -Success $false -Status Blocked -Code 'VerifiedToolReparseDenied' -Message 'Verified peer tool reparse points are not admitted.' -ExitCode 74}
    $actual=Get-WinCareSha256 -LiteralPath $path;if($actual -ne [string]$p.Sha256){return New-WinCareResult -Success $false -Status Blocked -Code 'VerifiedToolHashMismatch' -Message 'Verified peer tool hash does not match the approved value.' -ExitCode 74}
    try{$null=Test-WinCareVerifiedPeerToolArguments -ToolId ([string]$p.ToolId) -Arguments @($p.Arguments) -Mutation ([bool]$p.Mutation)}catch{return New-WinCareResult -Success $false -Status Blocked -Code 'VerifiedToolArgumentsDenied' -Message $_.Exception.Message -ExitCode 5}
    if([string]$p.ToolId -eq 'Adb' -and [string]$p.ExpectedInputSha256){
        $shape=Get-WinCareAdbCommandShape -Arguments @($p.Arguments);$input=[string]$shape.Tail[0]
        if((Get-WinCareSha256 -LiteralPath $input) -ne [string]$p.ExpectedInputSha256){return New-WinCareResult -Success $false -Status Blocked -Code 'VerifiedToolInputChanged' -Message 'ADB input changed after preview.' -ExitCode 74}
    }
    Invoke-WinCareProcess -FilePath $path -ArgumentList @($p.Arguments) -SuccessExitCodes @($p.SuccessExitCodes) -RequireAdmin ([bool]$Action.RequiresAdmin) -TimeoutSeconds ([int]$Action.TimeoutSeconds)
}

function New-WinCareAdbInventoryPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$AdbPath,[Parameter(Mandatory)][string]$Sha256)
    New-WinCareVerifiedPeerToolPlan -ToolId Adb -FilePath $AdbPath -Sha256 $Sha256 -Arguments @('devices','-l') -TimeoutSeconds 30
}

function New-WinCareXboxFullscreenExperiencePlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ViVeToolPath,[Parameter(Mandatory)][string]$Sha256,[bool]$Enabled=$true,[switch]$BasicFeatureSet)
    if($Sha256 -notmatch '^[a-fA-F0-9]{64}$'){throw 'ViVeTool SHA-256 is invalid.'}
    $verifiedPath=Assert-WinCareSafePath -LiteralPath $ViVeToolPath
    if((Get-WinCareSha256 -LiteralPath $verifiedPath) -ne $Sha256.ToLowerInvariant()){throw 'ViVeTool hash does not match the supplied SHA-256.'}
    $ids=if($BasicFeatureSet){'/id:52580392,50902630'}else{'/id:52580392,50902630,59765208'}
    $verb=if($Enabled){'/enable'}else{'/disable'};$opposite=if($Enabled){'/disable'}else{'/enable'}
    $path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\OEM';$name='DeviceForm';$before=Get-WinCareRegistryValueState -Path $path -Name $name
    $registryComp=if($before.Exists){@{Type='SetRegistryValue';RequiresAdmin=$true;Parameters=@{Path=$path;Name=$name;Value=$before.Value;ValueType=$before.ValueType}}}else{@{Type='RemoveRegistryValue';RequiresAdmin=$true;Parameters=@{Path=$path;Name=$name}}}
    $registry=New-WinCareAction -Type SetRegistryValue -Label $(if($Enabled){'Set Xbox full-screen device form'}else{'Clear Xbox full-screen device form marker'}) -Risk Critical -Parameters @{Path=$path;Name=$name;Value=if($Enabled){46}else{0};ValueType='DWord';Before=$before} -RequiresAdmin $true -Reversible $true -Compensator $registryComp -Tags @('Xbox','LegacyUnsafe','DeviceForm') -SourceRecords @('SRC:f1b9beff21') -RecoveryDescription 'Restore the exact prior DeviceForm registry value.' -RestartPossible $true
    $tool=New-WinCareAction -Type RunVerifiedPeerTool -Label "$(if($Enabled){'Enable'}else{'Disable'}) Xbox Full Screen Experience feature IDs" -Risk Critical -Parameters @{ToolId='ViVeTool';FilePath=$verifiedPath;Sha256=$Sha256.ToLowerInvariant();Arguments=@($verb,$ids);Mutation=$true;SuccessExitCodes=@(0)} -RequiresAdmin $true -Reversible $true -Compensator @{Type='RunVerifiedPeerTool';RequiresAdmin=$true;Parameters=@{ToolId='ViVeTool';FilePath=$verifiedPath;Sha256=$Sha256.ToLowerInvariant();Arguments=@($opposite,$ids);Mutation=$true;SuccessExitCodes=@(0)}} -TimeoutSeconds 120 -Tags @('Xbox','LegacyUnsafe','ViVeTool') -SourceRecords @('SRC:f1b9beff21') -RecoveryDescription 'Run the exact opposite feature toggle with the same verified ViVeTool bytes.' -RestartPossible $true
    New-WinCarePlan -Title "$(if($Enabled){'Enable'}else{'Disable'}) Xbox Full Screen Experience" -Description 'Applies explicitly configured hidden Windows feature IDs and DeviceForm mutation. This can destabilize the shell and requires a reboot.' -Actions @($registry,$tool) -RollbackOnFailure $true -Metadata @{LegacyUnsafeProfile='xbox-fullscreen-experience';Acknowledgement='I ACCEPT LEGACY UNSAFE MUTATIONS';XboxFeatureIds=$ids;RebootRequired=$true} -SourceRecords @('SRC:f1b9beff21')
}


