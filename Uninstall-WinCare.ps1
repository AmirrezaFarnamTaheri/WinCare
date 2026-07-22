#requires -Version 7.2
[CmdletBinding(SupportsShouldProcess,ConfirmImpact='High')]
param(
    [string]$Destination=(Join-Path $env:LOCALAPPDATA 'Programs\WinCare'),
    [switch]$PurgeData
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'WinCare can only be uninstalled on Windows.'}

function Assert-UninstallIdentity {
    param([Parameter(Mandatory)][string]$Path)
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\');$root=[IO.Path]::GetPathRoot($full).TrimEnd('\')
    $blocked=@($root,$env:SystemRoot,$env:ProgramFiles,${env:ProgramFiles(x86)},$env:USERPROFILE,$env:LOCALAPPDATA,$env:APPDATA)|Where-Object{$_}|ForEach-Object{[IO.Path]::GetFullPath($_).TrimEnd('\')}
    if($full -in $blocked){throw "Refusing unsafe uninstall destination: $full"}
    $cursor=$full
    while($cursor -and (Test-Path -LiteralPath $cursor)){
        $ancestor=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
        if($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "The installation path traverses a reparse point: $cursor"}
        $next=Split-Path $cursor -Parent;if([string]::IsNullOrWhiteSpace($next) -or $next -eq $cursor){break};$cursor=$next
    }
    $item=Get-Item -LiteralPath $full -Force -ErrorAction Stop;if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'The installation root is a reparse point.'}
    foreach($child in Get-ChildItem -LiteralPath $full -Recurse -Force -ErrorAction Stop){if($child.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "The installation contains a reparse point and was not removed: $($child.FullName)"}}
    $markerPath=Join-Path $full '.wincare-install.json';$modulePath=Join-Path $full 'src\WinCare\WinCare.psd1'
    if(-not(Test-Path -LiteralPath $markerPath -PathType Leaf) -or -not(Test-Path -LiteralPath $modulePath -PathType Leaf)){throw 'Required WinCare identity files are missing.'}
    $marker=Get-Content -LiteralPath $markerPath -Raw|ConvertFrom-Json;$module=Import-PowerShellDataFile $modulePath
    if($marker.Product -ne 'WinCare' -or [string]$module.GUID -ne 'f43eb775-7d08-42ec-9888-8b1bd79e90a3' -or [string]$marker.ManifestGuid -ne [string]$module.GUID){throw 'The WinCare installation identity could not be verified.'}
    $releaseManifest=Join-Path $full 'RELEASE-MANIFEST.sha256'
    if(-not(Test-Path -LiteralPath $releaseManifest -PathType Leaf)){throw 'The installed release manifest is missing.'}
    $manifestHash=(Get-FileHash -LiteralPath $releaseManifest -Algorithm SHA256).Hash.ToLowerInvariant()
    if([string]$marker.ReleaseManifestSha256 -notmatch '^[a-f0-9]{64}$' -or $manifestHash -ne [string]$marker.ReleaseManifestSha256){throw 'The installed release manifest does not match the installation marker.'}
    return [pscustomobject]@{Path=$full;Marker=$marker;Module=$module}
}

$identity=Assert-UninstallIdentity $Destination;$Destination=$identity.Path
$parent=Split-Path $Destination -Parent;$leaf=Split-Path $Destination -Leaf
$tombstone=Join-Path $parent (".$leaf.removing."+[guid]::NewGuid().ToString('N'))
$dataRoot=Join-Path $env:LOCALAPPDATA 'WinCare';$dataTombstone=Join-Path (Split-Path $dataRoot -Parent) (".WinCare.data.removing."+[guid]::NewGuid().ToString('N'))
$shortcutPath=Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WinCare.lnk';$consoleShortcutPath=Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WinCare Console.lnk';$shortcutBackup=$null;$consoleShortcutBackup=$null
if($PSCmdlet.ShouldProcess($Destination,"Uninstall WinCare $($identity.Module.ModuleVersion)")){
    $movedInstall=$false;$movedData=$false;$commitComplete=$false
    try{
        if(Test-Path -LiteralPath $shortcutPath){$shortcutBackup="$shortcutPath.removing.$([guid]::NewGuid().ToString('N'))";Move-Item -LiteralPath $shortcutPath -Destination $shortcutBackup}
        if(Test-Path -LiteralPath $consoleShortcutPath){$consoleShortcutBackup="$consoleShortcutPath.removing.$([guid]::NewGuid().ToString('N'))";Move-Item -LiteralPath $consoleShortcutPath -Destination $consoleShortcutBackup}
        Move-Item -LiteralPath $Destination -Destination $tombstone;$movedInstall=$true
        if($PurgeData -and(Test-Path -LiteralPath $dataRoot)){
            $dataItem=Get-Item -LiteralPath $dataRoot -Force;if($dataItem.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'WinCare data root is a reparse point and was not purged.'}
            foreach($child in Get-ChildItem -LiteralPath $dataRoot -Recurse -Force -ErrorAction Stop){if($child.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "WinCare data contains a reparse point and was not purged: $($child.FullName)"}}
            Move-Item -LiteralPath $dataRoot -Destination $dataTombstone;$movedData=$true
        }
        $commitComplete=$true
    }catch{
        $originalError=$_.Exception.Message
        if(-not $commitComplete){
            $rollbackErrors=[Collections.Generic.List[string]]::new()
            if($movedData -and(Test-Path -LiteralPath $dataTombstone) -and -not(Test-Path -LiteralPath $dataRoot)){try{Move-Item -LiteralPath $dataTombstone -Destination $dataRoot -ErrorAction Stop}catch{$rollbackErrors.Add("restore data: $($_.Exception.Message)")}}
            if($movedInstall -and(Test-Path -LiteralPath $tombstone) -and -not(Test-Path -LiteralPath $Destination)){try{Move-Item -LiteralPath $tombstone -Destination $Destination -ErrorAction Stop}catch{$rollbackErrors.Add("restore installation: $($_.Exception.Message)")}}
            if($shortcutBackup -and(Test-Path -LiteralPath $shortcutBackup) -and -not(Test-Path -LiteralPath $shortcutPath)){try{Move-Item -LiteralPath $shortcutBackup -Destination $shortcutPath -ErrorAction Stop}catch{$rollbackErrors.Add("restore shortcut: $($_.Exception.Message)")}}
            if($consoleShortcutBackup -and(Test-Path -LiteralPath $consoleShortcutBackup) -and -not(Test-Path -LiteralPath $consoleShortcutPath)){try{Move-Item -LiteralPath $consoleShortcutBackup -Destination $consoleShortcutPath -ErrorAction Stop}catch{$rollbackErrors.Add("restore console shortcut: $($_.Exception.Message)")}}
            if($rollbackErrors.Count){throw "Uninstall staging failed and rollback was incomplete. Original error: $originalError. Rollback error(s): $($rollbackErrors -join '; ')"}
        }
        throw
    }
    if($commitComplete){
        $cleanupWarnings=[Collections.Generic.List[string]]::new()
        if($movedInstall -and(Test-Path -LiteralPath $tombstone)){try{Remove-Item -LiteralPath $tombstone -Recurse -Force -ErrorAction Stop}catch{$cleanupWarnings.Add("installation tombstone remains at '$tombstone': $($_.Exception.Message)")}}
        if($movedData -and(Test-Path -LiteralPath $dataTombstone)){try{Remove-Item -LiteralPath $dataTombstone -Recurse -Force -ErrorAction Stop}catch{$cleanupWarnings.Add("data tombstone remains at '$dataTombstone': $($_.Exception.Message)")}}
        if($shortcutBackup -and(Test-Path -LiteralPath $shortcutBackup)){try{Remove-Item -LiteralPath $shortcutBackup -Force -ErrorAction Stop}catch{$cleanupWarnings.Add("shortcut tombstone remains at '$shortcutBackup': $($_.Exception.Message)")}}
        if($consoleShortcutBackup -and(Test-Path -LiteralPath $consoleShortcutBackup)){try{Remove-Item -LiteralPath $consoleShortcutBackup -Force -ErrorAction Stop}catch{$cleanupWarnings.Add("console shortcut tombstone remains at '$consoleShortcutBackup': $($_.Exception.Message)")}}
        Write-Host 'WinCare was uninstalled.' -ForegroundColor Green
        if(-not $PurgeData){Write-Host 'Settings, reports, logs, extensions, and journals were preserved.'}
        foreach($warning in $cleanupWarnings){Write-Warning "Uninstall committed, but cleanup was incomplete: $warning"}
    }
}
