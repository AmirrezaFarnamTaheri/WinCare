

Describe 'WinCare V5 graphical console' {
    BeforeAll {
    $Root=Split-Path $PSScriptRoot -Parent
    $GuiRoot=Join-Path $Root 'src/WinCare/Data/Gui'
    $Headless=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/98-Headless.ps1') -Raw
    $Catalog=Get-Content -LiteralPath (Join-Path $GuiRoot 'actions.json') -Raw|ConvertFrom-Json -Depth 40
    $Xaml=Get-Content -LiteralPath (Join-Path $GuiRoot 'WinCare.MainWindow.xaml') -Raw
}
    It 'loads a secure componentized WPF shell with required named surfaces' {
        {[xml]$Xaml}|Should Not -Throw
        foreach($name in @('NavigationList','DashboardPanel','ActionsPanel','EvidencePanel','SettingsPanel','ConfirmOverlay','BusyOverlay','ActionList','ArgumentsTextBox','ActionOutputTextBox')){$Xaml|Should Match ('x:Name="'+[regex]::Escape($name)+'"')}
        $Xaml|Should Not -Match 'x:Class='
    }

    It 'uses accessible high-contrast design tokens and keyboard affordances' {
        foreach($color in @('#0B1020','#121A2B','#7C5CFC','#36C5F0','#2DD4A8','#F6C85F','#F97066','#F4F7FB')){$Xaml|Should Match [regex]::Escape($color)}
        $Xaml|Should Match 'AutomationProperties.Name="Search WinCare actions"'
        $Xaml|Should Match 'AutomationProperties.Name="Critical action acknowledgement"'
        (Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/10-GuiRuntime.ps1') -Raw)|Should Match 'ModifierKeys.*Control'
    }

    It 'keeps critical acknowledgement and policy gates visible in the GUI' {
        $Runtime=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/10-GuiRuntime.ps1') -Raw
        $Runtime|Should Match 'I ACCEPT LEGACY UNSAFE MUTATIONS'
        $Runtime|Should Match 'Invoke-WinCareHeadlessCommand|WinCare.ps1'
        $Runtime|Should Not -Match 'Invoke-WinCarePlan\s+-Plan'
        @($Catalog|Where-Object critical).Count|Should BeGreaterThan 0
        foreach($item in @($Catalog|Where-Object critical)){$item.risk|Should Be 'Critical'}
    }

    It 'exposes every headless command through curated or generated catalog coverage' {
        $Declared=[regex]::Match($Headless,'function Get-WinCareHeadlessCommandName\s*\{\s*@\((.*?)\)\s*\}','Singleline').Groups[1].Value
        $Commands=@([regex]::Matches($Declared,"'([^']+)'")|ForEach-Object{$_.Groups[1].Value})
        $GuiCode=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/00-GuiCatalog.ps1') -Raw
        $GuiCode|Should Match 'foreach\(\$command in \$commands\)'
        foreach($item in $Catalog){$item.command|Should -BeIn $Commands}
    }

    It 'ships GUI and terminal launchers plus separate installed shortcuts' {
        Test-Path -LiteralPath (Join-Path $Root 'WinCare-GUI.ps1')|Should Be $true
        Test-Path -LiteralPath (Join-Path $Root 'run-wincare-gui.cmd')|Should Be $true
        (Get-Content -LiteralPath (Join-Path $Root 'WinCare.ps1') -Raw)|Should Match '\[switch\]\$Gui'
        $Installer=Get-Content -LiteralPath (Join-Path $Root 'Install-WinCare.ps1') -Raw
        $Installer|Should Match 'WinCare-GUI.ps1'
        $Installer|Should Match 'WinCare Console.lnk'
    }

    It 'binds GUI synthesis to prior donor evidence without inventing new donors' {
        $Ledger=Import-Csv -LiteralPath (Join-Path $Root 'docs/convergence/adoption-matrix.csv')
        foreach($id in @('D111-V5-HEALTH-DASHBOARD','D127-V5-COMMAND-WORKBENCH','D127-V5-RISK-COMMUNICATION','D143-V5-CAPABILITY-NAVIGATION','D143-V5-EVIDENCE-EXPLORER')){$Ledger.record_id|Should Contain $id}
        $Inventory=Get-Content -LiteralPath (Join-Path $Root 'docs/convergence/donor-inventory.json') -Raw|ConvertFrom-Json
        @($Inventory).Count|Should Be 163
        @($Inventory|Where-Object{-not $_.duplicate_of}).Count|Should Be 151
    }

    It 'keeps the GUI catalog schema bounded and deterministic' {
        @($Catalog).Count|Should BeGreaterThan 20
        @($Catalog.id|Select-Object -Unique).Count|Should Be @($Catalog).Count
        foreach($item in $Catalog){$item.id|Should Match '^[a-z0-9-]+$';$item.command|Should Match '^[a-z0-9-]+$';{$item.defaultArguments|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable}|Should Not -Throw}
    }


    It 'uses dynamic theme resources and explicit accessibility modes' {
        foreach($token in @('BackgroundBrush','SurfaceBrush','AccentBrush','TextBrush')){$Xaml|Should Match ('\{DynamicResource '+[regex]::Escape($token)+'\}')}
        $Runtime=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/10-GuiRuntime.ps1') -Raw
        $Runtime|Should Match "ValidateSet\('Normal','HighContrast','Monochrome'\)"
        $Runtime|Should Match '\[Windows\.SystemParameters\]::HighContrast'
        $Runtime|Should Match 'Set-WinCareGuiTheme'
    }

    It 'launches commands through bounded argument-safe child processes' {
        $Runtime=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/10-GuiRuntime.ps1') -Raw
        $Runtime|Should Match '\[Diagnostics\.ProcessStartInfo\]::new\(\)'
        $Runtime|Should Match '\$psi\.ArgumentList\.Add'
        $Runtime|Should Match '\$psi\.WorkingDirectory=\$root'
        $Runtime|Should Match '24576 characters'
        $Runtime|Should Not -Match 'powershell\.exe\s+-Command'
    }

    It 'labels generated mutating commands conservatively and critical commands explicitly' {
        $GuiCode=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/UI/Gui/00-GuiCatalog.ps1') -Raw
        $GuiCode|Should Match "else\{'High'\}"
        foreach($command in @('pagefile-set','wua-uninstall','wdac-deploy','injection-surface-quarantine','run-automation','group-policy-import','sysmon-uninstall','offline-package-add')){$GuiCode|Should Match [regex]::Escape($command)}
    }

    It 'rolls back both installed shortcuts transactionally' {
        $Installer=Get-Content -LiteralPath (Join-Path $Root 'Install-WinCare.ps1') -Raw
        $Uninstaller=Get-Content -LiteralPath (Join-Path $Root 'Uninstall-WinCare.ps1') -Raw
        foreach($needle in @('shortcutBackup','consoleShortcutBackup','restore previous shortcut','restore previous console shortcut')){$Installer|Should Match [regex]::Escape($needle)}
        foreach($needle in @('shortcutBackup','consoleShortcutBackup','restore shortcut','restore console shortcut')){$Uninstaller|Should Match [regex]::Escape($needle)}
    }

    It 'preserves graphical, terminal, and headless interfaces in one module authority path' {
        $Manifest=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/WinCare.psd1') -Raw
        $Manifest|Should Match "ModuleVersion\s*=\s*'5.1.0'"
        $Manifest|Should Match "'Start-WinCareGui'"
        $Manifest|Should Match "'Start-WinCare'"
        $Manifest|Should Match "'Invoke-WinCareHeadlessCommand'"
    }
}
