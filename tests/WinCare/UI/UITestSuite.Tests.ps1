Describe 'WinCare User Interface Test Suite' {
    BeforeAll {
        $manifestPath = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\src\WinCare\WinCare.psd1')
        Import-Module $manifestPath -Force -ErrorAction Stop
    }

    InModuleScope WinCare {
        Context 'Console and theme functions' {
            It 'returns valid colors for theme roles' {
                (Get-WinCareColor -Role 'Primary') | Should -Not -BeNullOrEmpty
                (Get-WinCareColor -Role 'Accent') | Should -Not -BeNullOrEmpty
                (Get-WinCareColor -Role 'Success') | Should -Not -BeNullOrEmpty
            }

            It 'retrieves console dimensions without throwing' {
                $size = Get-WinCareConsoleSize
                $size.Width | Should -BeGreaterThan 0
                $size.Height | Should -BeGreaterThan 0
            }
        }

        Context 'Screen module loading' {
            It 'loads the dashboard screen through the authoritative module manifest' {
                (Get-Command Show-WinCareDashboardScreen -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
            }
        }
    }
}
