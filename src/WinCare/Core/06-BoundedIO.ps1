function Read-WinCareBoundedStreamBytes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][IO.Stream]$Stream,
        [ValidateRange(1,1073741824)][long]$MaximumBytes=4194304,
        [ValidateRange(-1,1073741824)][long]$ExpectedBytes=-1
    )
    if(-not $Stream.CanRead) { throw 'The supplied stream is not readable.' }
    if($ExpectedBytes -gt $MaximumBytes) { throw "Expected stream length exceeds the $MaximumBytes-byte ceiling." }
    $capacity=[int][Math]::Min($MaximumBytes,[Math]::Max(0L,$ExpectedBytes))
    $memory=[IO.MemoryStream]::new($capacity)
    $buffer=[byte[]]::new(65536)
    try {
        while(($read=$Stream.Read($buffer,0,$buffer.Length)) -gt 0) {
            if($memory.Length + $read -gt $MaximumBytes) { throw "Stream exceeded the $MaximumBytes-byte read ceiling." }
            $memory.Write($buffer,0,$read)
        }
        if($ExpectedBytes -ge 0 -and $memory.Length -ne $ExpectedBytes) {
            throw "Stream length changed during reading: expected $ExpectedBytes, observed $($memory.Length)."
        }
        return $memory.ToArray()
    } finally {
        [Array]::Clear($buffer,0,$buffer.Length)
        $memory.Dispose()
    }
}

function Read-WinCareBoundedStreamText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][IO.Stream]$Stream,
        [ValidateRange(1,1073741824)][long]$MaximumBytes=4194304,
        [ValidateRange(-1,1073741824)][long]$ExpectedBytes=-1
    )
    $bytes=Read-WinCareBoundedStreamBytes -Stream $Stream -MaximumBytes $MaximumBytes -ExpectedBytes $ExpectedBytes
    try { return [Text.UTF8Encoding]::new($false,$true).GetString($bytes) }
    catch [Text.DecoderFallbackException] { throw 'Stream is not valid UTF-8.' }
    finally { if($bytes.Length) { [Array]::Clear($bytes,0,$bytes.Length) } }
}

function Read-WinCareBoundedFileBytes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,1073741824)][long]$MaximumBytes=4194304
    )
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Bounded reads require a regular non-reparse file: $path"
    }
    if([long]$item.Length -gt $MaximumBytes) {
        throw "File exceeds the $MaximumBytes-byte read ceiling: $path"
    }
    $stream=[IO.FileStream]::new(
        $path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read,
        65536,
        [IO.FileOptions]::SequentialScan
    )
    $memory=[IO.MemoryStream]::new([int][Math]::Min($MaximumBytes, [Math]::Max(0L, [long]$item.Length)))
    $buffer=[byte[]]::new(65536)
    try {
        while(($read=$stream.Read($buffer,0,$buffer.Length)) -gt 0) {
            if($memory.Length + $read -gt $MaximumBytes) {
                throw "File grew beyond the $MaximumBytes-byte read ceiling: $path"
            }
            $memory.Write($buffer,0,$read)
        }
        if($memory.Length -ne [long]$item.Length) {
            throw "File length changed during reading: expected $($item.Length), observed $($memory.Length): $path"
        }
        return $memory.ToArray()
    } finally {
        [Array]::Clear($buffer,0,$buffer.Length)
        $memory.Dispose()
        $stream.Dispose()
    }
}

function Read-WinCareBoundedText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,1073741824)][long]$MaximumBytes=4194304
    )
    $bytes=Read-WinCareBoundedFileBytes -LiteralPath $LiteralPath -MaximumBytes $MaximumBytes
    try {
        return [Text.UTF8Encoding]::new($false,$true).GetString($bytes)
    } catch [Text.DecoderFallbackException] {
        throw "File is not valid UTF-8: $LiteralPath"
    } finally {
        if($bytes.Length -gt 0) { [Array]::Clear($bytes,0,$bytes.Length) }
    }
}

function Read-WinCareBoundedJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,1073741824)][long]$MaximumBytes=4194304,
        [ValidateRange(2,100)][int]$Depth=50,
        [switch]$AsHashtable
    )
    $text=Read-WinCareBoundedText -LiteralPath $LiteralPath -MaximumBytes $MaximumBytes
    if([string]::IsNullOrWhiteSpace($text)) { throw "JSON file is empty: $LiteralPath" }
    if($AsHashtable) { return $text | ConvertFrom-Json -AsHashtable -Depth $Depth }
    return $text | ConvertFrom-Json -Depth $Depth
}

function Read-WinCareBoundedLines {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,1073741824)][long]$MaximumBytes=4194304,
        [ValidateRange(1,1000000)][int]$MaximumLines=50000,
        [ValidateRange(1,1048576)][int]$MaximumLineCharacters=65536
    )
    $text=Read-WinCareBoundedText -LiteralPath $LiteralPath -MaximumBytes $MaximumBytes
    $lines=[regex]::Split($text,"\r\n|\n|\r")
    if($lines.Count -gt $MaximumLines) {
        throw "File exceeds the $MaximumLines-line ceiling: $LiteralPath"
    }
    foreach($line in $lines) {
        if($line.Length -gt $MaximumLineCharacters) {
            throw "File contains a line longer than $MaximumLineCharacters characters: $LiteralPath"
        }
    }
    return @($lines)
}

function Get-WinCareBoundedTreeEntry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,2000000)][int]$MaximumEntries=100000,
        [ValidateRange(1024,268435456)][long]$MaximumPathMetadataBytes=33554432,
        [ValidateSet('All','File','Directory')][string]$EntryType='All'
    )
    $root=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $rootItem=Get-Item -LiteralPath $root -Force -ErrorAction Stop
    if(-not $rootItem.PSIsContainer -or ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Bounded tree traversal requires a regular directory: $root"
    }
    $encoding=[Text.UTF8Encoding]::new($false,$true)
    $count=0
    $metadataBytes=0L
    foreach($entry in Get-ChildItem -LiteralPath $root -Force -Recurse -ErrorAction Stop) {
        $count++
        if($count -gt $MaximumEntries) { throw "Tree exceeds the $MaximumEntries-entry ceiling: $root" }
        if(($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Tree contains a reparse entry: $($entry.FullName)"
        }
        if(-not (Test-WinCarePathWithin -Child $entry.FullName -Parent $root)) {
            throw "Tree traversal escaped the approved root: $($entry.FullName)"
        }
        $relative=[IO.Path]::GetRelativePath($root,$entry.FullName).Replace('\','/')
        if([string]::IsNullOrWhiteSpace($relative) -or $relative -eq '.' -or $relative.StartsWith('../',[StringComparison]::Ordinal)) {
            throw "Tree traversal produced an unsafe relative path: $($entry.FullName)"
        }
        $metadataBytes += [long]$encoding.GetByteCount($relative)+1L
        if($metadataBytes -gt $MaximumPathMetadataBytes) {
            throw "Tree path metadata exceeds the $MaximumPathMetadataBytes-byte ceiling: $root"
        }
        if($EntryType -eq 'File' -and $entry.PSIsContainer) { continue }
        if($EntryType -eq 'Directory' -and -not $entry.PSIsContainer) { continue }
        Write-Output $entry
    }
}
