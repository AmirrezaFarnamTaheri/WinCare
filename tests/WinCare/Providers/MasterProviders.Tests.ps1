# Pester 5 executes Describe bodies -- and therefore InModuleScope -- during
# Discovery, which runs before any BeforeAll. The module must already be loaded
# at that point, otherwise InModuleScope throws "No modules named 'WinCare' are
# currently loaded" and the entire container fails without running one test.
# This import is deliberately at file scope; the BeforeAll imports below remain
# for run-time state. $PSScriptRoot is used so the path never depends on the
# caller's working directory.
Import-Module (Join-Path $PSScriptRoot '../../../src/WinCare/WinCare.psd1') -Force -Global

Describe 'WinCare provider module suite' {
    BeforeAll {
        $manifestPath = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1')
        Import-Module $manifestPath -Force -ErrorAction Stop
    }

    InModuleScope WinCare {
        Context 'Provider function availability' {
            It 'loads reviewed provider functions through the authoritative module boundary' {
                $commands = @(
                    'Get-WinCareWifiDiagnostic',
                    'Get-WinCareSecurityBaselineState',
                    'Get-WinCareWirelessBaselineAudit',
                    'New-WinCareCleanupPlan',
                    'Invoke-WinCareSmartFileOrganizer',
                    'Get-WinCarePolicyFileEntries',
                    'Get-WinCareProcessHealthOverview',
                    'Get-WinCareAvailableDriverUpdates',
                    'Get-WinCareMaintenancePlaybooks',
                    'Test-WinCareDpiBypassStrategy',
                    'Get-WinCareActiveProcessConnections',
                    'Get-WinCareSysmonThreatMap',
                    'Resolve-WinCareDohQuery',
                    'Invoke-WinCareFuzzyDeduplication',
                    'Invoke-WinCareOnnxDiagnosticScan',
                    'Enable-WinCareEbpfFilter',
                    'Audit-WinCareUefiDbxSignatures',
                    'Invoke-WinCarePqcKeyExchange',
                    'Synthesize-WinCareCodePatch',
                    'Start-WinCareRaftMeshNode',
                    'Stop-WinCareFleetCoordinator',
                    'Predict-WinCareKernelBsodFault',
                    'Invoke-WinCareWasiPlugin',
                    'Send-WinCareMultiCloudTelemetry'
                )
                foreach ($name in $commands) {
                    Get-Command $name -ErrorAction Stop | Should -Not -BeNullOrEmpty
                }
            }
        }
    }
}
