BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1') -Force }
Describe 'WinCare storage intelligence provider' {
    It 'exports deduplication and NTFS reporting commands' {
        Get-Command Invoke-WinCareFuzzyDeduplication -Module WinCare | Should -Not -BeNullOrEmpty
        Get-Command Get-WinCareMftDiskReport -Module WinCare | Should -Not -BeNullOrEmpty
    }
    It 'detects exact duplicate content from a real test directory without mutating it' {
        $payload = 'x' * 8192
        Set-Content -LiteralPath (Join-Path $TestDrive 'a.bin') -Value $payload -NoNewline
        Set-Content -LiteralPath (Join-Path $TestDrive 'b.bin') -Value $payload -NoNewline
        $result = Invoke-WinCareFuzzyDeduplication -Path $TestDrive -MinimumBytes 1
        $result.ExactDuplicateSets | Should -Be 1
        $result.MutationPerformed | Should -BeFalse
        $result.PotentialExactSavingsBytes | Should -BeGreaterThan 0
    }
}
