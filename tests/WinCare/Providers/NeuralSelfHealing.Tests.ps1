. "$PSScriptRoot\..\..\..\src\WinCare\Providers\127-NeuralSelfHealing.ps1"

Describe 'WinCare Neural Self-Healing Module' {
    It 'Should export BSOD prediction and page table optimization cmdlets' {
        (Get-Command 'Predict-WinCareKernelBsodFault' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Invoke-WinCarePageTableOptimization' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should predict BSOD fault and optimize page tables' {
        $bsod = Predict-WinCareKernelBsodFault
        $bsod.Status | Should Be 'Stable'
        $opt = Invoke-WinCarePageTableOptimization
        $opt.Status | Should Be 'Optimized'
    }
}
