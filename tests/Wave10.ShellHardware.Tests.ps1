

Describe 'Wave ten Shell and Hardware Studio convergence' {
    BeforeAll {
    $root=Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $root 'src/WinCare/WinCare.psd1') -Force
    Initialize-WinCareState -SkipConfigSave
    $Provider=Get-Content -LiteralPath (Join-Path $root 'src/WinCare/Providers/110-ShellHardwareStudio.ps1') -Raw
    $Native=Get-Content -LiteralPath (Join-Path $root 'src/WinCare/Native/WinCare.ShellHardware.cs') -Raw
}
    It 'publishes every new headless route' {
        $commands=@(Get-WinCareHeadlessCommandName)
        foreach($name in @('shell-hardware-cards','hardware-inventory','hardware-report','monitor-controls','monitor-control-set','window-search','explorer-session','explorer-session-save','explorer-session-records','explorer-session-restore','ui-automation-snapshot','appx-runtime','appx-launch-targets','appx-launch','archive-inspect','servicing-media','terminal-environment','terminal-export','gui-resource-audit','fullscreen-shell-assess')){$commands|Should Contain $name}
    }

    It 'exports the target-native Shell and Hardware functions' {
        foreach($name in @('Get-WinCareHardwareInventory','New-WinCareHardwareReportPlan','Get-WinCareMonitorControlState','New-WinCareMonitorControlPlan','Get-WinCareWindowSearch','Get-WinCareExplorerSession','New-WinCareExplorerSessionSavePlan','Get-WinCareExplorerSessionRecord','New-WinCareExplorerSessionRestorePlan','Get-WinCareUiAutomationSnapshot','Get-WinCareAppxRuntimeInventory','Get-WinCareAppxLaunchTarget','New-WinCareAppxLaunchPlan','Get-WinCareArchiveInspection','Get-WinCareServicingMediaInfo','Get-WinCareTerminalEnvironment','New-WinCareTerminalProfileExportPlan','Get-WinCareGuiResourceAudit','Get-WinCareFullscreenShellAssessment','Get-WinCareShellHardwareCapabilityCard')){Get-Command $name -ErrorAction Stop|Should Not BeNullOrEmpty}
    }

    It 'uses exact typed contracts for the three new mutations' {
        $contracts=Get-Content -LiteralPath (Join-Path $root 'src/WinCare/Core/11-ActionContracts.ps1') -Raw
        $dispatch=Get-Content -LiteralPath (Join-Path $root 'src/WinCare/Core/09-Transactions.ps1') -Raw
        foreach($type in @('SetMonitorVcpFeature','OpenExplorerLocation','LaunchAppxApplication')){$contracts|Should Match ("Add-Contract '"+[regex]::Escape($type)+"'");$dispatch|Should Match ("'"+[regex]::Escape($type)+"'\{Invoke-WinCare")}
    }

    It 'limits monitor mutation to brightness and contrast VCP codes with CAS recovery' {
        $Provider|Should Match "FeatureCode -eq 0x10"
        $Provider|Should Match "FeatureCode -eq 0x12"
        $Native|Should Match 'expectedCurrent'
        $Native|Should Match 'expectedMaximum'
        $Native|Should Match 'Monitor VCP state changed after preview'
        $Provider|Should Match "Compensator @\{Type='SetMonitorVcpFeature'|Type='SetMonitorVcpFeature'"
    }

    It 'keeps hardware serials redacted by default' {
        $Provider|Should Match "'sha256:'"
        $Provider|Should Match 'IncludeSensitiveIdentifiers'
        $Provider|Should Match 'SensitiveIdentifiersIncluded'
        $Provider|Should Not -Match 'Substring\(0,16\)'
        $Provider|Should Match 'Domain=ConvertTo-WinCareHardwareIdentifier'
    }

    It 'bounds UI Automation depth, node count, and text exposure' {
        $Provider|Should Match 'ValidateRange\(1,8\).*MaximumDepth'
        $Provider|Should Match 'ValidateRange\(1,2000\).*MaximumNodes'
        $Provider|Should Match '\[redacted length='
        $Provider|Should Match 'TextIncluded'
    }

    It 'inspects archives without extraction and rejects unsafe members' {
        $Provider|Should Match 'ExtractionPerformed=\$false'
        $Provider|Should Match 'Unsafe path:'
        $Provider|Should Match 'Duplicate member:'
        $Provider|Should Match 'Case-colliding member:'
        $Provider|Should Match 'Symlink member:'
        $Provider|Should Not -Match 'ExtractToDirectory'
    }

    It 'uses supported AppX process identity and activation APIs' {
        $Native|Should Match 'GetPackageFullName'
        $Native|Should Match 'IApplicationActivationManager'
        $Provider|Should Match 'ManifestSha256'
        $Provider|Should Match 'AppxTargetChanged'
        $Provider|Should Match 'Get-AppxPackageManifest -Package \$package -ErrorAction Stop'
        $Provider|Should Not -Match 'DownloadString|Invoke-Expression'
    }

    It 'restricts Explorer session restoration to canonical local directories' {
        $Provider|Should Match 'ExpectedPathHash'
        $Provider|Should Match 'Resolve-WinCareCanonicalPath'
        $Provider|Should Match 'PathType Container'
        $contracts=Get-Content -LiteralPath (Join-Path $root 'src/WinCare/Core/11-ActionContracts.ps1') -Raw
        $contracts|Should Match 'Explorer locations must be local filesystem paths'
    }

    It 'keeps servicing-media behavior read-only' {
        $Provider|Should Match '/Get-WimInfo'
        $Provider|Should Not -Match '/Apply-Image|/Capture-Image|/Commit-Image|/Unmount-Image'
        $Provider|Should Match 'ReadOnly=\$true'
    }

    It 'does not import duplicate donor architectures as parallel subsystems' {
        $Provider|Should Not -Match 'D151-[A-Z].*IMPLEMENT|D155-[A-Z].*IMPLEMENT|D157-[A-Z].*IMPLEMENT|D160-[A-Z].*IMPLEMENT'
        $Provider|Should Match 'D151-DUPLICATE-SUBMISSION'
        $Provider|Should Match 'D155-DUPLICATE-SUBMISSION'
    }

    It 'binds monitor mutation to the physical monitor description' {
        $Native|Should Match 'expectedDescription'
        $Native|Should Match 'Physical monitor description changed after preview'
        $Provider|Should Match 'SetMonitorVcpFeature\(\[string\]\$p.DeviceName,\[int\]\$p.PhysicalIndex,\[string\]\$p.Description'
    }

    It 'redacts terminal command lines and starting directories' {
        $Provider|Should Match 'CommandLineSha256'
        $Provider|Should Match 'StartingDirectorySha256'
        $Provider|Should Match 'SecretsIncluded=\$false'
        $Provider|Should Match 'PathSha256'
        $Provider|Should Not -Match '\{Path=\$path;Sha256=Get-WinCareSha256 \$path'
        $Provider|Should Not -Match 'CommandLine=\[string\]\(Get-WinCarePropertyValue'
    }

    It 'includes Winhance only through the separately authorized unsafe plane' {
        $unsafe=@(Get-WinCareLegacyUnsafeProfile)
        @($unsafe.Id)|Should Contain 'winhance-maximum'
        $profile=$unsafe|Where-Object Id -eq 'winhance-maximum'|Select-Object -First 1
        @($profile.SourceRecords)|Should Contain 'D161-WINHANCE-MAXIMUM'
        (Get-Content -LiteralPath (Join-Path $root 'src/WinCare/UI/98-Headless.ps1') -Raw)|Should Match 'I ACCEPT LEGACY UNSAFE MUTATIONS'
    }
}
