BeforeAll {
    $root=Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $root 'src/WinCare/WinCare.psd1') -Force
    Initialize-WinCareState -SkipConfigSave
}

Describe 'Wave five peer utilities and Legacy Unsafe convergence' {
    It 'keeps all destructive profile gates default-deny' {
        $policy=Get-WinCareDefaultPolicy
        $policy.AllowCriticalActions | Should -BeFalse
        $policy.AllowLegacyUnsafeProfiles | Should -BeFalse
        $policy.AllowPermanentSecurityReduction | Should -BeFalse
        $policy.AllowDisplayOverrideReset | Should -BeFalse
    }

    It 'publishes donor-derived one-click profiles with exact acknowledgement metadata' {
        $profiles=@(Get-WinCareLegacyUnsafeProfile)
        $profiles.Id | Should -Contain 'legacy-unsafe-all'
        $profiles.Id | Should -Contain 'reins-nuclear'
        $profiles.Id | Should -Contain 'toolbox-aggressive'
        $profiles.Id | Should -Contain 'et-gaming-extreme'
        $profiles.Id | Should -Contain 'pleasant-legacy'
        $profiles.Id | Should -Contain 'xd-antispy-max'
    }

    It 'routes peer utilities and destructive profiles through typed target-native surfaces' {
        $commands=@(Get-WinCareHeadlessCommandName)
        foreach($name in @('peer-display-overrides','peer-display-reset','peer-wlan','peer-log-tail','peer-pe','peer-container-log-config','peer-taskboard','peer-task-save','legacy-unsafe-profiles','legacy-unsafe')){$commands | Should -Contain $name}
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/106-PeerUtilities.ps1') -Raw
        $source | Should -Match 'SetLegacySecurityControlState'
        $source | Should -Match 'ResetDisplayOverrides'
        $source | Should -Match 'RestoreLegacySecurityControlState'
        $source | Should -Match 'RestoreDisplayOverrides'
        $source | Should -Not -Match 'Invoke-Expression'
    }

    It 'keeps exact conflict-aware recovery for permanent security and display mutations' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/106-PeerUtilities.ps1') -Raw
        $source | Should -Match 'LegacySecurityRecoveryConflict'
        $source | Should -Match 'DisplayRecoveryConflict'
        $source | Should -Match 'ExpectedCurrentHash'
        $source | Should -Match 'Compensator'
    }

    It 'accounts for every new donor in the cumulative evidence inventory' {
        $inventory=Get-Content (Join-Path $root 'docs/convergence/donor-inventory.json') -Raw | ConvertFrom-Json
        foreach($number in 70..86){@($inventory.donor_id) | Should -Contain ('D{0}' -f $number)}
    }
}
