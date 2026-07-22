BeforeAll {
    $root=Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $root 'src/WinCare/WinCare.psd1') -Force
    Initialize-WinCareState -SkipConfigSave
}

Describe 'Wave six Workspace and Device Studio convergence' {
    It 'keeps external and Xbox mutations default-deny' {
        $policy=Get-WinCareDefaultPolicy
        $policy.AllowVerifiedPeerTools | Should -BeFalse
        $policy.AllowAdbMutation | Should -BeFalse
        $policy.AllowXboxFullscreenExperience | Should -BeFalse
        $policy.AllowLegacyUnsafeProfiles | Should -BeFalse
        $policy.AllowCriticalActions | Should -BeFalse
    }

    It 'publishes every Workspace Studio headless route' {
        $commands=@(Get-WinCareHeadlessCommandName)
        foreach($name in @('studio-snapshot','studio-monitoring-export','studio-file-workspaces','studio-file-workspace-save','studio-layout-profiles','studio-layout-save','studio-package','studio-brightness-schedules','studio-brightness-save','studio-brightness-apply','studio-folder-appearance','studio-kanata-validate','studio-syncthing','studio-wezterm','studio-adb-inventory','studio-xbox-fse')){$commands | Should -Contain $name}
    }

    It 'admits archives without extracting or executing them' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/107-WorkspaceStudio.ps1') -Raw
        $source | Should -Match 'PackageArchiveEntryLimit'
        $source | Should -Match 'duplicate or case-colliding'
        $source | Should -Match 'unsafe member path'
        $source | Should -Match 'Extracted=\$false'
        $source | Should -Match 'Executed=\$false'
        $source | Should -Not -Match 'ExtractToDirectory'
    }

    It 'uses exact-hash no-shell external tool admission' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/107-WorkspaceStudio.ps1') -Raw
        $source | Should -Match 'RunVerifiedPeerTool'
        $source | Should -Match 'VerifiedToolHashMismatch'
        $source | Should -Match 'Test-WinCareVerifiedPeerToolArguments'
        $source | Should -Match 'VerifiedToolReparseDenied'
        $source | Should -Match 'ExpectedInputSha256'
        $source | Should -Match 'ADB shell command is outside the read-only allowlist'
        $source | Should -Not -Match 'Invoke-Expression'
        $source | Should -Not -Match 'cmd\.exe /c'
    }

    It 'includes the requested unsafe Xbox one-click mutation with exact evidence values' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/107-WorkspaceStudio.ps1') -Raw
        $source | Should -Match '52580392,50902630,59765208'
        $source | Should -Match "DeviceForm"
        $source | Should -Match "Value=if\(\$Enabled\)\{46\}"
        $source | Should -Match 'I ACCEPT LEGACY UNSAFE MUTATIONS'
        $source | Should -Match "LegacyUnsafeProfile='xbox-fullscreen-experience'"
    }

    It 'retains exact conflict-aware folder appearance recovery' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/107-WorkspaceStudio.ps1') -Raw
        $source | Should -Match 'FolderAppearanceChanged'
        $source | Should -Match 'FolderAppearanceRecoveryConflict'
        $source | Should -Match 'ExpectedAfterHash'
        $source | Should -Match 'DesktopIniContentBase64'
        $source | Should -Match 'FolderIconResourceChanged'
        $source | Should -Match 'IconResourceSha256'
    }

    It 'accounts for every new donor in cumulative evidence' {
        $inventory=Get-Content (Join-Path $root 'docs/convergence/donor-inventory.json') -Raw | ConvertFrom-Json
        foreach($number in 87..105){@($inventory.donor_id) | Should -Contain ('D{0}' -f $number)}
    }
}
