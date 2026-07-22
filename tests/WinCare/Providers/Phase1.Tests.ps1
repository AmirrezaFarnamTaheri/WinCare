Describe "WinCare Phase 1 Provider Suite" {
    BeforeAll {
        $providerRoot = "D:\WinCare-5.2.0\WinCare-5.2.0\src\WinCare\Providers"
        . (Join-Path $providerRoot "70-Network.ps1")
        . (Join-Path $providerRoot "74-SecurityBaseline.ps1")
        . (Join-Path $providerRoot "75-Security.ps1")
        . (Join-Path $providerRoot "20-Cleanup.ps1")
        . (Join-Path $providerRoot "30-Storage.ps1")
    }

    Context "70-Network Provider" {
        It "Should query Wi-Fi diagnostics without error" {
            $diag = Get-WinCareWifiDiagnostic
            $diag | Should Not BeNullOrEmpty
            $diag.InterfaceName | Should Be "Wi-Fi"
        }

        It "Should perform captive portal test and return status object" {
            $cp = Test-WinCareCaptivePortal
            $cp | Should Not BeNullOrEmpty
            $cp.Details | Should Not BeNullOrEmpty
        }
    }

    Context "74-SecurityBaseline Provider" {
        It "Should query security baseline state from WinCare.Native.dll" {
            $sec = Get-WinCareSecurityBaselineState
            $sec | Should Not BeNullOrEmpty
            $sec.OverallHardeningScore | Should Not BeNullOrEmpty
        }
    }

    Context "75-Security Provider" {
        It "Should audit wireless security baseline" {
            $audit = Get-WinCareWirelessBaselineAudit
            $audit | Should Not BeNullOrEmpty
        }
    }

    Context "20-Cleanup Provider" {
        It "Should generate a valid cleanup plan" {
            $plan = New-WinCareCleanupPlan
            $plan | Should Not BeNullOrEmpty
            $plan.Target | Should Be "System Temp & DISM WinSxS"
        }
    }
}
