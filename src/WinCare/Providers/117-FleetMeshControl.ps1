#requires -Version 7.2

function Get-WinCareFleetCoordinatorStatePath {
    [CmdletBinding()]
    param([string]$StatePath)
    if($StatePath) {
        return Resolve-WinCareCanonicalPath -LiteralPath $StatePath -AllowMissing
    }
    $base=if($env:LOCALAPPDATA){$env:LOCALAPPDATA}else{[IO.Path]::GetTempPath()}
    Resolve-WinCareCanonicalPath -LiteralPath (Join-Path $base 'WinCare\fleet-coordinator-state.json') -AllowMissing
}

function Get-WinCareFleetNodeInventory {
    [CmdletBinding()]
    param(
        [ValidateCount(0,64)][string[]]$PeerUri=@(),
        [hashtable]$PeerBearerToken=@{},
        [string]$StatePath,
        [switch]$AllowPrivateNetworkPeers,
        [ValidateRange(1,30)][int]$TimeoutSeconds=5
    )

    $nodes=[Collections.Generic.List[object]]::new()
    $localPath=Get-WinCareFleetCoordinatorStatePath -StatePath $StatePath
    if(Test-Path -LiteralPath $localPath -PathType Leaf) {
        try {
            $local=Read-WinCareBoundedJson -LiteralPath $localPath -MaximumBytes 1048576 -Depth 16
            $nodes.Add([pscustomobject]@{
                Uri=$null
                NodeId=[string]$local.NodeId
                Role=[string]$local.Role
                Revision=[long]$local.Revision
                PolicyHash=[string]$local.PolicyHash
                Protocol=[string]$local.Protocol
                Reachable=$true
                Authenticated=$true
                Local=$true
                Error=$null
                EvidenceType='BoundedLocalFleetState'
            })
        } catch {
            $nodes.Add([pscustomobject]@{
                Uri=$null
                NodeId=$env:COMPUTERNAME
                Role='Local'
                Revision=$null
                PolicyHash=$null
                Protocol=$null
                Reachable=$false
                Authenticated=$false
                Local=$true
                Error=$_.Exception.Message
                EvidenceType='LocalFleetStateFailure'
            })
        }
    } else {
        $nodes.Add([pscustomobject]@{
            Uri=$null
            NodeId=$env:COMPUTERNAME
            Role='Local'
            Revision=$null
            PolicyHash=$null
            Protocol=$null
            Reachable=$true
            Authenticated=$true
            Local=$true
            Error=$null
            EvidenceType='LocalHostIdentity'
        })
    }

    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($peer in @($PeerUri)) {
        if([string]::IsNullOrWhiteSpace($peer) -or -not $seen.Add($peer)) { continue }
        try {
            $admission=Test-WinCareFleetPeerUri -Peer $peer -AllowPrivateNetwork:$AllowPrivateNetworkPeers
            $token=[string]$PeerBearerToken[$peer]
            if([string]::IsNullOrWhiteSpace($token) -or $token.Length -lt 32) {
                throw 'A bearer token of at least 32 characters is required for each peer.'
            }
            $healthUri=[uri]::new([uri]$admission.Uri,'/health')
            $healthAdmission=Assert-WinCareServiceUri -Uri $healthUri `
                -AllowPrivateNetwork:([bool]$admission.AllowPrivateNetwork) `
                -AllowLoopbackHttp:([bool]$admission.AllowLoopbackHttp)
            $response=Invoke-WinCareBoundedJsonHttpRequest -Method GET `
                -Admission $healthAdmission `
                -Headers @{Authorization='Bearer '+$token} `
                -TimeoutSeconds $TimeoutSeconds `
                -MaximumResponseBytes 65536
            if(-not $response.IsSuccessStatusCode) {
                throw "Peer returned HTTP $($response.StatusCode)."
            }
            $health=$response.Body|ConvertFrom-Json -Depth 8 -ErrorAction Stop
            if(-not [bool]$health.ok) { throw 'Peer health response did not report success.' }
            if([string]$health.protocol -ne 'AuthenticatedHealthQuorumV1') {
                throw 'Peer returned an incompatible fleet protocol.'
            }
            $nodes.Add([pscustomobject]@{
                Uri=$peer
                NodeId=[string]$health.nodeId
                Role=[string]$health.role
                Revision=[long]$health.revision
                PolicyHash=[string]$health.policyHash
                Protocol=[string]$health.protocol
                Reachable=$true
                Authenticated=$true
                Local=$false
                Error=$null
                ResponseSha256=[string]$response.ResponseSha256
                EvidenceType='AuthenticatedBoundedFleetHealth'
            })
        } catch {
            $nodes.Add([pscustomobject]@{
                Uri=$peer
                NodeId=$null
                Role=$null
                Revision=$null
                PolicyHash=$null
                Protocol=$null
                Reachable=$false
                Authenticated=$false
                Local=$false
                Error=(ConvertTo-WinCareRedactedScalar -Value $_.Exception.Message -MaximumStringCharacters 1024)
                EvidenceType='FleetPeerAdmissionOrHealthFailure'
            })
        }
    }

    $total=$nodes.Count
    $reachable=@($nodes|Where-Object Reachable).Count
    $quorum=[math]::Floor($total/2)+1
    [pscustomobject]@{
        CapturedAt=[datetime]::UtcNow.ToString('o')
        Nodes=@($nodes)
        TotalNodes=$total
        ReachableNodes=$reachable
        QuorumRequired=$quorum
        QuorumAvailable=$reachable -ge $quorum
        ReplicationProtocol='None'
        RaftImplemented=$false
        EvidenceType='AuthenticatedFleetInventory'
    }
}

function Compare-WinCareFleetPolicyState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PolicyPath,
        [ValidateCount(0,64)][string[]]$PeerUri=@(),
        [hashtable]$PeerBearerToken=@{},
        [string]$StatePath,
        [switch]$AllowPrivateNetworkPeers,
        [ValidateRange(1,30)][int]$TimeoutSeconds=5
    )

    $policy=Assert-WinCareSafePath -LiteralPath $PolicyPath
    $item=Get-Item -LiteralPath $policy -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'The fleet policy must be a regular non-reparse file.'
    }
    if([long]$item.Length -gt 16777216) { throw 'The fleet policy exceeds the 16 MiB limit.' }
    $localHash=Get-WinCareSha256 -LiteralPath $policy -MaximumBytes 16777216
    $inventory=Get-WinCareFleetNodeInventory `
        -PeerUri $PeerUri `
        -PeerBearerToken $PeerBearerToken `
        -StatePath $StatePath `
        -AllowPrivateNetworkPeers:$AllowPrivateNetworkPeers `
        -TimeoutSeconds $TimeoutSeconds

    $comparisons=foreach($node in @($inventory.Nodes)) {
        $remoteHash=[string]$node.PolicyHash
        $state=if(-not $node.Reachable) {
            'Unreachable'
        } elseif([string]::IsNullOrWhiteSpace($remoteHash)) {
            'Unknown'
        } elseif($remoteHash -eq $localHash) {
            'InSync'
        } else {
            'Drifted'
        }
        [pscustomobject]@{
            NodeId=$node.NodeId
            Uri=$node.Uri
            Local=$node.Local
            Reachable=$node.Reachable
            LocalPolicySha256=$localHash
            ObservedPolicySha256=$remoteHash
            State=$state
            EvidenceType='PolicyDigestComparison'
        }
    }

    [pscustomobject]@{
        PolicyPath=$policy
        PolicySha256=$localHash
        Nodes=@($comparisons)
        InSyncCount=@($comparisons|Where-Object State -eq 'InSync').Count
        DriftedCount=@($comparisons|Where-Object State -eq 'Drifted').Count
        UnknownCount=@($comparisons|Where-Object State -in @('Unknown','Unreachable')).Count
        QuorumAvailable=$inventory.QuorumAvailable
        MutationPerformed=$false
        EvidenceType='FleetPolicyDigestAudit'
    }
}
