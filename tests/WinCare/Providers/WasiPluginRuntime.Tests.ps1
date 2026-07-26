BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }
Describe 'WinCare WASI runtime adapter' {
    It 'exports execution and catalog commands' {
        Get-Command Invoke-WinCareWasiPlugin -Module WinCare | Should -Not -BeNullOrEmpty
        Get-Command Get-WinCareWasiMarketplaceCatalog -Module WinCare | Should -Not -BeNullOrEmpty
    }
    It 'blocks a missing plugin before probing the runtime' {
        $result = Invoke-WinCareWasiPlugin -PluginPath (Join-Path $TestDrive 'missing.wasm')
        $result.Success | Should -BeFalse
        $result.Status | Should -Be 'Blocked'
        $result.Code | Should -Be 'WasiPluginNotFound'
    }
    It 'loads the configured catalog and reports observed entries' {
        $catalog = Get-WinCareWasiMarketplaceCatalog
        $catalog.Success | Should -BeTrue
        $catalog.Data.EvidenceType | Should -BeIn @('LocalCatalogProbe','VerifiedLocalPluginCatalog')
        ($null -eq $catalog.Data.Entries) | Should -BeFalse
    }
}
