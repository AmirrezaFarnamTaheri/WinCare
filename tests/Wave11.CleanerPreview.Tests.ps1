BeforeAll {
    $root=Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $root 'src/WinCare/WinCare.psd1') -Force
    Initialize-WinCareState -SkipConfigSave
    $Provider=Get-Content -LiteralPath (Join-Path $root 'src/WinCare/Providers/111-CleanerPreviewStudio.ps1') -Raw
    $Contracts=Get-Content -LiteralPath (Join-Path $root 'src/WinCare/Core/11-ActionContracts.ps1') -Raw
    $Dispatch=Get-Content -LiteralPath (Join-Path $root 'src/WinCare/Core/09-Transactions.ps1') -Raw
    $Headless=Get-Content -LiteralPath (Join-Path $root 'src/WinCare/UI/98-Headless.ps1') -Raw
}

Describe 'Wave eleven cleaner and preview convergence' {
    It 'publishes all cleaner and preview headless routes' {
        $commands=@(Get-WinCareHeadlessCommandName)
        foreach($name in @('cleaner-preview-cards','cleaner-disk-pressure','cleaner-disk-pressure-schedule','cleaner-winapp2','cleaner-winapp2-run','cleaner-relocation-profiles','cleaner-relocation-assess','cleaner-relocation','file-preview-profiles','file-preview','file-preview-export','preview-handlers')){$commands|Should -Contain $name}
    }

    It 'exports all target-native public functions' {
        foreach($name in @('Get-WinCareDiskPressureAssessment','New-WinCareDiskPressureCleanupPlan','New-WinCareDiskPressureAutomationPlan','Import-WinCareWinapp2Catalog','Get-WinCareWinapp2Assessment','New-WinCareWinapp2CleanupPlan','Get-WinCareApplicationDataRelocationProfile','Get-WinCareApplicationDataRelocationAssessment','New-WinCareApplicationDataRelocationPlan','Get-WinCareFilePreviewProfile','Get-WinCareFilePreview','Get-WinCarePreviewHandlerAssessment','New-WinCareFilePreviewExportPlan','Get-WinCareCleanerPreviewCapabilityCard')){Get-Command $name -ErrorAction Stop|Should -Not -BeNullOrEmpty}
    }

    It 'uses typed contracts and dispatcher routes for every new mutation' {
        foreach($type in @('DeleteAdmittedCleanupFiles','RelocateDirectoryWithJunction','RestoreRelocatedDirectory')){$Contracts|Should -Match ("Add-Contract '"+[regex]::Escape($type)+"'");$Dispatch|Should -Match ("'"+[regex]::Escape($type)+"'\{Invoke-WinCare")}
    }

    It 'keeps disk-pressure cleanup on canonical target identifiers' {
        $Provider|Should -Match "Get-WinCareCleanupTarget -Refresh"
        $Provider|Should -Match 'Prefetch purge'
        $Provider|Should -Match 'Drive-root wildcard scan'
        $Provider|Should -Match 'Defender history deletion'
        $Provider|Should -Match 'Restore-point deletion'
        $Provider|Should -Not -Match 'Get-ChildItem\s+-Path\s+[A-Za-z]:\\\s+-Recurse'
    }

    It 'admits only bounded FileKey Winapp2 rules' {
        $Provider|Should -Match "\^FileKey\\d\{1,5\}\$"
        $Provider|Should -Match '2 MiB'
        $Provider|Should -Match '50,000 lines'
        $Provider|Should -Match '5,000 admitted file rules'
        $Provider|Should -Match 'UnsupportedDirective'
        $Provider|Should -Not -Match 'Invoke-Expression|ScriptBlock::Create'
    }

    It 'creates an exact Winapp2 file manifest' {
        $Provider|Should -Match 'LastWriteUtcTicks'
        $Provider|Should -Match 'Sha256=Get-WinCareSha256'
        $Provider|Should -Match 'CleanupCatalogChanged'
        $Provider|Should -Match 'CleanupEntryChanged'
        $Provider|Should -Match 'File changed immediately before deletion'
    }

    It 'never terminates applications for relocation' {
        $Provider|Should -Match 'RelocationProcessRunning'
        $Provider|Should -Match 'Close these applications before relocation'
        $Provider|Should -Not -Match 'Stop-Process|taskkill|TerminateProcess'
    }

    It 'verifies relocation copy, source hash, junction target, and recovery hash' {
        $Provider|Should -Match 'RelocationSourceChanged'
        $Provider|Should -Match 'staging copy failed hash verification'
        $Provider|Should -Match 'junction target does not match'
        $Provider|Should -Match 'RelocationDestinationChanged'
        $Provider|Should -Match "Compensator=@\{Type='RestoreRelocatedDirectory'|Type='RestoreRelocatedDirectory'"
    }

    It 'selects deterministic priority-based preview profiles' {
        $profiles=@(Get-WinCareFilePreviewProfile)
        @($profiles.Id)|Should -Contain 'structured-json'
        @($profiles.Id)|Should -Contain 'archive-container'
        @($profiles.Id)|Should -Contain 'portable-executable'
        @($profiles.Id)|Should -Contain 'binary'
        ($profiles|Sort-Object Priority -Descending|Select-Object -First 1).Priority|Should -BeGreaterThan 0
    }

    It 'keeps preview local bounded and non-executing' {
        $Provider|Should -Match 'Remote UNC paths are not admitted'
        $Provider|Should -Match 'MaximumBytes=1048576'
        $Provider|Should -Match "Execution='None'"
        $Provider|Should -Match 'DtdProcessing.*Prohibit'
        $Provider|Should -Not -Match 'Start-Process.*LiteralPath|Invoke-Item|WebBrowser|Navigate\('
    }

    It 'redacts text by default and reports active content' {
        $Provider|Should -Match 'ConvertTo-WinCareRedactedObject'
        $Provider|Should -Match 'TextIncluded=\[bool\]\$IncludeText'
        $Provider|Should -Match 'ActiveContent=\[bool\]\$profile.ActiveContent'
        $Provider|Should -Match 'ActiveContentBlocked'
    }

    It 'assesses preview handlers without loading them' {
        $Provider|Should -Match 'PreviewHandlers'
        $Provider|Should -Match 'ServerPathHash'
        $Provider|Should -Match 'Loaded=\$false'
        $Provider|Should -Not -Match 'Activator::CreateInstance|CoCreateInstance'
    }

    It 'does not import QuickLook global keyboard hooks or plugin runtime' {
        $Provider|Should -Not -Match 'SetWindowsHookEx|WH_KEYBOARD|GlobalKeyboardHook|LoadFrom\('
        $Provider|Should -Match 'D166-QUICKLOOK-ACTIVE-CONTENT-GUARDRAIL'
    }

    It 'retains the duplicate Toolkit submission as provenance only' {
        $Provider|Should -Not -Match 'D165-[A-Z].*IMPLEMENT'
        $Headless|Should -Not -Match 'windows-community-toolkit-runtime'
    }

    It 'integrates the dedicated TUI and curated GUI surfaces' {
        Test-Path -LiteralPath (Join-Path $root 'src/WinCare/UI/Screens/95-CleanerPreviewStudio.ps1')|Should -BeTrue
        (Get-Content -LiteralPath (Join-Path $root 'src/WinCare/UI/99-Main.ps1') -Raw)|Should -Match "Action='CleanerPreview'"
        $actions=Get-Content -LiteralPath (Join-Path $root 'src/WinCare/Data/Gui/actions.json') -Raw|ConvertFrom-Json
        @($actions.id)|Should -Contain 'disk-pressure-cleanup'
        @($actions.id)|Should -Contain 'local-file-preview'
    }
}
