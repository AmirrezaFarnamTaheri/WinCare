function Get-WinCareSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath)
    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) { return $null }
    (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-WinCareSha256Text {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $hash=[Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text))
    [Convert]::ToHexString($hash).ToLowerInvariant()
}


function ConvertTo-WinCareCanonicalValue {
    [CmdletBinding()]
    param([AllowNull()][object]$Value,[ValidateRange(0,100)][int]$Depth=0)
    if($Depth -gt 80){throw 'Canonical serialization exceeded the maximum object depth.'}
    if($null -eq $Value){return $null}
    if($Value -is [datetime]){return $Value.ToUniversalTime().ToString('o')}
    if($Value -is [datetimeoffset]){return $Value.ToUniversalTime().ToString('o')}
    if($Value -is [guid]){return $Value.ToString('D').ToLowerInvariant()}
    if($Value -is [timespan]){return $Value.ToString('c')}
    if($Value -is [enum]){return $Value.ToString()}
    if($Value -is [string] -or $Value -is [char] -or $Value -is [bool] -or $Value.GetType().IsPrimitive -or $Value -is [decimal]){return $Value}
    if($Value -is [Collections.IDictionary]){
        $ordered=[ordered]@{}
        foreach($key in @($Value.Keys|ForEach-Object{[string]$_}|Sort-Object -CaseSensitive)){
            $ordered[$key]=ConvertTo-WinCareCanonicalValue -Value $Value[$key] -Depth ($Depth+1)
        }
        return $ordered
    }
    if($Value -is [Collections.IEnumerable]){
        $items=[Collections.Generic.List[object]]::new()
        foreach($item in $Value){$items.Add((ConvertTo-WinCareCanonicalValue -Value $item -Depth ($Depth+1)))}
        return @($items)
    }
    $properties=@($Value.PSObject.Properties|Where-Object{$_.MemberType -in @('NoteProperty','Property','AliasProperty','ScriptProperty')}|Sort-Object Name -CaseSensitive)
    if($properties.Count -eq 0){return [string]$Value}
    $object=[ordered]@{}
    foreach($property in $properties){$object[$property.Name]=ConvertTo-WinCareCanonicalValue -Value $property.Value -Depth ($Depth+1)}
    return $object
}

function ConvertTo-WinCareCanonicalJson {
    [CmdletBinding()]
    param([AllowNull()][object]$InputObject,[ValidateRange(2,100)][int]$Depth=80)
    ConvertTo-WinCareCanonicalValue -Value $InputObject|ConvertTo-Json -Compress -Depth $Depth
}

function Get-WinCareCanonicalObjectHash {
    [CmdletBinding()]
    param([AllowNull()][object]$InputObject)
    Get-WinCareSha256Text -Text (ConvertTo-WinCareCanonicalJson -InputObject $InputObject -Depth 80)
}

function Get-WinCarePathContentHash {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath)
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if(-not $item.PSIsContainer){return Get-WinCareSha256 -LiteralPath $path}
    $builder=[Text.StringBuilder]::new()
    foreach($entry in Get-ChildItem -LiteralPath $path -Force -Recurse -ErrorAction Stop|Sort-Object FullName){
        if(($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw "Cannot hash a tree containing a reparse point: $($entry.FullName)"}
        $relative=[IO.Path]::GetRelativePath($path,$entry.FullName).Replace('\','/')
        if($entry.PSIsContainer){$null=$builder.Append('D:').Append($relative).Append("`n")}
        else{$null=$builder.Append('F:').Append($relative).Append(':').Append($entry.Length).Append(':').Append((Get-WinCareSha256 $entry.FullName)).Append("`n")}
    }
    Get-WinCareSha256Text -Text $builder.ToString()
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
        $prefix=$parentPath + [IO.Path]::DirectorySeparatorChar
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

function Write-WinCareAtomicJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath,[Parameter(Mandatory)][object]$InputObject,[int]$Depth=40)
    $parent=Split-Path -Parent $LiteralPath
    if ($parent) { $null=New-Item -ItemType Directory -Path $parent -Force }
    $temp=Join-Path $parent ('.{0}.{1}.tmp' -f ([IO.Path]::GetFileName($LiteralPath)),[guid]::NewGuid().ToString('N'))
    try {
        $InputObject | ConvertTo-Json -Depth $Depth | Set-Content -LiteralPath $temp -Encoding utf8NoBOM
        Move-Item -LiteralPath $temp -Destination $LiteralPath -Force
    } finally { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
}

function Write-WinCareAtomicText {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath,[AllowEmptyString()][Parameter(Mandatory)][string]$Text)
    $parent=Split-Path -Parent $LiteralPath
    if ($parent) { $null=New-Item -ItemType Directory -Path $parent -Force }
    $temp=Join-Path $parent ('.{0}.{1}.tmp' -f ([IO.Path]::GetFileName($LiteralPath)),[guid]::NewGuid().ToString('N'))
    try {
        [IO.File]::WriteAllText($temp,$Text,[Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temp -Destination $LiteralPath -Force
    } finally { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
}

function Test-WinCareStrictObjectKeys {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$InputObject,[Parameter(Mandatory)][string[]]$AllowedKeys,[string]$Context='object')
    $keys=if ($InputObject -is [Collections.IDictionary]) {@($InputObject.Keys)} else {@($InputObject.PSObject.Properties.Name)}
    $unknown=@($keys | Where-Object { $_ -notin $AllowedKeys })
    if ($unknown.Count -gt 0) { throw "Unknown $Context field(s): $($unknown -join ', ')" }
    return $true
}

function ConvertTo-WinCareRedactedObject {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$InputObject)
    $json=$InputObject | ConvertTo-Json -Depth 40
    $patterns=@(
        '(?i)(password|token|secret|apikey|api_key|authorization)\s*["'']?\s*[:=]\s*["''][^"'']+["'']',
        '(?i)bearer\s+[A-Za-z0-9._~+/-]+=*',
        '(?i)sk-[A-Za-z0-9_-]{16,}'
    )
    foreach ($pattern in $patterns) { $json=[regex]::Replace($json,$pattern,'$1:"[REDACTED]"') }
    if ((Get-WinCareConfig 'RedactUserNameInReports') -and $env:USERNAME) { $json=$json.Replace($env:USERNAME,'[USER]') }
    return $json | ConvertFrom-Json -Depth 40
}

function Get-WinCareModuleTreeHash {
    [CmdletBinding()]
    param()
    $files=Get-ChildItem -LiteralPath $script:WinCareModuleRoot -File -Recurse | Sort-Object FullName
    $builder=[Text.StringBuilder]::new()
    foreach ($file in $files) {
        $relative=[IO.Path]::GetRelativePath($script:WinCareModuleRoot,$file.FullName).Replace('\','/')
        $null=$builder.Append($relative).Append(':').Append((Get-WinCareSha256 $file.FullName)).Append("`n")
    }
    $hash=[Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($builder.ToString()))
    return [Convert]::ToHexString($hash).ToLowerInvariant()
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

function Get-WinCareLocalIntegrityKey {
    [CmdletBinding()]
    param()
    Ensure-WinCareState
    Initialize-WinCareProtectedDataSupport
    $keyPath=Join-Path (Join-Path $script:WinCareState.Root 'Cache') 'local-integrity.key'
    if(Test-Path -LiteralPath $keyPath -PathType Leaf){
        $protected=[IO.File]::ReadAllBytes($keyPath)
        if($protected.Length -lt 16){throw 'Local integrity key file is invalid.'}
        if($IsWindows){
            try { return [Security.Cryptography.ProtectedData]::Unprotect($protected,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser) }
            catch { throw "Local integrity key could not be unprotected: $($_.Exception.Message)" }
        }
        return $protected
    }
    $key=[byte[]]::new(32);[Security.Cryptography.RandomNumberGenerator]::Fill($key)
    $stored=if($IsWindows){[Security.Cryptography.ProtectedData]::Protect($key,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)}else{$key}
    $parent=Split-Path -Parent $keyPath;$null=New-Item -ItemType Directory -Path $parent -Force
    $temp=Join-Path $parent ('.integrity.'+[guid]::NewGuid().ToString('N')+'.tmp')
    try{[IO.File]::WriteAllBytes($temp,$stored);Move-Item -LiteralPath $temp -Destination $keyPath -Force}finally{Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue}
    if($IsWindows){
        try{
            $acl=Get-Acl -LiteralPath $keyPath;$acl.SetAccessRuleProtection($true,$false)
            foreach($rule in @($acl.Access)){$acl.RemoveAccessRuleAll($rule)}
            $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','Allow'))
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]'S-1-5-18','FullControl','Allow'))
            Set-Acl -LiteralPath $keyPath -AclObject $acl -ErrorAction Stop
        }catch{Remove-Item -LiteralPath $keyPath -Force -ErrorAction SilentlyContinue;throw "Local integrity key ACL could not be secured: $($_.Exception.Message)"}
    }
    return $key
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
    $envelope=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json -AsHashtable -Depth 50
    Unprotect-WinCareJsonRecord -Envelope $envelope -Purpose $Purpose -AsHashtable:$AsHashtable
}
