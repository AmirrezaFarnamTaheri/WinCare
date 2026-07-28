Import-Module (Join-Path $PSScriptRoot '../src/WinCare/WinCare.psd1') -Force -Global

Describe 'Cleanup and AppX contracts' {
    InModuleScope WinCare {
        BeforeAll {
            $root=Split-Path -Parent $PSScriptRoot
            Initialize-WinCareState -SkipConfigSave
        }

        It 'keeps destructive one-click profiles default-deny' {
            $policy=Get-WinCareDefaultPolicy
            $policy.AllowLegacyUnsafeProfiles | Should -Be $false
            $policy.AllowCriticalActions | Should -Be $false
            $policy.DeniedActionTypes | Should -Contain 'DeleteEventLogs'
            $policy.DeniedActionTypes | Should -Contain 'PurgePrefetch'
        }

        It 'publishes the cleanup and AppX headless routes' {
            $commands=@(Get-WinCareHeadlessCommandName)
            foreach($name in @('explorer-quick','deep-clean-profiles','deep-clean','appx-selection-profiles','appx-selection-assess','appx-selection','offline-appx-selection')){$commands | Should -Contain $name}
        }

        It 'registers the new typed mutation contracts' {
            foreach($type in @('RemoveAppxPackageAllUsers','ClearWindowsUpdateDownloadCache','StartComponentCleanup')){Get-WinCareActionContract -Type $type | Should -Not -BeNullOrEmpty}
            (Get-WinCareActionContract -Type 'RemoveAppxPackageAllUsers').AlwaysAdmin | Should -Be $true
            (Get-WinCareActionContract -Type 'RemoveAppxPackageAllUsers').MinimumRisk | Should -Be 'Critical'
            (Get-WinCareActionContract -Type 'ClearWindowsUpdateDownloadCache').MinimumRisk | Should -Be 'Critical'
        }

        It 'ships all requested unsafe profiles with the exact acknowledgement' {
            $profiles=@(Get-WinCareLegacyUnsafeProfile)
            foreach($id in @('windows-cleaner-utility-all','remove-ms-store-apps-all','windows-repo-nuclear')){@($profiles.Id) | Should -Contain $id}
            $headless=Get-Content (Join-Path $root 'src/WinCare/UI/98-Headless.ps1') -Raw
            $headless | Should -Match 'I ACCEPT LEGACY UNSAFE MUTATIONS'
        }

        It 'uses bounded wildcard lists rather than arbitrary regex execution' {
            $source=Get-Content (Join-Path $root 'src/WinCare/Providers/108-CleanupMods.ps1') -Raw
            $source | Should -Match 'AppX selection list exceeds 64 KiB'
            $source | Should -Match '1\..200 unique patterns'
            $source | Should -Match 'WildcardPattern'
            $source | Should -Match "Get-WinCarePolicy 'MaximumPlanActions'"
            $source | Should -Not -Match 'Invoke-Expression'
            $source | Should -Not -Match 'Remove-AppxPackage\s+\$\w+\s+-Confirm:\$false\s+-ErrorAction SilentlyContinue'
        }

        It 'revalidates protected all-user package identity at execution' {
            $source=Get-Content (Join-Path $root 'src/WinCare/Providers/108-CleanupMods.ps1') -Raw
            $source | Should -Match 'AppxAllUsersIdentityChanged'
            $source | Should -Match 'AppxAllUsersProtectedPackage'
        }

        It 'reserves the bare match-all wildcard for trusted built-in profiles' {
            Test-WinCareAppxSelectionPattern '*' | Should -Be $false
            Test-WinCareAppxSelectionPattern '*' -AllowMatchAll | Should -Be $true
            $profile=Get-WinCareAppxSelectionProfile -Id 'remove-every-removable-appx' | Select-Object -First 1
            $profile.Patterns | Should -Contain '*'
            [bool]$profile.AllowMatchAll | Should -Be $true
        }

        It 'preserves drive-root, event-log, Prefetch, and Defender evidence guardrails' {
            $source=Get-Content (Join-Path $root 'src/WinCare/Providers/108-CleanupMods.ps1') -Raw
            $source | Should -Match 'Root-drive sweeps, event-log deletion, Prefetch purge, Defender evidence deletion, and network reset remain excluded'
            $source | Should -Not -Match 'wevtutil\s+cl'
            $source | Should -Not -Match 'SYSTEMDRIVE.*\\\*\.log'
            $source | Should -Not -Match 'Windows Defender\\Scans\\History'
        }

        It 'coordinates Windows Update cache services and does not use ResetBase' {
            $source=Get-Content (Join-Path $root 'src/WinCare/Providers/108-CleanupMods.ps1') -Raw
            $source | Should -Match "@\('bits','wuauserv'\)"
            $source | Should -Match 'StartComponentCleanup'
            $source | Should -Not -Match '/ResetBase'
            $source | Should -Not -Match '-ResetBase'
        }
    }
}
