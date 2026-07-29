# Pester 5 executes Describe bodies -- and therefore InModuleScope -- during
# Discovery, which runs before any BeforeAll, so the module is imported at file
# scope. These providers are internal (not in FunctionsToExport), which is why
# the assertions run inside InModuleScope rather than dot-sourcing the provider
# files: dot-sourcing a provider in isolation leaves its Core dependencies
# (New-WinCareResult, Invoke-WinCareProcess, ...) undefined, so every call threw
# CommandNotFoundException.
Import-Module (Join-Path $PSScriptRoot '../../../src/WinCare/WinCare.psd1') -Force -Global

Describe 'WinCare Phase 1 Provider Suite' {
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

        Context '70-Network provider' {
            It 'reports Wi-Fi diagnostics as bounded evidence rather than throwing' {
                # Asserted on shape, not on a specific adapter name: hosted CI
                # runners have no WLAN AutoConfig service, so Supported is
                # legitimately $false there. The observed contract is
                # Supported/Interfaces/EvidenceType/Error.
                $diagnostic = Get-WinCareWifiDiagnostic
                $diagnostic | Should -Not -BeNullOrEmpty
                $diagnostic.PSObject.Properties.Name | Should -Contain 'Supported'
                $diagnostic.PSObject.Properties.Name | Should -Contain 'Interfaces'
                $diagnostic.EvidenceType | Should -Not -BeNullOrEmpty
            }

            It 'requires an explicit captive-portal target and expected content' {
                # The probe is deliberately fail-closed: it will not reach out to
                # any default endpoint. Asserted on the parameter contract rather
                # than by invoking it, because invoking it would make a real
                # network request from the test suite.
                $command = Get-Command Test-WinCareCaptivePortal
                foreach ($required in @('TargetUri', 'ExpectedContent')) {
                    $parameter = $command.Parameters[$required]
                    $parameter | Should -Not -BeNullOrEmpty
                    @($parameter.Attributes |
                        Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] -and $_.Mandatory }
                    ).Count | Should -BeGreaterThan 0
                }
                # Only http(s) targets are admissible.
                @($command.Parameters['TargetUri'].Attributes |
                    Where-Object { $_ -is [System.Management.Automation.ValidatePatternAttribute] }
                ).Count | Should -BeGreaterThan 0
            }
        }

        Context '74-SecurityBaseline provider' {
            It 'reports baseline control state without claiming unavailable evidence' {
                $baseline = Get-WinCareSecurityBaselineState
                $baseline | Should -Not -BeNullOrEmpty
                $baseline.PSObject.Properties.Name | Should -Contain 'Supported'
                $baseline.PSObject.Properties.Name | Should -Contain 'OverallState'
                $baseline.PSObject.Properties.Name | Should -Contain 'Controls'
                if (-not $baseline.Supported) {
                    $baseline.OverallState | Should -Not -Be 'Hardened'
                }
            }
        }

        Context '75-Security provider' {
            It 'audits the wireless baseline and returns evidence' {
                $audit = Get-WinCareWirelessBaselineAudit
                $audit | Should -Not -BeNullOrEmpty
            }
        }

        Context '20-Cleanup provider' {
            It 'builds a cleanup plan whose actions are all typed' {
                $plan = New-WinCareCleanupPlan
                $plan | Should -Not -BeNullOrEmpty
                $plan.PSObject.Properties.Name | Should -Contain 'Actions'
                foreach ($action in @($plan.Actions)) {
                    $action.Type | Should -Not -BeNullOrEmpty
                }
            }
        }
    }
}
