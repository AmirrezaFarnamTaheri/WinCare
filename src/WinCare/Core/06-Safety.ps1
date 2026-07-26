function Get-WinCareSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath)
    if(-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) { return $null }
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $before=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if($before.PSIsContainer -or ($before.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "SHA-256 inputs must be regular non-reparse files: $path"
    }
    $stream=[IO.FileStream]::new(
        $path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read,
        1048576,
        [IO.FileOptions]::SequentialScan
    )
    $algorithm=[Security.Cryptography.SHA256]::Create()
    try {
        $hash=$algorithm.ComputeHash($stream)
        $after=Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if(
            $after.PSIsContainer -or
            ($after.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            [long]$after.Length -ne [long]$before.Length -or
            $after.LastWriteTimeUtc -ne $before.LastWriteTimeUtc
        ) { throw "SHA-256 input changed while hashing: $path" }
        try { return [Convert]::ToHexString($hash).ToLowerInvariant() }
        finally { if($hash.Length) { [Array]::Clear($hash,0,$hash.Length) } }
    } finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Get-WinCareSha256Text {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [ValidateRange(1,268435456)][int]$MaximumBytes=16777216
    )
    $encoding=[Text.UTF8Encoding]::new($false,$true)
    $byteCount=$encoding.GetByteCount($Text)
    if($byteCount -gt $MaximumBytes) {
        throw "SHA-256 text input exceeds the $MaximumBytes-byte ceiling."
    }
    $bytes=$encoding.GetBytes($Text)
    try {
        $hash=[Security.Cryptography.SHA256]::HashData($bytes)
        try { return [Convert]::ToHexString($hash).ToLowerInvariant() }
        finally { if($hash.Length) { [Array]::Clear($hash,0,$hash.Length) } }
    } finally {
        if($bytes.Length) { [Array]::Clear($bytes,0,$bytes.Length) }
    }
}


function Get-WinCarePathContentHash {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,1000000)][int]$MaximumEntries=100000,
        [ValidateRange(1024,268435456)][long]$MaximumPathMetadataBytes=33554432
    )
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if(-not $item.PSIsContainer){return Get-WinCareSha256 -LiteralPath $path}

    $encoding=[Text.UTF8Encoding]::new($false,$true)
    $relativePaths=[Collections.Generic.List[string]]::new()
    $entries=[Collections.Generic.Dictionary[string,IO.FileSystemInfo]]::new([StringComparer]::Ordinal)
    $pathMetadataBytes=0L
    foreach($entry in Get-ChildItem -LiteralPath $path -Force -Recurse -ErrorAction Stop) {
        if($relativePaths.Count -ge $MaximumEntries) {
            throw "Path contains more than $MaximumEntries entries: $path"
        }
        if(($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Cannot hash a tree containing a reparse point: $($entry.FullName)"
        }
        if(-not (Test-WinCarePathWithin -Child $entry.FullName -Parent $path)) {
            throw "Tree enumeration escaped the approved root: $($entry.FullName)"
        }
        $relative=[IO.Path]::GetRelativePath($path,$entry.FullName).Replace('\','/')
        if([string]::IsNullOrWhiteSpace($relative) -or $relative -eq '.' -or $relative.StartsWith('../',[StringComparison]::Ordinal)) {
            throw "Tree enumeration produced an invalid relative path: $($entry.FullName)"
        }
        $pathMetadataBytes += [long]$encoding.GetByteCount($relative) + 1L
        if($pathMetadataBytes -gt $MaximumPathMetadataBytes) {
            throw "Tree path metadata exceeded the $MaximumPathMetadataBytes-byte ceiling: $path"
        }
        if(-not $entries.TryAdd($relative,$entry)) {
            throw "Tree enumeration produced a duplicate relative path: $relative"
        }
        $relativePaths.Add($relative)
    }
    $relativePaths.Sort([StringComparer]::Ordinal)

    $incremental=[Security.Cryptography.IncrementalHash]::CreateHash(
        [Security.Cryptography.HashAlgorithmName]::SHA256
    )
    try {
        foreach($relative in $relativePaths) {
            $entry=$entries[$relative]
            if($entry.PSIsContainer) {
                $record="D:$relative`n"
            } else {
                $before=Get-Item -LiteralPath $entry.FullName -Force -ErrorAction Stop
                if($before.PSIsContainer -or ($before.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Tree entry changed type during hashing: $($entry.FullName)"
                }
                $hash=Get-WinCareSha256 -LiteralPath $before.FullName
                $after=Get-Item -LiteralPath $before.FullName -Force -ErrorAction Stop
                if(
                    $after.PSIsContainer -or
                    ($after.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                    [long]$after.Length -ne [long]$before.Length -or
                    $after.LastWriteTimeUtc -ne $before.LastWriteTimeUtc
                ) {
                    throw "Tree entry changed during hashing: $($entry.FullName)"
                }
                $record="F:$relative`:$($before.Length):$hash`n"
            }
            $recordBytes=$encoding.GetBytes($record)
            try { $incremental.AppendData($recordBytes) }
            finally { if($recordBytes.Length) { [Array]::Clear($recordBytes,0,$recordBytes.Length) } }
        }
        $digest=$incremental.GetHashAndReset()
        try { return [Convert]::ToHexString($digest).ToLowerInvariant() }
        finally { if($digest.Length) { [Array]::Clear($digest,0,$digest.Length) } }
    } finally {
        $incremental.Dispose()
    }
}

function Measure-WinCarePathTree {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath,[ValidateRange(1,2000000)][int]$MaximumEntries=1000000)
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if(-not $item.PSIsContainer){return [pscustomobject]@{Bytes=[long]$item.Length;Files=1L;Directories=0L}}
    $bytes=0L;$files=0L;$directories=0L;$entries=0
    foreach($entry in Get-ChildItem -LiteralPath $path -Force -Recurse -ErrorAction Stop){
        $entries++;if($entries -gt $MaximumEntries){throw "Path contains more than $MaximumEntries entries."}
        if(($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw "Cannot measure a tree containing a reparse point: $($entry.FullName)"}
        if($entry.PSIsContainer){$directories++}else{$files++;$bytes+=[long]$entry.Length}
    }
    [pscustomobject]@{Bytes=$bytes;Files=$files;Directories=$directories}
}

function Resolve-WinCareCanonicalPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath,[switch]$AllowMissing)
    if ([string]::IsNullOrWhiteSpace($LiteralPath)) { throw 'Path is empty.' }
    $expanded=[Environment]::ExpandEnvironmentVariables($LiteralPath)
    $full=[IO.Path]::GetFullPath($expanded)
    if (-not $AllowMissing -and -not (Test-Path -LiteralPath $full)) { throw "Path does not exist: $full" }
    $root=[IO.Path]::GetPathRoot($full)
    if([string]::Equals($full,$root,[StringComparison]::OrdinalIgnoreCase)){return $root}
    return $full.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
}

function Test-WinCarePathWithin {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Child,[Parameter(Mandatory)][string]$Parent,[switch]$AllowEqual)
    try {
        $childPath=Resolve-WinCareCanonicalPath -LiteralPath $Child -AllowMissing
        $parentPath=Resolve-WinCareCanonicalPath -LiteralPath $Parent -AllowMissing
        $comparison=if ($IsWindows) {[StringComparison]::OrdinalIgnoreCase} else {[StringComparison]::Ordinal}
        if ($AllowEqual -and [string]::Equals($childPath,$parentPath,$comparison)) { return $true }
        $separator=[string][IO.Path]::DirectorySeparatorChar
        $prefix=if($parentPath.EndsWith($separator,[StringComparison]::Ordinal)){$parentPath}else{$parentPath+$separator}
        return $childPath.StartsWith($prefix,$comparison)
    } catch { return $false }
}

function Assert-WinCareSafePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [string[]]$AllowedRoots=@(),
        [switch]$AllowMissing,
        [switch]$AllowReparsePoint
    )
    $full=Resolve-WinCareCanonicalPath -LiteralPath $LiteralPath -AllowMissing:$AllowMissing
    if($IsWindows){
        $root=[IO.Path]::GetPathRoot($full)
        if($full.Substring($root.Length).Contains(':')){throw "Alternate data streams are not allowed: $full"}
    }
    if ($AllowedRoots.Count -gt 0) {
        $inside=$false
        foreach ($root in $AllowedRoots) {
            if (Test-WinCarePathWithin -Child $full -Parent $root -AllowEqual) { $inside=$true; break }
        }
        if (-not $inside) { throw "Path is outside approved roots: $full" }
    }
    if (-not $AllowReparsePoint) {
        $cursorPath=$full
        while (-not (Test-Path -LiteralPath $cursorPath)) {
            $parent=Split-Path -Parent $cursorPath
            if (-not $parent -or $parent -eq $cursorPath) { break }
            $cursorPath=$parent
        }
        if (Test-Path -LiteralPath $cursorPath) {
            $cursor=Get-Item -LiteralPath $cursorPath -Force -ErrorAction Stop
            while ($cursor) {
                if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse points are not allowed: $($cursor.FullName)" }
                $parent=Split-Path -Parent $cursor.FullName
                if (-not $parent -or $parent -eq $cursor.FullName) { break }
                $cursor=Get-Item -LiteralPath $parent -Force -ErrorAction SilentlyContinue
            }
        } elseif (-not $AllowMissing) {
            throw "Path does not exist: $full"
        }
    }
    return $full
}

function Write-WinCareAtomicBytes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [Parameter(Mandatory)][byte[]]$Bytes,
        [ValidateRange(0,1073741824)][long]$MaximumBytes=268435456
    )
    if([long]$Bytes.Length -gt $MaximumBytes){throw "Atomic write payload exceeds the $MaximumBytes-byte ceiling."}
    $path=Resolve-WinCareCanonicalPath -LiteralPath $LiteralPath -AllowMissing
    $parent=Split-Path -Parent $path
    if([string]::IsNullOrWhiteSpace($parent)){throw 'Atomic writes require a path with a parent directory.'}
    $null=Assert-WinCareSafePath -LiteralPath $parent -AllowMissing
    $null=New-Item -ItemType Directory -Path $parent -Force
    $null=Assert-WinCareSafePath -LiteralPath $parent
    $null=Assert-WinCareSafePath -LiteralPath $path -AllowMissing
    if((Test-Path -LiteralPath $path) -and -not (Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Atomic write target is not a regular file: $path"
    }
    $beforeExists=Test-Path -LiteralPath $path -PathType Leaf
    $beforeHash=if($beforeExists){Get-WinCareSha256 -LiteralPath $path}else{''}
    $digest=[Security.Cryptography.SHA256]::HashData($Bytes)
    try{$expectedHash=[Convert]::ToHexString($digest).ToLowerInvariant()}
    finally{if($digest.Length){[Array]::Clear($digest,0,$digest.Length)}}
    $leaf=[IO.Path]::GetFileName($path)
    $temp=Join-Path $parent ('.{0}.{1}.tmp' -f $leaf,[guid]::NewGuid().ToString('N'))
    $backup=Join-Path $parent ('.{0}.{1}.bak' -f $leaf,[guid]::NewGuid().ToString('N'))
    $failed=Join-Path $parent ('.{0}.{1}.failed' -f $leaf,[guid]::NewGuid().ToString('N'))
    $replacedExisting=$false
    $createdNew=$false
    $preserveRecoveryArtifacts=$false
    try{
        $stream=[IO.FileStream]::new($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,65536,[IO.FileOptions]::WriteThrough)
        try{$stream.Write($Bytes,0,$Bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
        $null=Assert-WinCareSafePath -LiteralPath $temp
        if((Get-WinCareSha256 -LiteralPath $temp) -ne $expectedHash){throw 'Atomic staging hash verification failed.'}
        $null=Assert-WinCareSafePath -LiteralPath $path -AllowMissing
        if($beforeExists){
            [IO.File]::Replace($temp,$path,$backup,$false)
            $replacedExisting=$true
        }else{
            [IO.File]::Move($temp,$path)
            $createdNew=$true
        }
        if((Get-WinCareSha256 -LiteralPath $path) -ne $expectedHash){throw 'Atomic destination hash verification failed.'}
        Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    }catch{
        $primary=$_.Exception
        try{
            if($replacedExisting -and (Test-Path -LiteralPath $backup -PathType Leaf)){
                if(Test-Path -LiteralPath $path -PathType Leaf){[IO.File]::Replace($backup,$path,$failed,$false)}
                else{[IO.File]::Move($backup,$path)}
                if((Get-WinCareSha256 -LiteralPath $path) -ne $beforeHash){throw 'Atomic write rollback hash verification failed.'}
                Remove-Item -LiteralPath $failed -Force -ErrorAction SilentlyContinue
            }elseif($createdNew -and (Test-Path -LiteralPath $path)){
                Remove-Item -LiteralPath $path -Force -ErrorAction Stop
            }elseif(-not (Test-Path -LiteralPath $path) -and (Test-Path -LiteralPath $backup -PathType Leaf)){
                [IO.File]::Move($backup,$path)
                if($beforeHash -and (Get-WinCareSha256 -LiteralPath $path) -ne $beforeHash){throw 'Atomic write emergency recovery hash verification failed.'}
            }
        }catch{
            $preserveRecoveryArtifacts=$true
            throw [AggregateException]::new('Atomic write failed and rollback also failed.',[Exception[]]@($primary,$_.Exception))
        }
        throw $primary
    }finally{
        Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
        if(-not $preserveRecoveryArtifacts){
            if(Test-Path -LiteralPath $path -PathType Leaf){Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue}
            Remove-Item -LiteralPath $failed -Force -ErrorAction SilentlyContinue
        }
    }
}

function Write-WinCareAtomicJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [Parameter(Mandatory)][object]$InputObject,
        [ValidateRange(2,100)][int]$Depth=40
    )
    $json=$InputObject|ConvertTo-Json -Depth $Depth
    Write-WinCareAtomicBytes -LiteralPath $LiteralPath -Bytes ([Text.UTF8Encoding]::new($false).GetBytes($json+"`n"))
}

function Write-WinCareAtomicText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [AllowEmptyString()][Parameter(Mandatory)][string]$Text
    )
    Write-WinCareAtomicBytes -LiteralPath $LiteralPath -Bytes ([Text.UTF8Encoding]::new($false).GetBytes($Text))
}

function Test-WinCareStrictObjectKeys {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$InputObject,[Parameter(Mandatory)][string[]]$AllowedKeys,[string]$Context='object')
    $keys=if ($InputObject -is [Collections.IDictionary]) {@($InputObject.Keys)} else {@($InputObject.PSObject.Properties.Name)}
    $unknown=@($keys | Where-Object { $_ -notin $AllowedKeys })
    if ($unknown.Count -gt 0) { throw "Unknown $Context field(s): $($unknown -join ', ')" }
    return $true
}

function Test-WinCareSensitivePropertyName {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name)
    return $Name -match '(?i)^(password|passwd|pwd|token|access[-_]?token|refresh[-_]?token|secret|api[-_]?key|authorization|credential|private[-_]?key|client[-_]?secret)$'
}

function ConvertTo-WinCareRedactedScalar {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Value,
        [ValidateRange(16,1048576)][int]$MaximumStringCharacters=65536
    )
    if($null -eq $Value -or $Value -isnot [string]) { return $Value }
    $redacted=[string]$Value
    $redacted=[regex]::Replace($redacted,'(?i)\bBearer\s+[A-Za-z0-9._~+/-]+=*','Bearer [REDACTED]')
    $redacted=[regex]::Replace($redacted,'(?i)\bsk-[A-Za-z0-9_-]{16,}\b','[REDACTED]')
    $redacted=[regex]::Replace(
        $redacted,
        '(?i)\b(password|passwd|pwd|token|secret|api[-_]?key|authorization|client[-_]?secret)\b(\s*[:=]\s*)(?:"[^"]*"|''[^'']*''|[^\s,;]+)',
        '${1}${2}[REDACTED]'
    )
    $redactUser=$false
    if($script:WinCareState -is [Collections.IDictionary] -and $script:WinCareState.ContainsKey('Config')) {
        $redactUser=[bool]$script:WinCareState.Config.RedactUserNameInReports
    }
    if($redactUser -and -not [string]::IsNullOrWhiteSpace($env:USERNAME)) {
        $redacted=[regex]::Replace(
            $redacted,
            [regex]::Escape($env:USERNAME),
            '[USER]',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase
        )
    }
    if($redacted.Length -gt $MaximumStringCharacters) {
        return $redacted.Substring(0,$MaximumStringCharacters) + '[TRUNCATED]'
    }
    return $redacted
}

function ConvertTo-WinCareRedactedValueInternal {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][hashtable]$State,
        [ValidateRange(0,64)][int]$Depth=0
    )
    if($null -eq $Value) { return $null }
    if($Depth -ge $State.MaximumDepth) { return '[MAXIMUM DEPTH]' }
    $State.Items++
    if($State.Items -gt $State.MaximumItems) { return '[ITEM LIMIT]' }
    if($Value -is [string]) {
        return ConvertTo-WinCareRedactedScalar -Value $Value -MaximumStringCharacters $State.MaximumStringCharacters
    }
    $isScalar=$Value -is [datetime] -or
        $Value -is [datetimeoffset] -or
        $Value -is [guid] -or
        $Value -is [timespan] -or
        $Value -is [enum] -or
        $Value.GetType().IsPrimitive -or
        $Value -is [decimal]
    if($isScalar) { return $Value }

    $identity=[Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($Value)
    if(-not $State.Seen.Add($identity)) { return '[CYCLE]' }
    try {
        if($Value -is [Collections.IDictionary]) {
            $copy=[ordered]@{}
            $count=0
            foreach($key in @($Value.Keys | Sort-Object { [string]$_ })) {
                $count++
                if($count -gt $State.MaximumProperties) { $copy['__truncated__']='[PROPERTY LIMIT]'; break }
                $name=ConvertTo-WinCareRedactedScalar -Value ([string]$key) -MaximumStringCharacters 256
                $copy[$name]=if(Test-WinCareSensitivePropertyName -Name $name) {
                    '[REDACTED]'
                } else {
                    ConvertTo-WinCareRedactedValueInternal -Value $Value[$key] -State $State -Depth ($Depth+1)
                }
            }
            return $copy
        }
        if($Value -is [Collections.IEnumerable]) {
            $copy=[Collections.Generic.List[object]]::new()
            $count=0
            foreach($item in $Value) {
                $count++
                if($count -gt $State.MaximumCollectionItems) { $copy.Add('[COLLECTION LIMIT]'); break }
                $copy.Add((ConvertTo-WinCareRedactedValueInternal -Value $item -State $State -Depth ($Depth+1)))
            }
            return @($copy)
        }
        $copy=[ordered]@{}
        $properties=@($Value.PSObject.Properties | Where-Object {
            $_.MemberType -in @('NoteProperty','Property','AliasProperty')
        } | Sort-Object Name)
        $count=0
        foreach($property in $properties) {
            $count++
            if($count -gt $State.MaximumProperties) { $copy['__truncated__']='[PROPERTY LIMIT]'; break }
            $name=[string]$property.Name
            $copy[$name]=if(Test-WinCareSensitivePropertyName -Name $name) {
                '[REDACTED]'
            } else {
                try {
                    ConvertTo-WinCareRedactedValueInternal -Value $property.Value -State $State -Depth ($Depth+1)
                } catch {
                    '[UNREADABLE PROPERTY]'
                }
            }
        }
        if($properties.Count -eq 0) {
            return ConvertTo-WinCareRedactedScalar -Value ([string]$Value) -MaximumStringCharacters $State.MaximumStringCharacters
        }
        return $copy
    } finally {
        $null=$State.Seen.Remove($identity)
    }
}

function ConvertTo-WinCareRedactedValue {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Value,
        [ValidateRange(1,64)][int]$MaximumDepth=24,
        [ValidateRange(1,100000)][int]$MaximumItems=10000,
        [ValidateRange(1,10000)][int]$MaximumCollectionItems=2048,
        [ValidateRange(1,10000)][int]$MaximumProperties=512,
        [ValidateRange(16,1048576)][int]$MaximumStringCharacters=65536
    )
    $state=@{
        MaximumDepth=$MaximumDepth
        MaximumItems=$MaximumItems
        MaximumCollectionItems=$MaximumCollectionItems
        MaximumProperties=$MaximumProperties
        MaximumStringCharacters=$MaximumStringCharacters
        Items=0
        Seen=[Collections.Generic.HashSet[int]]::new()
    }
    ConvertTo-WinCareRedactedValueInternal -Value $Value -State $state
}

function ConvertTo-WinCareRedactedObject {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$InputObject)
    ConvertTo-WinCareRedactedValue -Value $InputObject
}

function Test-WinCareMaintenanceWindow {
    [CmdletBinding()]
    param([string]$Window)
    if ([string]::IsNullOrWhiteSpace($Window)) { return $true }
    if ($Window -notmatch '^(?<start>\d{2}:\d{2})-(?<end>\d{2}:\d{2})$') { throw "Invalid maintenance window: $Window" }
    $now=[datetime]::Now.TimeOfDay
    $start=[timespan]::ParseExact($Matches.start,'hh\:mm',$null)
    $end=[timespan]::ParseExact($Matches.end,'hh\:mm',$null)
    if ($start -le $end) { return $now -ge $start -and $now -le $end }
    return $now -ge $start -or $now -le $end
}

function Initialize-WinCareProtectedDataSupport {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return}
    if(-not ('System.Security.Cryptography.ProtectedData' -as [type])){
        try{Add-Type -AssemblyName System.Security.Cryptography.ProtectedData -ErrorAction Stop}
        catch{throw "Windows data-protection support is unavailable: $($_.Exception.Message)"}
    }
}

function Read-WinCareLocalIntegrityKeyFile {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath)
    $stored=Read-WinCareBoundedFileBytes -LiteralPath $LiteralPath -MaximumBytes 4096
    if($stored.Length -lt 16){throw 'Local integrity key file is invalid.'}
    $key=if($IsWindows){
        try{[Security.Cryptography.ProtectedData]::Unprotect($stored,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)}
        catch{throw "Local integrity key could not be unprotected: $($_.Exception.Message)"}
    }else{$stored}
    if($key.Length -ne 32){[Array]::Clear($key,0,$key.Length);throw 'Local integrity key length is invalid.'}
    return $key
}

function Get-WinCareLocalIntegrityKey {
    [CmdletBinding()]
    param([ValidateRange(1,60)][int]$LockTimeoutSeconds=10)
    Ensure-WinCareState
    Initialize-WinCareProtectedDataSupport
    $parent=Join-Path $script:WinCareState.Root 'Cache'
    $null=New-Item -ItemType Directory -Path $parent -Force
    $null=Assert-WinCareSafePath -LiteralPath $parent
    $keyPath=Join-Path $parent 'local-integrity.key'
    $lockPath=Join-Path $parent 'local-integrity.key.lock'
    if(Test-Path -LiteralPath $keyPath -PathType Leaf){return Read-WinCareLocalIntegrityKeyFile -LiteralPath $keyPath}
    $deadline=[datetime]::UtcNow.AddSeconds($LockTimeoutSeconds)
    $lock=$null
    while($null -eq $lock -and [datetime]::UtcNow -lt $deadline){
        try{$lock=[IO.FileStream]::new($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}
        catch [IO.IOException]{Start-Sleep -Milliseconds 50}
    }
    if($null -eq $lock){throw 'Timed out acquiring the local integrity-key initialization lock.'}
    try{
        if(Test-Path -LiteralPath $keyPath -PathType Leaf){return Read-WinCareLocalIntegrityKeyFile -LiteralPath $keyPath}
        if(Test-Path -LiteralPath $keyPath){throw 'Local integrity-key path exists but is not a regular file.'}
        $key=[byte[]]::new(32)
        [Security.Cryptography.RandomNumberGenerator]::Fill($key)
        $stored=if($IsWindows){[Security.Cryptography.ProtectedData]::Protect($key,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)}else{$key.Clone()}
        $created=$false
        try{
            $stream=[IO.FileStream]::new($keyPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::WriteThrough)
            try{$stream.Write($stored,0,$stored.Length);$stream.Flush($true)}finally{$stream.Dispose()}
            $created=$true
            if($IsWindows){
                $acl=Get-Acl -LiteralPath $keyPath
                $acl.SetAccessRuleProtection($true,$false)
                foreach($rule in @($acl.Access)){$acl.RemoveAccessRuleAll($rule)}
                $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
                $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','Allow'))
                $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]'S-1-5-18','FullControl','Allow'))
                Set-Acl -LiteralPath $keyPath -AclObject $acl -ErrorAction Stop
            }elseif([IO.File].GetMethod('SetUnixFileMode')){
                [IO.File]::SetUnixFileMode($keyPath,[IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)
            }
            return $key
        }catch{
            if($created){Remove-Item -LiteralPath $keyPath -Force -ErrorAction SilentlyContinue}
            [Array]::Clear($key,0,$key.Length)
            throw "Local integrity key could not be initialized securely: $($_.Exception.Message)"
        }finally{if($stored -is [byte[]]){[Array]::Clear($stored,0,$stored.Length)}}
    }finally{$lock.Dispose()}
}

function Protect-WinCareJsonRecord {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$InputObject,[string]$Purpose='WinCare.Record')
    $payloadJson=$InputObject|ConvertTo-Json -Compress -Depth 50
    $payloadBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payloadJson))
    $key=Get-WinCareLocalIntegrityKey
    try{
        $hmac=[Security.Cryptography.HMACSHA256]::new($key)
        $text="$Purpose`n$payloadBase64"
        $signature=[Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
        [ordered]@{SchemaVersion=1;Purpose=$Purpose;PayloadBase64=$payloadBase64;Signature=$signature}
    }finally{if($hmac){$hmac.Dispose()};[Array]::Clear($key,0,$key.Length)}
}

function Unprotect-WinCareJsonRecord {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Envelope,[string]$Purpose='WinCare.Record',[switch]$AsHashtable)
    $null=Test-WinCareStrictObjectKeys -InputObject $Envelope -AllowedKeys @('SchemaVersion','Purpose','PayloadBase64','Signature') -Context 'protected record'
    if([int]$Envelope.SchemaVersion -ne 1 -or [string]$Envelope.Purpose -ne $Purpose){throw 'Protected record schema or purpose is invalid.'}
    if([string]$Envelope.Signature -notmatch '^[a-fA-F0-9]{64}$'){throw 'Protected record signature is invalid.'}
    if([string]$Envelope.PayloadBase64 -notmatch '^[A-Za-z0-9+/]*={0,2}$' -or [string]$Envelope.PayloadBase64.Length -gt 16777216){throw 'Protected record payload is invalid or oversized.'}
    $key=Get-WinCareLocalIntegrityKey
    try{
        $hmac=[Security.Cryptography.HMACSHA256]::new($key)
        $text="$Purpose`n$([string]$Envelope.PayloadBase64)"
        $expected=$hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))
        $actual=[Convert]::FromHexString([string]$Envelope.Signature)
        if(-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals($expected,$actual)){throw 'Protected record authentication failed.'}
        $json=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$Envelope.PayloadBase64))
        if($AsHashtable){return $json|ConvertFrom-Json -AsHashtable -Depth 50}
        return $json|ConvertFrom-Json -Depth 50
    }finally{if($hmac){$hmac.Dispose()};[Array]::Clear($key,0,$key.Length)}
}

function Write-WinCareProtectedJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath,[Parameter(Mandatory)][object]$InputObject,[string]$Purpose='WinCare.Record')
    Write-WinCareAtomicJson -LiteralPath $LiteralPath -InputObject (Protect-WinCareJsonRecord -InputObject $InputObject -Purpose $Purpose)
}

function Read-WinCareProtectedJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath,[string]$Purpose='WinCare.Record',[switch]$AsHashtable)
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $envelope=Read-WinCareBoundedJson -LiteralPath $path -MaximumBytes 16777216 -Depth 50 -AsHashtable
    Unprotect-WinCareJsonRecord -Envelope $envelope -Purpose $Purpose -AsHashtable:$AsHashtable
}

function New-WinCareIsolatedStagingDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Prefix
    )
    $parent = [System.IO.Path]::GetTempPath()
    $folderName = "$Prefix-$([System.Guid]::NewGuid().ToString('N'))"
    $fullPath = [System.IO.Path]::Combine($parent, $folderName)

    $dirInfo = [System.IO.Directory]::CreateDirectory($fullPath)
    if ($IsWindows) {
        try {
            $acl = $dirInfo.GetAccessControl()
            $acl.SetAccessRuleProtection($true, $false)

            $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
            $systemUser = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::LocalSystemSid)
            $adminUser = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid)

            $fullControl = [System.Security.AccessControl.FileSystemRights]::FullControl
            $inheritFlags = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
            $propFlags = [System.Security.AccessControl.PropagationFlags]::None
            $allow = [System.Security.AccessControl.AccessControlType]::Allow

            $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($currentUser, $fullControl, $inheritFlags, $propFlags, $allow)))
            $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($systemUser, $fullControl, $inheritFlags, $propFlags, $allow)))
            $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($adminUser, $fullControl, $inheritFlags, $propFlags, $allow)))

            $dirInfo.SetAccessControl($acl)
        } catch {}
    }
    return $fullPath
}

