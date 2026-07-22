. "$PSScriptRoot\..\..\..\src\WinCare\Providers\115-SelfHealingCopilot.ps1"

Describe 'WinCare Self-Healing Copilot Module' {
    It 'Should export AI diagnostic and SMART prediction cmdlets' {
        (Get-Command 'Invoke-WinCareOnnxDiagnosticScan' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Get-WinCareSmartFailurePrediction' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should execute ONNX diagnostic scan and return SMART prediction' {
        $onnx = Invoke-WinCareOnnxDiagnosticScan
        $onnx.Status | Should Be 'Healthy'
        $smart = Get-WinCareSmartFailurePrediction -DriveId 'NVMe-0'
        $smart.HealthPercentage | Should Be 99
    }
}
