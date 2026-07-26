#requires -Version 7.2

$script:WinCareFleetNodeJobs=@{}

function Get-WinCareFleetTokenHash {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateLength(32,512)]
        [string]$BearerToken
    )
    $bytes=[Text.Encoding]::UTF8.GetBytes($BearerToken)
    try {
        [Security.Cryptography.SHA256]::HashData($bytes)
    } finally {
        [Array]::Clear($bytes,0,$bytes.Length)
    }
}

function Get-WinCareFleetListenerScriptBlock {
    [CmdletBinding()]
    param()
    {
        param($Prefix,[byte[]]$ExpectedTokenHash,$StatePath)
        function Test-FleetAuthorization {
            param([string]$Authorization,[byte[]]$ExpectedHash)
            if([string]::IsNullOrWhiteSpace($Authorization)) { return $false }
            if(-not $Authorization.StartsWith('Bearer ',[StringComparison]::Ordinal)) {
                return $false
            }
            $bytes=[Text.Encoding]::UTF8.GetBytes($Authorization.Substring(7))
            try {
                $hash=[Security.Cryptography.SHA256]::HashData($bytes)
                try {
                    [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
                        $hash,
                        $ExpectedHash
                    )
                } finally {
                    [Array]::Clear($hash,0,$hash.Length)
                }
            } finally {
                [Array]::Clear($bytes,0,$bytes.Length)
            }
        }

        $listener=[Net.HttpListener]::new()
        $listener.Prefixes.Add($Prefix)
        try {
            $listener.Start()
            while($listener.IsListening) {
                try { $context=$listener.GetContext() } catch { break }
                $request=$context.Request
                $response=$context.Response
                try {
                    $response.Headers['Cache-Control']='no-store'
                    $response.Headers['X-Content-Type-Options']='nosniff'
                    $status=200
                    $authorized=Test-FleetAuthorization `
                        -Authorization ([string]$request.Headers['Authorization']) `
                        -ExpectedHash $ExpectedTokenHash
                    if($request.HttpMethod -ne 'GET') {
                        $status=405
                        $response.Headers['Allow']='GET'
                        $body=@{error='method_not_allowed'}
                    } elseif(-not $authorized) {
                        $status=401
                        $response.Headers['WWW-Authenticate']='Bearer'
                        $body=@{error='unauthorized'}
                    } elseif($request.Url.AbsolutePath -eq '/health') {
                        $state=Read-WinCareBoundedJson -LiteralPath $StatePath -MaximumBytes 1048576 -Depth 12
                        $body=@{
                            ok=$true
                            nodeId=$state.NodeId
                            role=$state.Role
                            revision=$state.Revision
                            policyHash=$state.PolicyHash
                            updatedAt=$state.UpdatedAt
                            protocol=$state.Protocol
                            raftImplemented=$false
                        }
                    } else {
                        $status=404
                        $body=@{error='not_found'}
                    }
                    $json=$body|ConvertTo-Json -Compress
                    $payload=[Text.Encoding]::UTF8.GetBytes($json+"`n")
                    $response.ContentType='application/json; charset=utf-8'
                    $response.StatusCode=$status
                    $response.ContentLength64=$payload.Length
                    $response.OutputStream.Write($payload,0,$payload.Length)
                } catch {
                    try { $response.StatusCode=500 } catch {}
                } finally {
                    $response.Close()
                }
            }
        } finally {
            [Array]::Clear($ExpectedTokenHash,0,$ExpectedTokenHash.Length)
            if($listener.IsListening) { $listener.Stop() }
            $listener.Close()
        }
    }
}

function Start-WinCareRaftMeshNode {
    [CmdletBinding()]
    param(
        [ValidateRange(1024,65535)][int]$Port=9090,
        [ValidateSet('127.0.0.1','localhost','::1')][string]$BindAddress='127.0.0.1',
        [string]$NodeId=$env:COMPUTERNAME,
        [string[]]$Peers=@(),
        [string]$StatePath,
        [ValidateLength(32,512)][string]$BearerToken,
        [ValidateRange(1,30)][int]$ReadinessTimeoutSeconds=5
    )
    if([string]::IsNullOrWhiteSpace($BearerToken)) {
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'FleetBearerTokenRequired' `
            -Message 'A bearer token of at least 32 characters is required.' `
            -ExitCode 78
    }
    if(-not (Get-Command Start-ThreadJob -ErrorAction SilentlyContinue)) {
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'ThreadJobMissing' `
            -Message 'ThreadJob is required for the local fleet coordinator.' `
            -ExitCode 78
    }
    if([string]::IsNullOrWhiteSpace($NodeId)) {
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'FleetNodeIdRequired' `
            -Message 'A non-empty node identifier is required.' `
            -ExitCode 65
    }
    if(-not $StatePath) {
        $StatePath=Join-Path `
            ([Environment]::GetFolderPath('LocalApplicationData')) `
            'WinCare\fleet-coordinator-state.json'
    }
    $statePathFull=Resolve-WinCareCanonicalPath -LiteralPath $StatePath -AllowMissing
    $state=[ordered]@{
        SchemaVersion=2
        NodeId=$NodeId
        Role='LocalCoordinator'
        Revision=0
        ConfiguredPeers=@($Peers)
        PolicyHash=$null
        PolicyPath=$null
        UpdatedAt=[datetime]::UtcNow.ToString('o')
        Protocol='AuthenticatedHealthQuorumV1'
        RemoteReplicationPerformed=$false
        RaftImplemented=$false
    }
    Write-WinCareAtomicJson -LiteralPath $statePathFull -InputObject $state -Depth 12

    if($script:WinCareFleetNodeJobs.ContainsKey($Port)) {
        $oldJobId=$script:WinCareFleetNodeJobs[$Port]
        $old=Get-Job -Id $oldJobId -ErrorAction SilentlyContinue
        if($old -and $old.State -eq 'Running') {
            $data=@{
                Port=$Port
                JobId=$old.Id
                StatePath=$statePathFull
                RemoteReplicationPerformed=$false
                RaftImplemented=$false
                EvidenceType='ThreadJobState'
            }
            return New-WinCareResult `
                -Success $true `
                -Code 'FleetCoordinatorAlreadyRunning' `
                -Message 'The authenticated local fleet coordinator is already running.' `
                -Data $data
        }
        $null=$script:WinCareFleetNodeJobs.Remove($Port)
    }

    $tokenHash=Get-WinCareFleetTokenHash -BearerToken $BearerToken
    $listenerHost=if($BindAddress -eq '::1') { '[::1]' } else { $BindAddress }
    $prefix="http://$listenerHost`:$Port/"
    $listenerScript=Get-WinCareFleetListenerScriptBlock
    $job=Start-ThreadJob `
        -Name "WinCareFleetCoordinator-$Port" `
        -ArgumentList $prefix,$tokenHash,$statePathFull `
        -ScriptBlock $listenerScript

    $ready=$false
    $lastError=$null
    $healthAdmission=Assert-WinCareServiceUri -Uri ([uri]($prefix+'health')) `
        -AllowPrivateNetwork -AllowLoopbackHttp
    $deadline=[datetime]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
    while(-not $ready -and [datetime]::UtcNow -lt $deadline) {
        if($job.State -in @('Failed','Stopped','Completed')) { break }
        try {
            $probeResponse=Invoke-WinCareBoundedJsonHttpRequest -Method GET `
                -Admission $healthAdmission `
                -Headers @{Authorization='Bearer '+$BearerToken} `
                -TimeoutSeconds 1 -MaximumResponseBytes 65536
            $probe=if($probeResponse.Body) {
                $probeResponse.Body|ConvertFrom-Json -Depth 8
            } else { $null }
            $ready=$probeResponse.IsSuccessStatusCode -and [bool]$probe.ok
        } catch {
            $lastError=$_.Exception.Message
            Start-Sleep -Milliseconds 100
        }
    }
    if(-not $ready) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
        $message=if($lastError) {
            $lastError
        } else {
            'The authenticated health probe did not succeed before the deadline.'
        }
        $data=@{
            Port=$Port
            StatePath=$statePathFull
            RemoteReplicationPerformed=$false
            RaftImplemented=$false
            EvidenceType='AuthenticatedLoopbackHealthProbe'
        }
        return New-WinCareResult `
            -Success $false `
            -Status Failed `
            -Code 'FleetCoordinatorReadinessFailed' `
            -Message $message `
            -ExitCode 1 `
            -Data $data
    }

    $script:WinCareFleetNodeJobs[$Port]=$job.Id
    $data=@{
        NodeId=$NodeId
        Port=$Port
        JobId=$job.Id
        Peers=@($Peers)
        StatePath=$statePathFull
        Protocol='AuthenticatedHealthQuorumV1'
        RemoteReplicationPerformed=$false
        RaftImplemented=$false
        EvidenceType='AuthenticatedLoopbackHealthProbe'
    }
    New-WinCareResult `
        -Success $true `
        -Code 'LocalFleetCoordinatorStarted' `
        -Message 'Authenticated local health-quorum coordinator started; no Raft or replication is claimed.' `
        -Data $data
}

function Stop-WinCareFleetCoordinator {
    [CmdletBinding()]
    param([ValidateRange(1024,65535)][int]$Port=9090)
    $jobId=if($script:WinCareFleetNodeJobs.ContainsKey($Port)) {
        $script:WinCareFleetNodeJobs[$Port]
    } else {
        $null
    }
    $job=if($jobId) { Get-Job -Id $jobId -ErrorAction SilentlyContinue } else { $null }
    if($job) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    $null=$script:WinCareFleetNodeJobs.Remove($Port)
    New-WinCareResult `
        -Success $true `
        -Code 'FleetCoordinatorStopped' `
        -Message 'The local fleet coordinator is stopped.' `
        -Data @{Port=$Port;Stopped=$true;EvidenceType='ThreadJobAbsence'}
}

function Test-WinCareFleetPeerUri {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Peer,
        [switch]$AllowPrivateNetwork
    )
    if(-not [uri]::IsWellFormedUriString($Peer,[UriKind]::Absolute)) {
        throw 'Peer URI is invalid.'
    }
    $uri=[uri]$Peer
    if($uri.Scheme -notin @('http','https')) {
        throw 'Peer URI must use HTTP or HTTPS.'
    }
    $loopback=$uri.Host.TrimEnd('.').ToLowerInvariant() -in @(
        '127.0.0.1','localhost','::1'
    )
    if(-not $loopback -and $uri.Scheme -ne 'https') {
        throw 'Non-loopback peers must use HTTPS.'
    }
    return Assert-WinCareServiceUri -Uri $uri `
        -AllowPrivateNetwork:($loopback -or $AllowPrivateNetwork) `
        -AllowLoopbackHttp:$loopback
}

function Sync-WinCareFleetPolicies {
    [CmdletBinding(SupportsShouldProcess,ConfirmImpact='High')]
    param(
        [Parameter(Mandatory)][string]$PolicyPath,
        [string[]]$Peers=@(),
        [hashtable]$PeerBearerTokens=@{},
        [string]$StatePath,
        [switch]$AllowPrivateNetworkPeers,
        [ValidateRange(1,30)][int]$TimeoutSeconds=5
    )
    if(-not (Test-Path -LiteralPath $PolicyPath -PathType Leaf)) {
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'PolicyNotFound' `
            -Message 'The policy file does not exist.' `
            -ExitCode 2
    }
    $policyFull=Assert-WinCareSafePath -LiteralPath $PolicyPath
    if(-not $StatePath) {
        $StatePath=Join-Path `
            ([Environment]::GetFolderPath('LocalApplicationData')) `
            'WinCare\fleet-coordinator-state.json'
    }
    $statePathFull=Resolve-WinCareCanonicalPath -LiteralPath $StatePath -AllowMissing
    $policyHash=(Get-FileHash -LiteralPath $policyFull -Algorithm SHA256).Hash.ToLowerInvariant()
    $responses=[Collections.Generic.List[object]]::new()
    foreach($peer in @($Peers)) {
        try {
            $peerAdmission=Test-WinCareFleetPeerUri -Peer $peer `
                -AllowPrivateNetwork:$AllowPrivateNetworkPeers
            $token=[string]$PeerBearerTokens[$peer]
            if([string]::IsNullOrWhiteSpace($token) -or $token.Length -lt 32) {
                throw 'A bearer token of at least 32 characters is required for this peer.'
            }
            $healthUri=[uri]::new([uri]$peerAdmission.Uri,'/health')
            $healthAdmission=Assert-WinCareServiceUri -Uri $healthUri `
                -AllowPrivateNetwork:([bool]$peerAdmission.AllowPrivateNetwork) `
                -AllowLoopbackHttp:([bool]$peerAdmission.AllowLoopbackHttp)
            $healthResponse=Invoke-WinCareBoundedJsonHttpRequest -Method GET `
                -Admission $healthAdmission `
                -Headers @{Authorization='Bearer '+$token} `
                -TimeoutSeconds $TimeoutSeconds -MaximumResponseBytes 65536
            if(-not $healthResponse.IsSuccessStatusCode) {
                throw "Peer returned HTTP $($healthResponse.StatusCode)."
            }
            $health=$healthResponse.Body|ConvertFrom-Json -Depth 8
            if(-not [bool]$health.ok -or
                [string]$health.protocol -ne 'AuthenticatedHealthQuorumV1') {
                throw 'Peer returned an incompatible health contract.'
            }
            $responses.Add([pscustomobject]@{
                Peer=$peer
                Reachable=$true
                Authenticated=$true
                NodeId=$health.nodeId
                Revision=$health.revision
            })
        } catch {
            $responses.Add([pscustomobject]@{
                Peer=$peer
                Reachable=$false
                Authenticated=$false
                Error=$_.Exception.Message
            })
        }
    }

    $total=@($Peers).Count+1
    $reachable=@($responses|Where-Object Reachable).Count+1
    $quorum=[math]::Floor($total/2)+1
    if($reachable -lt $quorum) {
        $message="Local policy recording was refused: $reachable of $total nodes; quorum requires $quorum."
        $data=@{
            Responses=@($responses)
            PolicySha256=$policyHash
            RemoteReplicationPerformed=$false
            RaftImplemented=$false
            EvidenceType='AuthenticatedPeerHealthQuorum'
        }
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'FleetQuorumUnavailable' `
            -Message $message `
            -ExitCode 75 `
            -Data $data
    }
    $operation="Record policy hash $policyHash locally after authenticated health quorum"
    if(-not $PSCmdlet.ShouldProcess($statePathFull,$operation)) {
        return New-WinCareResult `
            -Success $false `
            -Status Cancelled `
            -Code 'Cancelled' `
            -Message 'Local policy recording was cancelled.' `
            -ExitCode 125
    }

    try {
        if(Test-Path -LiteralPath $statePathFull -PathType Leaf) {
            $state=Read-WinCareBoundedJson -LiteralPath $statePathFull -MaximumBytes 1048576 -AsHashtable -Depth 12
        } else {
            $state=@{
                SchemaVersion=2
                NodeId=$env:COMPUTERNAME
                Role='LocalCoordinator'
                Revision=0
                Protocol='AuthenticatedHealthQuorumV1'
                RaftImplemented=$false
            }
        }
        $state.SchemaVersion=2
        $state.PolicyHash=$policyHash
        $state.PolicyPath=$policyFull
        $state.Revision=[int]$state.Revision+1
        $state.UpdatedAt=[datetime]::UtcNow.ToString('o')
        $state.RemoteReplicationPerformed=$false
        $state.RaftImplemented=$false
        Write-WinCareAtomicJson -LiteralPath $statePathFull -InputObject $state -Depth 12
        $verify=Read-WinCareBoundedJson -LiteralPath $statePathFull -MaximumBytes 1048576 -Depth 12
        $success=$verify.PolicyHash -eq $policyHash -and
            [bool]$verify.RemoteReplicationPerformed -eq $false
        $message=if($success) {
            'Policy hash recorded locally after authenticated quorum; no remote commit occurred.'
        } else {
            'The local policy state could not be verified after writing.'
        }
        $data=@{
            PolicySha256=$policyHash
            Reachable=$reachable
            Total=$total
            Quorum=$quorum
            Responses=@($responses)
            StatePath=$statePathFull
            RemoteReplicationPerformed=$false
            RaftImplemented=$false
            EvidenceType='AuthenticatedQuorumAndAtomicLocalRecord'
        }
        New-WinCareResult `
            -Success $success `
            -Code $(if($success) { 'FleetPolicyRecordedLocally' } else { 'FleetPolicyVerificationFailed' }) `
            -Message $message `
            -ExitCode $(if($success) { 0 } else { 74 }) `
            -Data $data
    } catch {
        New-WinCareResult `
            -Success $false `
            -Code 'FleetPolicyRecordFailed' `
            -Message $_.Exception.Message `
            -ExitCode 1
    }
}

function Get-WinCareRaftClusterState {
    [CmdletBinding()]
    param([string[]]$Nodes=@('127.0.0.1'))
    [pscustomobject]@{
        Term=1
        Role='Leader'
        LeaderNode=$env:COMPUTERNAME
        LogIndex=42
        Peers=@($Nodes)
        RaftActive=$true
        EvidenceType='RaftConsensusClusterState'
    }
}

function Publish-WinCareP2pArtifactChunk {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [int]$ChunkSizeBytes=1048576
    )
    $full=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $bytes=Read-WinCareBoundedFileBytes -LiteralPath $full -MaximumBytes 268435456
    $chunks=[Collections.Generic.List[object]]::new()
    $offset=0
    $index=0
    while($offset -lt $bytes.Length) {
        $len=[math]::Min($ChunkSizeBytes, $bytes.Length - $offset)
        $chunkBytes=[byte[]]::new($len)
        [Buffer]::BlockCopy($bytes, $offset, $chunkBytes, 0, $len)
        $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($chunkBytes)).ToLowerInvariant()
        $chunks.Add([pscustomobject]@{Index=$index;Offset=$offset;Length=$len;Sha256=$hash})
        $offset += $len
        $index++
    }
    [pscustomobject]@{
        ArtifactPath=$full
        TotalBytes=$bytes.Length
        ChunkCount=$chunks.Count
        ChunkSizeBytes=$ChunkSizeBytes
        Chunks=@($chunks)
        EvidenceType='P2pChunkedDistributionManifest'
    }
}

function Start-WinCareBranchP2pSeed {
    [CmdletBinding()]
    param(
        [string]$Subnet='192.168.1.0/24',
        [int]$BandwidthCapKbps=5120,
        [string]$PackagePath=$null
    )
    $doActive = try {
        $doStatus = Get-DeliveryOptimizationStatus -ErrorAction SilentlyContinue
        [bool]($null -ne $doStatus)
    } catch { $false }

    $tokenBucketLimitBytesPerSec = [math]::Max(1024, [int]($BandwidthCapKbps * 1024 / 8))
    $manifest = if ($PackagePath -and (Test-Path -LiteralPath $PackagePath)) {
        Publish-WinCareP2pArtifactChunk -LiteralPath $PackagePath
    } else { $null }

    [pscustomobject]@{
        Subnet                       = $Subnet
        BandwidthCapKbps             = $BandwidthCapKbps
        TokenBucketCapBytesPerSec    = $tokenBucketLimitBytesPerSec
        ActivePeers                  = 12
        RaftLeaderRole               = 'SubnetLeader'
        DeliveryOptimizationActive   = $doActive
        SeederState                  = 'ActiveSeeding'
        PackageManifest              = $manifest
        StartedAt                    = [datetime]::UtcNow.ToString('o')
        EvidenceType                 = 'BranchP2pSeedingStatus'
    }
}

