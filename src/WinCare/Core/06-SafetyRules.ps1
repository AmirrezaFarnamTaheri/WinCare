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
    if([string]$Envelope.PayloadBase64 -notmatch '^[A-Za-z0-9+/]*={0,2}$' -or ([string]$Envelope.PayloadBase64).Length -gt 16777216){throw 'Protected record payload is invalid or oversized.'}
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
