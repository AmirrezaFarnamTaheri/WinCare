BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }

Describe 'WinCare authenticated local fleet coordinator' {
    It 'exports a complete coordinator lifecycle and local policy recorder' {
        foreach($name in @('Start-WinCareRaftMeshNode','Stop-WinCareFleetCoordinator','Sync-WinCareFleetPolicies')){
            Get-Command $name -Module WinCare | Should -Not -BeNullOrEmpty
        }
    }

    It 'truthfully declares that remote replication and Raft are not implemented' {
        $source=Get-Content (Join-Path $PSScriptRoot '..\..\..\src\WinCare\Providers\126-P2pRaftFleetOrchestrator.ps1') -Raw
        $source|Should -Match 'RaftImplemented=\$false'
        $source|Should -Match 'RemoteReplicationPerformed=\$false'
        $source|Should -Not -Match "AbsolutePath -eq '/state'"
        $source|Should -Match 'FixedTimeEquals'
    }

    It 'requires authentication before starting the listener' {
        $result=Start-WinCareRaftMeshNode -Port 19090
        $result.Success|Should -BeFalse
        $result.Status|Should -Be 'Blocked'
        $result.Code|Should -Be 'FleetBearerTokenRequired'
    }

    It 'blocks local policy recording when authenticated quorum is unavailable' {
        $policy=Join-Path $TestDrive 'policy.json'
        '{}'|Set-Content -LiteralPath $policy
        $result=Sync-WinCareFleetPolicies -PolicyPath $policy -Peers @('http://127.0.0.1:1') -StatePath (Join-Path $TestDrive 'state.json') -TimeoutSeconds 1 -Confirm:$false
        $result.Success|Should -BeFalse
        $result.Status|Should -Be 'Blocked'
        $result.Code|Should -Be 'FleetQuorumUnavailable'
        $result.Data.RemoteReplicationPerformed|Should -BeFalse
    }
}
