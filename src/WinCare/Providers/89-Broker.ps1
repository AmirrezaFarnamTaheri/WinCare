function Get-WinCareBrokerToken {
    [CmdletBinding()]
    param()

    $key = Get-WinCareLocalIntegrityKey
    $hmac = $null
    try {
        $hmac = [Security.Cryptography.HMACSHA256]::new($key)
        return $hmac.ComputeHash(
            [Text.Encoding]::UTF8.GetBytes('WinCare.LocalBroker.v3')
        )
    } finally {
        [Array]::Clear($key, 0, $key.Length)
        if ($hmac) { $hmac.Dispose() }
    }
}

function Get-WinCareBrokerCanonicalText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Envelope,
        [string[]]$Fields = @(
            'schemaVersion', 'channel', 'requestId', 'timestamp',
            'nonce', 'command', 'arguments'
        )
    )

    $values = [Collections.Generic.List[string]]::new()
    foreach ($field in $Fields) {
        $value = $Envelope[$field]
        if ($field -in @('arguments', 'data')) {
            $value = if ($null -eq $value) {
                'null'
            } else {
                ConvertTo-WinCareCanonicalJson -InputObject $value -Depth 40
            }
        }
        $values.Add([string]$value)
    }
    return $values -join "`n"
}

function Get-WinCareBrokerSignature {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Envelope,
        [Parameter(Mandatory)][byte[]]$Token,
        [string[]]$Fields = @(
            'schemaVersion', 'channel', 'requestId', 'timestamp',
            'nonce', 'command', 'arguments'
        )
    )

    $hmac = [Security.Cryptography.HMACSHA256]::new($Token)
    try {
        $canonical = Get-WinCareBrokerCanonicalText -Envelope $Envelope -Fields $Fields
        $hash = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))
        return [Convert]::ToHexString($hash).ToLowerInvariant()
    } finally {
        $hmac.Dispose()
    }
}

function Test-WinCareBrokerSignature {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Envelope,
        [Parameter(Mandatory)][byte[]]$Token,
        [Parameter(Mandatory)][string[]]$Fields
    )

    $expected = Get-WinCareBrokerSignature -Envelope $Envelope -Token $Token -Fields $Fields
    $actual = [string]$Envelope.signature
    if ($actual -notmatch '^[a-f0-9]{64}$') { return $false }
    return [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
        [Convert]::FromHexString($expected),
        [Convert]::FromHexString($actual)
    )
}

function Test-WinCareBrokerEnvelope {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Envelope,
        [Parameter(Mandatory)][byte[]]$Token
    )

    $allowed = @(
        'schemaVersion', 'channel', 'requestId', 'timestamp',
        'nonce', 'command', 'arguments', 'signature'
    )
    $null = Test-WinCareStrictObjectKeys -InputObject $Envelope `
        -AllowedKeys $allowed -Context 'broker envelope'
    if ([int]$Envelope.schemaVersion -ne 3) {
        throw 'Unsupported broker protocol version.'
    }
    if ([string]$Envelope.channel -ne 'wincare.local.readonly') {
        throw 'Invalid broker channel.'
    }
    if ([string]$Envelope.requestId -notmatch '^[a-f0-9]{32}$' -or
        [string]$Envelope.nonce -notmatch '^[a-f0-9]{64}$') {
        throw 'Invalid broker request identity.'
    }
    $timestamp = [datetime]::Parse(
        [string]$Envelope.timestamp,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind
    ).ToUniversalTime()
    if ([math]::Abs(([datetime]::UtcNow - $timestamp).TotalMinutes) -gt 5) {
        throw 'Broker request expired.'
    }
    if ([string]$Envelope.command -notin (Get-WinCareBrokerReadOnlyCommandName)) {
        throw 'Broker commands are restricted to the read-only allowlist.'
    }
    if ($Envelope.arguments -isnot [Collections.IDictionary]) {
        throw 'Broker arguments must be an object.'
    }
    $argumentsJson = ConvertTo-WinCareCanonicalJson -InputObject $Envelope.arguments -Depth 30
    if ([Text.Encoding]::UTF8.GetByteCount($argumentsJson) -gt 262144) {
        throw 'Broker arguments exceed 256 KiB.'
    }
    $signatureFields = @(
        'schemaVersion', 'channel', 'requestId', 'timestamp',
        'nonce', 'command', 'arguments'
    )
    if (-not (Test-WinCareBrokerSignature -Envelope $Envelope -Token $Token `
        -Fields $signatureFields)) {
        throw 'Broker signature validation failed.'
    }

    $nonceRoot = Join-Path $script:WinCareState.Root 'Broker'
    $null = New-Item -ItemType Directory -Path $nonceRoot -Force
    $noncePath = Join-Path $nonceRoot ('nonce-' + $Envelope.nonce)
    try {
        $stream = [IO.File]::Open(
            $noncePath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None
        )
        try {
            $bytes = [Text.Encoding]::ASCII.GetBytes([datetime]::UtcNow.ToString('o'))
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        } finally {
            $stream.Dispose()
        }
    } catch [IO.IOException] {
        throw 'Broker request replay detected.'
    }
    Get-ChildItem -LiteralPath $nonceRoot -Filter 'nonce-*' -File `
        -ErrorAction SilentlyContinue |
        Where-Object LastWriteTimeUtc -lt ([datetime]::UtcNow.AddHours(-1)) |
        Remove-Item -Force -ErrorAction SilentlyContinue
    return $true
}

function New-WinCareBrokerResponseEvent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RequestId,
        [Parameter(Mandatory)][ValidateSet('accepted', 'chunk', 'complete', 'error')][string]$Event,
        [Parameter(Mandatory)][int]$Sequence,
        [AllowNull()][object]$Data,
        [Parameter(Mandatory)][byte[]]$Token
    )

    $envelope = [ordered]@{
        schemaVersion = 3
        channel = 'wincare.local.readonly'
        requestId = $RequestId
        timestamp = [datetime]::UtcNow.ToString('o')
        event = $Event
        sequence = $Sequence
        data = $Data
    }
    $envelope.signature = Get-WinCareBrokerSignature -Envelope $envelope `
        -Token $Token -Fields @(
            'schemaVersion', 'channel', 'requestId', 'timestamp',
            'event', 'sequence', 'data'
        )
    return $envelope
}

function Test-WinCareBrokerResponseEvent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Envelope,
        [Parameter(Mandatory)][byte[]]$Token,
        [Parameter(Mandatory)][string]$RequestId,
        [Parameter(Mandatory)][int]$ExpectedSequence
    )

    $null = Test-WinCareStrictObjectKeys -InputObject $Envelope -AllowedKeys @(
        'schemaVersion', 'channel', 'requestId', 'timestamp',
        'event', 'sequence', 'data', 'signature'
    ) -Context 'broker response'
    if ([int]$Envelope.schemaVersion -ne 3 -or
        [string]$Envelope.channel -ne 'wincare.local.readonly' -or
        [string]$Envelope.requestId -ne $RequestId -or
        [int]$Envelope.sequence -ne $ExpectedSequence) {
        throw 'Broker response identity or sequence is invalid.'
    }
    if ([string]$Envelope.event -notin @('accepted', 'chunk', 'complete', 'error')) {
        throw 'Broker response event is invalid.'
    }
    $timestamp = [datetime]::Parse(
        [string]$Envelope.timestamp,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind
    ).ToUniversalTime()
    if ([math]::Abs(([datetime]::UtcNow - $timestamp).TotalMinutes) -gt 5) {
        throw 'Broker response timestamp is outside the accepted window.'
    }
    if (-not (Test-WinCareBrokerSignature -Envelope $Envelope -Token $Token `
        -Fields @(
            'schemaVersion', 'channel', 'requestId', 'timestamp',
            'event', 'sequence', 'data'
        ))) {
        throw 'Broker response signature validation failed.'
    }
    return $true
}

function Read-WinCareBrokerExactBytes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][IO.Stream]$Stream,
        [Parameter(Mandatory)][ValidateRange(1, 16777216)][int]$Count,
        [Parameter(Mandatory)][ValidateRange(1, 300)][int]$TimeoutSeconds
    )

    $buffer = [byte[]]::new($Count)
    $offset = 0
    $cancellation = [Threading.CancellationTokenSource]::new()
    $cancellation.CancelAfter($TimeoutSeconds * 1000)
    try {
        while ($offset -lt $Count) {
            try {
                $read = $Stream.ReadAsync(
                    $buffer,
                    $offset,
                    $Count - $offset,
                    $cancellation.Token
                ).GetAwaiter().GetResult()
            } catch [OperationCanceledException] {
                throw 'Broker frame read timed out.'
            }
            if ($read -le 0) { throw 'Broker disconnected before completing a frame.' }
            $offset += $read
        }
        return $buffer
    } catch {
        [Array]::Clear($buffer, 0, $buffer.Length)
        throw
    } finally {
        $cancellation.Dispose()
    }
}

function Read-WinCareBrokerFrame {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][IO.Stream]$Stream,
        [ValidateRange(1, 16777216)][int]$MaximumBytes = 1048576,
        [ValidateRange(1, 300)][int]$TimeoutSeconds = 30
    )

    $prefix = Read-WinCareBrokerExactBytes -Stream $Stream -Count 4 `
        -TimeoutSeconds $TimeoutSeconds
    try {
        if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($prefix) }
        $length = [BitConverter]::ToUInt32($prefix, 0)
    } finally {
        [Array]::Clear($prefix, 0, $prefix.Length)
    }
    if ($length -lt 1 -or $length -gt $MaximumBytes) {
        throw "Broker frame length is outside 1..$MaximumBytes bytes."
    }
    $payload = Read-WinCareBrokerExactBytes -Stream $Stream -Count ([int]$length) `
        -TimeoutSeconds $TimeoutSeconds
    try {
        return [Text.UTF8Encoding]::new($false, $true).GetString($payload)
    } finally {
        [Array]::Clear($payload, 0, $payload.Length)
    }
}

function Write-WinCareBrokerFrame {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][IO.Stream]$Stream,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [ValidateRange(1, 16777216)][int]$MaximumBytes = 1048576,
        [ValidateRange(1, 300)][int]$TimeoutSeconds = 30
    )

    $payload = [Text.UTF8Encoding]::new($false, $true).GetBytes($Text)
    if ($payload.Length -lt 1 -or $payload.Length -gt $MaximumBytes) {
        [Array]::Clear($payload, 0, $payload.Length)
        throw "Broker frame length is outside 1..$MaximumBytes bytes."
    }
    $prefix = [BitConverter]::GetBytes([uint32]$payload.Length)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($prefix) }
    $cancellation = [Threading.CancellationTokenSource]::new()
    $cancellation.CancelAfter($TimeoutSeconds * 1000)
    try {
        $Stream.WriteAsync($prefix, 0, $prefix.Length, $cancellation.Token).GetAwaiter().GetResult()
        $Stream.WriteAsync($payload, 0, $payload.Length, $cancellation.Token).GetAwaiter().GetResult()
        $Stream.FlushAsync($cancellation.Token).GetAwaiter().GetResult()
    } catch [OperationCanceledException] {
        throw 'Broker frame write timed out.'
    } finally {
        $cancellation.Dispose()
        [Array]::Clear($prefix, 0, $prefix.Length)
        [Array]::Clear($payload, 0, $payload.Length)
    }
}

function Start-WinCareLocalBroker {
    [CmdletBinding()]
    param(
        [string]$PipeName = (
            'WinCare-' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value.Replace('-', '_')
        ),
        [ValidateRange(1, 1000)][int]$MaxRequests = 50,
        [ValidateRange(5, 3600)][int]$IdleTimeoutSeconds = 300,
        [ValidateRange(1, 300)][int]$FrameTimeoutSeconds = 30
    )

    if (-not $IsWindows) { throw 'The named-pipe broker is available only on Windows.' }
    if (-not [bool](Get-WinCarePolicy 'AllowLocalBroker')) {
        throw 'The local broker is disabled by policy.'
    }
    if ($PipeName -notmatch '^[A-Za-z0-9._-]{8,160}$') {
        throw 'Invalid broker pipe name.'
    }

    $token = Get-WinCareBrokerToken
    $processed = 0
    try {
        while ($processed -lt $MaxRequests) {
            $options = [IO.Pipes.PipeOptions]::Asynchronous -bor
                [IO.Pipes.PipeOptions]::CurrentUserOnly
            $server = [IO.Pipes.NamedPipeServerStream]::new(
                $PipeName,
                [IO.Pipes.PipeDirection]::InOut,
                1,
                [IO.Pipes.PipeTransmissionMode]::Byte,
                $options,
                65536,
                65536
            )
            $sequence = 0
            $requestId = 'unknown'
            try {
                $wait = $server.WaitForConnectionAsync()
                if (-not $wait.Wait($IdleTimeoutSeconds * 1000)) { return }
                $requestText = Read-WinCareBrokerFrame -Stream $server `
                    -MaximumBytes 1048576 -TimeoutSeconds $FrameTimeoutSeconds
                $envelope = $requestText | ConvertFrom-Json -AsHashtable -Depth 40
                $requestId = [string]$envelope.requestId
                $null = Test-WinCareBrokerEnvelope -Envelope $envelope -Token $token

                $accepted = New-WinCareBrokerResponseEvent -RequestId $requestId `
                    -Event accepted -Sequence $sequence `
                    -Data @{ command = $envelope.command } -Token $token
                Write-WinCareBrokerFrame -Stream $server `
                    -Text ($accepted | ConvertTo-Json -Compress -Depth 20) `
                    -TimeoutSeconds $FrameTimeoutSeconds
                $sequence++

                $result = Invoke-WinCareHeadlessCommand `
                    -Command ([string]$envelope.command) `
                    -Arguments ([hashtable]$envelope.arguments) -JsonObject
                $payload = $result | ConvertTo-Json -Compress -Depth 50
                if ([Text.Encoding]::UTF8.GetByteCount($payload) -gt 16MB) {
                    throw 'Broker response exceeds the 16 MiB limit.'
                }
                $chunkCharacters = 60000
                $chunks = [math]::Max(
                    1,
                    [math]::Ceiling($payload.Length / [double]$chunkCharacters)
                )
                for ($index = 0; $index -lt $chunks; $index++) {
                    $offset = $index * $chunkCharacters
                    $length = [math]::Min($chunkCharacters, $payload.Length - $offset)
                    $part = $payload.Substring($offset, $length)
                    $chunk = New-WinCareBrokerResponseEvent -RequestId $requestId `
                        -Event chunk -Sequence $sequence `
                        -Data @{ index = $index; count = $chunks; text = $part } `
                        -Token $token
                    Write-WinCareBrokerFrame -Stream $server `
                        -Text ($chunk | ConvertTo-Json -Compress -Depth 20) `
                        -TimeoutSeconds $FrameTimeoutSeconds
                    $sequence++
                }
                $complete = New-WinCareBrokerResponseEvent -RequestId $requestId `
                    -Event complete -Sequence $sequence -Data @{ chunks = $chunks } `
                    -Token $token
                Write-WinCareBrokerFrame -Stream $server `
                    -Text ($complete | ConvertTo-Json -Compress -Depth 20) `
                    -TimeoutSeconds $FrameTimeoutSeconds
                $processed++
            } catch {
                if ($server.IsConnected) {
                    try {
                        $errorEvent = New-WinCareBrokerResponseEvent `
                            -RequestId $requestId -Event error -Sequence $sequence `
                            -Data @{ message = $_.Exception.Message } -Token $token
                        Write-WinCareBrokerFrame -Stream $server `
                            -Text ($errorEvent | ConvertTo-Json -Compress -Depth 20) `
                            -TimeoutSeconds $FrameTimeoutSeconds
                    } catch {
                        Write-Verbose 'The broker error response could not be delivered.'
                    }
                }
            } finally {
                $server.Dispose()
            }
        }
    } finally {
        [Array]::Clear($token, 0, $token.Length)
    }
}

function Invoke-WinCareBrokerRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Command,
        [hashtable]$Arguments = @{},
        [string]$PipeName = (
            'WinCare-' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value.Replace('-', '_')
        ),
        [ValidateRange(1, 300)][int]$TimeoutSeconds = 30,
        [ValidateRange(1048576, 16777216)][int]$MaximumResponseBytes = 16777216
    )

    if ($Command -notin (Get-WinCareBrokerReadOnlyCommandName)) {
        throw 'Broker commands are restricted to the read-only allowlist.'
    }
    if ($PipeName -notmatch '^[A-Za-z0-9._-]{8,160}$') {
        throw 'Invalid broker pipe name.'
    }

    $token = Get-WinCareBrokerToken
    $requestId = [guid]::NewGuid().ToString('N')
    $envelope = [ordered]@{
        schemaVersion = 3
        channel = 'wincare.local.readonly'
        requestId = $requestId
        timestamp = [datetime]::UtcNow.ToString('o')
        nonce = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
        command = $Command
        arguments = $Arguments
    }
    $envelope.signature = Get-WinCareBrokerSignature -Envelope $envelope `
        -Token $token -Fields @(
            'schemaVersion', 'channel', 'requestId', 'timestamp',
            'nonce', 'command', 'arguments'
        )
    $client = [IO.Pipes.NamedPipeClientStream]::new(
        '.',
        $PipeName,
        [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::Asynchronous
    )
    try {
        $client.Connect($TimeoutSeconds * 1000)
        Write-WinCareBrokerFrame -Stream $client `
            -Text ($envelope | ConvertTo-Json -Compress -Depth 30) `
            -MaximumBytes 1048576 -TimeoutSeconds $TimeoutSeconds

        $parts = [Collections.Generic.List[string]]::new()
        $expectedSequence = 0
        $expectedChunks = $null
        $totalResponseBytes = 0L
        while ($true) {
            $line = Read-WinCareBrokerFrame -Stream $client `
                -MaximumBytes 1048576 -TimeoutSeconds $TimeoutSeconds
            $event = $line | ConvertFrom-Json -AsHashtable -Depth 30
            $null = Test-WinCareBrokerResponseEvent -Envelope $event -Token $token `
                -RequestId $requestId -ExpectedSequence $expectedSequence
            $expectedSequence++
            if ($event.event -eq 'error') { throw [string]$event.data.message }
            if ($event.event -eq 'chunk') {
                if ([int]$event.data.index -ne $parts.Count) {
                    throw 'Broker chunk order is invalid.'
                }
                $expectedChunks = [int]$event.data.count
                if ($expectedChunks -lt 1 -or $expectedChunks -gt 1024) {
                    throw 'Broker chunk count is outside the accepted range.'
                }
                $part = [string]$event.data.text
                $totalResponseBytes += [Text.Encoding]::UTF8.GetByteCount($part)
                if ($totalResponseBytes -gt $MaximumResponseBytes) {
                    throw 'Broker response exceeds the client assembly limit.'
                }
                $parts.Add($part)
            }
            if ($event.event -eq 'complete') {
                if ($null -eq $expectedChunks -or $parts.Count -ne $expectedChunks -or
                    [int]$event.data.chunks -ne $expectedChunks) {
                    throw 'Broker response is incomplete.'
                }
                break
            }
        }
        return (($parts -join '') | ConvertFrom-Json -Depth 50)
    } finally {
        $client.Dispose()
        [Array]::Clear($token, 0, $token.Length)
    }
}
