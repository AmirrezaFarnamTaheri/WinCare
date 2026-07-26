BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }
Describe 'WinCare network measurement provider' {
    It 'exports DoH and CDN measurement commands' {
        Get-Command Resolve-WinCareDohQuery -Module WinCare | Should -Not -BeNullOrEmpty
        Get-Command Optimize-WinCareCdnRouting -Module WinCare | Should -Not -BeNullOrEmpty
    }
    It 'blocks malformed DNS names before any network request' {
        $result = Resolve-WinCareDohQuery -Domain 'invalid name with spaces'
        $result.Success | Should -BeFalse
        $result.Status | Should -Be 'Blocked'
        $result.Code | Should -Be 'InvalidDnsName'
    }
    It 'reports measurements and never claims optimization without evidence' {
        $result = Optimize-WinCareCdnRouting -Nodes @('127.0.0.1') -SampleCount 1 -TimeoutMilliseconds 100 -Port 9
        $result.EvaluatedNodes | Should -Be 1
        $result.Status | Should -BeIn @('Measured','Unreachable')
        $result.PSObject.Properties.Name | Should -Contain 'Measurements'
    }
}
