function Get-WinCareCleanerPreviewRoot {
    [CmdletBinding()]param()
    Ensure-WinCareState
    $root=Join-Path $script:WinCareState.Root 'CleanerPreviewStudio'
    if(-not(Test-Path -LiteralPath $root)){$null=New-Item -ItemType Directory -Path $root -Force}
    $root
}

function Get-WinCareDiskPressureAssessment {
    [CmdletBinding()]param(
        [string]$Drive=$env:SystemDrive,
        [ValidateRange(1,50)][int]$MinimumFreePercent=15,
        [ValidateRange(1073741824,1099511627776)][long]$MinimumFreeBytes=21474836480
    )
    if(-not $IsWindows){return [pscustomobject]@{Available=$false;Reason='WindowsRequired';SourceRecords=@('D164-WINDOWS-CLEANER-DISK-PRESSURE')}}
    if([string]::IsNullOrWhiteSpace($Drive)){$Drive=$env:SystemDrive}
    $root=if($Drive -match '^[A-Za-z]:$'){$Drive+'\'}else{$Drive}
    $info=[IO.DriveInfo]::new($root)
    if(-not $info.IsReady){throw "Drive is not ready: $root"}
    $free=[long]$info.AvailableFreeSpace;$total=[long]$info.TotalSize
    $percent=if($total -gt 0){[math]::Round(($free*100.0)/$total,2)}else{0}
    $profile=if($percent -lt 5 -or $free -lt 5GB){'Emergency'}elseif($percent -lt $MinimumFreePercent -or $free -lt $MinimumFreeBytes){'Balanced'}else{'Conservative'}
    $targetIds=switch($profile){
        'Conservative'{@('user-temp','thumbnails','d3d-cache','windows-caches','inetcache-logs','locallow-temp')}
        'Balanced'{@('user-temp','windows-temp','thumbnails','d3d-cache','wer-archive','wer-queue','delivery-optimization','windows-caches','inetcache-logs','locallow-temp','icon-cache')}
        default{@('user-temp','windows-temp','thumbnails','d3d-cache','wer-archive','wer-queue','delivery-optimization','windows-caches','inetcache-logs','locallow-temp','icon-cache','crash-dumps','windows-minidumps','memory-dump','wer-temp','cbs-persist-logs','mosetup-logs','panther-logs')}
    }
    $targets=@(Get-WinCareCleanupTarget -Refresh|Where-Object{$_.Id -in $targetIds -and $_.Exists -and $_.FileCount -gt 0})
    [pscustomobject]@{
        SchemaVersion=1;CapturedAt=[datetime]::UtcNow.ToString('o');Available=$true;Drive=$info.Name;DriveFormat=$info.DriveFormat
        TotalBytes=$total;FreeBytes=$free;FreePercent=$percent;MinimumFreePercent=$MinimumFreePercent;MinimumFreeBytes=$MinimumFreeBytes
        RecommendedProfile=$profile;Pressure=($profile -ne 'Conservative');EstimatedReclaimBytes=[long](($targets|Measure-Object EstimatedBytes -Sum).Sum)
        TargetCount=$targets.Count;Targets=$targets;ExcludedBehaviors=@('Prefetch purge','Drive-root wildcard scan','Defender history deletion','Restore-point deletion','Unbounded Installer MSP deletion')
        SourceRecords=@('D164-WINDOWS-CLEANER-DISK-PRESSURE','D164-WINDOWS-CLEANER-GUARDRAILS')
    }
}

function New-WinCareDiskPressureCleanupPlan {
    [CmdletBinding()]param(
        [ValidateSet('Auto','Conservative','Balanced','Emergency')][string]$Profile='Auto',
        [ValidateRange(1,50)][int]$MinimumFreePercent=15,
        [ValidateRange(1073741824,1099511627776)][long]$MinimumFreeBytes=21474836480
    )
    $assessment=Get-WinCareDiskPressureAssessment -MinimumFreePercent $MinimumFreePercent -MinimumFreeBytes $MinimumFreeBytes
    $selected=if($Profile -eq 'Auto'){$assessment.RecommendedProfile}else{$Profile}
    $ids=switch($selected){
        'Conservative'{@('user-temp','thumbnails','d3d-cache','windows-caches','inetcache-logs','locallow-temp')}
        'Balanced'{@('user-temp','windows-temp','thumbnails','d3d-cache','wer-archive','wer-queue','delivery-optimization','windows-caches','inetcache-logs','locallow-temp','icon-cache')}
        default{@('user-temp','windows-temp','thumbnails','d3d-cache','wer-archive','wer-queue','delivery-optimization','windows-caches','inetcache-logs','locallow-temp','icon-cache','crash-dumps','windows-minidumps','memory-dump','wer-temp','cbs-persist-logs','mosetup-logs','panther-logs')}
    }
    $targets=@(Get-WinCareCleanupTarget -Refresh|Where-Object{$_.Id -in $ids -and $_.Exists -and $_.FileCount -gt 0})
    if($targets.Count -eq 0){throw 'No eligible files are currently present for the selected disk-pressure profile.'}
    $plan=New-WinCareCleanupPlan -Targets $targets
    $plan.Title="Disk-pressure cleanup: $selected"
    $plan.Description='Deletes only canonical WinCare cleanup targets selected by a bounded disk-pressure profile. It never scans a drive root or deletes restore points, Defender evidence, Prefetch, or Installer patches.'
    $plan.SourceRecords=@('D164-WINDOWS-CLEANER-DISK-PRESSURE','D164-WINDOWS-CLEANER-GUARDRAILS')
    $plan.Metadata=[ordered]@{Profile=$selected;FreeBytesBefore=$assessment.FreeBytes;FreePercentBefore=$assessment.FreePercent;MinimumFreeBytes=$MinimumFreeBytes;MinimumFreePercent=$MinimumFreePercent}
    $plan
}

function New-WinCareDiskPressureAutomationPlan {
    [CmdletBinding()]param(
        [ValidatePattern('^[A-Za-z0-9._-]{3,80}$')][string]$Id='disk-pressure-cleanup',
        [ValidateSet('Conservative','Balanced')][string]$Profile='Conservative',
        [ValidatePattern('^(daily@(?:[01]\d|2[0-3]):[0-5]\d|weekly:(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)@(?:[01]\d|2[0-3]):[0-5]\d)$')][string]$Schedule='weekly:Sunday@03:00',
        [ValidateRange(1,50)][int]$MinimumFreePercent=15,
        [ValidateRange(1073741824,1099511627776)][long]$MinimumFreeBytes=21474836480
    )
    $profileObject=[ordered]@{schemaVersion=1;id=$Id;title='WinCare disk-pressure cleanup';command='cleaner-disk-pressure';arguments=[ordered]@{Profile=$Profile;MinimumFreePercent=$MinimumFreePercent;MinimumFreeBytes=$MinimumFreeBytes};apply=$true;schedule=$Schedule;maximumRuntimeMinutes=120;batteryAllowed=$false;networkRequired=$false;notification='EventLog';sourceRecords=@('D164-WINDOWS-CLEANER-AUTO-CLEAN')}
    New-WinCareAutomationTaskPlan -Profile $profileObject
}

function Get-WinCareCleanupAllowedRoot {
    [CmdletBinding()]param()
    $items=[Collections.Generic.List[string]]::new()
    foreach($candidate in @($env:TEMP,$env:LOCALAPPDATA,$env:APPDATA,(Join-Path $env:USERPROFILE 'AppData'),(Join-Path $env:WINDIR 'Temp'),(Join-Path $env:ProgramData 'Microsoft\Windows\WER'))){
        if([string]::IsNullOrWhiteSpace([string]$candidate)){continue}
        try{$resolved=Resolve-WinCareCanonicalPath -LiteralPath $candidate -AllowMissing;if($resolved -notin $items){$items.Add($resolved)}}catch{Write-Verbose 'A candidate cleanup root was unavailable.'}
    }
    @($items)
}

function Import-WinCareWinapp2Catalog {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath,[string[]]$AllowedRoots=@())
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $item=Get-Item -LiteralPath $path -Force
    if($item.PSIsContainer -or $item.Length -gt 2097152){throw 'Winapp2 input must be a regular file no larger than 2 MiB.'}
    if([IO.Path]::GetExtension($path) -notin @('.ini','.txt')){throw 'Winapp2 input must use .ini or .txt.'}
    if($AllowedRoots.Count -eq 0){$AllowedRoots=@(Get-WinCareCleanupAllowedRoot)}
    $accepted=[Collections.Generic.List[object]]::new();$rejected=[Collections.Generic.List[object]]::new();$section='';$lineNumber=0
    foreach($raw in Get-Content -LiteralPath $path -Encoding utf8){
        $lineNumber++;if($lineNumber -gt 50000){throw 'Winapp2 input exceeds 50,000 lines.'}
        if($raw.Length -gt 4096){$rejected.Add([pscustomobject]@{Line=$lineNumber;Reason='LineTooLong'});continue}
        $line=$raw.Trim();if(-not $line -or $line.StartsWith(';') -or $line.StartsWith('#')){continue}
        if($line -match '^\[(?<name>[^\]]{1,160})\]$'){$section=$Matches.name.Trim();continue}
        if(-not $section){$rejected.Add([pscustomobject]@{Line=$lineNumber;Reason='MissingSection'});continue}
        $separator=$line.IndexOf('=');if($separator -lt 1){continue}
        $key=$line.Substring(0,$separator).Trim();$value=$line.Substring($separator+1).Trim()
        if($key -notmatch '^FileKey\d{1,5}$'){
            if($key -match '^(RegKey|ExcludeKey|Warning|Detect|DetectFile)'){$rejected.Add([pscustomobject]@{Section=$section;Line=$lineNumber;Key=$key;Reason='UnsupportedDirective'})}
            continue
        }
        $parts=@($value -split '\|');if($parts.Count -ne 2){$rejected.Add([pscustomobject]@{Section=$section;Line=$lineNumber;Key=$key;Reason='ExpectedPathAndPattern'});continue}
        $rawPath=[Environment]::ExpandEnvironmentVariables($parts[0].Trim());$pattern=$parts[1].Trim()
        if($pattern -notmatch '^[A-Za-z0-9*?._ ()\[\]-]{1,128}$' -or $pattern.IndexOfAny([char[]]'\/:') -ge 0){$rejected.Add([pscustomobject]@{Section=$section;Line=$lineNumber;Key=$key;Reason='UnsafePattern'});continue}
        try{$expanded=Resolve-WinCareCanonicalPath -LiteralPath $rawPath -AllowMissing}catch{$rejected.Add([pscustomobject]@{Section=$section;Line=$lineNumber;Key=$key;Reason='InvalidPath'});continue}
        $inside=$false;foreach($root in $AllowedRoots){if(Test-WinCarePathWithin -Child $expanded -Parent $root -AllowEqual){$inside=$true;break}}
        $driveRoot=[IO.Path]::GetPathRoot($expanded).TrimEnd('\')
        if(-not $inside -or $expanded.TrimEnd('\') -eq $driveRoot){$rejected.Add([pscustomobject]@{Section=$section;Line=$lineNumber;Key=$key;Reason='OutsideApprovedRoots'});continue}
        $accepted.Add([pscustomobject]@{Section=$section;Key=$key;Line=$lineNumber;Path=$expanded;Pattern=$pattern;Recursive=$false})
        if($accepted.Count -gt 5000){throw 'Winapp2 input exceeds 5,000 admitted file rules.'}
    }
    [pscustomobject]@{SchemaVersion=1;Path=$path;Sha256=Get-WinCareSha256 $path;Accepted=@($accepted);Rejected=@($rejected);AllowedRoots=@($AllowedRoots);Execution='None';SourceRecords=@('D164-WINDOWS-CLEANER-WINAPP2','D164-WINDOWS-CLEANER-GUARDRAILS')}
}

function Get-WinCareWinapp2Assessment {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath,[string[]]$Section=@(),[ValidateRange(0,8760)][int]$MinimumAgeHours=24,[ValidateRange(1,1000)][int]$MaximumFiles=500,[ValidateRange(1048576,2147483648)][long]$MaximumBytes=1073741824)
    $catalog=Import-WinCareWinapp2Catalog -LiteralPath $LiteralPath
    $rules=@($catalog.Accepted|Where-Object{$Section.Count -eq 0 -or $_.Section -in $Section})
    $entries=[Collections.Generic.List[object]]::new();$total=0L;$truncated=$false
    foreach($rule in $rules){
        if(-not(Test-Path -LiteralPath $rule.Path -PathType Container)){continue}
        $cutoff=(Get-Date).AddHours(-$MinimumAgeHours)
        foreach($file in Get-ChildItem -LiteralPath $rule.Path -Filter $rule.Pattern -File -Force -ErrorAction SilentlyContinue){
            if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0 -or $file.LastWriteTime -ge $cutoff){continue}
            if($entries.Count -ge $MaximumFiles -or ($total+[long]$file.Length) -gt $MaximumBytes){$truncated=$true;break}
            $entries.Add([pscustomobject]@{Section=$rule.Section;Path=$file.FullName;Length=[long]$file.Length;LastWriteUtcTicks=[long]$file.LastWriteTimeUtc.Ticks;Sha256=Get-WinCareSha256 $file.FullName})
            $total+=[long]$file.Length
        }
        if($truncated){break}
    }
    [pscustomobject]@{SchemaVersion=1;CatalogPath=$catalog.Path;CatalogSha256=$catalog.Sha256;SelectedSections=@($Section);Rules=$rules.Count;EligibleFiles=$entries.Count;EligibleBytes=$total;Truncated=$truncated;Entries=@($entries);RejectedRules=$catalog.Rejected;Bounds=[pscustomobject]@{MinimumAgeHours=$MinimumAgeHours;MaximumFiles=$MaximumFiles;MaximumBytes=$MaximumBytes};SourceRecords=@('D164-WINDOWS-CLEANER-WINAPP2')}
}

function New-WinCareWinapp2CleanupPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath,[string[]]$Section=@(),[ValidateRange(0,8760)][int]$MinimumAgeHours=24,[ValidateRange(1,1000)][int]$MaximumFiles=500,[ValidateRange(1048576,2147483648)][long]$MaximumBytes=1073741824)
    $assessment=Get-WinCareWinapp2Assessment -LiteralPath $LiteralPath -Section $Section -MinimumAgeHours $MinimumAgeHours -MaximumFiles $MaximumFiles -MaximumBytes $MaximumBytes
    if($assessment.EligibleFiles -eq 0){throw 'No admitted Winapp2 files currently satisfy the selected bounds.'}
    $allowed=@(Get-WinCareCleanupAllowedRoot)
    $admin=@($assessment.Entries|Where-Object{(Test-WinCarePathWithin -Child $_.Path -Parent $env:WINDIR -AllowEqual) -or (Test-WinCarePathWithin -Child $_.Path -Parent $env:ProgramData -AllowEqual)}).Count -gt 0
    $action=New-WinCareAction -Type DeleteAdmittedCleanupFiles -Label "Delete $($assessment.EligibleFiles) admitted Winapp2 files" -Risk High -Parameters @{CatalogPath=$assessment.CatalogPath;CatalogSha256=$assessment.CatalogSha256;Entries=@($assessment.Entries);AllowedRoots=$allowed;MaximumBytes=[long]$MaximumBytes} -RequiresAdmin:$admin -EstimatedBytes ([long]$assessment.EligibleBytes) -Verification 'Every file is revalidated by canonical path, length, timestamp, and SHA-256 before any deletion begins.' -Tags @('Cleanup','Winapp2','NonReversible') -SourceRecords @('D164-WINDOWS-CLEANER-WINAPP2','D164-WINDOWS-CLEANER-GUARDRAILS')
    New-WinCarePlan -Title 'Admitted Winapp2 cleanup' -Description 'Deletes only the exact bounded files admitted from FileKey rules under approved cache roots. Registry directives, exclusions, drive-root scans, recursion, and scripts are not supported.' -Actions @($action) -SourceRecords @($action.SourceRecords)
}

function Invoke-WinCareDeleteAdmittedCleanupFilesAction {
    param([Parameter(Mandatory)][object]$Action)
    $p=$Action.Parameters;$catalog=Assert-WinCareSafePath -LiteralPath ([string]$p.CatalogPath)
    if((Get-WinCareSha256 $catalog) -ne [string]$p.CatalogSha256){return New-WinCareResult -Success $false -Status Blocked -Code 'CleanupCatalogChanged' -Message 'The admitted cleanup catalog changed after preview.' -ExitCode 74}
    $entries=@($p.Entries);if($entries.Count -lt 1 -or $entries.Count -gt 1000){return New-WinCareResult -Success $false -Status Blocked -Code 'CleanupManifestInvalid' -Message 'Cleanup manifest count is outside 1..1000.' -ExitCode 22}
    $allowed=@($p.AllowedRoots);$maximum=[long]$p.MaximumBytes;$total=0L;$verified=[Collections.Generic.List[object]]::new()
    foreach($entry in $entries){
        $path=Assert-WinCareSafePath -LiteralPath ([string](Get-WinCarePropertyValue $entry 'Path' '')) -AllowedRoots $allowed
        $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){return New-WinCareResult -Success $false -Status Blocked -Code 'CleanupEntryUnsafe' -Message "Cleanup entry is not a regular file: $path" -ExitCode 22}
        $length=[long](Get-WinCarePropertyValue $entry 'Length' -1);$ticks=[long](Get-WinCarePropertyValue $entry 'LastWriteUtcTicks' -1);$hash=[string](Get-WinCarePropertyValue $entry 'Sha256' '')
        if($item.Length -ne $length -or $item.LastWriteTimeUtc.Ticks -ne $ticks -or (Get-WinCareSha256 $path) -ne $hash){return New-WinCareResult -Success $false -Status Blocked -Code 'CleanupEntryChanged' -Message "Cleanup entry changed after preview: $path" -ExitCode 74}
        $total+=$length;if($total -gt $maximum){return New-WinCareResult -Success $false -Status Blocked -Code 'CleanupManifestTooLarge' -Message 'Cleanup manifest exceeds its byte limit.' -ExitCode 22}
        $verified.Add([pscustomobject]@{Path=$path;Length=$length})
    }
    $deleted=0;$bytes=0L;$failures=[Collections.Generic.List[string]]::new()
    foreach($entry in $verified){
        try{
            $current=Get-Item -LiteralPath $entry.Path -Force -ErrorAction Stop
            $manifest=@($entries|Where-Object{[string](Get-WinCarePropertyValue $_ 'Path' '') -eq $entry.Path}|Select-Object -First 1)
            if($manifest.Count -ne 1 -or $current.Length -ne [long](Get-WinCarePropertyValue $manifest[0] 'Length' -1) -or $current.LastWriteTimeUtc.Ticks -ne [long](Get-WinCarePropertyValue $manifest[0] 'LastWriteUtcTicks' -1) -or (Get-WinCareSha256 $entry.Path) -ne [string](Get-WinCarePropertyValue $manifest[0] 'Sha256' '')){throw 'File changed immediately before deletion.'}
            Remove-Item -LiteralPath $entry.Path -Force -ErrorAction Stop;$deleted++;$bytes+=$entry.Length
        }catch{$failures.Add("$($entry.Path): $($_.Exception.Message)")}
    }
    $success=$failures.Count -eq 0
    New-WinCareResult -Success $success -Status $(if($success){'Succeeded'}else{'Failed'}) -Code $(if($success){'AdmittedCleanupCompleted'}else{'AdmittedCleanupPartialFailure'}) -Message $(if($success){'All admitted cleanup files were deleted.'}else{'Some admitted cleanup files could not be deleted.'}) -Data @{DeletedFiles=$deleted;DeletedBytes=$bytes;RequestedFiles=$verified.Count} -Warnings @($failures) -ExitCode $(if($success){0}else{1})
}

function Get-WinCareApplicationDataRelocationProfile {
    [CmdletBinding()]param([string]$Id)
    $profiles=[Collections.Generic.List[object]]::new()
    foreach($definition in @(
        @{Id='chrome-cache';Title='Chrome profile cache';Root=$env:LOCALAPPDATA;Relative='Google\Chrome\User Data\Default\Cache';ProcessNames=@('chrome')},
        @{Id='edge-cache';Title='Microsoft Edge profile cache';Root=$env:LOCALAPPDATA;Relative='Microsoft\Edge\User Data\Default\Cache';ProcessNames=@('msedge')},
        @{Id='vscode-workspace-storage';Title='Visual Studio Code workspace storage';Root=$env:APPDATA;Relative='Code\User\workspaceStorage';ProcessNames=@('Code')},
        @{Id='steam-shader-cache';Title='Steam shader cache';Root=${env:ProgramFiles(x86)};Relative='Steam\steamapps\shadercache';ProcessNames=@('steam')}
    )){
        if([string]::IsNullOrWhiteSpace([string]$definition.Root)){continue}
        $profiles.Add([pscustomobject]@{Id=$definition.Id;Title=$definition.Title;Source=(Join-Path ([string]$definition.Root) ([string]$definition.Relative));ProcessNames=@($definition.ProcessNames);Risk='High'})
    }
    foreach($profile in $profiles){$profile|Add-Member -NotePropertyName SourceRecords -NotePropertyValue @('D164-WINDOWS-CLEANER-SOFTWARE-MOVE') -Force}
    if($Id){@($profiles|Where-Object Id -eq $Id)}else{@($profiles)}
}

function Get-WinCareApplicationDataRelocationAssessment {
    [CmdletBinding()]param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]{3,80}$')][string]$ProfileId,[Parameter(Mandatory)][string]$DestinationRoot)
    $profile=@(Get-WinCareApplicationDataRelocationProfile -Id $ProfileId)|Select-Object -First 1;if(-not $profile){throw "Unknown relocation profile: $ProfileId"}
    $source=Assert-WinCareSafePath -LiteralPath $profile.Source
    $sourceItem=Get-Item -LiteralPath $source -Force;if(-not $sourceItem.PSIsContainer -or ($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw 'Relocation source must be a normal directory, not a junction or symlink.'}
    $destinationRoot=Assert-WinCareSafePath -LiteralPath $DestinationRoot -AllowMissing
    $destination=Resolve-WinCareCanonicalPath -LiteralPath (Join-Path $destinationRoot $ProfileId) -AllowMissing
    if((Test-WinCarePathWithin -Child $destination -Parent $source -AllowEqual) -or (Test-WinCarePathWithin -Child $source -Parent $destination -AllowEqual)){throw 'Relocation source and destination cannot contain one another.'}
    $running=@(Get-Process -Name $profile.ProcessNames -ErrorAction SilentlyContinue|Select-Object Id,ProcessName,StartTime)
    $measure=Measure-WinCarePathTree -LiteralPath $source -MaximumEntries 1000000
    [pscustomobject]@{SchemaVersion=1;Profile=$profile;Source=$source;Destination=$destination;SourceHash=Get-WinCarePathContentHash $source;Bytes=[long]$measure.Bytes;Files=[long]$measure.Files;Directories=[long]$measure.Directories;RunningProcesses=$running;Ready=($running.Count -eq 0 -and -not(Test-Path -LiteralPath $destination));DestinationExists=Test-Path -LiteralPath $destination;SourceRecords=@('D164-WINDOWS-CLEANER-SOFTWARE-MOVE')}
}

function New-WinCareApplicationDataRelocationPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ProfileId,[Parameter(Mandatory)][string]$DestinationRoot)
    $a=Get-WinCareApplicationDataRelocationAssessment -ProfileId $ProfileId -DestinationRoot $DestinationRoot
    if($a.RunningProcesses.Count){throw "Close these applications before relocation: $(@($a.RunningProcesses.ProcessName|Sort-Object -Unique)-join ', ')"}
    if($a.DestinationExists){throw 'Relocation destination already exists.'}
    $allowed=@((Split-Path -Parent $a.Source),(Resolve-WinCareCanonicalPath -LiteralPath $DestinationRoot -AllowMissing))
    $parameters=@{ProfileId=$ProfileId;Source=$a.Source;Destination=$a.Destination;ExpectedSourceHash=$a.SourceHash;ProcessNames=@($a.Profile.ProcessNames);AllowedRoots=$allowed;MaximumBytes=[long]$a.Bytes}
    $compensator=@{Type='RestoreRelocatedDirectory';Parameters=@{Source=$a.Source;Destination=$a.Destination;ExpectedDestinationHash=$a.SourceHash;ProcessNames=@($a.Profile.ProcessNames);AllowedRoots=$allowed;MaximumBytes=[long]$a.Bytes}}
    $action=New-WinCareAction -Type RelocateDirectoryWithJunction -Label "Relocate $($a.Profile.Title)" -Risk High -Parameters $parameters -Reversible $true -Compensator $compensator -EstimatedBytes ([long]$a.Bytes) -Verification 'The copied tree hash, junction target, and final destination hash must match the previewed source.' -Tags @('Relocation','Junction','CompareAndSwap') -SourceRecords @('D164-WINDOWS-CLEANER-SOFTWARE-MOVE') -RecoveryDescription 'Remove the exact junction and copy the unchanged destination tree back to its original path.'
    New-WinCarePlan -Title "Relocate application data: $($a.Profile.Title)" -Description 'Copies and content-verifies the bounded directory, swaps it behind a junction, and performs internal rollback on any failure. WinCare never terminates applications automatically.' -Actions @($action) -SourceRecords @($action.SourceRecords)
}

function Test-WinCareRelocationProcessesStopped {
    param([string[]]$ProcessNames)
    @($ProcessNames|ForEach-Object{Get-Process -Name $_ -ErrorAction SilentlyContinue}|Where-Object{$_}).Count -eq 0
}

function Invoke-WinCareRelocateDirectoryAction {
    param([Parameter(Mandatory)][object]$Action)
    $p=$Action.Parameters;$allowed=@($p.AllowedRoots)
    $source=Assert-WinCareSafePath -LiteralPath ([string]$p.Source) -AllowedRoots $allowed
    $destination=Assert-WinCareSafePath -LiteralPath ([string]$p.Destination) -AllowedRoots $allowed -AllowMissing
    if(-not(Test-WinCareRelocationProcessesStopped -ProcessNames @($p.ProcessNames))){return New-WinCareResult -Success $false -Status Blocked -Code 'RelocationProcessRunning' -Message 'A related application started after preview.' -ExitCode 74}
    if((Get-WinCarePathContentHash $source) -ne [string]$p.ExpectedSourceHash){return New-WinCareResult -Success $false -Status Blocked -Code 'RelocationSourceChanged' -Message 'Relocation source changed after preview.' -ExitCode 74}
    if(Test-Path -LiteralPath $destination){return New-WinCareResult -Success $false -Status Blocked -Code 'RelocationDestinationExists' -Message 'Relocation destination already exists.' -ExitCode 17}
    $measure=Measure-WinCarePathTree -LiteralPath $source;if($measure.Bytes -gt [long]$p.MaximumBytes){return New-WinCareResult -Success $false -Status Blocked -Code 'RelocationSizeChanged' -Message 'Relocation source exceeds the previewed byte bound.' -ExitCode 74}
    $destinationParent=Split-Path -Parent $destination;$null=New-Item -ItemType Directory -Path $destinationParent -Force
    $temp=Join-Path $destinationParent ('.wincare-relocation-'+[guid]::NewGuid().ToString('N'))
    $backup=Join-Path (Split-Path -Parent $source) ('.wincare-original-'+[guid]::NewGuid().ToString('N'))
    $sourceMoved=$false;$destinationCommitted=$false;$junctionCreated=$false
    try{
        Copy-Item -LiteralPath $source -Destination $temp -Recurse -Force -ErrorAction Stop
        if((Get-WinCarePathContentHash $temp) -ne [string]$p.ExpectedSourceHash){throw 'Relocation staging copy failed hash verification.'}
        Move-Item -LiteralPath $source -Destination $backup -ErrorAction Stop;$sourceMoved=$true
        Move-Item -LiteralPath $temp -Destination $destination -ErrorAction Stop;$destinationCommitted=$true
        $null=New-Item -ItemType Junction -Path $source -Target $destination -ErrorAction Stop;$junctionCreated=$true
        $junction=Get-Item -LiteralPath $source -Force -ErrorAction Stop
        if(($junction.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0){throw 'Relocation source is not a junction after commit.'}
        $junctionTarget=[string](Get-WinCarePropertyValue $junction 'LinkTarget' (Get-WinCarePropertyValue $junction 'Target' ''))
        if([string]::IsNullOrWhiteSpace($junctionTarget) -or (Resolve-WinCareCanonicalPath -LiteralPath $junctionTarget) -ne $destination){throw 'Relocation junction target does not match the previewed destination.'}
        if((Get-WinCarePathContentHash $destination) -ne [string]$p.ExpectedSourceHash){throw 'Relocation destination changed during commit.'}
        Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction Stop;$sourceMoved=$false
        return New-WinCareResult -Success $true -Code 'DirectoryRelocated' -Message 'Application data was copied, verified, and replaced by a junction.' -Data @{Source=$source;Destination=$destination;Sha256=$p.ExpectedSourceHash;Bytes=$measure.Bytes;Files=$measure.Files}
    }catch{
        $warnings=[Collections.Generic.List[string]]::new();$failure=$_.Exception.Message
        try{if($junctionCreated -and (Test-Path -LiteralPath $source)){Remove-Item -LiteralPath $source -Force -ErrorAction Stop}}catch{$warnings.Add("Junction rollback failed: $($_.Exception.Message)")}
        try{if($destinationCommitted -and (Test-Path -LiteralPath $destination)){Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction Stop}}catch{$warnings.Add("Destination rollback failed: $($_.Exception.Message)")}
        try{if($sourceMoved -and (Test-Path -LiteralPath $backup) -and -not(Test-Path -LiteralPath $source)){Move-Item -LiteralPath $backup -Destination $source -ErrorAction Stop}}catch{$warnings.Add("Source rollback failed: $($_.Exception.Message)")}
        return New-WinCareResult -Success $false -Status Failed -Code $(if($warnings.Count){'RelocationRollbackFailed'}else{'RelocationFailedRolledBack'}) -Message $failure -Warnings @($warnings) -ExitCode 1
    }finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}}
}

function Invoke-WinCareRestoreRelocatedDirectoryAction {
    param([Parameter(Mandatory)][object]$Action)
    $p=$Action.Parameters;$allowed=@($p.AllowedRoots)
    $source=Assert-WinCareSafePath -LiteralPath ([string]$p.Source) -AllowedRoots $allowed -AllowReparsePoint
    $destination=Assert-WinCareSafePath -LiteralPath ([string]$p.Destination) -AllowedRoots $allowed
    if(-not(Test-WinCareRelocationProcessesStopped -ProcessNames @($p.ProcessNames))){return New-WinCareResult -Success $false -Status Blocked -Code 'RelocationProcessRunning' -Message 'Close related applications before restoring relocated data.' -ExitCode 74}
    $sourceItem=Get-Item -LiteralPath $source -Force;if(($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0){return New-WinCareResult -Success $false -Status Blocked -Code 'RelocationJunctionMissing' -Message 'The original path is no longer the expected junction.' -ExitCode 74}
    $junctionTarget=[string](Get-WinCarePropertyValue $sourceItem 'LinkTarget' (Get-WinCarePropertyValue $sourceItem 'Target' ''));if([string]::IsNullOrWhiteSpace($junctionTarget) -or (Resolve-WinCareCanonicalPath -LiteralPath $junctionTarget) -ne $destination){return New-WinCareResult -Success $false -Status Blocked -Code 'RelocationJunctionChanged' -Message 'The junction target changed after the original operation.' -ExitCode 74}
    if((Get-WinCarePathContentHash $destination) -ne [string]$p.ExpectedDestinationHash){return New-WinCareResult -Success $false -Status Blocked -Code 'RelocationDestinationChanged' -Message 'Relocated data changed after the original operation; automatic restore is blocked.' -ExitCode 74}
    $temp=Join-Path (Split-Path -Parent $source) ('.wincare-restore-'+[guid]::NewGuid().ToString('N'))
    try{
        Copy-Item -LiteralPath $destination -Destination $temp -Recurse -Force -ErrorAction Stop
        if((Get-WinCarePathContentHash $temp) -ne [string]$p.ExpectedDestinationHash){throw 'Restore staging copy failed verification.'}
        Remove-Item -LiteralPath $source -Force -ErrorAction Stop
        Move-Item -LiteralPath $temp -Destination $source -ErrorAction Stop
        Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction Stop
        $success=(Get-WinCarePathContentHash $source) -eq [string]$p.ExpectedDestinationHash -and -not(Test-Path -LiteralPath $destination)
        New-WinCareResult -Success $success -Code $(if($success){'RelocationRestored'}else{'RelocationRestoreVerificationFailed'}) -Message $(if($success){'Relocated data was restored to the original path.'}else{'Restored data failed final verification.'}) -Data @{Source=$source;Destination=$destination} -ExitCode $(if($success){0}else{74})
    }catch{New-WinCareResult -Success $false -Status Failed -Code 'RelocationRestoreFailed' -Message $_.Exception.Message -ExitCode 1}
    finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}}
}

function Get-WinCareFilePreviewProfile {
    [CmdletBinding()]param([string]$Extension)
    $profiles=@(
        [pscustomobject]@{Id='structured-json';Extensions=@('.json','.jsonc','.ipynb');Kind='StructuredText';Priority=100;ActiveContent=$false},
        [pscustomobject]@{Id='structured-xml';Extensions=@('.xml','.xaml','.csproj','.props','.targets','.svg');Kind='StructuredText';Priority=95;ActiveContent=$true},
        [pscustomobject]@{Id='delimited-table';Extensions=@('.csv','.tsv','.psv');Kind='DelimitedText';Priority=90;ActiveContent=$false},
        [pscustomobject]@{Id='archive-container';Extensions=@('.zip','.appx','.msix','.msixbundle','.appxbundle','.nupkg','.snupkg','.docx','.xlsx','.pptx','.vsix','.jar','.whl');Kind='Archive';Priority=85;ActiveContent=$false},
        [pscustomobject]@{Id='portable-executable';Extensions=@('.exe','.dll','.sys','.cpl','.scr','.ocx');Kind='PortableExecutable';Priority=85;ActiveContent=$true},
        [pscustomobject]@{Id='image-metadata';Extensions=@('.png','.jpg','.jpeg','.gif','.bmp','.webp','.ico','.tif','.tiff');Kind='ImageMetadata';Priority=80;ActiveContent=$false},
        [pscustomobject]@{Id='pdf-metadata';Extensions=@('.pdf');Kind='PdfMetadata';Priority=75;ActiveContent=$false},
        [pscustomobject]@{Id='plain-text';Extensions=@('.txt','.md','.markdown','.log','.ini','.cfg','.conf','.yaml','.yml','.toml','.ps1','.psm1','.psd1','.bat','.cmd','.reg','.js','.ts','.tsx','.jsx','.cs','.cpp','.c','.h','.rs','.py','.java','.kt','.html','.css','.sql');Kind='Text';Priority=50;ActiveContent=$true},
        [pscustomobject]@{Id='binary';Extensions=@();Kind='Binary';Priority=-100;ActiveContent=$false}
    )
    foreach($profile in $profiles){$profile|Add-Member -NotePropertyName SourceRecords -NotePropertyValue @('D166-QUICKLOOK-PREVIEW-PIPELINE') -Force}
    if($Extension){$ext=if($Extension.StartsWith('.')){$Extension.ToLowerInvariant()}else{'.'+$Extension.ToLowerInvariant()};@($profiles|Where-Object{$ext -in $_.Extensions}|Sort-Object Priority -Descending|Select-Object -First 1)}else{@($profiles)}
}

function Get-WinCareFilePreview {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath,[ValidateRange(4096,4194304)][int]$MaximumBytes=1048576,[ValidateRange(1,1000)][int]$MaximumLines=200,[switch]$IncludeText)
    if($LiteralPath.StartsWith('\\')){throw 'Remote UNC paths are not admitted to local preview.'}
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $item=Get-Item -LiteralPath $path -Force
    if($item.PSIsContainer){
        $measure=Measure-WinCarePathTree -LiteralPath $path -MaximumEntries 10000
        $children=@(Get-ChildItem -LiteralPath $path -Force -ErrorAction SilentlyContinue|Where-Object{($_.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0}|Sort-Object PSIsContainer,Name|Select-Object -First 200 Name,PSIsContainer,Length,LastWriteTimeUtc)
        return [pscustomobject]@{SchemaVersion=1;Path=$path;Kind='Directory';Execution='None';Bytes=$measure.Bytes;Files=$measure.Files;Directories=$measure.Directories;Children=$children;Truncated=($children.Count -ge 200);SourceRecords=@('D166-QUICKLOOK-PREVIEW-PIPELINE')}
    }
    $extension=$item.Extension.ToLowerInvariant();$profile=@(Get-WinCareFilePreviewProfile -Extension $extension)|Select-Object -First 1;if(-not $profile){$profile=@(Get-WinCareFilePreviewProfile|Where-Object Id -eq 'binary')[0]}
    $readLength=[int][math]::Min([long]$MaximumBytes,[long]$item.Length);$bytes=New-Object byte[] $readLength
    $stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try{$actual=$stream.Read($bytes,0,$readLength)}finally{$stream.Dispose()}
    if($actual -eq 0){$bytes=[byte[]]@()}elseif($actual -lt $bytes.Length){$bytes=$bytes[0..($actual-1)]}
    $magic=if($actual){[Convert]::ToHexString($bytes[0..([math]::Min(31,$actual-1))]).ToLowerInvariant()}else{''}
    $hash=if($item.Length -le 268435456){Get-WinCareSha256 $path}else{$null}
    $detail=$null;$lines=@();$truncated=$item.Length -gt $MaximumBytes
    switch([string]$profile.Kind){
        'Archive'{try{$detail=Get-WinCareArchiveInspection -LiteralPath $path -MaximumMembers 5000 -MaximumExpandedBytes 2147483648 -MaximumCompressionRatio 200}catch{$detail=[pscustomobject]@{Error=$_.Exception.Message}}}
        'PortableExecutable'{try{$detail=Get-WinCarePeMetadata -LiteralPath $path}catch{$detail=[pscustomobject]@{Error=$_.Exception.Message}}}
        'ImageMetadata'{try{$detail=Get-WinCareImageMetadata -LiteralPath $path}catch{$detail=[pscustomobject]@{Error=$_.Exception.Message}}}
        'PdfMetadata'{$text=[Text.Encoding]::ASCII.GetString($bytes);$version=if($text -match '^%PDF-(?<v>[0-9.]+)'){$Matches.v}else{''};$pageMatches=[regex]::Matches($text,'/Type\s*/Page\b').Count;$detail=[pscustomobject]@{Version=$version;ApproximatePageObjects=$pageMatches;Rendered=$false}}
        'StructuredText'{}
        'DelimitedText'{}
        'Text'{}
        default{$detail=[pscustomobject]@{HexPrefix=$magic;Rendered=$false}}
    }
    if($profile.Kind -in @('StructuredText','DelimitedText','Text') -and $IncludeText){
        $encoding=[Text.UTF8Encoding]::new($false,$false)
        if($actual -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE){$encoding=[Text.Encoding]::Unicode}
        elseif($actual -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF){$encoding=[Text.Encoding]::BigEndianUnicode}
        $text=$encoding.GetString($bytes)
        $rawLines=@($text -split "`r?`n"|Select-Object -First $MaximumLines)
        $lines=@(ConvertTo-WinCareRedactedObject -InputObject $rawLines)
        if(@($text -split "`r?`n").Count -gt $MaximumLines){$truncated=$true}
        if($profile.Id -eq 'structured-json'){try{$obj=$text|ConvertFrom-Json -Depth 30;$keys=if($null -eq $obj){@()}elseif($obj -is [Collections.IDictionary]){@($obj.Keys)}else{@($obj.PSObject.Properties.Name)};$detail=[pscustomobject]@{Valid=$true;RootType=if($null -eq $obj){'Null'}else{$obj.GetType().Name};TopLevelKeys=@($keys|Select-Object -First 100)}}catch{$detail=[pscustomobject]@{Valid=$false;Error=$_.Exception.Message}}}
        elseif($profile.Id -eq 'structured-xml'){
            try{$settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=$MaximumBytes;$reader=[Xml.XmlReader]::Create([IO.StringReader]::new($text),$settings);$root='';while($reader.Read()){if($reader.NodeType -eq [Xml.XmlNodeType]::Element){$root=$reader.LocalName;break}};$reader.Dispose();$detail=[pscustomobject]@{Valid=$true;RootElement=$root;ActiveContentBlocked=[bool]$profile.ActiveContent}}catch{$detail=[pscustomobject]@{Valid=$false;Error=$_.Exception.Message}}}
        elseif($profile.Kind -eq 'DelimitedText'){$delimiter=if($extension -eq '.tsv'){"`t"}elseif($extension -eq '.psv'){'|'}else{','};$counts=@($rawLines|Select-Object -First 20|ForEach-Object{($_.Split($delimiter)).Count});$detail=[pscustomobject]@{Delimiter=$delimiter;SampleRows=$counts.Count;MinimumColumns=if($counts){($counts|Measure-Object -Minimum).Minimum}else{0};MaximumColumns=if($counts){($counts|Measure-Object -Maximum).Maximum}else{0}}}
    }
    [pscustomobject]@{SchemaVersion=1;CapturedAt=[datetime]::UtcNow.ToString('o');Path=$path;Name=$item.Name;Extension=$extension;SizeBytes=[long]$item.Length;LastWriteTimeUtc=$item.LastWriteTimeUtc.ToString('o');Sha256=$hash;HashSkipped=($null -eq $hash);MagicHex=$magic;Profile=$profile.Id;Kind=$profile.Kind;ActiveContent=[bool]$profile.ActiveContent;Execution='None';TextIncluded=[bool]$IncludeText;TextLines=@($lines);Truncated=[bool]$truncated;Details=$detail;SourceRecords=@('D166-QUICKLOOK-PREVIEW-PIPELINE','D166-QUICKLOOK-ACTIVE-CONTENT-GUARDRAIL')}
}

function Get-WinCarePreviewHandlerAssessment {
    [CmdletBinding()]param([ValidateRange(1,512)][int]$Limit=256)
    if(-not $IsWindows){return @()}
    $rows=[Collections.Generic.List[object]]::new()
    foreach($root in @('Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers','Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers')){
        if(-not(Test-Path -LiteralPath $root)){continue}
        $item=Get-Item -LiteralPath $root -ErrorAction SilentlyContinue
        foreach($name in @($item.GetValueNames())){
            if($rows.Count -ge $Limit){break}
            $clsid=[string]$name;if($clsid -notmatch '^\{[A-Fa-f0-9-]{36}\}$'){continue}
            $server='';foreach($base in @('Registry::HKEY_CLASSES_ROOT\CLSID','Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID')){$key=Join-Path $base ($clsid+'\InprocServer32');if(Test-Path -LiteralPath $key){$server=[Environment]::ExpandEnvironmentVariables([string](Get-Item -LiteralPath $key).GetValue(''));if($server){break}}}
            $signature=if($server -and (Test-Path -LiteralPath $server -PathType Leaf)){try{(Get-AuthenticodeSignature -LiteralPath $server).Status.ToString()}catch{'Unknown'}}else{'Missing'}
            $rows.Add([pscustomobject]@{Scope=if($root -match 'CURRENT_USER'){'CurrentUser'}else{'LocalMachine'};Clsid=$clsid;Name=[string]$item.GetValue($name);ServerPathHash=if($server){Get-WinCareSha256Text $server}else{''};ServerPresent=[bool]($server -and(Test-Path -LiteralPath $server));Signature=$signature;Loaded=$false;Execution='None';SourceRecords=@('D166-QUICKLOOK-PLUGIN-PRIORITY','D166-QUICKLOOK-ACTIVE-CONTENT-GUARDRAIL')})
        }
    }
    @($rows)
}

function New-WinCareFilePreviewExportPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath,[ValidatePattern('^[A-Za-z0-9._-]{3,100}\.json$')][string]$Name='file-preview.json',[switch]$IncludeText)
    $preview=Get-WinCareFilePreview -LiteralPath $LiteralPath -IncludeText:$IncludeText
    $root=Join-Path (Get-WinCareCleanerPreviewRoot) 'Previews';$null=New-Item -ItemType Directory -Path $root -Force
    $path=Join-Path $root $Name;$content=[Text.Encoding]::UTF8.GetBytes((ConvertTo-WinCareCanonicalJson $preview -Depth 60)+"`n")
    if($content.Length -gt 8388608){throw 'Preview export exceeds the 8 MiB output limit.'}
    $action=New-WinCareManagedFileWriteAction -Path $path -Content $content -AllowedRoots @($root) -Label 'Write bounded local file preview' -SourceRecords @('D166-QUICKLOOK-PREVIEW-PIPELINE')
    New-WinCarePlan -Title 'Export file preview evidence' -Description 'Writes a bounded, redacted, non-executing local preview result.' -Actions @($action) -SourceRecords @($action.SourceRecords)
}

function Get-WinCareCleanerPreviewCapabilityCard {
    [CmdletBinding()]param([string]$Query)
    $cards=@(
        [pscustomobject]@{Id='disk-pressure';Title='Disk-pressure cleanup';Summary='Threshold-aware cleanup profiles over canonical WinCare targets plus scheduled automation.';Risk='Moderate';SourceRecords=@('D164-WINDOWS-CLEANER-DISK-PRESSURE','D164-WINDOWS-CLEANER-AUTO-CLEAN')},
        [pscustomobject]@{Id='winapp2';Title='Winapp2 admission';Summary='Bounded FileKey-only parser and exact file manifest; registry directives and broad scans are rejected.';Risk='High';SourceRecords=@('D164-WINDOWS-CLEANER-WINAPP2')},
        [pscustomobject]@{Id='relocation';Title='Verified application-data relocation';Summary='Copy, hash verification, junction commit, internal rollback, and exact recovery without process termination.';Risk='High';SourceRecords=@('D164-WINDOWS-CLEANER-SOFTWARE-MOVE')},
        [pscustomobject]@{Id='file-preview';Title='Local file preview';Summary='Priority-based, bounded, redacted, non-executing inspection of directories, text, tables, archives, images, PE files, and PDFs.';Risk='ReadOnly';SourceRecords=@('D166-QUICKLOOK-PREVIEW-PIPELINE')},
        [pscustomobject]@{Id='preview-handlers';Title='Preview-handler assessment';Summary='Inventory registered COM preview handlers and signatures without loading them.';Risk='ReadOnly';SourceRecords=@('D166-QUICKLOOK-PLUGIN-PRIORITY')}
    )
    if($Query){@($cards|Where-Object{($_.Title+' '+$_.Summary+' '+$_.Id) -match [regex]::Escape($Query)})}else{@($cards)}
}


