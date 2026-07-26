. "$PSScriptRoot\..\..\..\src\WinCare\Core\ScheduledTaskEngine.ps1"

Describe 'WinCare Scheduled Task Engine' {
    It 'Should export task registration cmdlets' {
        (Get-Command 'Register-WinCareMaintenanceTask' -ErrorAction SilentlyContinue) | Should -Not -Be $null
        (Get-Command 'Unregister-WinCareMaintenanceTask' -ErrorAction SilentlyContinue) | Should -Not -Be $null
    }

    It 'Should register and unregister maintenance tasks cleanly' {
        $reg = Register-WinCareMaintenanceTask -TaskName 'WinCareTestTask' -PlaybookId 'DailyStoragePurge'
        $reg.Status | Should -Be 'Registered'
        $unreg = Unregister-WinCareMaintenanceTask -TaskName 'WinCareTestTask'
        $unreg.Status | Should -Be 'Unregistered'
    }
}
