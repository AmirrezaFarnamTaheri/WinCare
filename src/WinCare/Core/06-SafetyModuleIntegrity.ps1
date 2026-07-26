#requires -Version 7.2
# Bounded, deterministic module identity used by elevation and detached hosts.

function Get-WinCareModuleTreeHash {
    [CmdletBinding()]
    param(
        [ValidateRange(1,100000)][int]$MaximumEntries=10000,
        [ValidateRange(1,50000)][int]$MaximumFiles=5000,
        [ValidateRange(1048576,2147483648)][long]$MaximumTotalBytes=536870912,
        [ValidateRange(1024,67108864)][long]$MaximumMetadataBytes=4194304,
        [ValidateRange(1024,268435456)][long]$MaximumFileBytes=67108864
    )
    $root=Assert-WinCareSafePath -LiteralPath $script:WinCareModuleRoot
    $encoding=[Text.UTF8Encoding]::new($false,$true)
    $relativePaths=[Collections.Generic.List[string]]::new()
    $records=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $totalBytes=0L
    $metadataBytes=0L
    $entryCount=0
    foreach($file in Get-WinCareBoundedTreeEntry -LiteralPath $root -MaximumEntries $MaximumEntries -MaximumPathMetadataBytes $MaximumMetadataBytes -EntryType File) {
        $entryCount++
        if($relativePaths.Count -ge $MaximumFiles) { throw "Module tree exceeds $MaximumFiles files." }
        if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Module tree contains a reparse file: $($file.FullName)" }
        if(-not (Test-WinCarePathWithin -Child $file.FullName -Parent $root)) { throw "Module file escaped the module root: $($file.FullName)" }
        if([long]$file.Length -gt $MaximumFileBytes) { throw "Module file exceeds $MaximumFileBytes bytes: $($file.FullName)" }
        $totalBytes += [long]$file.Length
        if($totalBytes -gt $MaximumTotalBytes) { throw "Module tree exceeds $MaximumTotalBytes bytes." }
        $relative=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
        if([string]::IsNullOrWhiteSpace($relative) -or $relative.StartsWith('../',[StringComparison]::Ordinal)) { throw "Module tree produced an unsafe relative path: $($file.FullName)" }
        $metadataBytes += [long]$encoding.GetByteCount($relative)+1L
        if($metadataBytes -gt $MaximumMetadataBytes) { throw "Module path metadata exceeds $MaximumMetadataBytes bytes." }
        $record=[pscustomobject]@{
            Path=$file.FullName
            Length=[long]$file.Length
            LastWriteTimeUtc=$file.LastWriteTimeUtc
        }
        if(-not $records.TryAdd($relative,$record)) {
            throw "Module tree contains a duplicate relative path: $relative"
        }
        $relativePaths.Add($relative)
    }
    $relativePaths.Sort([StringComparer]::Ordinal)
    $incremental=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        foreach($relative in $relativePaths) {
            $record=$records[$relative]
            $current=Get-Item -LiteralPath $record.Path -Force -ErrorAction Stop
            $changed=
                $current.PSIsContainer -or
                ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                [long]$current.Length -ne [long]$record.Length -or
                $current.LastWriteTimeUtc -ne $record.LastWriteTimeUtc
            if($changed) { throw "Module file changed before hashing: $($record.Path)" }
            $line="$relative`:$(Get-WinCareSha256 -LiteralPath $record.Path)`n"
            $bytes=$encoding.GetBytes($line)
            try { $incremental.AppendData($bytes) } finally { if($bytes.Length){[Array]::Clear($bytes,0,$bytes.Length)} }
        }
        $digest=$incremental.GetHashAndReset()
        try { return [Convert]::ToHexString($digest).ToLowerInvariant() }
        finally { if($digest.Length){[Array]::Clear($digest,0,$digest.Length)} }
    } finally { $incremental.Dispose() }
}
