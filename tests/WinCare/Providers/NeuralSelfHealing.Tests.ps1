BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }
Describe 'WinCare transparent reliability heuristics' {
    It 'exports kernel risk and working-set commands' {
        Get-Command Predict-WinCareKernelBsodFault -Module WinCare | Should -Not -BeNullOrEmpty
        Get-Command Invoke-WinCarePageTableOptimization -Module WinCare | Should -Not -BeNullOrEmpty
    }
    It 'does not claim neural inference or page-table optimization in source or results' {
        $source = Get-Content (Join-Path $PSScriptRoot '..\..\..\src\WinCare\Providers\127-NeuralSelfHealing.ps1') -Raw
        $source | Should -Match 'DeterministicEventHeuristic'
        $source | Should -Match 'not a neural prediction'
        $source | Should -Match 'does not optimize page tables'
    }
}
