BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }
Describe 'WinCare diagnostic provider' {
    It 'exports diagnostics and storage reliability commands' {
        Get-Command Invoke-WinCareOnnxDiagnosticScan -Module WinCare | Should -Not -BeNullOrEmpty
        Get-Command Get-WinCareSmartFailurePrediction -Module WinCare | Should -Not -BeNullOrEmpty
    }
    It 'labels deterministic fallback diagnostics truthfully when no model is supplied' {
        $result = Invoke-WinCareOnnxDiagnosticScan
        $result.Success | Should -BeTrue
        $result.Code | Should -Be 'HeuristicDiagnosticCompleted'
        $result.Data.Engine | Should -Be 'DeterministicHeuristics'
        $result.Data.ModelUsed | Should -BeFalse
        $result.Warnings -join ' ' | Should -Match 'not an ONNX|not a neural'
    }
}
