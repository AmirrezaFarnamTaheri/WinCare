BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }

Describe 'WinCare reviewed remediation adapter' {
    It 'exports remediation materialization and sandbox validation commands' {
        Get-Command Synthesize-WinCareCodePatch -Module WinCare | Should -Not -BeNullOrEmpty
        Get-Command Test-WinCarePatchInSandbox -Module WinCare | Should -Not -BeNullOrEmpty
    }

    It 'refuses to invent a remediation absent a reviewed catalog entry' {
        $catalog=Join-Path $TestDrive 'catalog.json'
        '{"SchemaVersion":1,"Entries":[]}'|Set-Content -LiteralPath $catalog
        $result=Synthesize-WinCareCodePatch -CveId 'CVE-2026-0001' -CatalogPath $catalog
        $result.Success|Should -BeFalse
        $result.Status|Should -Be 'Blocked'
        $result.Code|Should -Be 'NoReviewedRemediation'
    }

    It 'requires a hash-pinned external sandbox boundary' {
        $script=Join-Path $TestDrive 'safe.ps1'
        "'ok'"|Set-Content -LiteralPath $script
        $result=Test-WinCarePatchInSandbox -PatchScript $script
        $result.Success|Should -BeFalse
        $result.Status|Should -Be 'Blocked'
        $result.Code|Should -Be 'SandboxRunnerMissing'
    }

    It 'never treats child-process isolation as a sandbox' {
        $script=Join-Path $TestDrive 'safe.ps1'
        "'ok'"|Set-Content -LiteralPath $script
        $result=Test-WinCarePatchInSandbox -PatchScript $script -AllowProcessIsolation
        $result.Success|Should -BeFalse
        $result.Code|Should -Be 'ProcessIsolationDenied'
    }
}
