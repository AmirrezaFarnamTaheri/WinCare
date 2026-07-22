BeforeAll {
    $root=Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $root 'src/WinCare/WinCare.psd1') -Force
    Initialize-WinCareState -SkipConfigSave
}

Describe 'Wave eight system toolkit convergence' {
    It 'publishes the system toolkit headless routes' {
        $commands=@(Get-WinCareHeadlessCommandName)
        foreach($name in @('toolkit-diagnostics','toolkit-win32-error','toolkit-msi','hardening-profiles','hardening-assess','hardening-apply','maintenance-templates','maintenance-template-create','system-shortcuts','system-shortcuts-export','download-batch','torrent-metadata')){$commands | Should -Contain $name}
    }

    It 'exports the target-native toolkit functions' {
        foreach($name in @('Get-WinCareSystemToolkitDiagnostic','Get-WinCareHardeningProfile','New-WinCareHardeningProfilePlan','Get-WinCareMaintenanceTemplate','New-WinCareMaintenanceTemplatePlan','Get-WinCareSystemShortcutCatalog','Import-WinCareDownloadBatchDefinition','New-WinCareDownloadBatchPlan','Get-WinCareTorrentMetadata')){Get-Command $name -ErrorAction Stop | Should -Not -BeNullOrEmpty}
    }

    It 'ships bounded hardening profiles and unsafe one-click compositions' {
        @((Get-WinCareHardeningProfile).Id) | Should -Contain 'balanced'
        @((Get-WinCareHardeningProfile).Id) | Should -Contain 'strict'
        @((Get-WinCareHardeningProfile).Id) | Should -Contain 'hailmary'
        $profiles=@(Get-WinCareLegacyUnsafeProfile)
        foreach($id in @('hardening-hailmary','optimize-debloat-ultimate','personalization-legacy')){@($profiles.Id) | Should -Contain $id}
        $headless=Get-Content (Join-Path $root 'src/WinCare/UI/98-Headless.ps1') -Raw
        $headless | Should -Match 'I ACCEPT LEGACY UNSAFE MUTATIONS'
    }

    It 'keeps custom download batches bounded and schema validated' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/105-SystemToolkit.ps1') -Raw
        $source | Should -Match 'Download batch exceeds 256 KiB'
        $source | Should -Match '1\.\.100 jobs'
        $source | Should -Match 'Test-WinCareStrictObjectKeys'
        $source | Should -Match 'Duplicate download destination'
        $source | Should -Not -Match 'Invoke-Expression'
    }

    It 'parses only local bounded torrent metadata and does not implement peer networking' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/105-SystemToolkit.ps1') -Raw
        $source | Should -Match 'Torrent metadata must be between 1 byte and 16 MiB'
        $source | Should -Match 'Torrent metadata nesting exceeds 16 levels'
        $source | Should -Match "NetworkExecution='Not implemented'"
        $source | Should -Not -Match 'DHT|peer-discovery|tracker announce request|net\.connect'
    }

    It 'keeps shortcut exports under the WinCare state root and bounded' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/105-SystemToolkit.ps1') -Raw
        $source | Should -Match 'Get-WinCareSystemToolkitRoot'
        $source | Should -Match 'Shortcut export exceeds 1 MiB'
        $source | Should -Match 'New-WinCareManagedFileWriteAction'
    }

    It 'reuses the authoritative maintenance and download stores' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/105-SystemToolkit.ps1') -Raw
        $source | Should -Match 'New-WinCareMaintenanceWindowRecord'
        $source | Should -Match 'New-WinCareMaintenanceUpsertPlan'
        $source | Should -Match 'New-WinCareDownloadJobRecord'
        $source | Should -Match 'New-WinCareDownloadStoreReplacementPlan'
    }

    It 'includes the personalization-derived catalog rules' {
        $catalog=Get-Content (Join-Path $root 'src/WinCare/Data/Catalog/rules.json') -Raw | ConvertFrom-Json
        foreach($id in @('appearance.dark-mode','appearance.disable-spotlight','taskbar.hide-meet-now','taskbar.hide-people','taskbar.disable-feeds','settings.disable-online-tips')){@($catalog.rules.id) | Should -Contain $id}
    }

    It 'does not import donor elevation, streaming, or arbitrary script execution paths' {
        $source=Get-Content (Join-Path $root 'src/WinCare/Providers/105-SystemToolkit.ps1') -Raw
        $source | Should -Not -Match 'gsudo\.exe|Start-Process.+-Verb RunAs|Invoke-Expression|cmd\.exe\s+/c|node_modules'
    }
}
