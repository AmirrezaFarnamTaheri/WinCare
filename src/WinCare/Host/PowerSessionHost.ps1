#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModulePath,
    [Parameter(Mandatory)][string]$SessionId,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string]$ExpectedModuleTreeHash
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if($SessionId -notmatch '^[a-f0-9]{32}$'){throw 'Invalid session ID.'}
function Assert-PowerSessionNoReparse {
    param([Parameter(Mandatory)][string]$LiteralPath)
    $item=Get-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
    while($item){
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw "Power-session host refused a reparse path: $($item.FullName)"}
        $parent=Split-Path -Parent $item.FullName
        if(-not $parent -or $parent -eq $item.FullName){break}
        $item=Get-Item -LiteralPath $parent -Force -ErrorAction SilentlyContinue
    }
}
function Get-PowerSessionFileSha256 {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][long]$ExpectedLength,
        [Parameter(Mandatory)][datetime]$ExpectedLastWriteTimeUtc,
        [ValidateRange(1024,268435456)][long]$MaximumBytes=67108864
    )
    $before=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if($before.PSIsContainer -or ($before.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or [long]$before.Length -ne $ExpectedLength -or $before.LastWriteTimeUtc -ne $ExpectedLastWriteTimeUtc -or [long]$before.Length -gt $MaximumBytes){throw "Power-session module file is unsafe or changed: $Path"}
    $stream=[IO.FileStream]::new($before.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,65536,[IO.FileOptions]::SequentialScan)
    $sha=[Security.Cryptography.SHA256]::Create()
    try {
        $digest=$sha.ComputeHash($stream)
        $after=Get-Item -LiteralPath $before.FullName -Force -ErrorAction Stop
        if($after.PSIsContainer -or ($after.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or [long]$after.Length -ne $ExpectedLength -or $after.LastWriteTimeUtc -ne $ExpectedLastWriteTimeUtc){throw "Power-session module file changed while hashing: $Path"}
        try{return [Convert]::ToHexString($digest).ToLowerInvariant()}finally{if($digest.Length){[Array]::Clear($digest,0,$digest.Length)}}
    } finally {$sha.Dispose();$stream.Dispose()}
}

function Get-PowerSessionModuleTreeHash {
    param(
        [Parameter(Mandatory)][string]$Root,
        [ValidateRange(1,100000)][int]$MaximumEntries=10000,
        [ValidateRange(1,50000)][int]$MaximumFiles=5000,
        [ValidateRange(1048576,2147483648)][long]$MaximumTotalBytes=536870912,
        [ValidateRange(1024,67108864)][long]$MaximumMetadataBytes=4194304,
        [ValidateRange(1024,268435456)][long]$MaximumFileBytes=67108864
    )
    $rootPath=[IO.Path]::GetFullPath($Root);$prefix=$rootPath.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    $entryCount=0;$encoding=[Text.UTF8Encoding]::new($false,$true);$paths=[Collections.Generic.List[string]]::new();$records=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal);$totalBytes=0L;$metadataBytes=0L
    foreach($entry in Get-ChildItem -LiteralPath $rootPath -Force -Recurse -ErrorAction Stop){
        $entryCount++;if($entryCount -gt $MaximumEntries){throw "Module tree exceeds $MaximumEntries entries."};if(($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw "Module tree contains a reparse entry: $($entry.FullName)"};if(-not $entry.FullName.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw "Module entry escaped the module root: $($entry.FullName)"};if($entry.PSIsContainer){continue};$file=$entry
        if($paths.Count -ge $MaximumFiles){throw "Module tree exceeds $MaximumFiles files."};if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw "Power-session host refused a reparse member: $($file.FullName)"};if(-not $file.FullName.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw "Module file escaped the module root: $($file.FullName)"};if([long]$file.Length -gt $MaximumFileBytes){throw "Module file exceeds $MaximumFileBytes bytes: $($file.FullName)"}
        $totalBytes+=[long]$file.Length;if($totalBytes -gt $MaximumTotalBytes){throw "Module tree exceeds $MaximumTotalBytes bytes."};$relative=[IO.Path]::GetRelativePath($rootPath,$file.FullName).Replace('\','/');$metadataBytes+=[long]$encoding.GetByteCount($relative)+1L;if($metadataBytes -gt $MaximumMetadataBytes){throw "Module path metadata exceeds $MaximumMetadataBytes bytes."};if(-not $records.TryAdd($relative,[pscustomobject]@{Path=$file.FullName;Length=[long]$file.Length;LastWriteTimeUtc=$file.LastWriteTimeUtc})){throw "Duplicate module relative path: $relative"};$paths.Add($relative)
    }
    $paths.Sort([StringComparer]::Ordinal);$incremental=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try{foreach($relative in $paths){$record=$records[$relative];$hash=Get-PowerSessionFileSha256 -Path $record.Path -ExpectedLength $record.Length -ExpectedLastWriteTimeUtc $record.LastWriteTimeUtc -MaximumBytes $MaximumFileBytes;$bytes=$encoding.GetBytes("$relative`:$hash`n");try{$incremental.AppendData($bytes)}finally{if($bytes.Length){[Array]::Clear($bytes,0,$bytes.Length)}}};$digest=$incremental.GetHashAndReset();try{return [Convert]::ToHexString($digest).ToLowerInvariant()}finally{if($digest.Length){[Array]::Clear($digest,0,$digest.Length)}}}finally{$incremental.Dispose()}
}
