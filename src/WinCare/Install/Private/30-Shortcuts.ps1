function Get-WinCareShortcutRoot {param([string]$ShortcutRoot)if($ShortcutRoot){Assert-WinCareManagedPath $ShortcutRoot}else{Assert-WinCareManagedPath (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs')}}
function Test-WinCareShortcutIdentity {
    [CmdletBinding()]
    param([string]$ShortcutPath,[string]$ExpectedTarget,[AllowEmptyString()][string]$ExpectedArguments='',[string]$ExpectedWorkingDirectory)
    if(!(Test-Path -LiteralPath $ShortcutPath -PathType Leaf)){return $false};$shell=$null;$shortcut=$null
    try{$shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($ShortcutPath);([IO.Path]::GetFullPath($shortcut.TargetPath) -eq [IO.Path]::GetFullPath($ExpectedTarget)) -and ([string]$shortcut.Arguments -eq $ExpectedArguments) -and ([IO.Path]::GetFullPath($shortcut.WorkingDirectory) -eq [IO.Path]::GetFullPath($ExpectedWorkingDirectory))}catch{$false}finally{if($shortcut){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)};if($shell){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}}
}
function Get-WinCareShortcutFolder {param([string]$ShortcutRoot)Join-Path (Get-WinCareShortcutRoot $ShortcutRoot) 'WinCare'}
function Read-WinCareShortcutOwner {
    param([Parameter(Mandatory)][string]$Folder)
    $ownerPath=Join-Path $Folder '.wincare-shortcuts.json';if(!(Test-Path -LiteralPath $ownerPath -PathType Leaf)){throw 'Existing WinCare shortcut folder is not managed by this installer.'};$owner=Read-WinCareJson $ownerPath 65536;if([int]$owner.SchemaVersion -ne 1 -or $owner.ProductGuid -ne $script:ProductGuid){throw 'Existing shortcut folder has another owner.'};$owner
}
function Move-WinCareShortcutFolderToBackup {
    param([string]$ShortcutRoot)
    $folder=Get-WinCareShortcutFolder $ShortcutRoot;if(!(Test-Path -LiteralPath $folder)){return [pscustomobject]@{Folder=$folder;Backup=$null}};[void](Read-WinCareShortcutOwner $folder);$backup=Join-Path (Split-Path -LiteralPath $folder -Parent) ('.WinCare.shortcuts.backup-'+[guid]::NewGuid().ToString('N'));Move-Item -LiteralPath $folder -Destination $backup -ErrorAction Stop;[pscustomobject]@{Folder=$folder;Backup=$backup}
}
function Restore-WinCareShortcutBackup {
    param([Parameter(Mandatory)]$Transaction)
    if(Test-Path -LiteralPath $Transaction.Folder){Remove-WinCareTree $Transaction.Folder};if($Transaction.Backup -and (Test-Path -LiteralPath $Transaction.Backup)){Move-Item -LiteralPath $Transaction.Backup -Destination $Transaction.Folder -ErrorAction Stop}
}
function Install-WinCareShortcuts {
    param([string]$Destination,[guid]$InstallationId,[string]$ShortcutRoot)
    $folder=Get-WinCareShortcutFolder $ShortcutRoot;if(Test-Path -LiteralPath $folder){throw 'Shortcut transaction did not isolate the existing WinCare shortcut folder.'};[void][IO.Directory]::CreateDirectory($folder)
    $records=@([ordered]@{Name='WinCare.lnk';Target=Join-Path $Destination 'WinCare-GUI.exe';Arguments = '';WorkingDirectory = $Destination},[ordered]@{Name='WinCare Console.lnk';Target=Join-Path $Destination 'WinCare-TUI.exe';Arguments = '';WorkingDirectory = $Destination});$shell=$null
    try{$shell=New-Object -ComObject WScript.Shell;foreach($r in $records){$shortcut=$null;try{$shortcut=$shell.CreateShortcut((Join-Path $folder $r.Name));$shortcut.TargetPath=$r.Target;$shortcut.Arguments=$r.Arguments;$shortcut.WorkingDirectory=$r.WorkingDirectory;$shortcut.Save()}finally{if($shortcut){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)}}}}finally{if($shell){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}}
    foreach($r in $records){if(!(Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $r.Arguments $r.WorkingDirectory)){throw "Created shortcut failed identity verification: $($r.Name)"}}
    Write-WinCareJson (Join-Path $folder '.wincare-shortcuts.json') ([ordered]@{SchemaVersion=1;ProductGuid=$script:ProductGuid;InstallationId=$InstallationId.ToString();DestinationPathSha256=Get-WinCareCanonicalPathHash $Destination;Shortcuts=$records});$folder
}
function Remove-WinCareShortcuts {
    param($Marker,[string]$Destination,[string]$ShortcutRoot)
    if(!$Marker -or !$Marker.ShortcutFolder){return};$folder=[IO.Path]::GetFullPath([string]$Marker.ShortcutFolder);$base=[IO.Path]::GetFullPath((Get-WinCareShortcutRoot $ShortcutRoot)).TrimEnd('\')+'\';if(!$folder.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){return};$owner=Read-WinCareShortcutOwner $folder;if($owner.InstallationId -ne $Marker.InstallationId -or $owner.DestinationPathSha256 -ne (Get-WinCareCanonicalPathHash $Destination)){return};foreach($r in $owner.Shortcuts){$arguments=if($null -eq $r.Arguments){''}else{[string]$r.Arguments};$working=if($r.WorkingDirectory){[string]$r.WorkingDirectory}else{$Destination};if(!(Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $arguments $working)){return}};Remove-WinCareTree $folder
}
function Move-WinCareOwnedShortcutsToBackup {
    param($Marker,[string]$Destination,[string]$ShortcutRoot)
    if(!$Marker -or !$Marker.ShortcutFolder){return $null}
    $folder=[IO.Path]::GetFullPath([string]$Marker.ShortcutFolder);$expected=[IO.Path]::GetFullPath((Get-WinCareShortcutFolder $ShortcutRoot));if($folder -ne $expected){throw 'Managed shortcut folder does not match the configured shortcut root.'}
    $owner=Read-WinCareShortcutOwner $folder;if($owner.InstallationId -ne $Marker.InstallationId -or $owner.DestinationPathSha256 -ne (Get-WinCareCanonicalPathHash $Destination)){throw 'Managed shortcut ownership does not match the installation marker.'}
    foreach($r in $owner.Shortcuts){$arguments=if($null -eq $r.Arguments){''}else{[string]$r.Arguments};$working=if($r.WorkingDirectory){[string]$r.WorkingDirectory}else{$Destination};if(!(Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $arguments $working)){throw "Managed shortcut identity mismatch: $($r.Name)"}}
    $backup=Join-Path (Split-Path -LiteralPath $folder -Parent) ('.WinCare.shortcuts.remove-'+[guid]::NewGuid().ToString('N'));Move-Item -LiteralPath $folder -Destination $backup -ErrorAction Stop;[pscustomobject]@{Folder=$folder;Backup=$backup}
}
