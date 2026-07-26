#requires -Version 7.2
[CmdletBinding(SupportsShouldProcess,ConfirmImpact='High')]
param(
    [string]$Destination=(Join-Path $env:LOCALAPPDATA 'Programs\WinCare'),
    [switch]$PurgeData
)

Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $IsWindows) { throw 'WinCare can only be uninstalled on Windows.' }

function Get-WinCareUninstallerCanonicalPathHash {
    param([Parameter(Mandatory)][string]$Path)
    $canonical=[IO.Path]::GetFullPath($Path).TrimEnd('\').ToLowerInvariant()
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
}

function Get-WinCareUninstallerFileSha256 {
    param([Parameter(Mandatory)][string]$LiteralPath,[ValidateRange(1,2147483648)][long]$MaximumBytes=1073741824)
    $item=Get-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Expected a regular non-reparse file: $LiteralPath" }
    if([long]$item.Length -lt 1 -or [long]$item.Length -gt $MaximumBytes) { throw "File size is outside the allowed range: $LiteralPath" }
    $stream=[IO.FileStream]::new($item.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,1048576,[IO.FileOptions]::SequentialScan)
    $algorithm=[Security.Cryptography.SHA256]::Create()
    try {
        $digest=$algorithm.ComputeHash($stream)
        $after=Get-Item -LiteralPath $item.FullName -Force -ErrorAction Stop
        if($after.PSIsContainer -or ($after.Attributes -band [IO.FileAttributes]::ReparsePoint) -or [long]$after.Length -ne [long]$item.Length -or $after.LastWriteTimeUtc -ne $item.LastWriteTimeUtc) { throw "File changed while hashing: $LiteralPath" }
        try { return [Convert]::ToHexString($digest).ToLowerInvariant() }
        finally { if($digest.Length){[Array]::Clear($digest,0,$digest.Length)} }
    } finally { $algorithm.Dispose();$stream.Dispose() }
}

function Read-WinCareUninstallerBoundedUtf8Text {
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,16777216)][long]$MaximumBytes=4194304
    )
    $item=Get-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Expected a regular file: $LiteralPath" }
    if([long]$item.Length -gt $MaximumBytes) { throw "File exceeds the $MaximumBytes-byte limit: $LiteralPath" }
    $stream=[IO.FileStream]::new($item.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,65536,[IO.FileOptions]::SequentialScan)
    $memory=[IO.MemoryStream]::new([int]$item.Length)
    $buffer=[byte[]]::new(65536)
    try {
        while(($read=$stream.Read($buffer,0,$buffer.Length)) -gt 0) {
            if($memory.Length+$read -gt $MaximumBytes) { throw "File grew beyond the $MaximumBytes-byte limit: $LiteralPath" }
            $memory.Write($buffer,0,$read)
        }
        $bytes=$memory.ToArray()
        try { return [Text.UTF8Encoding]::new($false,$true).GetString($bytes) }
        finally { if($bytes.Length){[Array]::Clear($bytes,0,$bytes.Length)} }
    } finally {
        [Array]::Clear($buffer,0,$buffer.Length)
        $memory.Dispose()
        $stream.Dispose()
    }
}

function Assert-SafeRemovalPath {
    param([Parameter(Mandatory)][string]$Path)
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
    $root=[IO.Path]::GetPathRoot($full).TrimEnd('\')
    $blocked=@(
        $root,$env:SystemRoot,$env:ProgramFiles,${env:ProgramFiles(x86)},
        $env:USERPROFILE,$env:LOCALAPPDATA,$env:APPDATA
    ) | Where-Object { $_ } | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') }
    if($full -in $blocked) { throw "Refusing unsafe uninstall destination: $full" }
    $cursor=$full
    while($cursor -and (Test-Path -LiteralPath $cursor)) {
        $ancestor=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
        if($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Removal path traverses a reparse point: $cursor" }
        $next=Split-Path $cursor -Parent
        if([string]::IsNullOrWhiteSpace($next) -or $next -eq $cursor) { break }
        $cursor=$next
    }
    return $full
}

function Get-ReleaseManifestEntries {
    param([Parameter(Mandatory)][string]$ManifestPath)
    $entries=[ordered]@{}
    $case=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $text=Read-WinCareUninstallerBoundedUtf8Text -LiteralPath $ManifestPath -MaximumBytes 4194304
    foreach($line in $text -split "`r?`n") {
        if([string]::IsNullOrWhiteSpace($line)) { continue }
        if($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Malformed release manifest line: $line" }
        $relative=$Matches[2].Replace('/','\')
        if([IO.Path]::IsPathRooted($relative) -or @($relative -split '[\\/]').Contains('..')) { throw "Unsafe release-manifest path: $relative" }
        if(-not $case.Add($relative)) { throw "Duplicate or case-colliding manifest path: $relative" }
        if($entries.Count -ge 5000) { throw 'Release manifest exceeds 5000 entries.' }
        $entries[$relative]=$Matches[1]
    }
    if(-not $entries.Count) { throw 'The installed release manifest is empty.' }
    return $entries
}

function Test-InstalledReleaseTree {
    param([Parameter(Mandatory)][string]$Root)
    $rootPath=Assert-SafeRemovalPath $Root
    $manifestPath=Join-Path $rootPath 'RELEASE-MANIFEST.sha256'
    if(-not(Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'The installed release manifest is missing.' }
    $entries=Get-ReleaseManifestEntries -ManifestPath $manifestPath
    $actual=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $entryCount=0
    $totalBytes=0L
    foreach($item in Get-ChildItem -LiteralPath $rootPath -Recurse -Force -ErrorAction Stop) {
        $entryCount++
        if($entryCount -gt 10000) { throw 'The installation tree exceeds the 10000-entry verification ceiling.' }
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "The installation contains a reparse point: $($item.FullName)" }
        if($item.PSIsContainer) { continue }
        $relative=[IO.Path]::GetRelativePath($rootPath,$item.FullName)
        if($relative -in @('RELEASE-MANIFEST.sha256','.wincare-install.json')) { continue }
        if(-not $actual.Add($relative)) { throw "Duplicate or case-colliding installed path: $relative" }
        if(-not $entries.Contains($relative)) { throw "Unmanifested installed file: $relative" }
        if([long]$item.Length -gt 536870912L) { throw "Installed file exceeds the 512 MiB verification ceiling: $relative" }
        $totalBytes+=[long]$item.Length
        if($totalBytes -gt 2147483648L) { throw 'The installation exceeds the 2 GiB verification ceiling.' }
        $hash=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if($hash -ne [string]$entries[$relative]) { throw "Installed file hash mismatch: $relative" }
    }
    foreach($relative in $entries.Keys) {
        if(-not $actual.Contains([string]$relative)) { throw "Manifested installed file is missing: $relative" }
    }
    [pscustomobject]@{
        ManifestSha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Files=$entries.Count
        Bytes=$totalBytes
    }
}

function Assert-UninstallIdentity {
    param([Parameter(Mandatory)][string]$Path)
    $full=Assert-SafeRemovalPath $Path
    $markerPath=Join-Path $full '.wincare-install.json'
    $modulePath=Join-Path $full 'src\WinCare\WinCare.psd1'
    $receiptPath=Join-Path $full 'BUILD-RECEIPT.json'
    foreach($required in @($markerPath,$modulePath,$receiptPath)) {
        if(-not(Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required WinCare identity file is missing: $required" }
    }
    $evidence=Test-InstalledReleaseTree -Root $full
    $marker=Read-WinCareUninstallerBoundedUtf8Text -LiteralPath $markerPath -MaximumBytes 65536 | ConvertFrom-Json -Depth 20
    $receipt=Read-WinCareUninstallerBoundedUtf8Text -LiteralPath $receiptPath -MaximumBytes 1048576 | ConvertFrom-Json -Depth 40
    $module=Import-PowerShellDataFile $modulePath
    if([int]$marker.SchemaVersion -notin @(3,4) -or [string]$marker.Product -ne 'WinCare') { throw 'The WinCare installation marker schema is invalid.' }
    if([int]$marker.SchemaVersion -eq 4 -and ([string]$marker.LauncherExecutablePathSha256 -notmatch '^[a-f0-9]{64}$' -or [string]$marker.LauncherExecutableSha256 -notmatch '^[a-f0-9]{64}$')) { throw 'The WinCare launcher identity is invalid.' }
    if([string]$module.GUID -ne 'f43eb775-7d08-42ec-9888-8b1bd79e90a3' -or [string]$marker.ManifestGuid -ne [string]$module.GUID) { throw 'The WinCare installation product identity could not be verified.' }
    if([string]$marker.Version -ne [string]$module.ModuleVersion -or [string]$receipt.version -ne [string]$module.ModuleVersion) { throw 'The WinCare installation version evidence is inconsistent.' }
    if([string]$marker.InstallationPathSha256 -ne (Get-WinCareUninstallerCanonicalPathHash -Path $full)) { throw 'The WinCare marker is bound to a different installation path.' }
    if([string]$marker.ReleaseManifestSha256 -ne $evidence.ManifestSha256 -or [int]$marker.ManifestFileCount -ne [int]$evidence.Files) { throw 'The installed release manifest evidence is inconsistent.' }
    $receiptHash=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if([string]$marker.BuildReceiptSha256 -ne $receiptHash) { throw 'The installed build receipt does not match the installation marker.' }
    if([string]$receipt.schema -notin @('wincare.build.receipt/v1','wincare.build.receipt/v2') -or [string]$receipt.packageProfile -ne 'production' -or [bool]$receipt.source.dirty) { throw 'The installed build receipt is not promotable production evidence.' }
    if([string]$receipt.native.status -ne 'source-built-and-verified') { throw 'The installed native artifacts are not source-verified.' }
    [pscustomobject]@{Path=$full;Marker=$marker;Module=$module;Evidence=$evidence;Receipt=$receipt}
}

function Test-WinCareUninstallerDataTree {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedIdentityRoot,
        [ValidateRange(1,1000000)][int]$MaximumEntries=250000,
        [ValidateRange(1,8796093022208)][long]$MaximumTotalBytes=4398046511104
    )
    $full=Assert-SafeRemovalPath $Path
    $identityPath=Join-Path $full '.wincare-data.json'
    if(-not(Test-Path -LiteralPath $identityPath -PathType Leaf)) { throw 'WinCare data-root identity is missing; data was not purged.' }
    $identity=Read-WinCareUninstallerBoundedUtf8Text -LiteralPath $identityPath -MaximumBytes 65536 | ConvertFrom-Json -Depth 12
    if([int]$identity.SchemaVersion -ne 1 -or [string]$identity.Product -ne 'WinCare') { throw 'WinCare data-root identity schema is invalid.' }
    if([string]$identity.ManifestGuid -ne 'f43eb775-7d08-42ec-9888-8b1bd79e90a3') { throw 'WinCare data-root product identity is invalid.' }
    if([string]$identity.RootSha256 -ne (Get-WinCareUninstallerCanonicalPathHash -Path $ExpectedIdentityRoot)) { throw 'WinCare data-root identity is bound to a different path.' }
    $entries=0;$totalBytes=0L
    foreach($item in Get-ChildItem -LiteralPath $full -Recurse -Force -ErrorAction Stop) {
        $entries++
        if($entries -gt $MaximumEntries) { throw "WinCare data exceeds the $MaximumEntries-entry purge ceiling." }
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "WinCare data contains a reparse point and was not purged: $($item.FullName)" }
        if(-not $item.PSIsContainer) {
            $totalBytes+=[long]$item.Length
            if($totalBytes -gt $MaximumTotalBytes) { throw "WinCare data exceeds the $MaximumTotalBytes-byte purge ceiling." }
        }
    }
    [pscustomobject]@{Path=$full;Entries=$entries;Bytes=$totalBytes;Identity=$identity}
}

function Assert-DataRootIdentity {
    param([Parameter(Mandatory)][string]$Path)
    $full=Assert-SafeRemovalPath $Path
    $expected=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'WinCare')).TrimEnd('\')
    if(-not $full.Equals($expected,[StringComparison]::OrdinalIgnoreCase)) { throw 'WinCare data purge is restricted to the canonical per-user data root.' }
    $evidence=Test-WinCareUninstallerDataTree -Path $full -ExpectedIdentityRoot $expected
    return [string]$evidence.Path
}

function Test-WinCareShortcutIdentity {
    param(
        [Parameter(Mandatory)][string]$ShortcutPath,
        [Parameter(Mandatory)][string]$ExpectedScript,
        [Parameter(Mandatory)][string]$ExpectedArguments,
        [Parameter(Mandatory)][string]$ExpectedWorkingDirectory,
        [Parameter(Mandatory)][object]$InstallationMarker
    )
    if([int]$InstallationMarker.SchemaVersion -ne 4) { return $false }
    if(-not(Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) { return $false }
    $shell=$null;$shortcut=$null
    try {
        $shortcutItem=Get-Item -LiteralPath $ShortcutPath -Force -ErrorAction Stop
        if($shortcutItem.Attributes -band [IO.FileAttributes]::ReparsePoint){return $false}
        $shell=New-Object -ComObject WScript.Shell
        $shortcut=$shell.CreateShortcut($ShortcutPath)
        $target=[IO.Path]::GetFullPath([string]$shortcut.TargetPath).TrimEnd('\')
        $working=[IO.Path]::GetFullPath([string]$shortcut.WorkingDirectory).TrimEnd('\')
        $expectedScriptPath=[IO.Path]::GetFullPath($ExpectedScript)
        if(-not [string]::Equals([string]$shortcut.Arguments,$ExpectedArguments,[StringComparison]::Ordinal)){return $false}
        if(-not $working.Equals([IO.Path]::GetFullPath($ExpectedWorkingDirectory).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)){return $false}
        if([string]$shortcut.Arguments -notlike "*$expectedScriptPath*"){return $false}
        if((Get-WinCareUninstallerCanonicalPathHash -Path $target) -ne [string]$InstallationMarker.LauncherExecutablePathSha256){return $false}
        if(-not(Test-Path -LiteralPath $target -PathType Leaf)){return $false}
        return (Get-WinCareUninstallerFileSha256 -LiteralPath $target) -eq [string]$InstallationMarker.LauncherExecutableSha256
    } catch { return $false }
    finally {
        if($shortcut){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)}
        if($shell){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
    }
}

$identity=Assert-UninstallIdentity $Destination
$Destination=$identity.Path
$parent=Split-Path $Destination -Parent
$leaf=Split-Path $Destination -Leaf
$tombstone=Join-Path $parent (".$leaf.removing."+[guid]::NewGuid().ToString('N'))
if(Test-Path -LiteralPath $tombstone) { throw 'Uninstall tombstone unexpectedly exists.' }
$dataRoot=Join-Path $env:LOCALAPPDATA 'WinCare'
$dataTombstone=Join-Path (Split-Path $dataRoot -Parent) ('.WinCare.data.removing.'+[guid]::NewGuid().ToString('N'))
$shortcutPath=Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WinCare.lnk'
$consoleShortcutPath=Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WinCare Console.lnk'
$guiArguments="-NoLogo -NoProfile -STA -File `"$(Join-Path $Destination 'WinCare-GUI.ps1')`""
$consoleArguments="-NoLogo -NoProfile -File `"$(Join-Path $Destination 'WinCare.ps1')`""
$shortcutBackup=$null
$consoleShortcutBackup=$null
$shortcutWarnings=[Collections.Generic.List[string]]::new()

if($PSCmdlet.ShouldProcess($Destination,"Uninstall verified WinCare $($identity.Module.ModuleVersion)")) {
    $movedInstall=$false
    $movedData=$false
    $commitComplete=$false
    try {
        $identity=Assert-UninstallIdentity $Destination
        if(Test-WinCareShortcutIdentity -ShortcutPath $shortcutPath -ExpectedScript (Join-Path $Destination 'WinCare-GUI.ps1') -ExpectedArguments $guiArguments -ExpectedWorkingDirectory $Destination -InstallationMarker $identity.Marker) {
            $shortcutBackup="$shortcutPath.removing.$([guid]::NewGuid().ToString('N'))"
            [IO.File]::Move($shortcutPath,$shortcutBackup,$false)
        } elseif(Test-Path -LiteralPath $shortcutPath) { $shortcutWarnings.Add("Unrecognized shortcut was preserved: $shortcutPath") }
        if(Test-WinCareShortcutIdentity -ShortcutPath $consoleShortcutPath -ExpectedScript (Join-Path $Destination 'WinCare.ps1') -ExpectedArguments $consoleArguments -ExpectedWorkingDirectory $Destination -InstallationMarker $identity.Marker) {
            $consoleShortcutBackup="$consoleShortcutPath.removing.$([guid]::NewGuid().ToString('N'))"
            [IO.File]::Move($consoleShortcutPath,$consoleShortcutBackup,$false)
        } elseif(Test-Path -LiteralPath $consoleShortcutPath) { $shortcutWarnings.Add("Unrecognized shortcut was preserved: $consoleShortcutPath") }
        [IO.Directory]::Move($Destination,$tombstone)
        $movedInstall=$true
        if($PurgeData -and(Test-Path -LiteralPath $dataRoot)) {
            $dataRoot=Assert-DataRootIdentity $dataRoot
            if(Test-Path -LiteralPath $dataTombstone) { throw 'Data tombstone unexpectedly exists.' }
            [IO.Directory]::Move($dataRoot,$dataTombstone)
            $movedData=$true
        }
        $commitComplete=$true
    } catch {
        $originalError=$_.Exception.Message
        $rollbackErrors=[Collections.Generic.List[string]]::new()
        if($movedData -and(Test-Path -LiteralPath $dataTombstone) -and -not(Test-Path -LiteralPath $dataRoot)) {
            try { [IO.Directory]::Move($dataTombstone,$dataRoot) } catch { $rollbackErrors.Add("restore data: $($_.Exception.Message)") }
        }
        if($movedInstall -and(Test-Path -LiteralPath $tombstone) -and -not(Test-Path -LiteralPath $Destination)) {
            try { [IO.Directory]::Move($tombstone,$Destination) } catch { $rollbackErrors.Add("restore installation: $($_.Exception.Message)") }
        }
        if($shortcutBackup -and(Test-Path -LiteralPath $shortcutBackup) -and -not(Test-Path -LiteralPath $shortcutPath)) {
            try { [IO.File]::Move($shortcutBackup,$shortcutPath,$false) } catch { $rollbackErrors.Add("restore shortcut: $($_.Exception.Message)") }
        }
        if($consoleShortcutBackup -and(Test-Path -LiteralPath $consoleShortcutBackup) -and -not(Test-Path -LiteralPath $consoleShortcutPath)) {
            try { [IO.File]::Move($consoleShortcutBackup,$consoleShortcutPath,$false) } catch { $rollbackErrors.Add("restore console shortcut: $($_.Exception.Message)") }
        }
        if($rollbackErrors.Count) { throw "Uninstall staging failed and rollback was incomplete. Original error: $originalError. Rollback error(s): $($rollbackErrors -join '; ')" }
        throw
    }
    if($commitComplete) {
        $cleanupWarnings=[Collections.Generic.List[string]]::new()
        if($movedInstall -and(Test-Path -LiteralPath $tombstone)) {
            try {
                $safeTombstone=Assert-SafeRemovalPath $tombstone
                $null=Test-InstalledReleaseTree -Root $safeTombstone
                Remove-Item -LiteralPath $safeTombstone -Recurse -Force -ErrorAction Stop
            } catch { $cleanupWarnings.Add("installation tombstone remains at '$tombstone': $($_.Exception.Message)") }
        }
        if($movedData -and(Test-Path -LiteralPath $dataTombstone)) {
            try {
                $dataEvidence=Test-WinCareUninstallerDataTree -Path $dataTombstone -ExpectedIdentityRoot $dataRoot
                Remove-Item -LiteralPath $dataEvidence.Path -Recurse -Force -ErrorAction Stop
            } catch { $cleanupWarnings.Add("data tombstone remains at '$dataTombstone': $($_.Exception.Message)") }
        }
        foreach($shortcut in @($shortcutBackup,$consoleShortcutBackup)) {
            if($shortcut -and(Test-Path -LiteralPath $shortcut)) {
                try {
                    $shortcutItem=Get-Item -LiteralPath $shortcut -Force -ErrorAction Stop
                    if($shortcutItem.PSIsContainer -or ($shortcutItem.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Shortcut tombstone is not a regular non-reparse file.'}
                    Remove-Item -LiteralPath $shortcutItem.FullName -Force -ErrorAction Stop
                } catch { $cleanupWarnings.Add("shortcut tombstone remains at '$shortcut': $($_.Exception.Message)") }
            }
        }
        Write-Host 'WinCare was uninstalled.' -ForegroundColor Green
        if(-not $PurgeData) { Write-Host 'Settings, reports, logs, extensions, and journals were preserved.' }
        foreach($warning in @($shortcutWarnings)+@($cleanupWarnings)) { Write-Warning $warning }
    }
}
