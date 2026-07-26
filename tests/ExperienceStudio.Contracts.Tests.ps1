

Describe 'Experience Studio contracts' {
    BeforeAll {
    $root=Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $root 'src/WinCare/WinCare.psd1') -Force
    Initialize-WinCareState -SkipConfigSave
}
    It 'publishes all experience studio headless routes' {
        $commands=@(Get-WinCareHeadlessCommandName)
        foreach($name in @('experience-cards','experience-visual-asset','experience-sprite-layout','experience-visual-manifest','experience-location-state','experience-location-set','experience-remote-profiles','experience-remote-state','experience-remote-set','experience-power-profiles','experience-power-assess','experience-power-apply','experience-download-strategy','experience-privacy-profiles','experience-privacy-apply')){$commands | Should Contain $name}
    }

    It 'exports the target-native experience functions' {
        foreach($name in @('Get-WinCareVisualAssetInfo','Get-WinCareSpriteSheetLayout','New-WinCareVisualAssetManifestPlan','Get-WinCareLocationPrivacyState','New-WinCareLocationPrivacyPlan','Get-WinCareRemoteAccessProfile','Get-WinCareRemoteAccessState','New-WinCareRemoteAccessPlan','Get-WinCarePowerConditionProfile','Get-WinCarePowerConditionAssessment','New-WinCarePowerConditionPlan','Get-WinCareDownloadStrategy','Get-WinCarePrivacyProfile','New-WinCarePrivacyProfilePlan','Get-WinCareExperienceCapabilityCard')){Get-Command $name -ErrorAction Stop | Should Not BeNullOrEmpty}
    }

    It 'builds bounded sprite layouts without image execution' {
        $layout=Get-WinCareSpriteSheetLayout -SheetWidth 64 -SheetHeight 32 -FrameWidth 16 -FrameHeight 16
        $layout.Columns | Should Be 4
        $layout.Rows | Should Be 2
        $layout.FrameCount | Should Be 8
        { Get-WinCareSpriteSheetLayout -SheetWidth 100000 -SheetHeight 100000 -FrameWidth 1 -FrameHeight 1 } | Should Throw
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/109-ExperienceStudio.ps1') -Raw
        $source | Should Match "Execution='None'"
        $source | Should Not -Match 'Start-Process.+Asset|Invoke-Expression'
    }

    It 'keeps location privacy separate from service mutation' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/109-ExperienceStudio.ps1') -Raw
        $source | Should Match "ServiceMutation='None'"
        $source | Should Match 'CapabilityAccessManager\\ConsentStore\\location'
        $source | Should Not -Match "SetServiceStartMode.+lfsvc"
    }

    It 'admits only RDP and OpenSSH remote access profiles' {
        @((Get-WinCareRemoteAccessProfile).Id) | Should Be @('rdp','openssh')
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/109-ExperienceStudio.ps1') -Raw
        $source | Should Match 'RemoteDesktop-UserMode-In-TCP'
        $source | Should Match 'OpenSSH-Server-In-TCP'
        $source | Should Match 'SetServiceRuntimeState'
        $source | Should Match 'Stage 1 installs the OpenSSH Server capability'
        (Get-Content (Join-Path $root 'src/WinCare/Core/11-ActionContracts.ps1') -Raw) | Should Match "Add-Contract 'SetServiceRuntimeState'"
        (Get-Content (Join-Path $root 'src/WinCare/Core/09-Transactions.ps1') -Raw) | Should Match "'SetServiceRuntimeState'\{Invoke-WinCareServiceRuntimeStateAction"
        (Get-Content (Join-Path $root 'src/WinCare/Providers/25-SystemPolicy.ps1') -Raw) | Should Match 'function Invoke-WinCareServiceRuntimeStateAction'
        $source | Should Not -Match 'password|KMS|Enable-PSRemoting|New-NetFirewallRule'
    }

    It 'uses the bounded WinCare download engine without a parallel daemon' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/109-ExperienceStudio.ps1') -Raw
        $source | Should Match "Transport='BITS'"
        $source | Should Match "ParallelChunking='Not admitted'"
        $source | Should Match "RemoteDaemon='Not admitted'"
        $source | Should Not -Match 'ListenAndServe|http\.Server|32-way'
    }

    It 'ships reversible privacy profiles and separately authorized maximum presets' {
        @((Get-WinCarePrivacyProfile).Id) | Should Be @('balanced','strict','maximum')
        $unsafe=@(Get-WinCareLegacyUnsafeProfile)
        @($unsafe.Id) | Should Contain 'rytunex-maximum'
        @($unsafe.Id) | Should Contain 'privacy-sexy-maximum'
        $headless=Get-Content (Join-Path $root 'src/WinCare/UI/98-Headless.ps1') -Raw
        $headless | Should Match 'I ACCEPT LEGACY UNSAFE MUTATIONS'
    }

    It 'records exact duplicates as provenance rather than parallel features' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/109-ExperienceStudio.ps1') -Raw
        $source | Should Not -Match 'D131-|D132-|D134-|D138-|D142-'
    }
}
