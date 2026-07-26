BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }
Describe 'WinCare kernel-filter dependency adapter' {
    It 'exports eBPF and microsegmentation commands' {
        Get-Command Enable-WinCareEbpfFilter -Module WinCare | Should -Not -BeNullOrEmpty
        Get-Command Set-WinCareMicrosegmentationRule -Module WinCare | Should -Not -BeNullOrEmpty
    }
    It 'does not report synthetic eBPF success when the runtime is absent' {
        $result = Enable-WinCareEbpfFilter -Mode List -ToolPath (Join-Path $TestDrive 'missing-netsh.exe')
        $result.Success | Should -BeFalse
        $result.Status | Should -Be 'Blocked'
        $result.Code | Should -BeIn @('WindowsRequired','EbpfRuntimeMissing')
    }
    It 'blocks microsegmentation for a process that cannot be resolved' {
        $result = Set-WinCareMicrosegmentationRule -ProcessName 'wincare-process-that-does-not-exist.exe' -Action Block -Confirm:$false
        $result.Success | Should -BeFalse
        $result.Status | Should -Be 'Blocked'
    }
}
