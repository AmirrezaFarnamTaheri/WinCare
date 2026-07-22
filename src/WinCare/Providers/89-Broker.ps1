function Get-WinCareBrokerToken {
    [CmdletBinding()]param()
    $key=Get-WinCareLocalIntegrityKey
    try{
        $hmac=[Security.Cryptography.HMACSHA256]::new($key)
        return $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes('WinCare.LocalBroker.v2'))
    }finally{[Array]::Clear($key,0,$key.Length);if($hmac){$hmac.Dispose()}}
}

function Get-WinCareBrokerCanonicalText {
    param([Parameter(Mandatory)][Collections.IDictionary]$Envelope,[string[]]$Fields=@('schemaVersion','channel','requestId','timestamp','nonce','command','arguments'))
    $values=[Collections.Generic.List[string]]::new()
    foreach($field in $Fields){
        $value=$Envelope[$field]
        if($field -in @('arguments','data')){$value=if($null -eq $value){'null'}else{$value|ConvertTo-Json -Compress -Depth 40}}
        $values.Add([string]$value)
    }
    $values -join "`n"
}

function Get-WinCareBrokerSignature {
    param([Parameter(Mandatory)][Collections.IDictionary]$Envelope,[Parameter(Mandatory)][byte[]]$Token,[string[]]$Fields=@('schemaVersion','channel','requestId','timestamp','nonce','command','arguments'))
    $hmac=[Security.Cryptography.HMACSHA256]::new($Token)
    try{return [Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes((Get-WinCareBrokerCanonicalText -Envelope $Envelope -Fields $Fields)))).ToLowerInvariant()}
    finally{$hmac.Dispose()}
}

function Test-WinCareBrokerSignature {
    param([Collections.IDictionary]$Envelope,[byte[]]$Token,[string[]]$Fields)
    $expected=Get-WinCareBrokerSignature -Envelope $Envelope -Token $Token -Fields $Fields
    $actual=[string]$Envelope.signature
    if($actual -notmatch '^[a-f0-9]{64}$'){return $false}
    [Security.Cryptography.CryptographicOperations]::FixedTimeEquals([Convert]::FromHexString($expected),[Convert]::FromHexString($actual))
}

function Test-WinCareBrokerEnvelope {
    param([Parameter(Mandatory)][Collections.IDictionary]$Envelope,[Parameter(Mandatory)][byte[]]$Token)
    $allowed=@('schemaVersion','channel','requestId','timestamp','nonce','command','arguments','signature')
    $null=Test-WinCareStrictObjectKeys -InputObject $Envelope -AllowedKeys $allowed -Context 'broker envelope'
    if([int]$Envelope.schemaVersion -ne 2){throw 'Unsupported broker protocol version.'}
    if([string]$Envelope.channel -ne 'wincare.local.readonly'){throw 'Invalid broker channel.'}
    if([string]$Envelope.requestId -notmatch '^[a-f0-9]{32}$' -or [string]$Envelope.nonce -notmatch '^[a-f0-9]{64}$'){throw 'Invalid broker request identity.'}
    $timestamp=[datetime]::Parse([string]$Envelope.timestamp,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    if([math]::Abs(([datetime]::UtcNow-$timestamp).TotalMinutes)-gt 5){throw 'Broker request expired.'}
    if([string]$Envelope.command -notin (Get-WinCareBrokerReadOnlyCommandName)){throw 'Broker commands are restricted to the read-only allowlist.'}
    if($Envelope.arguments -isnot [Collections.IDictionary]){throw 'Broker arguments must be an object.'}
    $argumentsJson=$Envelope.arguments|ConvertTo-Json -Compress -Depth 30
    if([Text.Encoding]::UTF8.GetByteCount($argumentsJson)-gt 262144){throw 'Broker arguments exceed 256 KiB.'}
    if(-not(Test-WinCareBrokerSignature -Envelope $Envelope -Token $Token -Fields @('schemaVersion','channel','requestId','timestamp','nonce','command','arguments'))){throw 'Broker signature validation failed.'}
    $nonceRoot=Join-Path $script:WinCareState.Root 'Broker';$null=New-Item -ItemType Directory -Path $nonceRoot -Force
    $noncePath=Join-Path $nonceRoot ('nonce-'+$Envelope.nonce)
    try{$stream=[IO.File]::Open($noncePath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$bytes=[Text.Encoding]::ASCII.GetBytes([datetime]::UtcNow.ToString('o'));$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}}
    catch [System.IO.IOException]{throw 'Broker request replay detected.'}
    Get-ChildItem -LiteralPath $nonceRoot -Filter 'nonce-*' -File -ErrorAction SilentlyContinue|Where-Object LastWriteTimeUtc -lt ([datetime]::UtcNow.AddHours(-1))|Remove-Item -Force -ErrorAction SilentlyContinue
    return $true
}

function New-WinCareBrokerResponseEvent {
    param([string]$RequestId,[string]$Event,[int]$Sequence,[object]$Data,[byte[]]$Token)
    $envelope=[ordered]@{schemaVersion=2;channel='wincare.local.readonly';requestId=$RequestId;timestamp=[datetime]::UtcNow.ToString('o');event=$Event;sequence=$Sequence;data=$Data}
    $envelope.signature=Get-WinCareBrokerSignature -Envelope $envelope -Token $Token -Fields @('schemaVersion','channel','requestId','timestamp','event','sequence','data')
    $envelope
}

function Test-WinCareBrokerResponseEvent {
    param([Collections.IDictionary]$Envelope,[byte[]]$Token,[string]$RequestId,[int]$ExpectedSequence)
    $null=Test-WinCareStrictObjectKeys -InputObject $Envelope -AllowedKeys @('schemaVersion','channel','requestId','timestamp','event','sequence','data','signature') -Context 'broker response'
    if([int]$Envelope.schemaVersion -ne 2 -or [string]$Envelope.channel -ne 'wincare.local.readonly' -or [string]$Envelope.requestId -ne $RequestId -or [int]$Envelope.sequence -ne $ExpectedSequence){throw 'Broker response identity or sequence is invalid.'}
    if([string]$Envelope.event -notin @('accepted','chunk','complete','error')){throw 'Broker response event is invalid.'}
    if(-not(Test-WinCareBrokerSignature -Envelope $Envelope -Token $Token -Fields @('schemaVersion','channel','requestId','timestamp','event','sequence','data'))){throw 'Broker response signature validation failed.'}
    return $true
}

function Start-WinCareLocalBroker {
    [CmdletBinding()]param([string]$PipeName=('WinCare-'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value.Replace('-','_')),[ValidateRange(1,1000)][int]$MaxRequests=50,[ValidateRange(5,3600)][int]$IdleTimeoutSeconds=300)
    if(-not $IsWindows){throw 'The named-pipe broker is available only on Windows.'}
    if(-not[bool](Get-WinCarePolicy 'AllowLocalBroker')){throw 'The local broker is disabled by policy.'}
    if($PipeName -notmatch '^[A-Za-z0-9._-]{8,160}$'){throw 'Invalid broker pipe name.'}
    $token=Get-WinCareBrokerToken;$processed=0
    try{
        while($processed -lt $MaxRequests){
            $options=[IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly
            $server=[IO.Pipes.NamedPipeServerStream]::new($PipeName,[IO.Pipes.PipeDirection]::InOut,1,[IO.Pipes.PipeTransmissionMode]::Byte,$options,65536,65536)
            $reader=$null;$writer=$null;$sequence=0;$requestId='unknown'
            try{
                $wait=$server.WaitForConnectionAsync();if(-not $wait.Wait($IdleTimeoutSeconds*1000)){return}
                $reader=[IO.StreamReader]::new($server,[Text.Encoding]::UTF8,$false,65536,$true);$writer=[IO.StreamWriter]::new($server,[Text.UTF8Encoding]::new($false),65536,$true);$writer.AutoFlush=$true
                $line=$reader.ReadLine();if($null -eq $line -or [Text.Encoding]::UTF8.GetByteCount($line)-gt 1048576){throw 'Broker request is empty or too large.'}
                $envelope=$line|ConvertFrom-Json -AsHashtable -Depth 40;$requestId=[string]$envelope.requestId;$null=Test-WinCareBrokerEnvelope -Envelope $envelope -Token $token
                $writer.WriteLine((New-WinCareBrokerResponseEvent -RequestId $requestId -Event accepted -Sequence $sequence -Data @{command=$envelope.command} -Token $token|ConvertTo-Json -Compress -Depth 20));$sequence++
                $result=Invoke-WinCareHeadlessCommand -Command ([string]$envelope.command) -Arguments ([hashtable]$envelope.arguments) -JsonObject
                $payload=$result|ConvertTo-Json -Compress -Depth 50
                if([Text.Encoding]::UTF8.GetByteCount($payload)-gt 16MB){throw 'Broker response exceeds the 16 MiB limit.'}
                $chunks=[math]::Max(1,[math]::Ceiling($payload.Length/60000.0))
                for($i=0;$i-lt $chunks;$i++){$part=$payload.Substring($i*60000,[math]::Min(60000,$payload.Length-$i*60000));$writer.WriteLine((New-WinCareBrokerResponseEvent -RequestId $requestId -Event chunk -Sequence $sequence -Data @{index=$i;count=$chunks;text=$part} -Token $token|ConvertTo-Json -Compress -Depth 20));$sequence++}
                $writer.WriteLine((New-WinCareBrokerResponseEvent -RequestId $requestId -Event complete -Sequence $sequence -Data @{chunks=$chunks} -Token $token|ConvertTo-Json -Compress -Depth 20));$processed++
            }catch{
                try{$writer.WriteLine((New-WinCareBrokerResponseEvent -RequestId $requestId -Event error -Sequence $sequence -Data @{message=$_.Exception.Message} -Token $token|ConvertTo-Json -Compress -Depth 20))}catch { Write-Verbose 'A best-effort operation was unavailable.' }
            }finally{if($reader){$reader.Dispose()};if($writer){$writer.Dispose()};$server.Dispose()}
        }
    }finally{[Array]::Clear($token,0,$token.Length)}
}

function Invoke-WinCareBrokerRequest {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Command,[hashtable]$Arguments=@{},[string]$PipeName=('WinCare-'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value.Replace('-','_')),[ValidateRange(1,300)][int]$TimeoutSeconds=30)
    if($Command -notin (Get-WinCareBrokerReadOnlyCommandName)){throw 'Broker commands are restricted to the read-only allowlist.'}
    $token=Get-WinCareBrokerToken;$requestId=[guid]::NewGuid().ToString('N')
    $envelope=[ordered]@{schemaVersion=2;channel='wincare.local.readonly';requestId=$requestId;timestamp=[datetime]::UtcNow.ToString('o');nonce=([guid]::NewGuid().ToString('N')+[guid]::NewGuid().ToString('N'));command=$Command;arguments=$Arguments}
    $envelope.signature=Get-WinCareBrokerSignature -Envelope $envelope -Token $token -Fields @('schemaVersion','channel','requestId','timestamp','nonce','command','arguments')
    $client=[IO.Pipes.NamedPipeClientStream]::new('.',$PipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous);$reader=$null;$writer=$null
    try{
        $client.Connect($TimeoutSeconds*1000);$reader=[IO.StreamReader]::new($client,[Text.Encoding]::UTF8,$false,65536,$true);$writer=[IO.StreamWriter]::new($client,[Text.UTF8Encoding]::new($false),65536,$true);$writer.AutoFlush=$true;$writer.WriteLine(($envelope|ConvertTo-Json -Compress -Depth 30))
        $parts=[Collections.Generic.List[string]]::new();$expected=0;$expectedChunks=$null
        while($true){
            $read=$reader.ReadLineAsync();if(-not $read.Wait($TimeoutSeconds*1000)){throw 'Broker response timed out.'};$line=$read.Result;if($null -eq $line){throw 'Broker disconnected before completion.'}
            $event=$line|ConvertFrom-Json -AsHashtable -Depth 30;$null=Test-WinCareBrokerResponseEvent -Envelope $event -Token $token -RequestId $requestId -ExpectedSequence $expected;$expected++
            if($event.event -eq 'error'){throw [string]$event.data.message}
            if($event.event -eq 'chunk'){if([int]$event.data.index -ne $parts.Count){throw 'Broker chunk order is invalid.'};$expectedChunks=[int]$event.data.count;$parts.Add([string]$event.data.text)}
            if($event.event -eq 'complete'){if($null -eq $expectedChunks -or $parts.Count -ne $expectedChunks){throw 'Broker response is incomplete.'};break}
        }
        return (($parts -join '')|ConvertFrom-Json -Depth 50)
    }finally{if($reader){$reader.Dispose()};if($writer){$writer.Dispose()};$client.Dispose();[Array]::Clear($token,0,$token.Length)}
}



