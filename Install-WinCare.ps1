#requires -Version 7.2
[CmdletBinding(SupportsShouldProcess,ConfirmImpact='Medium')]
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\WinCare'),
    [switch]$NoStartMenuShortcut,
    [switch]$Force
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'WinCare can only be installed on Windows.'}

function Resolve-SafeInstallPath {
    param([Parameter(Mandatory)][string]$Path)
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
    $root=[IO.Path]::GetPathRoot($full).TrimEnd('\')
    $blocked=@($root,$env:SystemRoot,$env:ProgramFiles,${env:ProgramFiles(x86)},$env:USERPROFILE,$env:LOCALAPPDATA,$env:APPDATA)|Where-Object{$_}|ForEach-Object{[IO.Path]::GetFullPath($_).TrimEnd('\')}
    if($full -in $blocked){throw "Refusing unsafe installation destination: $full"}
    $cursor=$full
    while($cursor -and -not(Test-Path -LiteralPath $cursor)){
        $parent=Split-Path $cursor -Parent
        if([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor){break}
        $cursor=$parent
    }
    while($cursor -and (Test-Path -LiteralPath $cursor)){
        $item=Get-Item -LiteralPath $cursor -Force
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Installation path traverses a reparse point: $cursor"}
        $parent=Split-Path $cursor -Parent;if($parent -eq $cursor){break};$cursor=$parent
    }
    return $full
}

function Get-ManifestEntries {
    param([Parameter(Mandatory)][string]$ManifestPath)
    $entries=[ordered]@{};$case=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($line in Get-Content -LiteralPath $ManifestPath){
        if($line -notmatch '^([0-9a-f]{64})  (.+)$'){throw "Malformed release manifest line: $line"}
        $hash=$Matches[1];$relative=$Matches[2].Replace('/','\')
        if([IO.Path]::IsPathRooted($relative) -or @($relative -split '[\\/]').Contains('..')){throw "Unsafe release-manifest path: $relative"}
        if(-not $case.Add($relative)){throw "Duplicate or case-colliding manifest path: $relative"}
        $entries[$relative]=$hash
    }
    if(-not $entries.Count){throw 'The release manifest is empty.'}
    return $entries
}

function Test-ReleaseTree {
    param([Parameter(Mandatory)][string]$Root,[switch]$AllowInstallMarker)
    $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\');$manifestPath=Join-Path $rootPath 'RELEASE-MANIFEST.sha256'
    $rootItem=Get-Item -LiteralPath $rootPath -Force -ErrorAction Stop
    if($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Release root is a reparse point: $rootPath"}
    if(-not(Test-Path -LiteralPath $manifestPath -PathType Leaf)){throw 'RELEASE-MANIFEST.sha256 is required. Install from a verified release archive.'}
    $entries=Get-ManifestEntries $manifestPath
    foreach($directory in Get-ChildItem -LiteralPath $rootPath -Recurse -Directory -Force){
        if($directory.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Release tree contains a reparse-point directory: $($directory.FullName)"}
    }
    $actual=@(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Force|Where-Object{$_.FullName -ne $manifestPath -and ($AllowInstallMarker -or $_.Name -ne '.wincare-install.json')})
    $actualRelative=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($file in $actual){
        if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Release tree contains a reparse-point file: $($file.FullName)"}
        $relative=[IO.Path]::GetRelativePath($rootPath,$file.FullName)
        if($file.Name -eq '.wincare-install.json' -and $AllowInstallMarker){continue}
        if(-not $actualRelative.Add($relative)){throw "Release tree contains duplicate/case-colliding content: $relative"}
        if(-not $entries.Contains($relative)){throw "Unmanifested release file: $relative"}
        $actualHash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if($actualHash -ne $entries[$relative]){throw "Release file hash mismatch: $relative"}
    }
    foreach($relative in $entries.Keys){if(-not $actualRelative.Contains($relative)){throw "Manifested release file is missing: $relative"}}
    [pscustomobject]@{Verified=$true;ManifestSha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant();Files=$entries.Count}
}

function Test-InstalledIdentity {
    param([Parameter(Mandatory)][string]$Path)
    $markerPath=Join-Path $Path '.wincare-install.json';$modulePath=Join-Path $Path 'src\WinCare\WinCare.psd1'
    if(-not(Test-Path -LiteralPath $markerPath -PathType Leaf) -or -not(Test-Path -LiteralPath $modulePath -PathType Leaf)){throw 'The destination is not a complete marked WinCare installation.'}
    $marker=Get-Content -LiteralPath $markerPath -Raw|ConvertFrom-Json
    $manifest=Import-PowerShellDataFile $modulePath
    if($marker.Product -ne 'WinCare' -or [string]$manifest.GUID -ne 'f43eb775-7d08-42ec-9888-8b1bd79e90a3' -or [string]$marker.ManifestGuid -ne [string]$manifest.GUID){throw 'The existing installation identity is invalid.'}
    $null=Test-ReleaseTree -Root $Path -AllowInstallMarker
    return [pscustomobject]@{Marker=$marker;Manifest=$manifest}
}

$Destination=Resolve-SafeInstallPath $Destination
$sourceRoot=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$separator=[IO.Path]::DirectorySeparatorChar
if($Destination.Equals($sourceRoot,[StringComparison]::OrdinalIgnoreCase) -or $Destination.StartsWith($sourceRoot+$separator,[StringComparison]::OrdinalIgnoreCase) -or $sourceRoot.StartsWith($Destination+$separator,[StringComparison]::OrdinalIgnoreCase)){throw 'Source and installation destination must be separate non-nested directories.'}
$sourceEvidence=Test-ReleaseTree -Root $sourceRoot
if(Test-Path -LiteralPath $Destination){
    if(-not $Force){throw "Destination already exists: $Destination. Use -Force only for a verified WinCare replacement."}
    $null=Test-InstalledIdentity $Destination
}

$parent=Split-Path $Destination -Parent;$leaf=Split-Path $Destination -Leaf
$stage=Join-Path $parent (".$leaf.installing."+[guid]::NewGuid().ToString('N'))
$backup=Join-Path $parent (".$leaf.previous."+[guid]::NewGuid().ToString('N'))
$shortcutPath=Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WinCare.lnk'
$consoleShortcutPath=Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WinCare Console.lnk'
$shortcutBackup=$null;$consoleShortcutBackup=$null;$destinationMoved=$false;$stageCommitted=$false;$commitComplete=$false

if($PSCmdlet.ShouldProcess($Destination,'Install verified WinCare release')){
    try{
        $null=New-Item -ItemType Directory -Path $parent -Force
        $Destination=Resolve-SafeInstallPath $Destination
        $parent=Resolve-SafeInstallPath $parent
        $sourceBytes=[long]((Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Force|Measure-Object -Property Length -Sum).Sum)
        $destinationRoot=[IO.Path]::GetPathRoot($parent)
        try{$destinationDrive=[IO.DriveInfo]::new($destinationRoot)}catch{throw "Destination capacity could not be established: $($_.Exception.Message)"}
        $requiredBytes=[long]($sourceBytes+[Math]::Max(64MB,[Math]::Ceiling($sourceBytes*0.10)))
        if($destinationDrive.AvailableFreeSpace -lt $requiredBytes){throw "Insufficient destination space. Required: $requiredBytes bytes; available: $($destinationDrive.AvailableFreeSpace) bytes."}
        if((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $backup)){throw 'Installer staging or backup path unexpectedly exists.'}
        $null=New-Item -ItemType Directory -Path $stage -Force
        foreach($item in Get-ChildItem -LiteralPath $sourceRoot -Force|Where-Object{$_.Name -notin @('.git','artifacts','.vscode','.idea')}){Copy-Item -LiteralPath $item.FullName -Destination $stage -Recurse -Force}
        $stageEvidence=Test-ReleaseTree -Root $stage
        if($stageEvidence.ManifestSha256 -ne $sourceEvidence.ManifestSha256 -or $stageEvidence.Files -ne $sourceEvidence.Files){throw 'Staged release evidence does not match the verified source release.'}
        $module=Import-PowerShellDataFile (Join-Path $stage 'src\WinCare\WinCare.psd1')
        if([string]$module.GUID -ne 'f43eb775-7d08-42ec-9888-8b1bd79e90a3'){throw 'Staged module identity is invalid.'}
        [ordered]@{SchemaVersion=2;Product='WinCare';Version=[string]$module.ModuleVersion;ManifestGuid=[string]$module.GUID;InstalledAt=[datetime]::UtcNow.ToString('o');ReleaseManifestSha256=$stageEvidence.ManifestSha256;VerifiedRelease=[bool]$stageEvidence.Verified}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $stage '.wincare-install.json') -Encoding utf8
        if(Test-Path -LiteralPath $Destination){Move-Item -LiteralPath $Destination -Destination $backup;$destinationMoved=$true}
        Move-Item -LiteralPath $stage -Destination $Destination;$stageCommitted=$true
        $installed=Test-InstalledIdentity $Destination
        if([string]$installed.Manifest.ModuleVersion -ne [string]$module.ModuleVersion){throw 'Installed version verification failed.'}

        if(-not $NoStartMenuShortcut){
            if(Test-Path -LiteralPath $shortcutPath){$shortcutBackup="$shortcutPath.backup.$([guid]::NewGuid().ToString('N'))";Move-Item -LiteralPath $shortcutPath -Destination $shortcutBackup}
            if(Test-Path -LiteralPath $consoleShortcutPath){$consoleShortcutBackup="$consoleShortcutPath.backup.$([guid]::NewGuid().ToString('N'))";Move-Item -LiteralPath $consoleShortcutPath -Destination $consoleShortcutBackup}
            $pwsh=(Get-Command pwsh.exe -ErrorAction Stop).Source;$shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath=$pwsh;$shortcut.Arguments="-NoLogo -NoProfile -STA -File `"$(Join-Path $Destination 'WinCare-GUI.ps1')`"";$shortcut.WorkingDirectory=$Destination;$shortcut.Description='WinCare graphical Windows operations console';$shortcut.Save()
            $consoleShortcut=$shell.CreateShortcut($consoleShortcutPath);$consoleShortcut.TargetPath=$pwsh;$consoleShortcut.Arguments="-NoLogo -NoProfile -File `"$(Join-Path $Destination 'WinCare.ps1')`"";$consoleShortcut.WorkingDirectory=$Destination;$consoleShortcut.Description='WinCare terminal operations console';$consoleShortcut.Save()
            if(-not(Test-Path -LiteralPath $shortcutPath) -or -not(Test-Path -LiteralPath $consoleShortcutPath)){throw 'Start menu shortcut verification failed.'}
        }
        $commitComplete=$true
    }catch{
        $originalError=$_.Exception.Message
        if(-not $commitComplete){
            $rollbackErrors=[Collections.Generic.List[string]]::new()
            if($stageCommitted -and(Test-Path -LiteralPath $Destination)){try{Remove-Item -LiteralPath $Destination -Recurse -Force -ErrorAction Stop}catch{$rollbackErrors.Add("remove staged installation: $($_.Exception.Message)")}}
            if($destinationMoved -and(Test-Path -LiteralPath $backup) -and -not(Test-Path -LiteralPath $Destination)){try{Move-Item -LiteralPath $backup -Destination $Destination -ErrorAction Stop}catch{$rollbackErrors.Add("restore previous installation: $($_.Exception.Message)")}}
            if($consoleShortcutBackup -and(Test-Path -LiteralPath $consoleShortcutBackup)){
                try{if(Test-Path -LiteralPath $consoleShortcutPath){Remove-Item -LiteralPath $consoleShortcutPath -Force -ErrorAction Stop};Move-Item -LiteralPath $consoleShortcutBackup -Destination $consoleShortcutPath -ErrorAction Stop}catch{$rollbackErrors.Add("restore previous console shortcut: $($_.Exception.Message)")}
            }elseif(Test-Path -LiteralPath $consoleShortcutPath){try{Remove-Item -LiteralPath $consoleShortcutPath -Force -ErrorAction Stop}catch{$rollbackErrors.Add("remove new console shortcut: $($_.Exception.Message)")}}
            if($shortcutBackup -and(Test-Path -LiteralPath $shortcutBackup)){
                try{if(Test-Path -LiteralPath $shortcutPath){Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction Stop};Move-Item -LiteralPath $shortcutBackup -Destination $shortcutPath -ErrorAction Stop}catch{$rollbackErrors.Add("restore previous shortcut: $($_.Exception.Message)")}
            }elseif(Test-Path -LiteralPath $shortcutPath){try{Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction Stop}catch{$rollbackErrors.Add("remove new GUI shortcut: $($_.Exception.Message)")}}
            if($rollbackErrors.Count){throw "Installation failed and rollback was incomplete. Original error: $originalError. Rollback error(s): $($rollbackErrors -join '; ')"}
        }
        throw
    }finally{
        if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue}
        if((Test-Path -LiteralPath $backup) -and -not $destinationMoved){Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue}
    }
    if($commitComplete){
        $cleanupWarnings=[Collections.Generic.List[string]]::new()
        if(Test-Path -LiteralPath $backup){try{Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction Stop}catch{$cleanupWarnings.Add("previous installation remains at '$backup': $($_.Exception.Message)")}}
        if($shortcutBackup -and(Test-Path -LiteralPath $shortcutBackup)){try{Remove-Item -LiteralPath $shortcutBackup -Force -ErrorAction Stop}catch{$cleanupWarnings.Add("previous shortcut backup remains at '$shortcutBackup': $($_.Exception.Message)")}}
        if($consoleShortcutBackup -and(Test-Path -LiteralPath $consoleShortcutBackup)){try{Remove-Item -LiteralPath $consoleShortcutBackup -Force -ErrorAction Stop}catch{$cleanupWarnings.Add("previous console shortcut backup remains at '$consoleShortcutBackup': $($_.Exception.Message)")}}
        Write-Host "WinCare $($module.ModuleVersion) installed to $Destination" -ForegroundColor Green
        foreach($warning in $cleanupWarnings){Write-Warning "Installation committed, but cleanup was incomplete: $warning"}
    }
}
