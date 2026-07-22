. "$PSScriptRoot\..\..\..\src\WinCare\Providers\114-StorageIntelligence.ps1"

Describe 'WinCare Storage Intelligence Module' {
    It 'Should export fuzzy deduplication and MFT parser cmdlets' {
        (Get-Command 'Invoke-WinCareFuzzyDeduplication' -ErrorAction SilentlyContinue) | Should Not Be $null
        (Get-Command 'Get-WinCareMftDiskReport' -ErrorAction SilentlyContinue) | Should Not Be $null
    }

    It 'Should execute fuzzy deduplication and return MFT disk report' {
        $dedup = Invoke-WinCareFuzzyDeduplication
        $dedup.DuplicateSets | Should Be 4
        $mft = Get-WinCareMftDiskReport -DriveLetter 'C:'
        $mft.Drive | Should Be 'C:'
    }
}
