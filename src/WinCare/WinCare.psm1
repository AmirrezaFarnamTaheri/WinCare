Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:WinCareModuleRoot = $PSScriptRoot
$moduleVersion = $ExecutionContext.SessionState.Module.Version
if ($null -eq $moduleVersion -or $moduleVersion -eq [version]'0.0') {
    $moduleManifestPath = Join-Path $PSScriptRoot 'WinCare.psd1'
    $moduleManifest = Import-PowerShellDataFile -LiteralPath $moduleManifestPath
    $moduleVersion = [version][string]$moduleManifest.ModuleVersion
}
if ($null -eq $moduleVersion -or $moduleVersion -eq [version]'0.0') {
    throw 'WinCare module version could not be resolved from the module session or manifest.'
}
$script:WinCareVersion = $moduleVersion.ToString()
$script:WinCareSourceManifestFiles = @(
    'SourceManifests/Core.psd1'
    'SourceManifests/Providers.psd1'
    'SourceManifests/UI.psd1'
)
$script:WinCareSourceGroups = @(
    [pscustomobject]@{ Name='Core';      Manifest='SourceManifests/Core.psd1';      Folders=@('Core') }
    [pscustomobject]@{ Name='Providers'; Manifest='SourceManifests/Providers.psd1'; Folders=@('Providers') }
    [pscustomobject]@{ Name='UI';        Manifest='SourceManifests/UI.psd1';         Folders=@('UI','UI/Gui','UI/Screens') }
)

function Get-WinCarePreloadFileFingerprint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,67108864)][long]$MaximumBytes=16777216
    )
    $root=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    $full=[IO.Path]::GetFullPath($LiteralPath)
    if(-not $full.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){throw "Preload path escapes the module root: $LiteralPath"}
    $item=Get-Item -LiteralPath $full -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or [long]$item.Length -gt $MaximumBytes){throw "Unsafe or oversized preload file: $LiteralPath"}
    $stream=[IO.FileStream]::new($full,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,65536,[IO.FileOptions]::SequentialScan)
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$hash=$sha.ComputeHash($stream)}finally{$sha.Dispose();$stream.Dispose()}
    $after=Get-Item -LiteralPath $full -Force -ErrorAction Stop
    if([long]$after.Length -ne [long]$item.Length -or $after.LastWriteTimeUtc.Ticks -ne $item.LastWriteTimeUtc.Ticks){throw "Preload file changed while hashing: $LiteralPath"}
    [pscustomobject]@{Path=$full;Length=[long]$item.Length;Ticks=[long]$item.LastWriteTimeUtc.Ticks;Sha256=[Convert]::ToHexString($hash).ToLowerInvariant()}
}

function Get-WinCarePreloadGroupSourceHash {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Declared)
    $builder=[Text.StringBuilder]::new()
    foreach($relative in $Declared){
        $info=Get-WinCarePreloadFileFingerprint -LiteralPath (Join-Path $PSScriptRoot $relative)
        $null=$builder.Append($relative.Replace('\','/')).Append("`n").Append($info.Length).Append("`n").Append($info.Sha256).Append("`n")
    }
    $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes($builder.ToString())
    try{[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()}finally{[Array]::Clear($bytes,0,$bytes.Length)}
}

# Receipt cache v4. Warm loads still validate source and merged-cache content hashes,
# then dot-source three merged group files. Cold loads validate manifests, rebuild the
# caches atomically, and write an authenticated-by-content receipt.
$receiptPath=Join-Path $PSScriptRoot '.wincare-load-receipt.json'
$receiptHit=$false
$mergedPaths=[ordered]@{}

try{
    if(Test-Path -LiteralPath $receiptPath -PathType Leaf){
        $receiptInfo=Get-WinCarePreloadFileFingerprint -LiteralPath $receiptPath -MaximumBytes 1048576
        $stream=[IO.FileStream]::new($receiptInfo.Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        $reader=[IO.StreamReader]::new($stream,[Text.UTF8Encoding]::new($false,$true),$false,4096,$false)
        try{$receipt=$reader.ReadToEnd()|ConvertFrom-Json -Depth 12}finally{$reader.Dispose()}
        if([int]$receipt.SchemaVersion -eq 4 -and [string]$receipt.ModuleVersion -eq $script:WinCareVersion){
            $fingerprintsMatch=$true
            foreach($group in $script:WinCareSourceGroups){
                $manifestPath=Join-Path $PSScriptRoot ([string]$group.Manifest)
                $manifestInfo=Get-WinCarePreloadFileFingerprint -LiteralPath $manifestPath -MaximumBytes 1048576
                $sourceManifest=Import-PowerShellDataFile -LiteralPath $manifestInfo.Path
                if([int]$sourceManifest.SchemaVersion -ne 1 -or [string]$sourceManifest.Group -ne [string]$group.Name){$fingerprintsMatch=$false;break}
                $declared=@($sourceManifest.Files|ForEach-Object{([string]$_).Replace('\','/')})
                $stored=$receipt.Fingerprints.($group.Name)
                $mergedFile=Join-Path $PSScriptRoot ".wincare-merged-$($group.Name).ps1"
                $mergedInfo=Get-WinCarePreloadFileFingerprint -LiteralPath $mergedFile -MaximumBytes 33554432
                if($null -eq $stored -or [long]$stored.ManifestTicks -ne $manifestInfo.Ticks -or [long]$stored.ManifestLength -ne $manifestInfo.Length -or [string]$stored.SourceHash -ne (Get-WinCarePreloadGroupSourceHash -Declared $declared) -or [string]$stored.MergedHash -ne $mergedInfo.Sha256){$fingerprintsMatch=$false;break}
                $mergedPaths[$group.Name]=$mergedInfo.Path
            }
            $receiptHit=$fingerprintsMatch
        }
    }
}catch{
    Write-Verbose "WinCare: load receipt validation failed ($($_.Exception.Message)); running full validation."
    $receiptHit=$false
    $mergedPaths=[ordered]@{}
}

if(-not $receiptHit){
    $newFingerprints=[ordered]@{}
    $newMergedFiles=[ordered]@{}
    $allSourcePaths=[Collections.Generic.List[string]]::new()
    foreach($group in $script:WinCareSourceGroups){
        if([string]$group.Manifest -notin $script:WinCareSourceManifestFiles){throw "Source group $($group.Name) is not declared in WinCareSourceManifestFiles."}
        $manifestPath=Join-Path $PSScriptRoot ([string]$group.Manifest)
        $manifestInfo=Get-WinCarePreloadFileFingerprint -LiteralPath $manifestPath -MaximumBytes 1048576
        $sourceManifest=Import-PowerShellDataFile -LiteralPath $manifestInfo.Path
        if([int]$sourceManifest.SchemaVersion -ne 1 -or [string]$sourceManifest.Group -ne [string]$group.Name){throw "Source manifest identity is invalid: $($group.Manifest)"}
        $declared=@($sourceManifest.Files|ForEach-Object{([string]$_).Replace('\','/')})
        $duplicates=@($declared|Group-Object|Where-Object Count -gt 1|ForEach-Object Name)
        if($duplicates.Count){throw "Duplicate source entries in $($group.Manifest): $($duplicates -join ', ')"}
        $actual=[Collections.Generic.List[string]]::new()
        foreach($folder in @($group.Folders)){
            $folderPath=Join-Path $PSScriptRoot ([string]$folder)
            if(-not(Test-Path -LiteralPath $folderPath -PathType Container)){throw "Required module source directory is missing: $folder"}
            foreach($file in Get-ChildItem -LiteralPath $folderPath -Filter '*.ps1' -File|Sort-Object Name){$actual.Add([IO.Path]::GetRelativePath($PSScriptRoot,$file.FullName).Replace('\','/'))}
        }
        $missing=@($declared|Where-Object{$_ -notin $actual});$unexpected=@($actual|Where-Object{$_ -notin $declared})
        if($missing.Count -or $unexpected.Count){throw "WinCare $($group.Name) source manifest mismatch: missing=$($missing -join ','); unexpected=$($unexpected -join ',')"}
        if(($declared -join "`n") -ne (@($actual) -join "`n")){throw "WinCare $($group.Name) source manifest order is not canonical."}
        foreach($relativePath in $declared){$allSourcePaths.Add($relativePath)}
        $sourceHash=Get-WinCarePreloadGroupSourceHash -Declared $declared
        $mergedRelative=".wincare-merged-$($group.Name).ps1"
        $mergedAbs=Join-Path $PSScriptRoot $mergedRelative
        $mergedTmp="$mergedAbs.$([guid]::NewGuid().ToString('N')).tmp"
        try{
            $sb=[Text.StringBuilder]::new(1048576)
            foreach($rel in $declared){$null=$sb.AppendLine("# --- merged: $rel ---");$null=$sb.AppendLine([IO.File]::ReadAllText((Join-Path $PSScriptRoot $rel),[Text.Encoding]::UTF8))}
            [IO.File]::WriteAllText($mergedTmp,$sb.ToString(),[Text.UTF8Encoding]::new($false))
            [IO.File]::Move($mergedTmp,$mergedAbs,$true)
            $mergedInfo=Get-WinCarePreloadFileFingerprint -LiteralPath $mergedAbs -MaximumBytes 33554432
            $newMergedFiles[$group.Name]=$mergedRelative
            $newFingerprints[$group.Name]=[ordered]@{ManifestTicks=$manifestInfo.Ticks;ManifestLength=$manifestInfo.Length;SourceHash=$sourceHash;MergedHash=$mergedInfo.Sha256}
        }catch{
            Remove-Item -LiteralPath $mergedTmp -Force -ErrorAction SilentlyContinue
            Write-Verbose "WinCare: could not write merged cache for $($group.Name) ($($_.Exception.Message)); using individual files."
            $newMergedFiles[$group.Name]=$null
        }
    }
    $duplicateModuleFiles=@($allSourcePaths|Group-Object|Where-Object Count -gt 1|ForEach-Object Name)
    if($duplicateModuleFiles.Count){throw ('Source files assigned to multiple groups: '+($duplicateModuleFiles -join ', '))}
    foreach($relativePath in $allSourcePaths){. (Join-Path $PSScriptRoot $relativePath)}
    if($newMergedFiles.Values -notcontains $null){
        $receiptTmp="$receiptPath.$([guid]::NewGuid().ToString('N')).tmp"
        try{
            $receiptObj=[ordered]@{SchemaVersion=4;ModuleVersion=$script:WinCareVersion;WrittenUtc=[datetime]::UtcNow.ToString('o');Fingerprints=$newFingerprints;MergedFiles=$newMergedFiles}
            [IO.File]::WriteAllText($receiptTmp,($receiptObj|ConvertTo-Json -Depth 8 -Compress),[Text.UTF8Encoding]::new($false))
            [IO.File]::Move($receiptTmp,$receiptPath,$true)
        }catch{Remove-Item -LiteralPath $receiptTmp -Force -ErrorAction SilentlyContinue;Write-Verbose "WinCare: could not write load receipt ($($_.Exception.Message))."}
    }
}else{
    foreach($group in $script:WinCareSourceGroups){. $mergedPaths[$group.Name]}
}

# T2.1 (ponytail/shrink): replaced ~330-line manual list. Public API surface is
# enforced by tools/validate_module_manifest.py and source manifest PSD1 files.
# To make a function private, prefix with lowercase verb (not Verb-Noun) and add
# an explicit Export-ModuleMember deny list after this comment.
Export-ModuleMember -Function *