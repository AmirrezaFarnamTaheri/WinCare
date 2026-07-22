. "$PSScriptRoot\..\..\..\src\WinCare\Providers\116-KernelFilterEngine.ps1"

Describe 'WinCare eBPF Kernel Filter Module' {
    It 'Should export eBPF and microsegmentation cmdlets' {
        (Get-Command 'Enable-WinCareEbpfFilter' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Set-WinCareMicrosegmentationRule' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should enable eBPF filter and set microsegmentation rule' {
        $ebpf = Enable-WinCareEbpfFilter -Mode 'XDP_DROP'
        $ebpf.Status | Should Be 'Active'
        $micro = Set-WinCareMicrosegmentationRule -ProcessName 'test.exe'
        $micro.Status | Should Be 'Applied'
    }
}
