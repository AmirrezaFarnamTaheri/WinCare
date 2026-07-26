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


function Read-WinCareInstallerBoundedUtf8Text {
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


function Get-WinCareInstallerPowerShellExecutable {
    $path=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    if([string]::IsNullOrWhiteSpace($path)){throw 'The current PowerShell executable path is unavailable.'}
    $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'The current PowerShell executable is not a regular non-reparse file.'}
    return $item.FullName
}

function Get-WinCareInstallerFileSha256 {
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

function Test-WinCareInstallerShortcutIdentity {
    param(
        [Parameter(Mandatory)][string]$ShortcutPath,
        [Parameter(Mandatory)][string]$ExpectedTarget,
        [Parameter(Mandatory)][string]$ExpectedArguments,
        [Parameter(Mandatory)][string]$ExpectedWorkingDirectory
    )
    if(-not(Test-Path -LiteralPath $ShortcutPath -PathType Leaf)){return $false}
    $item=Get-Item -LiteralPath $ShortcutPath -Force -ErrorAction Stop
    if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){return $false}
    $shell=$null;$shortcut=$null
    try {
        $shell=New-Object -ComObject WScript.Shell
        $shortcut=$shell.CreateShortcut($ShortcutPath)
        $target=[IO.Path]::GetFullPath([string]$shortcut.TargetPath).TrimEnd('\')
        $working=[IO.Path]::GetFullPath([string]$shortcut.WorkingDirectory).TrimEnd('\')
        return $target.Equals([IO.Path]::GetFullPath($ExpectedTarget).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase) -and
            [string]::Equals([string]$shortcut.Arguments,$ExpectedArguments,[StringComparison]::Ordinal) -and
            $working.Equals([IO.Path]::GetFullPath($ExpectedWorkingDirectory).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)
    } catch { return $false }
    finally {
        if($shortcut){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)}
        if($shell){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
    }
}

function Get-WinCareInstallerCanonicalPathHash {
    param([Parameter(Mandatory)][string]$Path)
    $canonical=[IO.Path]::GetFullPath($Path).TrimEnd('\').ToLowerInvariant()
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
}

function Read-ReleaseMetadata {
    param([Parameter(Mandatory)][string]$Root)
    $receiptPath=Join-Path $Root 'BUILD-RECEIPT.json'
    $receipt=Read-WinCareInstallerBoundedUtf8Text -LiteralPath $receiptPath -MaximumBytes 1048576 | ConvertFrom-Json -Depth 40
    if([string]$receipt.schema -ne 'wincare.build.receipt/v1') { throw 'Unsupported build receipt schema.' }
    if([string]$receipt.packageProfile -ne 'production') { throw 'Only production-profile packages may be installed.' }
    if([bool]$receipt.source.dirty) { throw 'Dirty-source packages are non-promotable and cannot be installed.' }
    if([string]$receipt.native.status -ne 'source-built-and-verified') { throw 'The package does not contain source-verified native artifacts.' }
    $module=Import-PowerShellDataFile (Join-Path $Root 'src\WinCare\WinCare.psd1')
    if([string]$receipt.version -ne [string]$module.ModuleVersion) { throw 'Build receipt and module versions differ.' }
    [pscustomobject]@{ Receipt=$receipt; Module=$module; ReceiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash.ToLowerInvariant() }
}

function Get-ManifestEntries {
    param([Parameter(Mandatory)][string]$ManifestPath)
    $entries=[ordered]@{}
    $case=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $text=Read-WinCareInstallerBoundedUtf8Text -LiteralPath $ManifestPath -MaximumBytes 4194304
    foreach($line in $text -split "`r?`n") {
        if([string]::IsNullOrWhiteSpace($line)) { continue }
        if($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Malformed release manifest line: $line" }
        $hash=$Matches[1]
        $relative=$Matches[2].Replace('/','\')
        if([IO.Path]::IsPathRooted($relative) -or @($relative -split '[\\/]').Contains('..')) { throw "Unsafe release-manifest path: $relative" }
        if(-not $case.Add($relative)) { throw "Duplicate or case-colliding manifest path: $relative" }
        if($entries.Count -ge 5000) { throw 'Release manifest exceeds 5000 entries.' }
        $entries[$relative]=$hash
    }
    if(-not $entries.Count) { throw 'The release manifest is empty.' }
    return $entries
}

function Test-ReleaseTree {
    param([Parameter(Mandatory)][string]$Root,[switch]$AllowInstallMarker)
    $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\');$manifestPath=Join-Path $rootPath 'RELEASE-MANIFEST.sha256'
    $rootItem=Get-Item -LiteralPath $rootPath -Force -ErrorAction Stop
    if($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Release root is a reparse point: $rootPath"}
    if(-not(Test-Path -LiteralPath $manifestPath -PathType Leaf)){throw 'RELEASE-MANIFEST.sha256 is required. Install from a verified release archive.'}
    $entries=Get-ManifestEntries $manifestPath
    $actual=[Collections.Generic.List[IO.FileInfo]]::new()
    $entryCount=0
    $totalBytes=0L
    foreach($item in Get-ChildItem -LiteralPath $rootPath -Recurse -Force -ErrorAction Stop){
        $entryCount++
        if($entryCount -gt 10000){throw 'The release tree exceeds the 10000-entry verification ceiling.'}
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Release tree contains a reparse point: $($item.FullName)"}
        if($item.PSIsContainer){continue}
        if($item.FullName -eq $manifestPath -or ($AllowInstallMarker -and $item.Name -eq '.wincare-install.json')){continue}
        if([long]$item.Length -gt 536870912L){throw "Release file exceeds the 512 MiB verification ceiling: $($item.FullName)"}
        $totalBytes+=[long]$item.Length
        if($totalBytes -gt 2147483648L){throw 'The release tree exceeds the 2 GiB verification ceiling.'}
        $actual.Add($item)
    }
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


function Invoke-WinCareInstallerCapturedProcess {
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$Process,
        [ValidateRange(1,3600)][int]$TimeoutSeconds,
        [ValidateRange(1024,16777216)][int]$MaximumBytes=4194304
    )
    $stdout=[IO.MemoryStream]::new($MaximumBytes);$stderr=[IO.MemoryStream]::new($MaximumBytes)
    $stdoutBuffer=[byte[]]::new(65536);$stderrBuffer=[byte[]]::new(65536)
    $cancellation=[Threading.CancellationTokenSource]::new();$cancellation.CancelAfter($TimeoutSeconds*1000)
    $stdoutOpen=$true;$stderrOpen=$true;$stdoutTruncated=$false;$stderrTruncated=$false;$timedOut=$false
    try {
        if(-not $Process.Start()){throw 'The process did not start.'}
        $stdoutStream=$Process.StandardOutput.BaseStream;$stderrStream=$Process.StandardError.BaseStream
        $stdoutRead=$stdoutStream.ReadAsync($stdoutBuffer,0,$stdoutBuffer.Length,$cancellation.Token)
        $stderrRead=$stderrStream.ReadAsync($stderrBuffer,0,$stderrBuffer.Length,$cancellation.Token)
        while($stdoutOpen -or $stderrOpen) {
            $active=[Collections.Generic.List[Threading.Tasks.Task]]::new()
            if($stdoutOpen){$active.Add($stdoutRead)};if($stderrOpen){$active.Add($stderrRead)}
            try{$null=[Threading.Tasks.Task]::WhenAny($active.ToArray()).GetAwaiter().GetResult()}catch [OperationCanceledException]{$timedOut=$true;break}
            foreach($name in @('stdout','stderr')) {
                if($name -eq 'stdout'){$open=$stdoutOpen;$task=$stdoutRead;$buffer=$stdoutBuffer;$memory=$stdout}
                else{$open=$stderrOpen;$task=$stderrRead;$buffer=$stderrBuffer;$memory=$stderr}
                if(-not $open -or -not $task.IsCompleted){continue}
                try{$count=$task.GetAwaiter().GetResult()}catch [OperationCanceledException]{$timedOut=$true;break}
                if($count -eq 0){if($name -eq 'stdout'){$stdoutOpen=$false}else{$stderrOpen=$false};continue}
                $remaining=$MaximumBytes-$memory.Length;$accepted=[int][Math]::Min([long]$count,[Math]::Max(0L,$remaining))
                if($accepted -gt 0){$memory.Write($buffer,0,$accepted)}
                if($accepted -lt $count){if($name -eq 'stdout'){$stdoutTruncated=$true}else{$stderrTruncated=$true}}
                $next=if($name -eq 'stdout'){$stdoutStream.ReadAsync($stdoutBuffer,0,$stdoutBuffer.Length,$cancellation.Token)}else{$stderrStream.ReadAsync($stderrBuffer,0,$stderrBuffer.Length,$cancellation.Token)}
                if($name -eq 'stdout'){$stdoutRead=$next}else{$stderrRead=$next}
            }
            if($timedOut){break}
        }
        if($timedOut -or $cancellation.IsCancellationRequested){$timedOut=$true;try{$Process.Kill($true)}catch{} }
        if(-not $Process.HasExited){$Process.WaitForExit()}
        $stdoutBytes=$stdout.ToArray();$stderrBytes=$stderr.ToArray()
        try {
            [pscustomobject]@{
                TimedOut=$timedOut;ExitCode=if($timedOut){124}else{$Process.ExitCode}
                StdOut=[Text.UTF8Encoding]::new($false,$false).GetString($stdoutBytes)
                StdErr=[Text.UTF8Encoding]::new($false,$false).GetString($stderrBytes)
                StdoutTruncated=$stdoutTruncated;StderrTruncated=$stderrTruncated
            }
        } finally {
            if($stdoutBytes.Length){[Array]::Clear($stdoutBytes,0,$stdoutBytes.Length)}
            if($stderrBytes.Length){[Array]::Clear($stderrBytes,0,$stderrBytes.Length)}
        }
    } finally {
        $cancellation.Dispose();[Array]::Clear($stdoutBuffer,0,$stdoutBuffer.Length);[Array]::Clear($stderrBuffer,0,$stderrBuffer.Length)
        $stdout.Dispose();$stderr.Dispose()
    }
}

function Invoke-WinCareInstallSmokeTest {
    param([Parameter(Mandatory)][string]$Root,[int]$TimeoutSeconds=120)
    $launcher=Join-Path $Root 'WinCare.ps1'
    if(-not(Test-Path -LiteralPath $launcher -PathType Leaf)){throw "Staged launcher is missing: $launcher"}
    $pwsh=Get-WinCareInstallerPowerShellExecutable
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=$pwsh;$start.WorkingDirectory=$Root;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in @('-NoLogo','-NoProfile','-NonInteractive','-File',$launcher,'-Command','system','-Json')){[void]$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$start
    try{
        $capture=Invoke-WinCareInstallerCapturedProcess -Process $process -TimeoutSeconds $TimeoutSeconds -MaximumBytes 4194304
        if($capture.TimedOut){throw "Staged WinCare smoke test exceeded $TimeoutSeconds seconds."}
        if($capture.StdoutTruncated -or $capture.StderrTruncated){throw 'Staged WinCare smoke-test output exceeded the 4 MiB stream ceiling.'}
        $stdout=[string]$capture.StdOut;$stderr=[string]$capture.StdErr
        if($capture.ExitCode -ne 0){throw "Staged WinCare smoke test failed with exit code $($capture.ExitCode): $stderr"}
        try{$payload=$stdout|ConvertFrom-Json -Depth 50 -ErrorAction Stop}catch{throw "Staged WinCare smoke test returned invalid JSON: $($_.Exception.Message)"}
        if($null -eq $payload){throw 'Staged WinCare smoke test returned no JSON payload.'}
        [pscustomobject]@{ExitCode=$process.ExitCode;PayloadType=$payload.GetType().FullName;StdoutSha256=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($stdout))).ToLowerInvariant())}
    }finally{$process.Dispose()}
}

function Test-InstalledIdentity {
    param([Parameter(Mandatory)][string]$Path)
    $markerPath=Join-Path $Path '.wincare-install.json'
    $modulePath=Join-Path $Path 'src\WinCare\WinCare.psd1'
    if(-not(Test-Path -LiteralPath $markerPath -PathType Leaf) -or -not(Test-Path -LiteralPath $modulePath -PathType Leaf)) { throw 'The destination is not a complete marked WinCare installation.' }
    $marker=Read-WinCareInstallerBoundedUtf8Text -LiteralPath $markerPath -MaximumBytes 65536 | ConvertFrom-Json -Depth 20
    $manifest=Import-PowerShellDataFile $modulePath
    $evidence=Test-ReleaseTree -Root $Path -AllowInstallMarker
    $metadata=Read-ReleaseMetadata -Root $Path
    if([int]$marker.SchemaVersion -notin @(3,4) -or $marker.Product -ne 'WinCare') { throw 'The existing installation marker schema is invalid.' }
    if([int]$marker.SchemaVersion -eq 4 -and ([string]$marker.LauncherExecutablePathSha256 -notmatch '^[a-f0-9]{64}$' -or [string]$marker.LauncherExecutableSha256 -notmatch '^[a-f0-9]{64}$')) { throw 'The existing installation launcher identity is invalid.' }
    if([string]$manifest.GUID -ne 'f43eb775-7d08-42ec-9888-8b1bd79e90a3' -or [string]$marker.ManifestGuid -ne [string]$manifest.GUID) { throw 'The existing installation identity is invalid.' }
    if([string]$marker.Version -ne [string]$manifest.ModuleVersion) { throw 'The existing installation version marker is invalid.' }
    if([string]$marker.InstallationPathSha256 -ne (Get-WinCareInstallerCanonicalPathHash -Path $Path)) { throw 'The existing installation marker is bound to a different destination.' }
    if([string]$marker.ReleaseManifestSha256 -ne $evidence.ManifestSha256 -or [int]$marker.ManifestFileCount -ne [int]$evidence.Files) { throw 'The existing installation release evidence is invalid.' }
    if([string]$marker.BuildReceiptSha256 -ne $metadata.ReceiptSha256) { throw 'The existing installation build receipt is invalid.' }
    return [pscustomobject]@{Marker=$marker;Manifest=$manifest;Evidence=$evidence;Metadata=$metadata}
}

$Destination=Resolve-SafeInstallPath $Destination
$sourceRoot=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$separator=[IO.Path]::DirectorySeparatorChar
if($Destination.Equals($sourceRoot,[StringComparison]::OrdinalIgnoreCase) -or $Destination.StartsWith($sourceRoot+$separator,[StringComparison]::OrdinalIgnoreCase) -or $sourceRoot.StartsWith($Destination+$separator,[StringComparison]::OrdinalIgnoreCase)){throw 'Source and installation destination must be separate non-nested directories.'}
$sourceEvidence=Test-ReleaseTree -Root $sourceRoot
$sourceMetadata=Read-ReleaseMetadata -Root $sourceRoot
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
        $installerPowerShell=Get-WinCareInstallerPowerShellExecutable
        $installerPowerShellSha256=Get-WinCareInstallerFileSha256 -LiteralPath $installerPowerShell
        $smokeEvidence=Invoke-WinCareInstallSmokeTest -Root $stage
        [ordered]@{SchemaVersion=4;Product='WinCare';Version=[string]$module.ModuleVersion;ManifestGuid=[string]$module.GUID;InstallationId=[guid]::NewGuid().ToString('D');InstallationPathSha256=(Get-WinCareInstallerCanonicalPathHash -Path $Destination);InstalledAt=[datetime]::UtcNow.ToString('o');ReleaseManifestSha256=$stageEvidence.ManifestSha256;ManifestFileCount=[int]$stageEvidence.Files;BuildReceiptSha256=$sourceMetadata.ReceiptSha256;SourceCommit=[string]$sourceMetadata.Receipt.source.gitCommit;PackageInputTreeSha256=[string]$sourceMetadata.Receipt.packageInputs.treeSha256;LauncherExecutablePathSha256=(Get-WinCareInstallerCanonicalPathHash -Path $installerPowerShell);LauncherExecutableSha256=$installerPowerShellSha256;VerifiedRelease=[bool]$stageEvidence.Verified;SmokeTest=$smokeEvidence}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $stage '.wincare-install.json') -Encoding utf8NoBOM
        if(Test-Path -LiteralPath $Destination){Move-Item -LiteralPath $Destination -Destination $backup;$destinationMoved=$true}
        Move-Item -LiteralPath $stage -Destination $Destination;$stageCommitted=$true
        $installed=Test-InstalledIdentity $Destination
        if([string]$installed.Manifest.ModuleVersion -ne [string]$module.ModuleVersion){throw 'Installed version verification failed.'}

        if(-not $NoStartMenuShortcut){
            if(Test-Path -LiteralPath $shortcutPath){$shortcutBackup="$shortcutPath.backup.$([guid]::NewGuid().ToString('N'))";Move-Item -LiteralPath $shortcutPath -Destination $shortcutBackup}
            if(Test-Path -LiteralPath $consoleShortcutPath){$consoleShortcutBackup="$consoleShortcutPath.backup.$([guid]::NewGuid().ToString('N'))";Move-Item -LiteralPath $consoleShortcutPath -Destination $consoleShortcutBackup}
            $pwsh=$installerPowerShell
            $guiArguments="-NoLogo -NoProfile -STA -File `"$(Join-Path $Destination 'WinCare-GUI.ps1')`""
            $consoleArguments="-NoLogo -NoProfile -File `"$(Join-Path $Destination 'WinCare.ps1')`""
            $shell=$null;$shortcut=$null;$consoleShortcut=$null
            try {
                $shell=New-Object -ComObject WScript.Shell
                $shortcut=$shell.CreateShortcut($shortcutPath)
                $shortcut.TargetPath=$pwsh;$shortcut.Arguments=$guiArguments;$shortcut.WorkingDirectory=$Destination;$shortcut.Description='WinCare graphical Windows operations console';$shortcut.Save()
                $consoleShortcut=$shell.CreateShortcut($consoleShortcutPath)
                $consoleShortcut.TargetPath=$pwsh;$consoleShortcut.Arguments=$consoleArguments;$consoleShortcut.WorkingDirectory=$Destination;$consoleShortcut.Description='WinCare terminal operations console';$consoleShortcut.Save()
            } finally {
                if($consoleShortcut){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($consoleShortcut)}
                if($shortcut){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)}
                if($shell){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
            }
            if(-not(Test-WinCareInstallerShortcutIdentity -ShortcutPath $shortcutPath -ExpectedTarget $pwsh -ExpectedArguments $guiArguments -ExpectedWorkingDirectory $Destination) -or -not(Test-WinCareInstallerShortcutIdentity -ShortcutPath $consoleShortcutPath -ExpectedTarget $pwsh -ExpectedArguments $consoleArguments -ExpectedWorkingDirectory $Destination)){throw 'Start menu shortcut verification failed.'}
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
