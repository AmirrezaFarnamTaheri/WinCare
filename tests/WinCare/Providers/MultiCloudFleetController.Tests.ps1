BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }
Describe 'WinCare configured cloud telemetry adapter' {
    It 'exports telemetry and endpoint-health commands' {
        Get-Command Send-WinCareMultiCloudTelemetry -Module WinCare | Should -Not -BeNullOrEmpty
        Get-Command Get-WinCareFleetMeshStatus -Module WinCare | Should -Not -BeNullOrEmpty
    }
    It 'blocks transmission to an unconfigured cloud target' {
        $config = Join-Path $TestDrive 'targets.json'
        '{"SchemaVersion":1,"Targets":[]}' | Set-Content -LiteralPath $config
        $result = Send-WinCareMultiCloudTelemetry -CloudTarget 'not-configured' -ConfigurationPath $config -Telemetry @{value=1} -Confirm:$false
        $result.Success | Should -BeFalse
        $result.Status | Should -Be 'Blocked'
        $result.Code | Should -Be 'CloudTargetNotFound'
    }
    It 'reports an unconfigured mesh instead of inventing synchronized nodes' {
        $config = Join-Path $TestDrive 'empty-targets.json'
        '{"SchemaVersion":1,"Targets":[]}' | Set-Content -LiteralPath $config
        $result = Get-WinCareFleetMeshStatus -ConfigurationPath $config
        $result.Success | Should -BeTrue
        $result.Data.Health | Should -Be 'Unconfigured'
        $result.Data.ConfiguredEndpoints | Should -Be 0
    }
}
