Describe "WinCare Phase 2 Provider Suite" {
    BeforeAll {
        $providerRoot = Join-Path $PSScriptRoot "..\..\..\src\WinCare\Providers"
        . (Join-Path $providerRoot "25-SystemPolicy.ps1")
        . (Join-Path $providerRoot "27-RegistryTree.ps1")
        . (Join-Path $providerRoot "50-Health.ps1")
        . (Join-Path $providerRoot "55-Internals.ps1")
        . (Join-Path $providerRoot "56-MemoryManagement.ps1")
        . (Join-Path $providerRoot "60-Updates.ps1")
        . (Join-Path $providerRoot "65-WindowsUpdateAgent.ps1")
        . (Join-Path $providerRoot "87-Playbooks.ps1")
        . (Join-Path $providerRoot "106-PeerUtilities.ps1")
    }

    Context "25-SystemPolicy Provider" {
        It "Should return empty array when policy file does not exist" {
            $entries = Get-WinCarePolicyFileEntries -PolFilePath "C:\NonExistentFile.pol"
            $entries | Should BeNullOrEmpty
        }
    }

    Context "50-Health & 55-Internals Provider" {
        It "Should return process health overview" {
            $procs = Get-WinCareProcessHealthOverview
            $procs | Should Not BeNullOrEmpty
            $procs.Count | Should BeGreaterThan 0
        }

        It "Should query Fault-Tolerant Heap state" {
            $fth = Get-WinCareFaultTolerantHeapState
            $fth | Should Not BeNullOrEmpty
            $fth.FthEnabled | Should Be $true
        }
    }

    Context "60-Updates Provider" {
        It "Should query Windows Update service state" {
            $wu = Get-WinCareUpdateServiceState
            $wu | Should Not BeNullOrEmpty
            $wu.ServiceName | Should Be "wuauserv"
        }
    }

    Context "87-Playbooks & 106-PeerUtilities Provider" {
        It "Should list maintenance playbooks" {
            $pbs = Get-WinCareMaintenancePlaybooks
            $pbs.Count | Should Be 3
        }

        It "Should query peer discovery state" {
            $peer = Get-WinCarePeerDiscoveryState
            $peer.PeerDeviceName | Should Be $env:COMPUTERNAME
        }
    }
}
