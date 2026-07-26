Describe 'Advanced utilities and Legacy Unsafe contracts' {
    BeforeAll {
        $root=Split-Path -Parent $PSScriptRoot
        Import-Module (Join-Path $root 'src/WinCare/WinCare.psd1') -Force
    }

    InModuleScope WinCare {
        BeforeEach {
            $script:WinCareState=@{Config=Get-WinCareDefaultConfig;Policy=Get-WinCareDefaultPolicy;Root=$TestDrive;SessionId='0123456789abcdef0123456789abcdef';IsAdmin=$true;Capabilities=@{};ActionContracts=$null}
            foreach($name in @('Recovery','Reports','Logs','Journal','Backups')){New-Item -ItemType Directory -Path (Join-Path $TestDrive $name) -Force|Out-Null}
        }

        It 'keeps all destructive profile gates default-deny' {
            $policy=Get-WinCareDefaultPolicy
            $policy.AllowCriticalActions | Should Be $false
            $policy.AllowLegacyUnsafeProfiles | Should Be $false
            $policy.AllowPermanentSecurityReduction | Should Be $false
            $policy.AllowDisplayOverrideReset | Should Be $false
        }

        It 'publishes governed one-click profiles with exact acknowledgement metadata' {
            $profiles=@(Get-WinCareLegacyUnsafeProfile)
            foreach($id in @('legacy-unsafe-all','reins-nuclear','toolbox-aggressive','et-gaming-extreme','pleasant-legacy','xd-antispy-max')){@($profiles.Id)|Should Contain $id}
        }

        It 'routes advanced utilities and destructive profiles through typed product surfaces' {
            $commands=@(Get-WinCareHeadlessCommandName)
            foreach($name in @('peer-display-overrides','peer-display-reset','peer-wlan','peer-log-tail','peer-pe','peer-container-log-config','peer-taskboard','peer-task-save','legacy-unsafe-profiles','legacy-unsafe')){$commands|Should Contain $name}
            $contracts=Get-WinCareActionContractTable
            foreach($type in @('SetLegacySecurityControlState','ResetDisplayOverrides','RestoreLegacySecurityControlState','RestoreDisplayOverrides')){$contracts.Keys|Should Contain $type}
            (Get-Command Invoke-WinCareSetLegacySecurityControlStateAction).ScriptBlock.ToString()|Should Not -Match 'Invoke-Expression'
        }

        It 'keeps exact compare-and-swap recovery for permanent security mutations' {
            $source=(Get-Command Invoke-WinCareRestoreLegacySecurityControlStateAction).ScriptBlock.ToString()
            $source|Should Match 'LegacySecurityRecoveryConflict'
            $source|Should Match 'ExpectedCurrentHash'
            (Get-Command Invoke-WinCareSetLegacySecurityControlStateAction).ScriptBlock.ToString()|Should Match 'Compensator'
        }

        It 'keeps exact compare-and-swap recovery for display mutations' {
            $source=(Get-Command Invoke-WinCareRestoreDisplayOverridesAction).ScriptBlock.ToString()
            $source|Should Match 'DisplayRecoveryConflict'
            $source|Should Match 'ExpectedCurrentHash'
            (Get-Command Invoke-WinCareResetDisplayOverridesAction).ScriptBlock.ToString()|Should Match 'Compensator'
        }

    }
}
