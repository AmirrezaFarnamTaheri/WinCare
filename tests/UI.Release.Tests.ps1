
Describe 'UI and headless completeness' {
    #requires -Version 7.2
BeforeAll { Import-Module "$((Get-Location).Path)\src\WinCare\WinCare.psd1" -Force -Global }
  InModuleScope WinCare {
    It 'routes every visible main-menu action except Exit' {
      $actions=@(Get-WinCareMainMenuItems|ForEach-Object Action|Where-Object{$_ -ne 'Exit'})
      $routeText=Get-Content (Join-Path $script:WinCareModuleRoot 'UI\97-CommandPalette.ps1') -Raw
      foreach($action in $actions){$routeText|Should Match ("'"+[regex]::Escape($action)+"'\s*\{")}
    }
    It 'implements every declared headless command' {
      $text=Get-Content (Join-Path $script:WinCareModuleRoot 'UI\98-Headless.ps1') -Raw
      foreach($command in Get-WinCareHeadlessCommandName){$text|Should Match ("'"+[regex]::Escape($command)+"'\s*\{")}
    }
    It 'provides accessibility modes without color-only state' {
      $config=Get-WinCareDefaultConfig;$config.Theme|Should -BeIn @('Normal','HighContrast','Monochrome');$config.Keys|Should Contain Ascii;$config.Keys|Should Contain ScreenReaderMode;$config.Keys|Should Contain ReducedMotion
    }
  }
}

Describe 'Module and release metadata' {
  It 'uses one semantic version across module files' {
    $root=Split-Path $PSScriptRoot -Parent
    $psm=Get-Content (Join-Path $root 'src\WinCare\WinCare.psm1') -Raw;$psd=Import-PowerShellDataFile (Join-Path $root 'src\WinCare\WinCare.psd1')
    $psm|Should Match ("WinCareVersion = '"+[regex]::Escape($psd.ModuleVersion.ToString())+"'")
    $psd.ModuleVersion.ToString()|Should Be '1.0.0'
  }
  It 'does not export the unrestricted internal plan executor' {
    Get-Command Invoke-WinCarePlan -Module WinCare -ErrorAction SilentlyContinue|Should BeNullOrEmpty
  }
}
