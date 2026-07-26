BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }

Describe 'WinCare authenticated local metrics HTTP server' {
    It 'exports the complete server lifecycle' {
        foreach($name in @('Start-WinCareMetricsHttpServer','Get-WinCareMetricsHttpServerState','Stop-WinCareMetricsHttpServer')){
            Get-Command $name -Module WinCare | Should -Not -BeNullOrEmpty
        }
    }

    It 'requires a strong bearer token' {
        { Start-WinCareMetricsHttpServer -Port 18899 -BearerToken 'short' } | Should -Throw
    }

    It 'uses a fixed start timestamp and authenticated readiness probe' {
        $source=(Get-Command Start-WinCareMetricsHttpServer).ScriptBlock.ToString()
        $source | Should -Match 'FixedTimeEquals'
        $source | Should -Match 'AuthenticatedLoopbackHealthProbe'
        $source | Should -Match 'StartedUnix'
        $source | Should -Not -Match 'started_timestamp_seconds \$\(\[DateTimeOffset\]::UtcNow'
    }
}
