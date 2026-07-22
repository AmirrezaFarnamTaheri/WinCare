. "$PSScriptRoot\..\..\..\src\WinCare\Providers\132-MultiCloudFleetController.ps1"

Describe 'WinCare Multi-Cloud Fleet Controller Module' {
    It 'Should export multi-cloud telemetry and fleet status cmdlets' {
        (Get-Command 'Send-WinCareMultiCloudTelemetry' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Get-WinCareFleetMeshStatus' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should send multi-cloud telemetry and get fleet status' {
        $cloud = Send-WinCareMultiCloudTelemetry -CloudTarget 'Azure'
        $cloud.Status | Should Be 'Streamed'
        $fleet = Get-WinCareFleetMeshStatus
        $fleet.Status | Should Be 'Synchronized'
    }
}
