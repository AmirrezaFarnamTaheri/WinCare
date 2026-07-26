#requires -Version 7.2
# WINCARE_NETWORK_BOUNDARY: explicit-user-invocation

function Get-WinCareDohEndpoint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Cloudflare','Google','Quad9','Custom')]
        [string]$Provider,
        [uri]$CustomEndpoint
    )

    switch($Provider) {
        'Cloudflare' { [uri]'https://cloudflare-dns.com/dns-query' }
        'Google' { [uri]'https://dns.google/resolve' }
        'Quad9' { [uri]'https://dns.quad9.net/dns-query' }
        'Custom' {
            if(-not $CustomEndpoint -or $CustomEndpoint.Scheme -ne 'https') {
                throw 'A custom DoH endpoint must use HTTPS.'
            }
            $CustomEndpoint
        }
    }
}

function ConvertFrom-WinCareDohResponse {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Body,
        [ValidateRange(1,64)][int]$MaximumAnswers=64
    )

    $response=$Body|ConvertFrom-Json -Depth 16 -ErrorAction Stop
    if($null -eq $response.Status) {
        throw 'DoH response omitted the DNS status field.'
    }
    $rawAnswers=@(Get-WinCareBridgeProperty $response 'Answer' @())
    if($rawAnswers.Count -gt $MaximumAnswers) {
        throw "DoH response exceeds the $MaximumAnswers answer limit."
    }
    $answers=@(
        $rawAnswers|ForEach-Object {
            [pscustomobject]@{
                Name=$_.name
                Type=$_.type
                Ttl=$_.TTL
                Data=$_.data
            }
        }
    )
    [pscustomobject]@{
        Status=[int]$response.Status
        Answers=$answers
        AuthenticatedData=[bool]$response.AD
        Truncated=[bool]$response.TC
    }
}

function Resolve-WinCareDohQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Domain,
        [ValidateSet('Cloudflare','Google','Quad9','Custom')]
        [string]$Provider='Cloudflare',
        [ValidateSet('A','AAAA','TXT','MX','CNAME')]
        [string]$RecordType='A',
        [uri]$CustomEndpoint,
        [switch]$AllowPrivateNetwork,
        [ValidateRange(1,30)][int]$TimeoutSeconds=10,
        [ValidateRange(1024,1048576)][int]$MaximumResponseBytes=262144,
        [ValidateRange(1,64)][int]$MaximumAnswers=64
    )

    if(-not (Test-WinCareDnsHostName $Domain)) {
        return New-WinCareBridgeResult -Success $false -Status Blocked `
            -Code 'InvalidDnsName' -Message 'The DNS name is invalid.' -ExitCode 22
    }
    try {
        $endpoint=Get-WinCareDohEndpoint -Provider $Provider `
            -CustomEndpoint $CustomEndpoint
        $builder=[UriBuilder]::new($endpoint)
        $query=[Web.HttpUtility]::ParseQueryString($builder.Query)
        $query['name']=$Domain
        $query['type']=$RecordType
        $builder.Query=$query.ToString()
        $admission=Assert-WinCareServiceUri -Uri $builder.Uri `
            -AllowPrivateNetwork:($Provider -eq 'Custom' -and $AllowPrivateNetwork)
        $http=Invoke-WinCareBoundedJsonHttpRequest -Method GET -Admission $admission `
            -Headers @{Accept='application/dns-json'} `
            -TimeoutSeconds $TimeoutSeconds `
            -MaximumResponseBytes $MaximumResponseBytes
        if(-not $http.IsSuccessStatusCode) {
            throw "DoH endpoint returned HTTP $($http.StatusCode): $($http.ReasonPhrase)"
        }
        $response=ConvertFrom-WinCareDohResponse -Body $http.Body `
            -MaximumAnswers $MaximumAnswers
        $success=$response.Status -eq 0
        $message=if($success) {
            'DNS-over-HTTPS query completed.'
        } else {
            "DoH server returned DNS status $($response.Status)."
        }
        New-WinCareBridgeResult -Success $success `
            -Code $(if($success){'DohQuerySucceeded'}else{'DohQueryFailed'}) `
            -Message $message -ExitCode $(if($success){0}else{1}) `
            -Data @{
                Domain=$Domain
                RecordType=$RecordType
                Provider=$Provider
                Endpoint=$endpoint.AbsoluteUri
                ResolvedAddresses=$admission.ResolvedAddresses
                Status=$response.Status
                Answers=$response.Answers
                AuthenticatedData=$response.AuthenticatedData
                Truncated=$response.Truncated
                ResponseBytes=$http.BodyBytes
                ResponseSha256=$http.BodySha256
                EvidenceType='BoundedDohJsonResponse'
            }
    } catch {
        New-WinCareBridgeResult -Success $false -Code 'DohQueryException' `
            -Message $_.Exception.Message -ExitCode 1 `
            -Data @{
                Domain=$Domain
                Provider=$Provider
                Endpoint=if($endpoint){$endpoint.AbsoluteUri}else{$null}
            }
    }
}

function Optimize-WinCareCdnRouting {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Nodes,
        [ValidateRange(1,20)][int]$SampleCount=4,
        [ValidateRange(100,10000)][int]$TimeoutMilliseconds=1500,
        [ValidateRange(1,65535)][int]$Port=443
    )

    $measurements=[Collections.Generic.List[object]]::new()
    foreach($node in $Nodes) {
        $target=$node
        if([uri]::IsWellFormedUriString($node,[UriKind]::Absolute)) {
            $target=([uri]$node).Host
        }
        $measurements.Add(
            (Measure-WinCareNetworkEndpoint -Target $target `
                -SampleCount $SampleCount -TimeoutMilliseconds $TimeoutMilliseconds `
                -Port $Port)
        )
    }
    $ranked=@(
        $measurements|Where-Object Reachable|Sort-Object `
            @{Expression={if($null -ne $_.TcpConnectMs){$_.TcpConnectMs}else{[double]::MaxValue}}}, `
            @{Expression={if($null -ne $_.LatencyMs){$_.LatencyMs}else{[double]::MaxValue}}}
    )
    [pscustomobject]@{
        EvaluatedNodes=$Nodes.Count
        ReachableNodes=$ranked.Count
        BestNode=if($ranked.Count){$ranked[0].Target}else{$null}
        LowestTcpConnectMs=if($ranked.Count){$ranked[0].TcpConnectMs}else{$null}
        LowestIcmpLatencyMs=if($ranked.Count){$ranked[0].LatencyMs}else{$null}
        Measurements=@($measurements)
        Status=if($ranked.Count){'Measured'}else{'Unreachable'}
        CapturedAt=[datetime]::UtcNow.ToString('o')
    }
}
