. "$PSScriptRoot\..\..\..\src\WinCare\Providers\123-AutonomousPatchAgent.ps1"

Describe 'WinCare Autonomous Patch Agent Module' {
    It 'Should export patch synthesis and sandbox test cmdlets' {
        (Get-Command 'Synthesize-WinCareCodePatch' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Test-WinCarePatchInSandbox' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should synthesize patch and verify in sandbox' {
        $patch = Synthesize-WinCareCodePatch -CveId 'CVE-2026-0001'
        $patch.Status | Should Be 'Synthesized'
        $test = Test-WinCarePatchInSandbox -PatchScript 'Fix.ps1'
        $test.Status | Should Be 'Verified'
    }
}
