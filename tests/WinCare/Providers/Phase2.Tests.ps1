# See Phase1.Tests.ps1: file-scope import for Pester 5 Discovery, and
# InModuleScope because these providers are internal and depend on Core helpers
# that dot-sourcing a single provider file would leave undefined.
Import-Module (Join-Path $PSScriptRoot '../../../src/WinCare/WinCare.psd1') -Force -Global

Describe 'WinCare Phase 2 Provider Suite' {
    InModuleScope WinCare {
        BeforeEach {
            $script:WinCareState = @{
                Config = Get-WinCareDefaultConfig
                Policy = Get-WinCareDefaultPolicy
                Root = $TestDrive
                SessionId = '0123456789abcdef0123456789abcdef'
                IsAdmin = $false
                Capabilities = @{}
                ActionContracts = $null
            }
        }

        Context '25-SystemPolicy provider' {
            It 'reports a typed failure for a policy file that does not exist' {
                # A TestDrive path rather than a hardcoded C:\ path, so the case
                # is genuinely "absent" on every platform. The provider returns a
                # WinCare result carrying PolicyFileNotFound -- it does not return
                # an empty array, and it does not throw.
                $missing = Join-Path $TestDrive 'absent-registry.pol'
                $result = Get-WinCarePolicyFileEntries -PolFilePath $missing
                $result | Should -Not -BeNullOrEmpty
                $result.Success | Should -BeFalse
                $result.Code | Should -Be 'PolicyFileNotFound'
            }
        }

        Context '55-Internals provider' {
            It 'reports Fault-Tolerant Heap state without asserting it is enabled' {
                # Observed contract is Supported/Configured/Enabled/EffectiveState/...;
                # there is no FthEnabled property. A clean runner has no
                # HKLM:\Software\Microsoft\FTH key, so Enabled is legitimately
                # $null -- asserting $true would assert the environment rather
                # than the provider.
                $heap = Get-WinCareFaultTolerantHeapState
                $heap | Should -Not -BeNullOrEmpty
                $heap.PSObject.Properties.Name | Should -Contain 'Supported'
                $heap.PSObject.Properties.Name | Should -Contain 'Enabled'
                $heap.PSObject.Properties.Name | Should -Contain 'EffectiveState'
            }
        }

        Context '87-Playbooks provider' {
            It 'lists maintenance playbooks derived from the shipped catalog' {
                # Follows the catalog instead of a hardcoded count, so adding a
                # playbook no longer breaks the suite.
                $expected = @(Get-WinCarePlaybook).Count
                $playbooks = Get-WinCareMaintenancePlaybooks
                $playbooks | Should -Not -BeNullOrEmpty
                if ($expected -gt 0) {
                    @($playbooks).Count | Should -Be $expected
                }
            }
        }

        Context '106-PeerUtilities provider' {
            It 'reports peer discovery state for this device' {
                # The property is DeviceName, not PeerDeviceName.
                $peer = Get-WinCarePeerDiscoveryState
                $peer | Should -Not -BeNullOrEmpty
                $peer.PSObject.Properties.Name | Should -Contain 'DeviceName'
                $peer.PSObject.Properties.Name | Should -Contain 'EffectiveState'
                $peer.DeviceName | Should -Not -BeNullOrEmpty
            }
        }
    }
}
