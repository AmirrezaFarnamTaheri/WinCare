# Pester 5 executes Describe bodies -- and therefore InModuleScope -- during
# Discovery, which runs before any BeforeAll. The module must already be loaded
# at that point, otherwise InModuleScope throws "No modules named 'WinCare' are
# currently loaded" and the entire container fails without running one test.
# This import is deliberately at file scope; the BeforeAll imports below remain
# for run-time state. $PSScriptRoot is used so the path never depends on the
# caller's working directory.
Import-Module (Join-Path $PSScriptRoot '../src/WinCare/WinCare.psd1') -Force -Global

Describe 'System toolkit contracts' {
    BeforeAll {
        $root=Split-Path -Parent $PSScriptRoot
        Import-Module (Join-Path $root 'src/WinCare/WinCare.psd1') -Force
    }

    InModuleScope WinCare {
        BeforeEach {
            $script:WinCareState=@{Config=Get-WinCareDefaultConfig;Policy=Get-WinCareDefaultPolicy;Root=$TestDrive;SessionId='0123456789abcdef0123456789abcdef';IsAdmin=$true;Capabilities=@{};ActionContracts=$null}
            foreach($name in @('Toolkit','Downloads','Maintenance','Recovery','Reports','Logs','Journal','Backups')){New-Item -ItemType Directory -Path (Join-Path $TestDrive $name) -Force|Out-Null}
        }

        It 'publishes the system toolkit headless routes' {
            $commands=@(Get-WinCareHeadlessCommandName)
            foreach($name in @('toolkit-diagnostics','toolkit-win32-error','toolkit-msi','hardening-profiles','hardening-assess','hardening-apply','maintenance-templates','maintenance-template-create','system-shortcuts','system-shortcuts-export','download-batch','torrent-metadata')){$commands|Should -Contain $name}
        }

        It 'exports the target-native toolkit functions' {
            foreach($name in @('Get-WinCareSystemToolkitDiagnostic','Get-WinCareHardeningProfile','New-WinCareHardeningProfilePlan','Get-WinCareMaintenanceTemplate','New-WinCareMaintenanceTemplatePlan','Get-WinCareSystemShortcutCatalog','Import-WinCareDownloadBatchDefinition','New-WinCareDownloadBatchPlan','Get-WinCareTorrentMetadata')){Get-Command $name -ErrorAction Stop|Should -Not -BeNullOrEmpty}
        }

        It 'ships bounded hardening profiles and explicitly gated unsafe compositions' {
            foreach($id in @('balanced','strict','hailmary')){@((Get-WinCareHardeningProfile).Id)|Should -Contain $id}
            $profiles=@(Get-WinCareLegacyUnsafeProfile)
            foreach($id in @('hardening-hailmary','optimize-debloat-ultimate','personalization-legacy')){@($profiles.Id)|Should -Contain $id}
            $headless=Get-Content (Join-Path $script:WinCareModuleRoot 'UI/98-Headless.ps1') -Raw
            $headless|Should -Match 'I ACCEPT LEGACY UNSAFE MUTATIONS'
        }

        It 'keeps custom download batches bounded and schema validated' {
            $source=(Get-Command Import-WinCareDownloadBatchDefinition).ScriptBlock.ToString()
            $source|Should -Match 'Download batch exceeds 256 KiB'
            $source|Should -Match '1\.\.100 jobs'
            $source|Should -Match 'Test-WinCareStrictObjectKeys'
            $source|Should -Match 'Duplicate download destination'
            $source|Should -Not -Match 'Invoke-Expression'
        }

        It 'converts batch records through the authoritative protected download store' {
            (Get-Command Import-WinCareDownloadBatchDefinition).ScriptBlock.ToString()|Should -Match 'New-WinCareDownloadJobRecord'
            $source=(Get-Command New-WinCareDownloadBatchPlan).ScriptBlock.ToString()
            $source|Should -Match 'New-WinCareDownloadStoreReplacementPlan'
            $source|Should -Match 'MaximumDownloadJobs'
            $source|Should -Not -Match 'curl\.exe|Start-Process|Invoke-WebRequest'
        }

        It 'parses only local bounded torrent metadata and does not implement peer networking' {
            $source=(Get-Command Get-WinCareTorrentMetadata).ScriptBlock.ToString()
            $source|Should -Match 'Torrent metadata must be between 1 byte and 16 MiB'
            $source|Should -Match "NetworkExecution='Not implemented'"
            (Get-Command Read-WinCareBencodeValue).ScriptBlock.ToString()|Should -Match 'Torrent metadata nesting exceeds 16 levels'
            $source|Should -Not -Match 'DHT|peer-discovery|tracker announce request|net\.connect|Invoke-RestMethod|Invoke-WebRequest'
        }

        It 'keeps shortcut exports under the WinCare state root and bounded' {
            $source=(Get-Command New-WinCareSystemShortcutExportPlan).ScriptBlock.ToString()
            $source|Should -Match 'Get-WinCareSystemToolkitRoot'
            $source|Should -Match 'Shortcut export exceeds 1 MiB'
            $source|Should -Match 'New-WinCareManagedFileWriteAction'
        }

        It 'reuses the authoritative maintenance lifecycle store' {
            $source=(Get-Command New-WinCareMaintenanceTemplatePlan).ScriptBlock.ToString()
            $source|Should -Match 'New-WinCareMaintenanceWindowRecord'
            $source|Should -Match 'New-WinCareMaintenanceUpsertPlan'
        }

        It 'includes the personalization-derived catalog rules' {
            $catalog=Get-Content (Join-Path $script:WinCareModuleRoot 'Data/Catalog/rules.json') -Raw|ConvertFrom-Json
            foreach($id in @('appearance.dark-mode','appearance.disable-spotlight','taskbar.hide-meet-now','taskbar.hide-people','taskbar.disable-feeds','settings.disable-online-tips')){@($catalog.rules.id)|Should -Contain $id}
        }

        It 'does not import parallel elevation, streaming, or arbitrary script execution paths' {
            $commands=@('Import-WinCareDownloadBatchDefinition','New-WinCareDownloadBatchPlan','Get-WinCareTorrentMetadata','New-WinCareSystemShortcutExportPlan','New-WinCareMaintenanceTemplatePlan')
            $source=($commands|ForEach-Object{(Get-Command $_).ScriptBlock.ToString()})-join "`n"
            $source|Should -Not -Match 'gsudo\.exe|Start-Process.+-Verb RunAs|Invoke-Expression|cmd\.exe\s+/c|node_modules'
        }
    }
}
