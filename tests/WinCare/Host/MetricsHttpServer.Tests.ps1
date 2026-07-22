. "$PSScriptRoot\..\..\..\src\WinCare\Host\MetricsHttpServer.ps1"

Describe 'WinCare Local Metrics HTTP Server' {
    It 'Should export Metrics HTTP listener functions' {
        $functions = @('Start-WinCareMetricsHttpServer', 'Stop-WinCareMetricsHttpServer')
        foreach ($f in $functions) {
            (Get-Command $f -ErrorAction SilentlyContinue) | Should Not Be $null
        }
    }

    It 'Should initialize and stop HTTP listener cleanly' {
        $startResult = Start-WinCareMetricsHttpServer -Port 8899
        $startResult.Listening | Should Be $true
        $stopResult = Stop-WinCareMetricsHttpServer
        $stopResult.Stopped | Should Be $true
    }
}