. "$PSScriptRoot\..\..\..\src\WinCare\Providers\113-NetworkProxyControl.ps1"

Describe 'WinCare Network Proxy Control Module' {
    It 'Should export DoH and CDN optimizer cmdlets' {
        (Get-Command 'Resolve-WinCareDohQuery' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Optimize-WinCareCdnRouting' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should resolve DoH query and optimize CDN routing' {
        $doh = Resolve-WinCareDohQuery -Domain 'example.com'
        $doh.Secure | Should Be $true
        $cdn = Optimize-WinCareCdnRouting
        $cdn.Status | Should Be 'Optimized'
    }
}
