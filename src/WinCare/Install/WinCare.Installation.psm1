Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$script:ProductGuid='f43eb775-7d08-42ec-9888-8b1bd79e90a3'
$script:ReceiptSchemas=@('wincare.build.receipt/v2','wincare.build.receipt/v3')
$script:MarkerSchema=5

function Get-WinCareCanonicalPathHash {
    param([Parameter(Mandatory)][string]$Path)
    $canonical=[IO.Path]::GetFullPath($Path).TrimEnd('\').ToLowerInvariant()
    $bytes=[Text.Encoding]::UTF8.GetBytes($canonical)
    try{[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()}finally{[Array]::Clear($bytes,0,$bytes.Length)}
}
function Assert-WinCareManagedPath {
    param([Parameter(Mandatory)][string]$Path)
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
    $root=[IO.Path]::GetPathRoot($full).TrimEnd('\')
    $blocked=@($root,$env:SystemRoot,$env:ProgramFiles,${env:ProgramFiles(x86)},$env:USERPROFILE,$env:LOCALAPPDATA,$env:APPDATA)|Where-Object{$_}|ForEach-Object{[IO.Path]::GetFullPath($_).TrimEnd('\')}
    if($full -in $blocked){throw "Refusing unsafe WinCare path: $full"}
    $cursor=$full
    while($cursor -and -not(Test-Path -LiteralPath $cursor)){ $parent=Split-Path $cursor -Parent;if(!$parent -or $parent -eq $cursor){break};$cursor=$parent }
    while($cursor -and (Test-Path -LiteralPath $cursor)){ $item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop;if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "WinCare path traverses a reparse point: $cursor"};$parent=Split-Path $cursor -Parent;if(!$parent -or $parent -eq $cursor){break};$cursor=$parent }
    $full
}
function Get-WinCareFileHash {
    param([Parameter(Mandatory)][string]$Path,[long]$MaximumBytes=2147483648)
    $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -lt 1 -or $item.Length -gt $MaximumBytes){throw "Unsafe hash input: $Path"}
    $beforeLength=[long]$item.Length;$beforeTicks=$item.LastWriteTimeUtc.Ticks
    $stream=[IO.File]::Open($item.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$digest=$sha.ComputeHash($stream);$hex=[Convert]::ToHexString($digest).ToLowerInvariant();[Array]::Clear($digest,0,$digest.Length)}finally{$sha.Dispose();$stream.Dispose()}
    $after=Get-Item -LiteralPath $item.FullName -Force
    if($after.Length -ne $beforeLength -or $after.LastWriteTimeUtc.Ticks -ne $beforeTicks){throw "File changed while hashing: $Path"}
    [pscustomobject]@{Sha256=$hex;Bytes=$beforeLength}
}
function Read-WinCareJson {
    param([Parameter(Mandatory)][string]$Path,[int]$MaximumBytes=4194304)
    $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -gt $MaximumBytes){throw "Unsafe metadata file: $Path"}
    $stream=[IO.File]::Open($item.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $reader=[IO.StreamReader]::new($stream,[Text.UTF8Encoding]::new($false,$true),$true,65536,$false)
    try{$text=$reader.ReadToEnd();$text|ConvertFrom-Json -Depth 50 -ErrorAction Stop}finally{$reader.Dispose();$stream.Dispose()}
}
function Write-WinCareJson {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)]$Value)
    $temp="$Path.tmp-$([guid]::NewGuid().ToString('N'))";$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value|ConvertTo-Json -Depth 30)+[Environment]::NewLine)
    try{$stream=[IO.FileStream]::new($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,65536,[IO.FileOptions]::WriteThrough);try{$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()};Move-Item -LiteralPath $temp -Destination $Path -Force}catch{Remove-Item $temp -Force -ErrorAction SilentlyContinue;throw}finally{[Array]::Clear($bytes,0,$bytes.Length)}
}
function Get-WinCareManifest {
    param([string]$Path)
    $entries=[ordered]@{};$folded=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($line in [IO.File]::ReadLines($Path,[Text.Encoding]::ASCII)){
        if(!$line){continue};if($line -notmatch '^([0-9a-f]{64})  (.+)$'){throw "Malformed release manifest: $line"}
        $relative=$Matches[2].Replace('/','\');if([IO.Path]::IsPathRooted($relative) -or ($relative -split '[\\/]' -contains '..') -or !$folded.Add($relative)){throw "Unsafe or duplicate manifest path: $relative"};$entries[$relative]=$Matches[1]
    }
    if(!$entries.Count){throw 'The release manifest is empty.'};$entries
}
function Test-WinCareManagedReleaseTree {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Root,[switch]$AllowInstallMarker)
    $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\');$rootItem=Get-Item $rootPath -Force
    if(!$rootItem.PSIsContainer -or ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unsafe release root.'}
    $manifestPath=Join-Path $rootPath 'RELEASE-MANIFEST.sha256';$entries=Get-WinCareManifest $manifestPath
    $actual=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$count=0;$total=0L
    foreach($item in Get-ChildItem $rootPath -Recurse -Force){$count++;if($count -gt 10000){throw 'Release tree exceeds entry ceiling.'};if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Release tree contains a reparse point: $($item.FullName)"};if($item.PSIsContainer){continue};$rel=[IO.Path]::GetRelativePath($rootPath,$item.FullName);if($rel -eq 'RELEASE-MANIFEST.sha256' -or ($AllowInstallMarker -and $rel -eq '.wincare-install.json')){continue};$total+=$item.Length;if($total -gt 2147483648){throw 'Release tree exceeds byte ceiling.'};if(!$entries.Contains($rel) -or !$actual.Add($rel)){throw "Unmanifested or duplicate file: $rel"};if((Get-WinCareFileHash $item.FullName).Sha256 -ne $entries[$rel]){throw "Release hash mismatch: $rel"}}
    foreach($rel in $entries.Keys){if(!$actual.Contains($rel)){throw "Manifested file missing: $rel"}}
    [pscustomobject]@{Verified=$true;Files=$entries.Count;ManifestSha256=(Get-WinCareFileHash $manifestPath).Sha256;Root=$rootPath}
}
function Get-WinCareReleaseIdentity {
    param([string]$Root)
    $tree=Test-WinCareManagedReleaseTree $Root;$receiptPath=Join-Path $Root 'BUILD-RECEIPT.json';$receipt=Read-WinCareJson $receiptPath 4194304
    if($receipt.schema -notin $script:ReceiptSchemas -or $receipt.packageProfile -ne 'production' -or [bool]$receipt.source.dirty -or $receipt.native.status -ne 'source-built-and-verified'){throw 'Release receipt is not installable.'}
    $module=Import-PowerShellDataFile (Join-Path $Root 'src\WinCare\WinCare.psd1');if([string]$module.GUID -ne $script:ProductGuid -or [string]$module.ModuleVersion -ne [string]$receipt.version){throw 'Release identity mismatch.'}
    [pscustomobject]@{Tree=$tree;Receipt=$receipt;ReceiptSha256=(Get-WinCareFileHash $receiptPath).Sha256;Module=$module}
}
function Read-WinCareInstallMarker {
    param([string]$Destination,[switch]$SkipPathBinding)
    $marker=Read-WinCareJson (Join-Path $Destination '.wincare-install.json') 65536
    if([int]$marker.SchemaVersion -ne $script:MarkerSchema -or $marker.ProductGuid -ne $script:ProductGuid -or $marker.ManifestGuid -ne $script:ProductGuid -or [string]$marker.InstallationId -notmatch '^[0-9a-fA-F-]{36}$' -or [string]$marker.ReleaseManifestSha256 -notmatch '^[0-9a-f]{64}$' -or [string]$marker.BuildReceiptSha256 -notmatch '^[0-9a-f]{64}$'){throw 'Invalid WinCare installation marker.'}
    if(!$SkipPathBinding -and $marker.DestinationPathSha256 -ne (Get-WinCareCanonicalPathHash $Destination)){throw 'Installation marker is bound to another path.'};$marker
}
function Remove-WinCareTree {
    param([string]$Root)
    $rootPath=Assert-WinCareManagedPath $Root;if(!(Test-Path $rootPath)){return};$rootItem=Get-Item $rootPath -Force;if(!$rootItem.PSIsContainer -or ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unsafe deletion root.'}
    $stack=[Collections.Generic.Stack[object]]::new();$stack.Push([pscustomobject]@{P=$rootPath;V=$false});$count=0
    while($stack.Count){$frame=$stack.Pop();if($frame.V){[IO.Directory]::Delete($frame.P,$false);continue};$stack.Push([pscustomobject]@{P=$frame.P;V=$true});foreach($child in Get-ChildItem $frame.P -Force){$count++;if($count -gt 30000){throw 'Deletion entry ceiling exceeded.'};if($child.Attributes -band [IO.FileAttributes]::ReparsePoint){if($child.PSIsContainer){[IO.Directory]::Delete($child.FullName,$false)}else{[IO.File]::Delete($child.FullName)}}elseif($child.PSIsContainer){$stack.Push([pscustomobject]@{P=$child.FullName;V=$false})}else{[IO.File]::SetAttributes($child.FullName,[IO.FileAttributes]::Normal);[IO.File]::Delete($child.FullName)}}}
}
function Get-WinCareShortcutRoot { param([string]$ShortcutRoot) if($ShortcutRoot){[IO.Path]::GetFullPath($ShortcutRoot)}else{Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'} }
function Test-WinCareShortcutIdentity {
    [CmdletBinding()]
    param([string]$ShortcutPath,[string]$ExpectedTarget,[string]$ExpectedArguments,[string]$ExpectedWorkingDirectory)
    if(!(Test-Path $ShortcutPath -PathType Leaf)){return $false};$shell=$null;$shortcut=$null
    try{$shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($ShortcutPath);([IO.Path]::GetFullPath($shortcut.TargetPath) -eq [IO.Path]::GetFullPath($ExpectedTarget)) -and ([string]$shortcut.Arguments -eq $ExpectedArguments) -and ([IO.Path]::GetFullPath($shortcut.WorkingDirectory) -eq [IO.Path]::GetFullPath($ExpectedWorkingDirectory))}catch{$false}finally{if($shortcut){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)};if($shell){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}}
}
function Install-WinCareShortcuts {
    param([string]$Destination,[guid]$InstallationId,[string]$ShortcutRoot)
    $folder=Join-Path (Get-WinCareShortcutRoot $ShortcutRoot) 'WinCare';$ownerPath=Join-Path $folder '.wincare-shortcuts.json'
    if(Test-Path $folder){if(!(Test-Path $ownerPath -PathType Leaf)){throw 'Existing WinCare shortcut folder is not managed by this installer.'};$owner=Read-WinCareJson $ownerPath 65536;if($owner.ProductGuid -ne $script:ProductGuid){throw 'Existing shortcut folder has another owner.'}}else{New-Item -ItemType Directory $folder -Force|Out-Null}
    $records=@(@{Name='WinCare.lnk';Target=Join-Path $Destination 'WinCare-GUI.exe'},@{Name='WinCare Console.lnk';Target=Join-Path $Destination 'WinCare-TUI.exe'});$shell=$null
    try{$shell=New-Object -ComObject WScript.Shell;foreach($r in $records){$shortcut=$null;try{$shortcut=$shell.CreateShortcut((Join-Path $folder $r.Name));$shortcut.TargetPath=$r.Target;$shortcut.Arguments='';$shortcut.WorkingDirectory=$Destination;$shortcut.Save()}finally{if($shortcut){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)}}}}finally{if($shell){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}}
    Write-WinCareJson $ownerPath ([ordered]@{SchemaVersion=1;ProductGuid=$script:ProductGuid;InstallationId=$InstallationId.ToString();DestinationPathSha256=Get-WinCareCanonicalPathHash $Destination;Shortcuts=$records});$folder
}
function Remove-WinCareShortcuts {
    param($Marker,[string]$Destination,[string]$ShortcutRoot)
    if(!$Marker.ShortcutFolder){return};$folder=[IO.Path]::GetFullPath([string]$Marker.ShortcutFolder);$base=[IO.Path]::GetFullPath((Get-WinCareShortcutRoot $ShortcutRoot)).TrimEnd('\')+'\';if(!$folder.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){return};$ownerPath=Join-Path $folder '.wincare-shortcuts.json';if(!(Test-Path $ownerPath)){return};$owner=Read-WinCareJson $ownerPath 65536;if($owner.ProductGuid -ne $script:ProductGuid -or $owner.InstallationId -ne $Marker.InstallationId -or $owner.DestinationPathSha256 -ne (Get-WinCareCanonicalPathHash $Destination)){return};foreach($r in $owner.Shortcuts){if(!(Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $r.Arguments $Destination)){return}};Remove-WinCareTree $folder
}
function Install-WinCarePackage {
    [CmdletBinding(SupportsShouldProcess,ConfirmImpact='Medium')]
    param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$Destination,[switch]$NoStartMenuShortcut,[switch]$Force,[switch]$Repair,[string]$ShortcutRoot)
    if($Force -and $Repair){throw 'Choose either -Force or -Repair.'};$source=Get-WinCareReleaseIdentity ([IO.Path]::GetFullPath($SourceRoot));$dest=Assert-WinCareManagedPath $Destination;$parent=Split-Path $dest -Parent;New-Item -ItemType Directory $parent -Force|Out-Null;$id=[guid]::NewGuid();$operation='install'
    if(Test-Path $dest){if(!$Force -and !$Repair){throw 'Destination exists. Use -Force or -Repair.'};$old=Read-WinCareInstallMarker $dest;$id=[guid]$old.InstallationId;if($Repair){$operation='repair'}else{Test-WinCareManagedReleaseTree $dest -AllowInstallMarker|Out-Null;$operation='replace'}}
    if(!$PSCmdlet.ShouldProcess($dest,"$operation WinCare")){return};$stage=Join-Path $parent ('.WinCare.stage-'+[guid]::NewGuid().ToString('N'));$backup=Join-Path $parent ('.WinCare.backup-'+[guid]::NewGuid().ToString('N'));$moved=$false;$committed=$false
    try{Copy-Item $source.Tree.Root $stage -Recurse -Force; $stageTree=Test-WinCareManagedReleaseTree $stage;$marker=[ordered]@{SchemaVersion=$script:MarkerSchema;Product='WinCare';ProductGuid=$script:ProductGuid;ManifestGuid=$script:ProductGuid;InstallationId=$id.ToString();Version=[string]$source.Module.ModuleVersion;DestinationPathSha256=Get-WinCareCanonicalPathHash $dest;ReleaseManifestSha256=$stageTree.ManifestSha256;ManifestFileCount=$stageTree.Files;BuildReceiptSha256=$source.ReceiptSha256;InstalledAt=[datetime]::UtcNow.ToString('o');LastOperation=$operation;ShortcutFolder=$null};Write-WinCareJson (Join-Path $stage '.wincare-install.json') $marker;if(Test-Path $dest){Move-Item $dest $backup;$moved=$true};Move-Item $stage $dest;$committed=$true;if(!$NoStartMenuShortcut){$marker.ShortcutFolder=Install-WinCareShortcuts $dest $id $ShortcutRoot;Write-WinCareJson (Join-Path $dest '.wincare-install.json') $marker};Test-WinCareManagedReleaseTree $dest -AllowInstallMarker|Out-Null;if($moved){Remove-WinCareTree $backup};[pscustomobject]@{Status='installed';Operation=$operation;Destination=$dest;InstallationId=$id}}catch{if($committed -and (Test-Path $dest)){try{Remove-WinCareTree $dest}catch{}};if($moved -and (Test-Path $backup) -and !(Test-Path $dest)){Move-Item $backup $dest -ErrorAction SilentlyContinue};throw}finally{if(Test-Path $stage){try{Remove-WinCareTree $stage}catch{}}}
}
function Uninstall-WinCarePackage {
    [CmdletBinding(SupportsShouldProcess,ConfirmImpact='High')]
    param([Parameter(Mandatory)][string]$Destination,[switch]$PurgeData,[switch]$RemoveCorruptInstallation,[string]$ShortcutRoot)
    $dest=Assert-WinCareManagedPath $Destination;if(!(Test-Path $dest)){return [pscustomobject]@{Status='not-installed'}};$marker=Read-WinCareInstallMarker $dest;if(!$RemoveCorruptInstallation){Test-WinCareManagedReleaseTree $dest -AllowInstallMarker|Out-Null};if(!$PSCmdlet.ShouldProcess($dest,'uninstall WinCare')){return};$tomb=Join-Path (Split-Path $dest -Parent) ('.WinCare.remove-'+[guid]::NewGuid().ToString('N'));Move-Item $dest $tomb
    try{$check=Read-WinCareInstallMarker $tomb -SkipPathBinding;if($check.InstallationId -ne $marker.InstallationId){throw 'Tombstone identity changed.'};if(!$RemoveCorruptInstallation){Test-WinCareManagedReleaseTree $tomb -AllowInstallMarker|Out-Null};Remove-WinCareShortcuts $marker $dest $ShortcutRoot;Remove-WinCareTree $tomb}catch{if((Test-Path $tomb) -and !(Test-Path $dest)){Move-Item $tomb $dest -ErrorAction SilentlyContinue};throw}
    if($PurgeData){$data=Join-Path $env:LOCALAPPDATA 'WinCare';if(Test-Path $data){$identity=Read-WinCareJson (Join-Path $data '.wincare-data.json') 65536;if($identity.Product -ne 'WinCare' -or $identity.ManifestGuid -ne $script:ProductGuid -or $identity.RootSha256 -ne (Get-WinCareCanonicalPathHash $data)){throw 'WinCare data identity invalid.'};Remove-WinCareTree $data}}
    [pscustomobject]@{Status='uninstalled';Destination=$dest;RemovedCorruptInstallation=[bool]$RemoveCorruptInstallation;PurgedData=[bool]$PurgeData}
}
Export-ModuleMember -Function Install-WinCarePackage,Uninstall-WinCarePackage,Test-WinCareManagedReleaseTree,Test-WinCareShortcutIdentity
