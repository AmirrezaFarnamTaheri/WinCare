#requires -Version 7.2
<#
.SYNOPSIS
Authoritative bounded HTTP client and service-target admission boundary.
#>
function Test-WinCareRestrictedServiceAddress {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Net.IPAddress]$Address)

    if ($Address.IsIPv4MappedToIPv6) {
        return Test-WinCareRestrictedServiceAddress -Address $Address.MapToIPv4()
    }
    if ([Net.IPAddress]::IsLoopback($Address)) { return 'Loopback' }
    $bytes = $Address.GetAddressBytes()
    if ($Address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork) {
        if ($bytes[0] -eq 0) { return 'Unspecified' }
        if ($bytes[0] -eq 10) { return 'Private' }
        if ($bytes[0] -eq 100 -and $bytes[1] -ge 64 -and $bytes[1] -le 127) {
            return 'CarrierGradeNat'
        }
        if ($bytes[0] -eq 127) { return 'Loopback' }
        if ($bytes[0] -eq 169 -and $bytes[1] -eq 254) { return 'LinkLocal' }
        if ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) {
            return 'Private'
        }
        if ($bytes[0] -eq 192 -and $bytes[1] -eq 168) { return 'Private' }
        if ($bytes[0] -eq 192 -and $bytes[1] -eq 0 -and $bytes[2] -in @(0, 2)) {
            return 'Reserved'
        }
        if ($bytes[0] -eq 198 -and $bytes[1] -in @(18, 19, 51)) {
            return 'Reserved'
        }
        if ($bytes[0] -eq 203 -and $bytes[1] -eq 0 -and $bytes[2] -eq 113) {
            return 'Reserved'
        }
        if ($bytes[0] -ge 224) { return 'Reserved' }
        return $null
    }
    if ($Address.Equals([Net.IPAddress]::IPv6None) -or
        $Address.Equals([Net.IPAddress]::IPv6Any)) {
        return 'Unspecified'
    }
    if ($Address.IsIPv6LinkLocal -or $Address.IsIPv6Multicast -or
        $Address.IsIPv6SiteLocal) {
        return 'RestrictedIpv6'
    }
    if (($bytes[0] -band 0xfe) -eq 0xfc) { return 'UniqueLocalIpv6' }
    if ($bytes.Length -eq 16 -and $bytes[0] -eq 0x20 -and $bytes[1] -eq 0x01 -and
        $bytes[2] -eq 0x0d -and $bytes[3] -eq 0xb8) {
        return 'Reserved'
    }
    return $null
}

function Assert-WinCareServiceUri {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][uri]$Uri,
        [switch]$AllowPrivateNetwork,
        [switch]$AllowLoopbackHttp,
        [switch]$AllowExplicitHttp
    )

    if (-not $Uri.IsAbsoluteUri) { throw 'Service URI must be absolute.' }
    if ($Uri.AbsoluteUri.Length -gt 4096) { throw 'Service URI exceeds 4096 characters.' }
    if ($Uri.Scheme -notin @('http', 'https')) {
        throw 'Service URI must use HTTP or HTTPS.'
    }
    if (-not [string]::IsNullOrWhiteSpace($Uri.UserInfo)) {
        throw 'Service URI cannot contain user information.'
    }
    if (-not [string]::IsNullOrWhiteSpace($Uri.Fragment)) {
        throw 'Service URI cannot contain a fragment.'
    }
    if ([string]::IsNullOrWhiteSpace($Uri.DnsSafeHost)) {
        throw 'Service URI must contain a host.'
    }
    $loopbackHost = $Uri.Host.TrimEnd('.').ToLowerInvariant() -in @(
        'localhost', '127.0.0.1', '::1'
    )
    if ($Uri.Scheme -ne 'https') {
        $loopbackAllowed = $AllowLoopbackHttp -and $loopbackHost
        if (-not ($loopbackAllowed -or $AllowExplicitHttp)) {
            throw 'Remote service endpoints must use HTTPS unless explicit HTTP is authorized.'
        }
    }
    $metadataHosts = @(
        '169.254.169.254', '169.254.170.2', '100.100.100.200',
        'metadata.google.internal', 'metadata.azure.internal',
        'instance-data', 'instance-data.ec2.internal'
    )
    $normalizedHost = $Uri.Host.TrimEnd('.').ToLowerInvariant()
    if ($normalizedHost -in $metadataHosts) {
        throw 'Cloud instance-metadata endpoints are prohibited.'
    }
    $addresses = try {
        @([Net.Dns]::GetHostAddresses($Uri.DnsSafeHost))
    } catch {
        throw "Service hostname resolution failed: $($_.Exception.Message)"
    }
    if ($addresses.Count -eq 0) { throw 'Service hostname resolved to no addresses.' }
    foreach ($address in $addresses) {
        $restriction = Test-WinCareRestrictedServiceAddress -Address $address
        if ($restriction -in @(
            'LinkLocal', 'Reserved', 'RestrictedIpv6', 'Unspecified'
        )) {
            throw "Service endpoint resolved to prohibited address class: $restriction."
        }
        if ($restriction -eq 'Loopback' -and -not $loopbackHost) {
            throw 'A non-loopback hostname resolved to a loopback address.'
        }
        if ($restriction -and -not $AllowPrivateNetwork) {
            throw "Service endpoint resolved to non-public address class: $restriction."
        }
    }
    return [pscustomobject]@{
        SchemaVersion = 1
        Uri = $Uri
        ResolvedAddresses = @($addresses | ForEach-Object ToString | Sort-Object -Unique)
        AllowPrivateNetwork = [bool]$AllowPrivateNetwork
        AllowLoopbackHttp = [bool]$AllowLoopbackHttp
        AllowExplicitHttp = [bool]$AllowExplicitHttp
        AdmittedAt = [datetime]::UtcNow.ToString('o')
    }
}

function Test-WinCareHttpAdmission {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Admission)

    $null = Test-WinCareStrictObjectKeys -InputObject $Admission -AllowedKeys @(
        'SchemaVersion', 'Uri', 'ResolvedAddresses', 'AllowPrivateNetwork',
        'AllowLoopbackHttp', 'AllowExplicitHttp', 'AdmittedAt'
    ) -Context 'HTTP admission'
    if ([int]$Admission.SchemaVersion -ne 1) { throw 'Unsupported HTTP admission schema.' }
    $admittedAt = [datetime]::Parse(
        [string]$Admission.AdmittedAt,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind
    ).ToUniversalTime()
    if ($admittedAt -gt [datetime]::UtcNow.AddSeconds(5) -or
        $admittedAt -lt [datetime]::UtcNow.AddMinutes(-2)) {
        throw 'HTTP admission is stale or has an invalid timestamp.'
    }
    $fresh = Assert-WinCareServiceUri -Uri ([uri]$Admission.Uri) `
        -AllowPrivateNetwork:([bool]$Admission.AllowPrivateNetwork) `
        -AllowLoopbackHttp:([bool]$Admission.AllowLoopbackHttp) `
        -AllowExplicitHttp:([bool]$Admission.AllowExplicitHttp)
    $expected = @($Admission.ResolvedAddresses | ForEach-Object { [string]$_ } |
        Sort-Object -Unique)
    $actual = @($fresh.ResolvedAddresses | Sort-Object -Unique)
    if (($expected -join "`n") -ne ($actual -join "`n")) {
        throw 'Service hostname resolution changed after admission.'
    }
    return $fresh
}

function Add-WinCareHttpRequestHeaders {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Net.Http.HttpRequestMessage]$Request,
        [Parameter(Mandatory)][hashtable]$Headers
    )

    if ($Headers.Count -gt 64) { throw 'HTTP request contains more than 64 headers.' }
    $prohibited = @(
        'connection', 'content-length', 'host', 'proxy-authenticate',
        'proxy-authorization', 'te', 'trailer', 'transfer-encoding', 'upgrade'
    )
    $totalBytes = 0
    foreach ($name in $Headers.Keys) {
        $headerName = [string]$name
        $headerValue = [string]$Headers[$name]
        if ($headerName -notmatch '^[!#$%&''*+.^_`|~0-9A-Za-z-]{1,128}$') {
            throw "Invalid HTTP request header name: $headerName"
        }
        if ($headerName.ToLowerInvariant() -in $prohibited) {
            throw "Prohibited HTTP request header: $headerName"
        }
        if ($headerValue.Length -gt 8192 -or $headerValue -match '[\r\n]') {
            throw "Invalid HTTP request header value: $headerName"
        }
        $totalBytes += [Text.Encoding]::UTF8.GetByteCount($headerName + $headerValue)
        if ($totalBytes -gt 32768) { throw 'HTTP request headers exceed 32 KiB.' }
        if (-not $Request.Headers.TryAddWithoutValidation($headerName, $headerValue)) {
            throw "Unsupported HTTP request header: $headerName"
        }
    }
}

function Invoke-WinCareBoundedHttpRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'HEAD')][string]$Method,
        [Parameter(Mandatory)][object]$Admission,
        [hashtable]$Headers = @{},
        [AllowNull()][byte[]]$Body,
        [ValidateNotNullOrEmpty()][string]$ContentType = 'application/json',
        [ValidateRange(1, 120)][int]$TimeoutSeconds = 15,
        [ValidateRange(0, 16777216)][int]$MaximumRequestBytes = 4194304,
        [ValidateRange(1024, 16777216)][int]$MaximumResponseBytes = 1048576
    )

    $verifiedAdmission = Test-WinCareHttpAdmission -Admission $Admission
    if ($null -ne $Body -and $Body.Length -gt $MaximumRequestBytes) {
        throw "HTTP request body exceeds the $MaximumRequestBytes byte limit."
    }
    if ($null -ne $Body -and $Method -in @('GET', 'HEAD')) {
        throw "$Method requests cannot contain a body."
    }
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.AutomaticDecompression = [Net.DecompressionMethods]::None
    $handler.UseCookies = $false
    $handler.UseDefaultCredentials = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $request = [Net.Http.HttpRequestMessage]::new(
        [Net.Http.HttpMethod]::new($Method),
        [uri]$verifiedAdmission.Uri
    )
    $cancellation = [Threading.CancellationTokenSource]::new()
    $cancellation.CancelAfter($TimeoutSeconds * 1000)
    $response = $null
    $stream = $null
    $memory = $null
    try {
        Add-WinCareHttpRequestHeaders -Request $request -Headers $Headers
        if ($null -ne $Body) {
            $request.Content = [Net.Http.ByteArrayContent]::new($Body)
            $request.Content.Headers.ContentType = `
                [Net.Http.Headers.MediaTypeHeaderValue]::new($ContentType)
        }
        try {
            $response = $client.SendAsync(
                $request,
                [Net.Http.HttpCompletionOption]::ResponseHeadersRead,
                $cancellation.Token
            ).GetAwaiter().GetResult()
        } catch [OperationCanceledException] {
            throw "HTTP request timed out after $TimeoutSeconds seconds."
        }
        if ($response.Content.Headers.ContentLength.HasValue -and
            $response.Content.Headers.ContentLength.Value -gt $MaximumResponseBytes) {
            throw "HTTP response exceeds the $MaximumResponseBytes byte limit."
        }
        $stream = $response.Content.ReadAsStreamAsync($cancellation.Token).GetAwaiter().GetResult()
        $memory = [IO.MemoryStream]::new()
        $buffer = [byte[]]::new(65536)
        try {
            while ($true) {
                try {
                    $read = $stream.ReadAsync(
                        $buffer,
                        0,
                        $buffer.Length,
                        $cancellation.Token
                    ).GetAwaiter().GetResult()
                } catch [OperationCanceledException] {
                    throw "HTTP response read timed out after $TimeoutSeconds seconds."
                }
                if ($read -le 0) { break }
                if ($memory.Length + $read -gt $MaximumResponseBytes) {
                    throw "HTTP response exceeds the $MaximumResponseBytes byte limit."
                }
                $memory.Write($buffer, 0, $read)
            }
        } finally {
            [Array]::Clear($buffer, 0, $buffer.Length)
        }
        $bytes = $memory.ToArray()
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        return [pscustomobject]@{
            IsSuccessStatusCode = [bool]$response.IsSuccessStatusCode
            StatusCode = [int]$response.StatusCode
            ReasonPhrase = [string]$response.ReasonPhrase
            Body = $text
            BodyBytes = [long]$bytes.Length
            BodySha256 = [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($bytes)
            ).ToLowerInvariant()
            ContentType = [string]$response.Content.Headers.ContentType
            Location = [string]$response.Headers.Location
            RequestUri = [string]$verifiedAdmission.Uri
            ResolvedAddresses = @($verifiedAdmission.ResolvedAddresses)
        }
    } finally {
        $cancellation.Dispose()
        if ($memory) { $memory.Dispose() }
        if ($stream) { $stream.Dispose() }
        if ($response) { $response.Dispose() }
        $request.Dispose()
        $client.Dispose()
        $handler.Dispose()
    }
}

function Invoke-WinCareBoundedJsonHttpRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'HEAD')][string]$Method,
        [Parameter(Mandatory)][object]$Admission,
        [hashtable]$Headers = @{},
        [AllowNull()][byte[]]$Body,
        [ValidateRange(1, 120)][int]$TimeoutSeconds = 15,
        [ValidateRange(0, 16777216)][int]$MaximumRequestBytes = 4194304,
        [ValidateRange(1024, 16777216)][int]$MaximumResponseBytes = 1048576
    )

    return Invoke-WinCareBoundedHttpRequest -Method $Method -Admission $Admission `
        -Headers $Headers -Body $Body -ContentType 'application/json' `
        -TimeoutSeconds $TimeoutSeconds -MaximumRequestBytes $MaximumRequestBytes `
        -MaximumResponseBytes $MaximumResponseBytes
}
