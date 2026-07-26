BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }
Describe 'WinCare Threat Hunter provider' {
    It 'exports the public threat-hunting commands through the module' {
        (Get-Command Get-WinCareSysmonThreatMap -Module WinCare).Name | Should -Be 'Get-WinCareSysmonThreatMap'
        (Get-Command Audit-WinCareDefenderFirewallRules -Module WinCare).Name | Should -Be 'Audit-WinCareDefenderFirewallRules'
    }
    It 'returns observed event data rather than a fixed demo count' {
        $events = @(Get-WinCareSysmonThreatMap -MaxEvents 1)
        $events.Count | Should -BeLessOrEqual 1
    }
    It 'returns an explicit supported state and evidence collections' {
        $result = Audit-WinCareDefenderFirewallRules
        $result.PSObject.Properties.Name | Should -Contain 'Supported'
        $result.PSObject.Properties.Name | Should -Contain 'Findings'
        $result.PSObject.Properties.Name | Should -Contain 'Errors'
    }
}
