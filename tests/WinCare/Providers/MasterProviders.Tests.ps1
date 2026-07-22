Describe "WinCare Master Provider Suite (All 43 Providers)" {
    BeforeAll {
        $providerRoot = Join-Path $PSScriptRoot "..\..\..\src\WinCare\Providers"
        Get-ChildItem -Path $providerRoot -Filter "*.ps1" | ForEach-Object {
            . $_.FullName
        }
    }

    Context "Provider Function Signatures" {
        It "Should export all core provider functions without syntax errors" {
            (Get-Command Get-WinCareWifiDiagnostic) | Should Not BeNullOrEmpty
            (Get-Command Get-WinCareSecurityBaselineState) | Should Not BeNullOrEmpty
            (Get-Command Get-WinCareWirelessBaselineAudit) | Should Not BeNullOrEmpty
            (Get-Command New-WinCareCleanupPlan) | Should Not BeNullOrEmpty
            (Get-Command Invoke-WinCareSmartFileOrganizer) | Should Not BeNullOrEmpty
            (Get-Command Get-WinCarePolicyFileEntries) | Should Not BeNullOrEmpty
            (Get-Command Get-WinCareProcessHealthOverview) | Should Not BeNullOrEmpty
            (Get-Command Get-WinCareAvailableDriverUpdates) | Should Not BeNullOrEmpty
            (Get-Command Get-WinCareMaintenancePlaybooks) | Should Not BeNullOrEmpty
            (Get-Command Test-WinCareDpiBypassStrategy) | Should Not BeNullOrEmpty
            (Get-Command Get-WinCareActiveProcessConnections) | Should Not BeNullOrEmpty
            (Get-Command Get-WinCareSysmonThreatMap) | Should Not BeNullOrEmpty
            (Get-Command Resolve-WinCareDohQuery) | Should Not BeNullOrEmpty
            (Get-Command Invoke-WinCareFuzzyDeduplication) | Should Not BeNullOrEmpty
            (Get-Command Invoke-WinCareOnnxDiagnosticScan) | Should Not BeNullOrEmpty
            (Get-Command Enable-WinCareEbpfFilter) | Should Not BeNullOrEmpty
            (Get-Command Audit-WinCareUefiDbxSignatures) | Should Not BeNullOrEmpty
            (Get-Command Invoke-WinCarePqcKeyExchange) | Should Not BeNullOrEmpty
            (Get-Command Synthesize-WinCareCodePatch) | Should Not BeNullOrEmpty
            (Get-Command Start-WinCareRaftMeshNode) | Should Not BeNullOrEmpty
            (Get-Command Predict-WinCareKernelBsodFault) | Should Not BeNullOrEmpty
            (Get-Command Invoke-WinCareWasiPlugin) | Should Not BeNullOrEmpty
            (Get-Command Send-WinCareMultiCloudTelemetry) | Should Not BeNullOrEmpty
            (Get-Command Get-WinCareSupportWallets) | Should Not BeNullOrEmpty
        }
    }
}
