

Describe 'WinCare graphical console' {
    BeforeAll {
    $Root=Split-Path $PSScriptRoot -Parent
    $GuiRoot=Join-Path $Root 'src/WinCare/Data/Gui'
    $Headless=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/98-Headless.ps1') -Raw
    $Catalog=Get-Content -LiteralPath (Join-Path $GuiRoot 'actions.json') -Raw|ConvertFrom-Json -Depth 40
    $Xaml=Get-Content -LiteralPath (Join-Path $GuiRoot 'WinCare.MainWindow.xaml') -Raw
}
    It 'loads a secure componentized WPF shell with required named surfaces' {
        {[xml]$Xaml}|Should -Not -Throw
        foreach($name in @('NavigationList','DashboardPanel','ActionsPanel','AssurancePanel','SettingsPanel','ConfirmOverlay','BusyOverlay','ActionList','ArgumentsTextBox','ActionOutputTextBox')){$Xaml|Should -Match ('x:Name="'+[regex]::Escape($name)+'"')}
        $Xaml|Should -Not -Match 'x:Class='
    }

    It 'uses accessible high-contrast design tokens and keyboard affordances' {
        foreach($color in @('#0B1020','#121A2B','#2F80ED','#34D6E9','#3FB950','#D29922','#F85149','#E6EDF3')){$Xaml|Should -Match ([regex]::Escape($color))}
        $Xaml|Should -Match 'AutomationProperties.Name="Search WinCare actions"'
        $Xaml|Should -Match 'AutomationProperties.Name="Critical action acknowledgement"'
        (Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/10-GuiRuntime.ps1') -Raw)|Should -Match 'ModifierKeys.*Control'
    }

    It 'keeps critical acknowledgement and policy gates visible in the GUI' {
        $Runtime=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/10-GuiRuntime.ps1') -Raw
        $Runtime|Should -Match 'I ACCEPT LEGACY UNSAFE MUTATIONS'
        $Runtime|Should -Match 'Invoke-WinCareHeadlessCommand|WinCare.ps1'
        $Runtime|Should -Not -Match 'Invoke-WinCarePlan\s+-Plan'
        $CriticalCatalogItems=@($Catalog|Where-Object {
            $_.PSObject.Properties['critical'] -and [bool]$_.critical
        })
        $CriticalCatalogItems.Count|Should -BeGreaterThan 0
        foreach($item in $CriticalCatalogItems){$item.risk|Should -Be 'Critical'}
    }

    It 'exposes every headless command through curated or generated catalog coverage' {
        $Declared=[regex]::Match($Headless,'function Get-WinCareHeadlessCommandName\s*\{\s*@\((.*?)\)\s*\}','Singleline').Groups[1].Value
        $Commands=@([regex]::Matches($Declared,"'([^']+)'")|ForEach-Object{$_.Groups[1].Value})
        $GuiCode=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/00-GuiCatalog.ps1') -Raw
        $GuiCode|Should -Match 'foreach\(\$command in \$commands\)'
        foreach($item in $Catalog){$item.command|Should -BeIn $Commands}
    }

    It 'ships GUI and terminal launchers plus separate installed shortcuts' {
        Test-Path -LiteralPath (Join-Path $Root 'WinCare-GUI.ps1')|Should -Be $true
        Test-Path -LiteralPath (Join-Path $Root 'run-wincare-gui.cmd')|Should -Be $true
        (Get-Content -LiteralPath (Join-Path $Root 'WinCare.ps1') -Raw)|Should -Match '\[switch\]\$Gui'
        $Installer=Get-Content -LiteralPath (Join-Path $Root 'Install-WinCare.ps1') -Raw
        $Installer|Should -Match 'WinCare-GUI.ps1'
        $Installer|Should -Match 'WinCare Console.lnk'
    }


    It 'keeps the GUI catalog schema bounded and deterministic' {
        @($Catalog).Count|Should -BeGreaterThan 20
        @($Catalog.id|Select-Object -Unique).Count|Should -Be @($Catalog).Count
        foreach($item in $Catalog){$item.id|Should -Match '^[a-z0-9-]+$';$item.command|Should -Match '^[a-z0-9-]+$';{$item.defaultArguments|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable}|Should -Not -Throw}
    }


    It 'uses dynamic theme resources and explicit accessibility modes' {
        foreach($token in @('BackgroundBrush','SurfaceBrush','AccentBrush','TextBrush')){$Xaml|Should -Match ('\{DynamicResource '+[regex]::Escape($token)+'\}')}
        $Runtime=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/10-GuiRuntime.ps1') -Raw
        $Runtime|Should -Match "ValidateSet\('Normal','HighContrast','Monochrome'\)"
        $Runtime|Should -Match '\[Windows\.SystemParameters\]::HighContrast'
        $Runtime|Should -Match 'Set-WinCareGuiTheme'
    }

    It 'launches commands through bounded argument-safe child processes' {
        $Runtime=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/10-GuiRuntime.ps1') -Raw
        $Runtime|Should -Match '\[Diagnostics\.ProcessStartInfo\]::new\(\)'
        $Runtime|Should -Match '\$psi\.ArgumentList\.Add'
        $Runtime|Should -Match '\$psi\.WorkingDirectory=\$root'
        $Runtime|Should -Match '24576 bytes'
        $Runtime|Should -Not -Match 'powershell\.exe\s+-Command'
    }

    It 'labels generated mutating commands conservatively and critical commands explicitly' {
        $GuiCode=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/00-GuiCatalog.ps1') -Raw
        $GuiCode|Should -Match "else\{'High'\}"
        foreach($command in @('pagefile-set','wua-uninstall','wdac-deploy','injection-surface-quarantine','run-automation','group-policy-import','sysmon-uninstall','offline-package-add')){$GuiCode|Should -Match ([regex]::Escape($command))}
    }

    It 'rolls back both installed shortcuts transactionally' {
        $Installer=Get-Content -LiteralPath (Join-Path $Root 'Install-WinCare.ps1') -Raw
        $Uninstaller=Get-Content -LiteralPath (Join-Path $Root 'Uninstall-WinCare.ps1') -Raw
        foreach($needle in @('shortcutBackup','consoleShortcutBackup','restore previous shortcut','restore previous console shortcut')){$Installer|Should -Match ([regex]::Escape($needle))}
        foreach($needle in @('shortcutBackup','consoleShortcutBackup','restore shortcut','restore console shortcut')){$Uninstaller|Should -Match ([regex]::Escape($needle))}
    }

    It 'preserves graphical, terminal, and headless interfaces in one module authority path' {
        $Manifest=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/WinCare.psd1') -Raw
        $ModuleData=Import-PowerShellDataFile -LiteralPath (Join-Path $Root 'src/WinCare/WinCare.psd1')
        $ModuleData.ModuleVersion.ToString()|Should -Match '^\d+\.\d+\.\d+$'
        $Manifest|Should -Match "'Start-WinCareGui'"
        $Manifest|Should -Match "'Start-WinCare'"
        $Manifest|Should -Match "'Invoke-WinCareHeadlessCommand'"
    }
}
