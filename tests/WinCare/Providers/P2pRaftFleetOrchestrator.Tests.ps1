. "$PSScriptRoot\..\..\..\src\WinCare\Providers\126-P2pRaftFleetOrchestrator.ps1"

Describe 'WinCare P2P Raft Fleet Orchestrator Module' {
    It 'Should export Raft node start and policy sync cmdlets' {
        (Get-Command 'Start-WinCareRaftMeshNode' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Sync-WinCareFleetPolicies' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should start Raft node and synchronize fleet policies' {
        $raft = Start-WinCareRaftMeshNode -Port 9090
        $raft.Status | Should Be 'Active'
        $sync = Sync-WinCareFleetPolicies
        $sync.QuorumReached | Should Be $true
    }
}
