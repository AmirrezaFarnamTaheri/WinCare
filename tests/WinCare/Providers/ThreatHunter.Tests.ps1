. "$PSScriptRoot\..\..\..\src\WinCare\Providers\112-ThreatHunter.ps1"

Describe 'WinCare Threat Hunter Module' {
    It 'Should export threat hunting cmdlets' {
        (Get-Command 'Get-WinCareSysmonThreatMap' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Audit-WinCareDefenderFirewallRules' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should return threat map and defender firewall audit' {
        $threats = Get-WinCareSysmonThreatMap
        $threats.Count | Should Be 2
        $fw = Audit-WinCareDefenderFirewallRules
        $fw.Status | Should Be 'Audited'
    }
}
