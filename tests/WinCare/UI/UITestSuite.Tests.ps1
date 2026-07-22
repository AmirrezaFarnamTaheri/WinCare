Describe "WinCare User Interface Test Suite" {
    BeforeAll {
        $srcRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..\src\WinCare")
        $coreRoot = Join-Path $srcRoot "Core"
        if (Test-Path $coreRoot) {
            Get-ChildItem -Path $coreRoot -Filter "*.ps1" | ForEach-Object { . $_.FullName }
        }

        $providerRoot = Join-Path $srcRoot "Providers"
        Get-ChildItem -Path $providerRoot -Filter "*.ps1" | ForEach-Object {
            . $_.FullName
        }

        $uiRoot = Join-Path $srcRoot "UI"
        . "$uiRoot\00-Console.ps1"
        . "$uiRoot\01-Controls.ps1"
        . "$uiRoot\97-CommandPalette.ps1"
        . "$uiRoot\98-Headless.ps1"
        . "$uiRoot\99-Main.ps1"

        Get-ChildItem -Path "$uiRoot\Gui" -Filter "*.ps1" | ForEach-Object { . $_.FullName }
        Get-ChildItem -Path "$uiRoot\Screens" -Filter "*.ps1" | ForEach-Object { . $_.FullName }
    }

    Context "Console and Theme Functions" {
        It "Should return valid colors for theme roles" {
            (Get-WinCareColor -Role 'Primary') | Should Not BeNullOrEmpty
            (Get-WinCareColor -Role 'Accent') | Should Not BeNullOrEmpty
            (Get-WinCareColor -Role 'Success') | Should Not BeNullOrEmpty
        }

        It "Should retrieve console dimensions without throwing" {
            $size = Get-WinCareConsoleSize
            $size.Width | Should BeGreaterThan 0
            $size.Height | Should BeGreaterThan 0
        }
    }

    Context "Screen Modules Loading" {
        It "Should export core screen functions cleanly" {
            (Get-Command Show-WinCareDashboardScreen -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        }
    }
}
