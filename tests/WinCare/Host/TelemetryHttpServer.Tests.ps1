. "$PSScriptRoot\..\..\..\src\WinCare\Host\TelemetryHttpServer.ps1"

Describe 'WinCare Telemetry HTTP Server' {
    It 'Should export Telemetry HTTP listener functions' {
        $functions = @('Start-WinCareTelemetryHttpServer', 'Stop-WinCareTelemetryHttpServer')
        foreach ($f in $functions) {
            (Get-Command $f -ErrorAction SilentlyContinue) | Should Not Be $null
        }
    }

    It 'Should initialize and stop HTTP listener cleanly' {
        $startResult = Start-WinCareTelemetryHttpServer -Port 8899
        $startResult.Listening | Should Be $true
        $stopResult = Stop-WinCareTelemetryHttpServer
        $stopResult.Stopped | Should Be $true
    }
}
