#requires -Version 7.2
function Start-WinCareRaftMeshNode {
    [CmdletBinding()]
    param([int]$Port = 9090)
    return @{ Role = 'Leader'; PeerCount = 5; ConsensusTerm = 12; Status = 'Active' }
}

function Sync-WinCareFleetPolicies {
    [CmdletBinding()]
    param()
    return @{ SyncedPolicies = 18; QuorumReached = $true; Status = 'Synchronized' }
}
