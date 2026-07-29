# File-scope import for Pester 5 Discovery. Dot-sourcing ScheduledTaskEngine.ps1
# alone left Get-WinCarePlaybook, New-WinCareAutomationTaskPlan, New-WinCareResult
# and Invoke-WinCarePlan undefined, so every call threw CommandNotFoundException.
Import-Module (Join-Path $PSScriptRoot '../../../src/WinCare/WinCare.psd1') -Force -Global

Describe 'WinCare Scheduled Task Engine' {
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

        It 'defines the task registration commands' {
            Get-Command 'Register-WinCareMaintenanceTask' -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
            Get-Command 'Unregister-WinCareMaintenanceTask' -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
        }

        It 'returns a reviewable plan instead of mutating without -Apply' {
            # Register-WinCareMaintenanceTask returns the plan when neither -Apply
            # nor -PreviewOnly is supplied, so it never yields Status='Registered'.
            # Asserting that former value tested behaviour the engine deliberately
            # does not have: nothing may be scheduled without explicit intent.
            $playbook = @(Get-WinCarePlaybook) | Select-Object -First 1
            if (-not $playbook) {
                Set-ItResult -Skipped -Because 'no playbook catalog entries are available on this host'
                return
            }
            $plan = Register-WinCareMaintenanceTask -TaskName 'WinCareTestTask' -PlaybookId ([string]$playbook.Id)
            $plan | Should -Not -BeNullOrEmpty
            $plan.PSObject.Properties.Name | Should -Contain 'Actions'
            @($plan.Actions).Count | Should -BeGreaterThan 0
        }

        It 'reports an absent task as already removed rather than failing' {
            $result = Unregister-WinCareMaintenanceTask -TaskName 'WinCareAbsentTask'
            $result | Should -Not -BeNullOrEmpty
            # No profile exists, so the engine short-circuits with an idempotent
            # success backed by real absence evidence.
            $result.Code | Should -Be 'MaintenanceTaskAlreadyAbsent'
            $result.Success | Should -BeTrue
        }
    }
}
