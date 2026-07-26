#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RequestPath,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$RequestSha256,
    [Parameter(Mandatory)][string]$SecretPath,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$SecretSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-LocalSha256Bytes {
    param([Parameter(Mandatory)][byte[]]$Bytes)
    $hash = [Security.Cryptography.SHA256]::HashData($Bytes)
    try { return [Convert]::ToHexString($hash).ToLowerInvariant() }
    finally { [Array]::Clear($hash, 0, $hash.Length) }
}

function Get-LocalTextSha256 {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    try { return Get-LocalSha256Bytes -Bytes $bytes }
    finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}

function Get-LocalHmac {
    param([Parameter(Mandatory)][byte[]]$Key, [Parameter(Mandatory)][string]$Text)
    $hmac = [Security.Cryptography.HMACSHA256]::new($Key)
    try {
        $hash = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))
        try { return [Convert]::ToHexString($hash).ToLowerInvariant() }
        finally { [Array]::Clear($hash, 0, $hash.Length) }
    } finally { $hmac.Dispose() }
}

function Test-LocalFixedHex {
    param([Parameter(Mandatory)][string]$Expected, [Parameter(Mandatory)][string]$Actual)
    if ($Expected -notmatch '^[a-fA-F0-9]{64}$' -or
        $Actual -notmatch '^[a-fA-F0-9]{64}$') { return $false }
    return [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
        [Convert]::FromHexString($Expected),
        [Convert]::FromHexString($Actual)
    )
}

function Resolve-LocalPath {
    param([Parameter(Mandatory)][string]$Path, [switch]$AllowMissing)
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $AllowMissing -and -not (Test-Path -LiteralPath $full)) {
        throw "Path does not exist: $full"
    }
    return $full.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    )
}

function Test-LocalWithin {
    param(
        [Parameter(Mandatory)][string]$Child,
        [Parameter(Mandatory)][string]$Parent,
        [switch]$AllowEqual
    )
    $childPath = Resolve-LocalPath $Child -AllowMissing
    $parentPath = Resolve-LocalPath $Parent -AllowMissing
    $comparison = [StringComparison]::OrdinalIgnoreCase
    if ($AllowEqual -and [string]::Equals($childPath, $parentPath, $comparison)) {
        return $true
    }
    return $childPath.StartsWith(
        $parentPath + [IO.Path]::DirectorySeparatorChar,
        $comparison
    )
}

function Assert-LocalNoReparse {
    param([Parameter(Mandatory)][string]$Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    while ($item) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Reparse point is not allowed: $($item.FullName)"
        }
        $parent = Split-Path -Parent $item.FullName
        if (-not $parent -or $parent -eq $item.FullName) { break }
        $item = Get-Item -LiteralPath $parent -Force -ErrorAction SilentlyContinue
    }
}

function Read-LocalBoundedBytes {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][ValidateRange(1, 16777216)][int]$MaximumBytes
    )
    $stream = [IO.FileStream]::new(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::None,
        4096,
        [IO.FileOptions]::SequentialScan
    )
    try {
        if ($stream.Length -lt 1 -or $stream.Length -gt $MaximumBytes) {
            throw "File length is outside 1..$MaximumBytes bytes: $Path"
        }
        $bytes = [byte[]]::new([int]$stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -le 0) { throw "File ended unexpectedly: $Path" }
            $offset += $read
        }
        return $bytes
    } finally { $stream.Dispose() }
}

function Get-LocalFileSha256 {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][long]$ExpectedLength,
        [Parameter(Mandatory)][datetime]$ExpectedLastWriteTimeUtc,
        [ValidateRange(1024,268435456)][long]$MaximumBytes=67108864
    )
    $before=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if($before.PSIsContainer -or ($before.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or [long]$before.Length -ne $ExpectedLength -or $before.LastWriteTimeUtc -ne $ExpectedLastWriteTimeUtc -or [long]$before.Length -gt $MaximumBytes){throw "Elevated module file is unsafe or changed: $Path"}
    $stream=[IO.FileStream]::new($before.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,65536,[IO.FileOptions]::SequentialScan);$sha=[Security.Cryptography.SHA256]::Create()
    try{$digest=$sha.ComputeHash($stream);$after=Get-Item -LiteralPath $before.FullName -Force -ErrorAction Stop;if($after.PSIsContainer -or ($after.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or [long]$after.Length -ne $ExpectedLength -or $after.LastWriteTimeUtc -ne $ExpectedLastWriteTimeUtc){throw "Elevated module file changed while hashing: $Path"};try{return [Convert]::ToHexString($digest).ToLowerInvariant()}finally{if($digest.Length){[Array]::Clear($digest,0,$digest.Length)}}}finally{$sha.Dispose();$stream.Dispose()}
}

function Get-LocalModuleTreeHash {
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
        if($paths.Count -ge $MaximumFiles){throw "Module tree exceeds $MaximumFiles files."};Assert-LocalNoReparse $file.FullName;if(-not $file.FullName.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw "Module file escaped the module root: $($file.FullName)"};if([long]$file.Length -gt $MaximumFileBytes){throw "Module file exceeds $MaximumFileBytes bytes: $($file.FullName)"}
        $totalBytes+=[long]$file.Length;if($totalBytes -gt $MaximumTotalBytes){throw "Module tree exceeds $MaximumTotalBytes bytes."};$relative=[IO.Path]::GetRelativePath($rootPath,$file.FullName).Replace('\','/');$metadataBytes+=[long]$encoding.GetByteCount($relative)+1L;if($metadataBytes -gt $MaximumMetadataBytes){throw "Module path metadata exceeds $MaximumMetadataBytes bytes."};if(-not $records.TryAdd($relative,[pscustomobject]@{Path=$file.FullName;Length=[long]$file.Length;LastWriteTimeUtc=$file.LastWriteTimeUtc})){throw "Duplicate module relative path: $relative"};$paths.Add($relative)
    }
    $paths.Sort([StringComparer]::Ordinal);$incremental=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try{foreach($relative in $paths){$record=$records[$relative];$hash=Get-LocalFileSha256 -Path $record.Path -ExpectedLength $record.Length -ExpectedLastWriteTimeUtc $record.LastWriteTimeUtc -MaximumBytes $MaximumFileBytes;$bytes=$encoding.GetBytes("$relative`:$hash`n");try{$incremental.AppendData($bytes)}finally{if($bytes.Length){[Array]::Clear($bytes,0,$bytes.Length)}}};$digest=$incremental.GetHashAndReset();try{return [Convert]::ToHexString($digest).ToLowerInvariant()}finally{if($digest.Length){[Array]::Clear($digest,0,$digest.Length)}}}finally{$incremental.Dispose()}
}

function Write-LocalExclusiveJson {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object]$Object
    )
    $parent = Split-Path -Parent $Path
    Assert-LocalNoReparse $parent
    if (Test-Path -LiteralPath $Path) {
        throw 'The elevated result path already exists.'
    }
    $json = $Object | ConvertTo-Json -Depth 50
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
    if ($bytes.Length -gt 8388608) {
        [Array]::Clear($bytes, 0, $bytes.Length)
        throw 'The elevated result exceeds 8 MiB.'
    }
    $stream = $null
    try {
        $stream = [IO.FileStream]::new(
            $Path,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None,
            4096,
            [IO.FileOptions]::WriteThrough
        )
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    } finally {
        if ($stream) { $stream.Dispose() }
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Read-LocalOneTimeSecret {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedSha256,
        [Parameter(Mandatory)][string]$RequestDirectory
    )
    $full = Resolve-LocalPath $Path
    if (-not (Test-LocalWithin $full $RequestDirectory)) {
        throw 'Elevation secret is outside the authenticated handoff directory.'
    }
    Assert-LocalNoReparse $full
    $bytes = Read-LocalBoundedBytes -Path $full -MaximumBytes 64
    try {
        if ($bytes.Length -ne 32) { throw 'Elevation secret length is invalid.' }
        if (-not (Test-LocalFixedHex (Get-LocalSha256Bytes $bytes) $ExpectedSha256)) {
            throw 'Elevation secret hash mismatch.'
        }
        Remove-Item -LiteralPath $full -Force -ErrorAction Stop
        return $bytes
    } catch {
        [Array]::Clear($bytes, 0, $bytes.Length)
        throw
    }
}

function New-LocalFailureResult {
    param([AllowNull()][Collections.IDictionary]$Payload, [Parameter(Mandatory)][string]$Message)
    return [ordered]@{
        PSTypeName = 'WinCare.Result'
        SchemaVersion = 3
        Success = $false
        Status = 'Failed'
        Code = 'ElevatedHostFailure'
        Message = $Message
        Data = $null
        ExitCode = 1
        Warnings = @()
        OperationId = if ($Payload) { $Payload.OperationId } else { '' }
        ActionId = if ($Payload) { $Payload.ActionId } else { '' }
        Timestamp = [datetime]::UtcNow
    }
}

function Write-LocalResultEnvelope {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][byte[]]$Secret,
        [AllowNull()][Collections.IDictionary]$Payload,
        [Parameter(Mandatory)][object]$Result,
        [Parameter(Mandatory)][string]$ModuleTreeHash
    )
    $resultPayload = [ordered]@{
        SchemaVersion = 3
        Nonce = if ($Payload) { $Payload.Nonce } else { '' }
        OperationId = if ($Payload) { $Payload.OperationId } else { '' }
        ActionId = if ($Payload) { $Payload.ActionId } else { '' }
        ActionHash = if ($Payload) { $Payload.ActionHash } else { '' }
        ModuleTreeHash = $ModuleTreeHash
        FinishedAt = [datetime]::UtcNow.ToString('o')
        Result = $Result
    }
    $resultJson = $resultPayload | ConvertTo-Json -Compress -Depth 50
    $resultBytes = [Text.Encoding]::UTF8.GetBytes($resultJson)
    try {
        if ($resultBytes.Length -gt 6291456) { throw 'Elevated result payload exceeds 6 MiB.' }
        $resultBase64 = [Convert]::ToBase64String($resultBytes)
    } finally { [Array]::Clear($resultBytes, 0, $resultBytes.Length) }
    $wrapper = [ordered]@{
        SchemaVersion = 1
        PayloadBase64 = $resultBase64
        Signature = Get-LocalHmac $Secret $resultBase64
    }
    Write-LocalExclusiveJson $Path $wrapper
}

$secret = $null
$payload = $null
$resultPath = $null
$treeHash = ''
try {
    if (-not $IsWindows) { throw 'Elevated action host is Windows-only.' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )) {
        throw 'The elevated action host did not receive administrator privileges.'
    }

    $requestPathFull = Resolve-LocalPath $RequestPath
    Assert-LocalNoReparse $requestPathFull
    $requestDirectory = Split-Path -Parent $requestPathFull
    $secret = Read-LocalOneTimeSecret -Path $SecretPath `
        -ExpectedSha256 $SecretSha256 -RequestDirectory $requestDirectory
    $requestBytes = Read-LocalBoundedBytes -Path $requestPathFull -MaximumBytes 2097152
    try {
        if (-not (Test-LocalFixedHex (Get-LocalSha256Bytes $requestBytes) $RequestSha256)) {
            throw 'Elevation request file hash mismatch.'
        }
        $requestJson = [Text.UTF8Encoding]::new($false, $true).GetString($requestBytes)
    } finally { [Array]::Clear($requestBytes, 0, $requestBytes.Length) }

    $wrapper = $requestJson | ConvertFrom-Json -AsHashtable -Depth 50
    $unknown = @($wrapper.Keys | Where-Object {
        $_ -notin @('SchemaVersion', 'PayloadBase64', 'Signature')
    })
    if ($unknown.Count) { throw "Unknown request envelope field(s): $($unknown -join ', ')" }
    if ([int]$wrapper.SchemaVersion -ne 1) { throw 'Unsupported request envelope schema.' }
    if ([string]$wrapper.PayloadBase64 -notmatch '^[A-Za-z0-9+/]{1,2796204}={0,2}$') {
        throw 'Elevation request payload encoding is invalid or too large.'
    }
    $expected = Get-LocalHmac $secret ([string]$wrapper.PayloadBase64)
    if (-not (Test-LocalFixedHex $expected ([string]$wrapper.Signature))) {
        throw 'Elevation request authentication failed.'
    }
    $payloadBytes = [Convert]::FromBase64String([string]$wrapper.PayloadBase64)
    try {
        $payloadJson = [Text.UTF8Encoding]::new($false, $true).GetString($payloadBytes)
    } finally { [Array]::Clear($payloadBytes, 0, $payloadBytes.Length) }
    $payload = $payloadJson | ConvertFrom-Json -AsHashtable -Depth 50
    $allowed = @(
        'SchemaVersion', 'Nonce', 'CreatedAt', 'ExpiresAt', 'OperationId',
        'ActionId', 'ActionType', 'ActionHash', 'ActionBase64',
        'ModuleManifest', 'ModuleTreeHash', 'HelperHash', 'StateRoot',
        'PolicyHash', 'ResultPath'
    )
    $unknown = @($payload.Keys | Where-Object { $_ -notin $allowed })
    if ($unknown.Count) { throw "Unknown elevation payload field(s): $($unknown -join ', ')" }
    if ([int]$payload.SchemaVersion -ne 3) { throw 'Unsupported elevation payload schema.' }
    if ([string]$payload.Nonce -notmatch '^[a-f0-9]{32}$') {
        throw 'Invalid elevation nonce.'
    }
    $created = [datetime]::Parse(
        [string]$payload.CreatedAt,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind
    ).ToUniversalTime()
    $expires = [datetime]::Parse(
        [string]$payload.ExpiresAt,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind
    ).ToUniversalTime()
    $now = [datetime]::UtcNow
    if ($created -gt $now.AddMinutes(1) -or $expires -le $now -or
        $expires -gt $created.AddMinutes(10)) {
        throw 'Elevation request is expired or has an invalid lifetime.'
    }

    $resultPath = Resolve-LocalPath ([string]$payload.ResultPath) -AllowMissing
    if (-not (Test-LocalWithin $resultPath $requestDirectory) -or
        [IO.Path]::GetFileName($resultPath) -ne 'result.json') {
        throw 'Result path is outside the authenticated handoff directory.'
    }
    if (Test-Path -LiteralPath $resultPath) { throw 'Result path already exists.' }
    Assert-LocalNoReparse $requestDirectory
    $manifest = Resolve-LocalPath ([string]$payload.ModuleManifest)
    Assert-LocalNoReparse $manifest
    $moduleRoot = Split-Path -Parent $manifest
    Assert-LocalNoReparse $moduleRoot
    $helperBytes = Read-LocalBoundedBytes -Path $PSCommandPath -MaximumBytes 1048576
    try { $helperHash = Get-LocalSha256Bytes $helperBytes }
    finally { [Array]::Clear($helperBytes, 0, $helperBytes.Length) }
    if (-not (Test-LocalFixedHex $helperHash ([string]$payload.HelperHash))) {
        throw 'Elevated helper integrity validation failed.'
    }
    $treeHash = Get-LocalModuleTreeHash $moduleRoot
    if (-not (Test-LocalFixedHex $treeHash ([string]$payload.ModuleTreeHash))) {
        throw 'WinCare module tree changed after approval.'
    }

    if ([string]$payload.ActionBase64 -notmatch '^[A-Za-z0-9+/]{1,1398104}={0,2}$') {
        throw 'Elevated action encoding is invalid or too large.'
    }
    $actionBytes = [Convert]::FromBase64String([string]$payload.ActionBase64)
    try {
        $actionJson = [Text.UTF8Encoding]::new($false, $true).GetString($actionBytes)
    } finally { [Array]::Clear($actionBytes, 0, $actionBytes.Length) }
    if (-not (Test-LocalFixedHex (Get-LocalTextSha256 $actionJson) `
        ([string]$payload.ActionHash))) {
        throw 'Elevated action payload hash mismatch.'
    }
    $action = $actionJson | ConvertFrom-Json -AsHashtable -Depth 50
    if ([string]$action.Id -ne [string]$payload.ActionId -or
        [string]$action.Type -ne [string]$payload.ActionType -or
        -not [bool]$action.RequiresAdmin) {
        throw 'Elevated action identity or privilege declaration is invalid.'
    }

    $stateRoot = Resolve-LocalPath ([string]$payload.StateRoot) -AllowMissing
    if (Test-Path -LiteralPath $stateRoot) { Assert-LocalNoReparse $stateRoot }
    $replayRoot = Join-Path $stateRoot 'Broker\Elevation\Used'
    $null = New-Item -ItemType Directory -Path $replayRoot -Force
    Assert-LocalNoReparse $replayRoot
    $replayPath = Join-Path $replayRoot ([string]$payload.Nonce + '.used')
    try {
        $stream = [IO.File]::Open(
            $replayPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None
        )
        try { $stream.Flush($true) } finally { $stream.Dispose() }
    } catch {
        throw 'Elevation nonce was already used or could not be reserved.'
    }

    $module = Import-Module $manifest -Force -PassThru -ErrorAction Stop
    $result = & $module {
        param(
            $ElevatedAction,
            $ElevatedOperationId,
            $ExpectedStateRoot,
            $ExpectedPolicyHash,
            $ExpectedTreeHash
        )
        Initialize-WinCareState -SkipConfigSave
        if (-not $script:WinCareState.IsAdmin) {
            throw 'Administrative state was not established.'
        }
        if (-not (Test-WinCarePathWithin -Child $script:WinCareState.Root `
            -Parent $ExpectedStateRoot -AllowEqual)) {
            throw 'Elevated state root differs from the approved state root.'
        }
        $policyJson = ConvertTo-WinCareCanonicalJson `
            -InputObject $script:WinCareState.Policy -Depth 30
        if ((Get-WinCareSha256Text -Text $policyJson) -ne $ExpectedPolicyHash) {
            throw 'Effective policy changed after approval.'
        }
        if ((Get-WinCareModuleTreeHash) -ne $ExpectedTreeHash) {
            throw 'Module tree changed after import.'
        }
        if ([string]$ElevatedAction.Type -notin (Get-WinCareElevatedActionAllowlist)) {
            throw "Action type is not permitted in the elevated host: $($ElevatedAction.Type)"
        }
        $contract = Test-WinCareActionContract -Action $ElevatedAction -ForElevation
        if (-not $contract.Success) { throw $contract.Message }
        Invoke-WinCareAction -Action $ElevatedAction -OperationId $ElevatedOperationId
    } $action ([string]$payload.OperationId) $stateRoot `
        ([string]$payload.PolicyHash) $treeHash

    Write-LocalResultEnvelope -Path $resultPath -Secret $secret -Payload $payload `
        -Result $result -ModuleTreeHash $treeHash
    exit $(if ($result.Success) { 0 } else { 1 })
} catch {
    if ($secret -and $resultPath -and -not (Test-Path -LiteralPath $resultPath)) {
        try {
            $failure = New-LocalFailureResult -Payload $payload -Message $_.Exception.Message
            Write-LocalResultEnvelope -Path $resultPath -Secret $secret -Payload $payload `
                -Result $failure -ModuleTreeHash $treeHash
        } catch { Write-Verbose 'The authenticated failure result could not be written.' }
    }
    exit 1
} finally {
    if ($secret) { [Array]::Clear($secret, 0, $secret.Length) }
}
