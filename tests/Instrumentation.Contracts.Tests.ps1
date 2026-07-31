$modulePath = Join-Path $PSScriptRoot '..\src\WinCare\WinCare.psd1'
Import-Module $modulePath -Force

Describe 'Injection-surface snapshot validation' {
    InModuleScope WinCare {
        It 'accepts a matching recovery-value list by dictionary key' {
            $path = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows'
            $snapshot = [ordered]@{
                Surface = [ordered]@{
                    Path = $path
                    Name = 'AppInit_DLLs'
                    Exists = $true
                }
                Values = @(
                    [ordered]@{
                        Path = $path
                        Name = 'AppInit_DLLs'
                    }
                )
            }

            Test-WinCareInjectionSurfaceSnapshot $snapshot | Should -BeTrue
        }

        It 'returns false instead of throwing for an unapproved root' {
            $snapshot = [ordered]@{
                Path = 'HKCU:\Software\WinCare\Unapproved'
                Name = 'Debugger'
                Exists = $true
            }

            Test-WinCareInjectionSurfaceSnapshot $snapshot | Should -BeFalse
        }

        It 'returns false when recovery values do not match the declared target' {
            $path = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows'
            $snapshot = [ordered]@{
                Path = $path
                Name = 'AppInit_DLLs'
                Exists = $true
                Values = @(
                    [ordered]@{
                        Path = $path
                        Name = 'DifferentValue'
                    }
                )
            }

            Test-WinCareInjectionSurfaceSnapshot $snapshot | Should -BeFalse
        }
    }
}
