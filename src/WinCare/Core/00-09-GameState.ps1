#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: Steam inventory, archive validation, backup, restore, and rollback.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function Get-WinCareSteamLibraryPath {
    [CmdletBinding()]
    param()
    $paths=[Collections.Generic.List[string]]::new()
    if(-not $IsWindows) { return @() }
    $install=$null
    foreach($registryPath in @('HKCU:\Software\Valve\Steam','HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        try {
            $record=Get-ItemProperty $registryPath -ErrorAction Stop
            $candidate=if($record.SteamPath) { $record.SteamPath } else { $record.InstallPath }
            if($candidate) {
                $install=[IO.Path]::GetFullPath([string]$candidate)
                break
            }
        } catch { Write-Verbose "Steam registry discovery failed at $registryPath." }
    }
    if(-not $install -or -not (Test-Path -LiteralPath $install -PathType Container)) { return @() }
    $primary=Join-Path $install 'steamapps'
    if(Test-Path -LiteralPath $primary -PathType Container) { $paths.Add($primary) }
    $catalog=Join-Path $primary 'libraryfolders.vdf'
    if(Test-Path -LiteralPath $catalog -PathType Leaf) {
        foreach($line in Read-WinCareBoundedLines -LiteralPath $catalog -MaximumBytes 4194304 -MaximumLines 50000 -MaximumLineCharacters 65536) {
            if($line -notmatch '^\s*"path"\s*"(.+)"') { continue }
            $candidate=$Matches[1].Replace('\\','\')
            $steamApps=Join-Path $candidate 'steamapps'
            if(Test-Path -LiteralPath $steamApps -PathType Container) { $paths.Add([IO.Path]::GetFullPath($steamApps)) }
        }
    }
    return @($paths | Sort-Object -Unique)
}

function Get-WinCareSteamGame {
    [CmdletBinding()]
    param(
        [string[]]$AppId=@(),
        [ValidateRange(1,50000)][int]$MaximumManifests=10000
    )
    $games=[Collections.Generic.List[object]]::new()
    $manifestCount=0
    foreach($library in Get-WinCareSteamLibraryPath) {
        foreach($manifest in Get-ChildItem -LiteralPath $library -Filter 'appmanifest_*.acf' -File -ErrorAction SilentlyContinue) {
            $manifestCount++
            if($manifestCount -gt $MaximumManifests) { throw "Steam inventory exceeds the $MaximumManifests-manifest ceiling." }
            $data=@{}
            foreach($line in Read-WinCareBoundedLines -LiteralPath $manifest.FullName -MaximumBytes 1048576 -MaximumLines 20000 -MaximumLineCharacters 65536) {
                if($line -match '^\s*"([^"]+)"\s*"([^"]*)"') { $data[$Matches[1]]=$Matches[2] }
            }
            if($AppId.Count -and [string]$data.appid -notin $AppId) { continue }
            if([string]::IsNullOrWhiteSpace([string]$data.appid) -or [string]::IsNullOrWhiteSpace([string]$data.installdir)) { continue }
            $installDir=Join-Path (Join-Path $library 'common') ([string]$data.installdir)
            $sizeOnDisk=0L
            [long]::TryParse([string]$data.SizeOnDisk,[ref]$sizeOnDisk) | Out-Null
            $games.Add([pscustomobject]@{
                AppId=[string]$data.appid
                Name=[string]$data.name
                StateFlags=[string]$data.StateFlags
                InstallDir=$installDir
                Installed=Test-Path -LiteralPath $installDir -PathType Container
                LibraryPath=$library
                ManifestPath=$manifest.FullName
                ManifestSha256=Get-WinCareSha256 -LiteralPath $manifest.FullName
                LastUpdated=[string]$data.LastUpdated
                SizeOnDisk=$sizeOnDisk
                EvidenceType='SteamManifest'
            })
        }
    }
    return @($games | Sort-Object Name,AppId)
}

function Get-WinCareSteamUser {
    [CmdletBinding()]
    param()
    if(-not $IsWindows) { return @() }
    $steamRoot=$null
    try { $steamRoot=(Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath } catch { }
    if(-not $steamRoot) { return @() }
    $login=Join-Path $steamRoot 'config\loginusers.vdf'
    if(-not (Test-Path -LiteralPath $login -PathType Leaf)) { return @() }
    $users=[Collections.Generic.List[object]]::new()
    $current=$null
    foreach($line in Read-WinCareBoundedLines -LiteralPath $login -MaximumBytes 4194304 -MaximumLines 50000 -MaximumLineCharacters 65536) {
        if($line -match '^\s*"(\d{17})"\s*$') {
            $current=[ordered]@{SteamId=$Matches[1]}
        } elseif($current -and $line -match '^\s*"([^"]+)"\s*"([^"]*)"') {
            $current[$Matches[1]]=$Matches[2]
            if($Matches[1] -eq 'Timestamp') {
                $users.Add([pscustomobject]$current)
                $current=$null
            }
        }
    }
    return @($users)
}

function Get-WinCareSteamCloudFile {
    [CmdletBinding()]
    param(
        [ValidatePattern('^$|^\d{17}$')][string]$UserId='',
        [ValidatePattern('^$|^\d{1,12}$')][string]$AppId='',
        [ValidateRange(1,1000)][int]$MaximumUsers=100,
        [ValidateRange(1,50000)][int]$MaximumApps=5000,
        [ValidateRange(1,200000)][int]$MaximumFiles=100000,
        [ValidateRange(1,1099511627776)][long]$MaximumTotalBytes=107374182400,
        [ValidateRange(1,1099511627776)][long]$MaximumFileBytes=10737418240
    )
    if(-not $IsWindows) { return @() }
    try { $steamRoot=(Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath } catch { return @() }
    if(-not $steamRoot) { return @() }
    $root=Join-Path $steamRoot 'userdata'
    if(-not (Test-Path -LiteralPath $root -PathType Container)) { return @() }
    $root=Assert-WinCareSafePath -LiteralPath $root
    $users=if($UserId){@(Get-Item -LiteralPath (Join-Path $root $UserId) -Force -ErrorAction SilentlyContinue)}else{@(Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue | Select-Object -First ($MaximumUsers+1))}
    if($users.Count -gt $MaximumUsers) { throw "Steam inventory exceeds the $MaximumUsers-user ceiling." }
    $files=[Collections.Generic.List[object]]::new()
    $appCount=0
    $totalBytes=0L
    foreach($user in $users) {
        if(-not $user -or ($user.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or -not (Test-WinCarePathWithin -Child $user.FullName -Parent $root)) { throw 'Steam userdata contains an unsafe user directory.' }
        $apps=if($AppId){@(Get-Item -LiteralPath (Join-Path $user.FullName $AppId) -Force -ErrorAction SilentlyContinue)}else{@(Get-ChildItem -LiteralPath $user.FullName -Directory -Force -ErrorAction SilentlyContinue | Select-Object -First (($MaximumApps-$appCount)+1))}
        foreach($app in $apps) {
            if(-not $app) { continue }
            $appCount++
            if($appCount -gt $MaximumApps) { throw "Steam inventory exceeds the $MaximumApps-application ceiling." }
            if(($app.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or -not (Test-WinCarePathWithin -Child $app.FullName -Parent $user.FullName)) { throw 'Steam userdata contains an unsafe application directory.' }
            $remote=Join-Path $app.FullName 'remote'
            if(-not (Test-Path -LiteralPath $remote -PathType Container)) { continue }
            foreach($file in Get-WinCareBoundedTreeEntry -LiteralPath $remote -MaximumEntries ($MaximumFiles+1) -EntryType File) {
                if($files.Count -ge $MaximumFiles) { throw "Steam inventory exceeds the $MaximumFiles-file ceiling." }
                if([long]$file.Length -gt $MaximumFileBytes) { throw "Steam Cloud file exceeds $MaximumFileBytes bytes: $($file.FullName)" }
                $totalBytes += [long]$file.Length
                if($totalBytes -gt $MaximumTotalBytes) { throw "Steam Cloud inventory exceeds $MaximumTotalBytes bytes." }
                $files.Add([pscustomobject]@{
                    UserId=$user.Name
                    AppId=$app.Name
                    Path=$file.FullName
                    RelativePath=[IO.Path]::GetRelativePath($remote,$file.FullName).Replace('\','/')
                    Length=[long]$file.Length
                    LastWriteTimeUtc=$file.LastWriteTimeUtc
                    Sha256=Get-WinCareSha256 -LiteralPath $file.FullName
                })
            }
        }
    }
    return @($files)
}

function Get-WinCareSteamProcess {
    [CmdletBinding()]
    param()
    @(Get-Process steam,steamwebhelper,gameoverlayui -ErrorAction SilentlyContinue|Select-Object Id,ProcessName,StartTime,Path,CPU,WorkingSet64)
}
function Get-WinCareSteamCloudSnapshotHash {
    [CmdletBinding()]
    param([string]$UserId='',[string]$AppId='',[string]$Root='')
    if($Root){return Get-WinCarePathContentHash -LiteralPath $Root};$files=@(Get-WinCareSteamCloudFile -UserId $UserId -AppId $AppId|Select-Object RelativePath,Length,Sha256,LastWriteTimeUtc);if(Get-Command Get-WinCareCanonicalObjectHash -ErrorAction SilentlyContinue){return Get-WinCareCanonicalObjectHash $files};$json=$files|ConvertTo-Json -Compress -Depth 10;[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($json))).ToLowerInvariant()
}
function Get-WinCareGameIntegrityFinding {
    [CmdletBinding()]
    param([string[]]$ScanRoot=@(),[ValidateRange(1,20000)][int]$MaximumFilesPerGame=5000)
    $findings=[Collections.Generic.List[object]]::new();$targets=[Collections.Generic.List[object]]::new()
    foreach($game in Get-WinCareSteamGame){
        if(-not $game.Installed){$findings.Add([pscustomobject]@{Id="steam-missing-$($game.AppId)";Severity='Warning';Kind='MissingInstallDirectory';Title='Steam manifest points to a missing install directory';Name=$game.Name;Game=$game.Name;AppId=$game.AppId;Path=$game.InstallDir;Evidence="Manifest $($game.ManifestPath) names a directory that is absent.";EvidenceType='SteamManifestAndFileAbsence'});continue}
        $targets.Add([pscustomobject]@{Kind='SteamGame';Name=$game.Name;AppId=$game.AppId;Path=$game.InstallDir;ExpectedBytes=$game.SizeOnDisk})
    }
    foreach($root in @($ScanRoot)){
        if([string]::IsNullOrWhiteSpace($root)){continue};$resolved=(Resolve-Path -LiteralPath $root -ErrorAction Stop).Path;$item=Get-Item -LiteralPath $resolved -Force -ErrorAction Stop;if(-not $item.PSIsContainer){throw "Game integrity scan root is not a directory: $resolved"};if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "Game integrity scan roots cannot be reparse points: $resolved"};$targets.Add([pscustomobject]@{Kind='CustomRoot';Name=$item.Name;AppId=$null;Path=$resolved;ExpectedBytes=0})
    }
    $suspicious='(?i)(trainer|injector|dll[-_ ]?inject|cheat|aimbot|wallhack|mod[-_ ]?menu|process[-_ ]?hacker)'
    foreach($target in $targets){
        if($target.ExpectedBytes -gt 0){try{$measured=(Measure-WinCarePathTree $target.Path).Bytes;$difference=[math]::Abs($measured-$target.ExpectedBytes);if($difference -gt [math]::Max(100MB,$target.ExpectedBytes*0.25)){$findings.Add([pscustomobject]@{Id="steam-size-$($target.AppId)";Severity='Info';Kind='SizeDifference';Title='Steam manifest size differs materially from measured files';Name=$target.Name;Game=$target.Name;AppId=$target.AppId;Path=$target.Path;ManifestBytes=$target.ExpectedBytes;MeasuredBytes=$measured;Evidence="Manifest bytes=$($target.ExpectedBytes); measured bytes=$measured";EvidenceType='ManifestAndMeasuredTree'})}}catch{}}
        $scanned=0
        foreach($file in @(Get-ChildItem -LiteralPath $target.Path -File -Recurse -Force -ErrorAction SilentlyContinue)){
            if($scanned++ -ge $MaximumFilesPerGame){break};if($file.Name -notmatch $suspicious){continue};$signature=$null;if($file.Extension -in @('.exe','.dll','.sys','.ps1')){try{$signature=Get-AuthenticodeSignature -LiteralPath $file.FullName -ErrorAction Stop}catch{}}
            $hash=try{Get-WinCareSha256 -LiteralPath $file.FullName}catch{$null};$status=if($signature){$signature.Status.ToString()}else{'NotChecked'}
            $findings.Add([pscustomobject]@{Id=('game-suspicious-'+(Get-WinCareSha256Text $file.FullName).Substring(0,12));Severity='Warning';Kind='SuspiciousFilename';Title='Potential trainer or injector payload found; WinCare did not execute it';Name=$file.Name;Game=$target.Name;AppId=$target.AppId;Path=$file.FullName;Sha256=$hash;SignatureStatus=$status;Signer=if($signature -and $signature.SignerCertificate){$signature.SignerCertificate.Subject}else{$null};Evidence="Filename matched the configured indicator pattern; signature=$status; sha256=$hash";EvidenceType='FilenamePatternAuthenticodeAndHash'})
        }
    }
    return @($findings)
}
function New-WinCareSteamCloudBackupPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$UserId,[Parameter(Mandatory)][string]$AppId,[string]$Destination='',[string]$Name='')
    $files=@(Get-WinCareSteamCloudFile -UserId $UserId -AppId $AppId);if($files.Count -eq 0){throw 'No Steam Cloud files were found.'}
    $source=Split-Path -Parent ($files[0].Path);$snapshot=Get-WinCareSteamCloudSnapshotHash -Root $source
    if([string]::IsNullOrWhiteSpace($Destination)){
        $backupRoot=Join-Path (Get-WinCareBridgeStateRoot 'GameState') 'Backups';$null=New-Item -ItemType Directory -Path $backupRoot -Force
        if([string]::IsNullOrWhiteSpace($Name)){$Name="steam-$UserId-$AppId-$([datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ')).zip"}
        if($Name -notmatch '^[A-Za-z0-9._-]{1,120}$'){throw 'Steam backup name contains unsupported characters.'};if(-not $Name.EndsWith('.zip',[StringComparison]::OrdinalIgnoreCase)){$Name+='.zip'}
        $Destination=Join-Path $backupRoot $Name
    }
    $destinationPath=[IO.Path]::GetFullPath($Destination);$parent=Split-Path -Parent $destinationPath;if($parent){$null=New-Item -ItemType Directory -Path $parent -Force}
    $action=New-WinCareBridgeAction -Type 'CreateSteamCloudBackup' -Label "Back up Steam Cloud data for app $AppId" -Risk Low -Parameters @{UserId=$UserId;AppId=$AppId;SourceRoot=$source;Destination=$destinationPath;ExpectedSnapshotHash=$snapshot} -Verification 'The ZIP archive must pass entry-safety checks and its manifest hashes must match source files.'
    New-WinCareBridgePlan -Title 'Back up Steam Cloud data' -Actions @($action)
}
function New-WinCareSteamCloudRestorePlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$BackupPath,[Parameter(Mandatory)][string]$TargetRoot)
    $backup=(Resolve-Path $BackupPath -ErrorAction Stop).Path;if(-not (Test-WinCareSteamBackupArchive $backup)){throw 'Steam backup archive is invalid.'};$current=if(Test-Path $TargetRoot){Get-WinCarePathContentHash $TargetRoot}else{''};$action=New-WinCareBridgeAction -Type 'RestoreSteamCloudBackup' -Label 'Restore Steam Cloud backup' -Risk High -Reversible $false -Parameters @{BackupPath=$backup;BackupSha256=(Get-FileHash $backup -Algorithm SHA256).Hash.ToLowerInvariant();TargetRoot=[IO.Path]::GetFullPath($TargetRoot);ExpectedCurrentSnapshotHash=$current} -Verification 'Every restored file must match the archive manifest hash.';New-WinCareBridgePlan -Title 'Restore Steam Cloud data' -Actions @($action) -RollbackOnFailure $false
}
function Test-WinCareSteamBackupArchive {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[switch]$PassThru)
    $result=[ordered]@{Valid=$false;Path='';Manifest=$null;ArchiveSha256='';EntryCount=0;UncompressedBytes=0;Error=''}
    try{
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
        $resolved=(Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path;$result.Path=$resolved;$result.ArchiveSha256=Get-WinCareSha256 -LiteralPath $resolved
        $archive=[IO.Compression.ZipFile]::OpenRead($resolved)
        try{
            if($archive.Entries.Count -gt 100000){throw 'Steam backup contains too many members.'}
            $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach($entry in $archive.Entries){
                $name=[string]$entry.FullName
                if([string]::IsNullOrWhiteSpace($name) -or [IO.Path]::IsPathRooted($name) -or $name -match '\\' -or $name -match '(^|/)\.\.?(/|$)' -or $name.StartsWith('/') -or $name.Contains(':')){throw "Unsafe Steam backup member: $name"}
                if(-not $names.Add($name)){throw "Duplicate Steam backup member: $name"}
                $result.UncompressedBytes+=[int64]$entry.Length
                if($result.UncompressedBytes -gt 100GB){throw 'Steam backup uncompressed size exceeds 100 GiB.'}
            }
            $manifestEntry=$archive.GetEntry('wincare-game-backup.json')
            if(-not $manifestEntry){throw 'Steam backup manifest winCare-game-backup.json is missing.'}
            if($manifestEntry.Length -gt 16MB){throw 'Steam backup manifest exceeds 16 MiB.'}
            $manifestStream=$manifestEntry.Open()
            try{
                $manifestText=Read-WinCareBoundedStreamText -Stream $manifestStream `
                    -MaximumBytes 16777216 -ExpectedBytes ([long]$manifestEntry.Length)
                $manifest=$manifestText|ConvertFrom-Json -Depth 50
            }finally{$manifestStream.Dispose()}
            if([int]$manifest.SchemaVersion -ne 2){throw 'Steam backup manifest schema is unsupported.'}
            $files=@($manifest.Files);if($files.Count -lt 1){throw 'Steam backup manifest contains no files.'}
            $listed=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach($file in $files){
                $relative=[string]$file.Path
                if([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative -match '\\' -or $relative -match '(^|/)\.\.?(/|$)' -or $relative.StartsWith('/') -or $relative.Contains(':')){throw "Unsafe Steam backup member: payload/$relative"}
                if(-not $listed.Add($relative)){throw "Duplicate Steam backup member: payload/$relative"}
                if([string]$file.Sha256 -notmatch '^[a-f0-9]{64}$'){throw "Steam backup hash is invalid: $relative"}
                if([int64]$file.Length -lt 0){throw "Steam backup size is invalid: $relative"}
                $member=$archive.GetEntry('payload/'+$relative)
                if(-not $member){throw "Steam backup manifest references a missing member: $relative"}
                if([int64]$member.Length -ne [int64]$file.Length){throw "Steam backup size mismatch: $relative"}
                $stream=$member.Open();$sha=[Security.Cryptography.SHA256]::Create()
                try{$actual=[Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant()}finally{$sha.Dispose();$stream.Dispose()}
                if($actual -ne [string]$file.Sha256){throw "Steam backup hash mismatch: $relative"}
            }
            foreach($entry in $archive.Entries){
                if($entry.FullName -eq 'wincare-game-backup.json'){continue}
                if(-not $entry.FullName.StartsWith('payload/')){throw "Steam backup contains an unlisted entry: $($entry.FullName)"}
                $relative=$entry.FullName.Substring(8)
                if(-not $listed.Contains($relative)){throw "Steam backup contains an unlisted entry: $($entry.FullName)"}
            }
            $result.Valid=$true;$result.Manifest=$manifest;$result.EntryCount=$archive.Entries.Count
        }finally{$archive.Dispose()}
    }catch{$result.Error=$_.Exception.Message}
    if($PassThru){return [pscustomobject]$result}
    return [bool]$result.Valid
}
function Get-WinCareSteamBackupSourceFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceRoot,
        [ValidateRange(1,200000)][int]$MaximumFiles=100000,
        [ValidateRange(1,1099511627776)][long]$MaximumTotalBytes=107374182400,
        [ValidateRange(1,1099511627776)][long]$MaximumFileBytes=10737418240
    )
    $files=[Collections.Generic.List[object]]::new();$totalBytes=0L
    foreach($file in Get-WinCareBoundedTreeEntry -LiteralPath $SourceRoot -MaximumEntries ($MaximumFiles+1) -EntryType File){
        if($files.Count -ge $MaximumFiles){throw "Steam backup exceeds the $MaximumFiles-file ceiling."}
        if([long]$file.Length -gt $MaximumFileBytes){throw "Steam backup file exceeds $MaximumFileBytes bytes: $($file.FullName)"}
        $totalBytes += [long]$file.Length;if($totalBytes -gt $MaximumTotalBytes){throw "Steam backup source exceeds $MaximumTotalBytes bytes."}
        $relative=[IO.Path]::GetRelativePath($SourceRoot,$file.FullName).Replace('\','/')
        $files.Add([pscustomobject]@{Path=$relative;Length=[int64]$file.Length;Sha256=Get-WinCareSha256 -LiteralPath $file.FullName;LastWriteTimeUtc=$file.LastWriteTimeUtc.ToString('o');SourcePath=$file.FullName})
    }
    return @($files)
}

function Write-WinCareSteamBackupArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$SourceRoot,
        [string]$UserId='',
        [string]$AppId='',
        [ValidateRange(1,200000)][int]$MaximumFiles=100000,
        [ValidateRange(1,1099511627776)][long]$MaximumTotalBytes=107374182400,
        [ValidateRange(1,1099511627776)][long]$MaximumFileBytes=10737418240
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $source=(Resolve-Path -LiteralPath $SourceRoot -ErrorAction Stop).Path;$destination=[IO.Path]::GetFullPath($Path);$parent=Split-Path -Parent $destination;$null=New-Item -ItemType Directory -Path $parent -Force
    $temp=Join-Path $parent ('.'+[IO.Path]::GetFileName($destination)+'.'+[guid]::NewGuid().ToString('N')+'.tmp')
    $backup=Join-Path $parent ('.'+[IO.Path]::GetFileName($destination)+'.'+[guid]::NewGuid().ToString('N')+'.bak')
    $files=@(Get-WinCareSteamBackupSourceFile -SourceRoot $source -MaximumFiles $MaximumFiles -MaximumTotalBytes $MaximumTotalBytes -MaximumFileBytes $MaximumFileBytes)
    if($files.Count -eq 0){throw 'Steam backup source contains no files.'}
    $manifest=[ordered]@{SchemaVersion=2;UserId=$UserId;AppId=$AppId;CreatedAt=[datetime]::UtcNow.ToString('o');SourceSnapshotHash=Get-WinCareSteamCloudSnapshotHash -Root $source;Files=@($files|ForEach-Object{[ordered]@{Path=$_.Path;Length=$_.Length;Sha256=$_.Sha256;LastWriteTimeUtc=$_.LastWriteTimeUtc}})}
    $hadExisting=Test-Path -LiteralPath $destination
    try{
        $stream=[IO.FileStream]::new($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        try{
            $archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true,[Text.Encoding]::UTF8)
            try{
                $manifestEntry=$archive.CreateEntry('wincare-game-backup.json',[IO.Compression.CompressionLevel]::Optimal);$manifestEntry.LastWriteTime=[datetimeoffset]::UnixEpoch
                $writer=[IO.StreamWriter]::new($manifestEntry.Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write(($manifest|ConvertTo-Json -Depth 50))}finally{$writer.Dispose()}
                foreach($file in $files){
                    $entry=$archive.CreateEntry('payload/'+$file.Path,[IO.Compression.CompressionLevel]::Optimal);$entry.LastWriteTime=[datetimeoffset]::UnixEpoch
                    $input=[IO.File]::OpenRead($file.SourcePath);$output=$entry.Open();try{$input.CopyTo($output)}finally{$output.Dispose();$input.Dispose()}
                }
            }finally{$archive.Dispose()}
            $stream.Flush($true)
        }finally{$stream.Dispose()}
        $verified=Test-WinCareSteamBackupArchive -Path $temp -PassThru
        if(-not $verified.Valid){throw "Steam backup archive verification failed: $($verified.Error)"}
        if($hadExisting){[IO.File]::Replace($temp,$destination,$backup,$true)}else{[IO.File]::Move($temp,$destination)}
        $final=Test-WinCareSteamBackupArchive -Path $destination -PassThru
        if(-not $final.Valid){throw "Committed Steam backup verification failed: $($final.Error)"}
        if(Test-Path -LiteralPath $backup){Remove-Item -LiteralPath $backup -Force -ErrorAction Stop}
        return $true
    }catch{
        $primary=$_.Exception.Message
        if($hadExisting -and (Test-Path -LiteralPath $backup)){
            try{if(Test-Path -LiteralPath $destination){[IO.File]::Replace($backup,$destination,$null,$true)}else{[IO.File]::Move($backup,$destination)}}catch{throw "$primary Existing archive rollback failed: $($_.Exception.Message)"}
        }
        throw $primary
    }finally{
        if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue}
        if(Test-Path -LiteralPath $backup){Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue}
    }
}
function Restore-WinCareSteamBackupArchiveInternal {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$TargetRoot)
    $validation=Test-WinCareSteamBackupArchive -Path $Path -PassThru
    if(-not $validation.Valid){return [pscustomobject]@{Success=$false;Code='SteamBackupInvalid';Message=$validation.Error;ProviderRollback=$false}}
    $target=[IO.Path]::GetFullPath($TargetRoot);$parent=Split-Path -Parent $target;$null=New-Item -ItemType Directory -Path $parent -Force
    $staging=Join-Path $parent ('.'+[IO.Path]::GetFileName($target)+'.wincare-stage-'+[guid]::NewGuid().ToString('N'))
    $tombstone=Join-Path $parent ('.'+[IO.Path]::GetFileName($target)+'.wincare-old-'+[guid]::NewGuid().ToString('N'))
    $hadExisting=Test-Path -LiteralPath $target;$committed=$false
    try{
        $null=New-Item -ItemType Directory -Path $staging -Force
        $archive=[IO.Compression.ZipFile]::OpenRead($validation.Path)
        try{
            foreach($file in @($validation.Manifest.Files)){
                $relative=[string]$file.Path;$entry=$archive.GetEntry('payload/'+$relative);$destination=Join-Path $staging $relative
                $full=[IO.Path]::GetFullPath($destination);if(-not $full.StartsWith(([IO.Path]::GetFullPath($staging)+[IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)){throw "Unsafe Steam restore member: $relative"}
                $null=New-Item -ItemType Directory -Path (Split-Path -Parent $full) -Force
                $input=$entry.Open();$output=[IO.FileStream]::new($full,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,65536,[IO.FileOptions]::WriteThrough)
                try{$input.CopyTo($output);$output.Flush($true)}finally{$output.Dispose();$input.Dispose()}
                $item=Get-Item -LiteralPath $full -Force;if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Restored Steam file is a reparse point: $relative"}
                if([int64]$item.Length -ne [int64]$file.Length){throw "Restored Steam file size mismatch: $relative"}
                if((Get-WinCareSha256 -LiteralPath $full) -ne [string]$file.Sha256){throw "Restored Steam file hash mismatch: $relative"}
            }
        }finally{$archive.Dispose()}
        if($hadExisting){[IO.Directory]::Move($target,$tombstone)}
        [IO.Directory]::Move($staging,$target);$committed=$true
        $after=Get-WinCareSteamCloudSnapshotHash -Root $target
        foreach($file in @($validation.Manifest.Files)){if((Get-WinCareSha256 -LiteralPath (Join-Path $target ([string]$file.Path))) -ne [string]$file.Sha256){throw "Committed Steam restore hash mismatch: $($file.Path)"}}
        if($hadExisting -and (Test-Path -LiteralPath $tombstone)){Remove-Item -LiteralPath $tombstone -Recurse -Force -ErrorAction Stop}
        return [pscustomobject]@{Success=$true;Code='SteamBackupRestored';Message='Steam backup restored and manifest-verified.';AfterSnapshotHash=$after;ProviderRollback=$false}
    }catch{
        $primary=$_.Exception.Message;$rollbackAttempted=$committed -or $hadExisting;$rollbackOk=$true;$rollbackError=''
        try{
            if($committed -and (Test-Path -LiteralPath $target)){Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop}
            if($hadExisting -and (Test-Path -LiteralPath $tombstone)){[IO.Directory]::Move($tombstone,$target)}
        }catch{$rollbackOk=$false;$rollbackError=$_.Exception.Message}
        return [pscustomobject]@{Success=$false;Code=if($rollbackAttempted -and $rollbackOk){'SteamRestoreFailedRolledBack'}elseif($rollbackAttempted){'SteamRestoreRollbackFailed'}else{'SteamRestoreFailed'};Message=if($rollbackError){"$primary Provider rollback failed: $rollbackError"}else{$primary};ProviderRollback=$rollbackAttempted;RollbackSucceeded=$rollbackOk}
    }finally{
        if(Test-Path -LiteralPath $staging){Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue}
        if(Test-Path -LiteralPath $tombstone){Remove-Item -LiteralPath $tombstone -Recurse -Force -ErrorAction SilentlyContinue}
    }
}
function Invoke-WinCareCreateSteamCloudBackupAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$source=(Resolve-Path $p.SourceRoot -ErrorAction Stop).Path;$current=Get-WinCarePathContentHash $source;if($current -ne [string]$p.ExpectedSnapshotHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'SteamCloudChanged' -Message 'Steam Cloud data changed after preview.' -ExitCode 74};try{$success=Write-WinCareSteamBackupArchive -Path $p.Destination -SourceRoot $source -UserId $p.UserId -AppId $p.AppId;New-WinCareBridgeResult -Success $success -Code $(if($success){'SteamBackupCreated'}else{'SteamBackupVerificationFailed'}) -Message $(if($success){'Steam Cloud backup was created and archive-verified.'}else{'Steam backup archive verification failed.'}) -Data @{Path=$p.Destination;Sha256=if($success){(Get-FileHash $p.Destination -Algorithm SHA256).Hash.ToLowerInvariant()}else{$null};SourceSnapshotHash=$current;EvidenceType='VerifiedSteamBackupArchive'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'SteamBackupFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareRemoveGameStateBackupAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;try{$path=(Resolve-Path $p.BackupPath -ErrorAction Stop).Path;if((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$p.ExpectedSha256){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'BackupChanged' -Message 'Backup archive changed after preview.' -ExitCode 74};Remove-Item -LiteralPath $path -Force -ErrorAction Stop;$success=-not (Test-Path $path);New-WinCareBridgeResult -Success $success -Code $(if($success){'GameBackupRemoved'}else{'GameBackupRemovalFailed'}) -Message $(if($success){'Game-state backup was removed.'}else{'Game-state backup remains present.'}) -Data @{Path=$path;EvidenceType='FileAbsence'} -ExitCode $(if($success){0}else{1})}catch{New-WinCareBridgeResult -Success $false -Code 'GameBackupRemovalFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareRestoreSteamCloudBackupAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    if(-not [bool](Get-WinCarePolicy 'AllowGameStateRestore')){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'GameStateRestoreDenied' -Message 'Policy AllowGameStateRestore is disabled.' -ExitCode 5}
    $running=@(Get-WinCareSteamProcess)
    if($running.Count){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'SteamRunning' -Message 'Steam and its helper processes must be closed before restoring cloud state.' -ExitCode 32 -Data @{Processes=$running;EvidenceType='LiveProcessObservation'}}
    $p=Get-WinCareActionParameters $Action
    try{
        $backup=(Resolve-Path -LiteralPath $p.BackupPath -ErrorAction Stop).Path
        if((Get-WinCareSha256 -LiteralPath $backup) -ne [string]$p.BackupSha256){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'BackupChanged' -Message 'Backup archive changed after preview.' -ExitCode 74}
        $target=[IO.Path]::GetFullPath($p.TargetRoot);$current=if(Test-Path -LiteralPath $target){Get-WinCareSteamCloudSnapshotHash -Root $target}else{''}
        if([string]$p.ExpectedCurrentSnapshotHash -and $current -ne [string]$p.ExpectedCurrentSnapshotHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'SteamTargetChanged' -Message 'Steam Cloud target changed after preview.' -ExitCode 74}
        $restored=Restore-WinCareSteamBackupArchiveInternal -Path $backup -TargetRoot $target
        if($restored.Success){return New-WinCareBridgeResult -Success $true -Code 'SteamBackupRestored' -Message $restored.Message -Data @{BackupPath=$backup;TargetRoot=$target;BeforeSnapshotHash=$current;AfterSnapshotHash=$restored.AfterSnapshotHash;ProviderRollback=$restored.ProviderRollback;EvidenceType='VerifiedRestoredFiles'}}
        New-WinCareBridgeResult -Success $false -Code $restored.Code -Message $restored.Message -ExitCode 1 -Data @{BackupPath=$backup;TargetRoot=$target;BeforeSnapshotHash=$current;ProviderRollback=$restored.ProviderRollback;RollbackSucceeded=$restored.RollbackSucceeded;EvidenceType='ProviderRollback'}
    }catch{New-WinCareBridgeResult -Success $false -Code 'SteamRestoreFailed' -Message $_.Exception.Message -ExitCode 1}
}
