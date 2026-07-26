# Pester 5 executes Describe bodies -- and therefore InModuleScope -- during
# Discovery, which runs before any BeforeAll. The module must already be loaded
# at that point, otherwise InModuleScope throws "No modules named 'WinCare' are
# currently loaded" and the entire container fails without running one test.
# This import is deliberately at file scope; the BeforeAll imports below remain
# for run-time state. $PSScriptRoot is used so the path never depends on the
# caller's working directory.
Import-Module (Join-Path $PSScriptRoot '../src/WinCare/WinCare.psd1') -Force -Global


Describe 'Catalog, preset, and application profile admission' {
    #requires -Version 7.2
BeforeAll { Import-Module "$((Get-Location).Path)\src\WinCare\WinCare.psd1" -Force -Global }
  InModuleScope WinCare {
    BeforeEach {$script:WinCareState=@{Root=$TestDrive;Config=Get-WinCareDefaultConfig;Policy=Get-WinCareDefaultPolicy;SessionId='0123456789abcdef0123456789abcdef';IsAdmin=$false;Capabilities=@{}}}
    It 'loads unique validated built-in rules' { $rules=@(Get-WinCareCatalog -Refresh);$rules.Count|Should -BeGreaterThan 10;@($rules.id|Sort-Object -Unique).Count|Should -Be $rules.Count }
    It 'loads presets whose rule IDs resolve' { foreach($preset in Get-WinCarePreset){foreach($id in $preset.ruleIds){$id|Should -BeIn @((Get-WinCareCatalog).id)}} }
    It 'uses literal package patterns in removal profiles' { foreach($p in Get-WinCareAppRemovalProfile){foreach($pattern in $p.patterns){$pattern|Should -Match '^[A-Za-z0-9_.-]+$'}} }
    It 'runs action-level semantic validation for catalog changes' {
      $rule=@{id='test.bad-cleanup';title='Bad cleanup';description='';category='Cleanup';risk='Low';requiresAdmin=$false;reversible=$false;restartPossible=$false;tags=@('test');sourceRecords=@('TEST');compatibility=@{};changes=@(@{type='DeleteCleanupTarget';parameters=@{Id='temp-files';Name='Temp';Path=$TestDrive;Pattern='..\\*.tmp';MinimumAgeHours=1;Kind='Files'};verification='';preconditions=@();postconditions=@()});recovery='None'}
      {Test-WinCareCatalogRule $rule}|Should -Throw
    }
  }
}

Describe 'Cleanup target authority binding' {
  InModuleScope WinCare {
    BeforeEach {
      $script:WinCareState=@{Root=$TestDrive;Config=Get-WinCareDefaultConfig;Policy=Get-WinCareDefaultPolicy;SessionId='0123456789abcdef0123456789abcdef';IsAdmin=$false;Capabilities=@{}}
      $canonical=Join-Path $TestDrive 'canonical';$foreign=Join-Path $TestDrive 'foreign'
      $null=New-Item -ItemType Directory -Path $canonical,$foreign -Force
      Set-Content -LiteralPath (Join-Path $foreign 'must-remain.tmp') -Value 'protected'
      Mock Get-WinCareCleanupTarget { @([pscustomobject]@{Id='temp-files';Name='Temporary files';Path=$canonical;Pattern='*.tmp';Kind='Files';Risk='Low';RequiresAdmin=$false;MinimumAgeHours=0;EstimatedBytes=0;FileCount=0;Exists=$true}) }
    }
    It 'blocks a caller from substituting a foreign cleanup path' {
      $foreign=Join-Path $TestDrive 'foreign'
      $action=New-WinCareAction -Type DeleteCleanupTarget -Label Cleanup -Risk Low -Parameters @{Id='temp-files';Name='Temporary files';Path=$foreign;Pattern='*.tmp';MinimumAgeHours=0;Kind='Files'}
      $result=Invoke-WinCareCleanupAction $action
      $result.Success|Should -Be $false
      $result.Code|Should -Be 'CleanupTargetRejected'
      Test-Path -LiteralPath (Join-Path $foreign 'must-remain.tmp')|Should -Be $true
    }
    It 'accepts only the exact canonical cleanup definition' {
      $canonical=Join-Path $TestDrive 'canonical'
      Set-Content -LiteralPath (Join-Path $canonical 'eligible.tmp') -Value 'delete'
      (Get-Item -LiteralPath (Join-Path $canonical 'eligible.tmp')).LastWriteTime=(Get-Date).AddHours(-2)
      $action=New-WinCareAction -Type DeleteCleanupTarget -Label Cleanup -Risk Low -Parameters @{Id='temp-files';Name='Temporary files';Path=$canonical;Pattern='*.tmp';MinimumAgeHours=0;Kind='Files'}
      $result=Invoke-WinCareCleanupAction $action
      $result.Success|Should -Be $true
      Test-Path -LiteralPath (Join-Path $canonical 'eligible.tmp')|Should -Be $false
    }
  }
}

Describe 'Declarative extension authority' {
  InModuleScope WinCare {
    BeforeEach {
      $script:WinCareState=@{Root=$TestDrive;Config=Get-WinCareDefaultConfig;Policy=Get-WinCareDefaultPolicy;SessionId='0123456789abcdef0123456789abcdef';IsAdmin=$false;Capabilities=@{}}
      $script:WinCareState.Policy.AllowUnsignedExtensions=$true
      $script:WinCareState.Policy.TrustedExtensionSigners=@()
      $null=New-Item -ItemType Directory -Path (Join-Path $TestDrive 'Cache') -Force
    }
    It 'does not let an unsigned extension alter the action catalog' {
      $source=Join-Path $TestDrive 'catalog-extension';$null=New-Item -ItemType Directory -Path $source
      $catalogPath=Join-Path $source 'catalog.json'
      @{schemaVersion=1;rules=@()}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $catalogPath -Encoding utf8
      $entry=[ordered]@{path='catalog.json';sha256=(Get-WinCareSha256 $catalogPath);bytes=[long](Get-Item $catalogPath).Length}
      [ordered]@{schemaVersion=1;id='test.catalog';name='Catalog test';version=$script:WinCareVersion;publisher='Tests';description='';minimumWinCareVersion=$script:WinCareVersion;files=@($entry);catalogFiles=@('catalog.json');knowledgeFiles=@();sourceRecords=@('TEST')}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $source 'extension.json') -Encoding utf8
      $archive=Join-Path $TestDrive 'catalog-extension.zip';[IO.Compression.ZipFile]::CreateFromDirectory($source,$archive)
      {Test-WinCareExtensionPackage $archive}| Should -Throw
    }
  }
}

Describe 'Provisioning admission and stage separation' {
  InModuleScope WinCare {
    BeforeEach {$script:WinCareState=@{Root=$TestDrive;Config=Get-WinCareDefaultConfig;Policy=Get-WinCareDefaultPolicy;SessionId='0123456789abcdef0123456789abcdef';IsAdmin=$false;Capabilities=@{}}}
    It 'rejects wildcard AppX removals' { $b=@{schemaVersion=1;name='Bad';description='';catalogRules=@();removeInstalledAppx=@('Microsoft.*');removeProvisionedAppx=@();capabilities=@();optionalFeatures=@();wingetPackages=@();userStage=@();systemStage=@();sourceRecords=@()};{Test-WinCareProvisioningBlueprint $b}|Should -Throw }
    It 'separates system and user actions' {
      $b=@{schemaVersion=1;name='Stages';description='';catalogRules=@();removeInstalledAppx=@('Microsoft.Test_1.0.0.0_x64__8wekyb3d8bbwe');removeProvisionedAppx=@();capabilities=@();optionalFeatures=@();wingetPackages=@('Microsoft.PowerToys');userStage=@();systemStage=@();sourceRecords=@('TEST')}
      $system=New-WinCareProvisioningPlan $b -Stage System;$user=New-WinCareProvisioningPlan $b -Stage User
      @($system.Actions.Type) | Should -Not -Contain RemoveAppxPackage
      @($user.Actions.Type) | Should -Contain RemoveAppxPackage
      @($user.Actions.Type) | Should -Contain InstallWingetPackage
    }
  }
}

Describe 'Automation profile admission' {
  InModuleScope WinCare {
    It 'rejects apparent secrets and recursive automation' {
      $p=@{schemaVersion=1;id='daily.safe';title='Daily';command='system';arguments=@{token='secret'};apply=$false;schedule='daily@03:00';maximumRuntimeMinutes=30;batteryAllowed=$false;networkRequired=$false;notification='None';sourceRecords=@()}
      {Test-WinCareAutomationProfile $p}|Should -Throw
      $p.arguments=@{};$p.command='run-automation';{Test-WinCareAutomationProfile $p}|Should -Throw
    }
    It 'accepts a bounded read-only profile' {
      $p=@{schemaVersion=1;id='daily.report';title='Daily report';command='reports';arguments=@{};apply=$false;schedule='weekly:Sunday@03:00';maximumRuntimeMinutes=30;batteryAllowed=$false;networkRequired=$false;notification='None';sourceRecords=@('USER:daily.report')}
      Test-WinCareAutomationProfile $p|Should -Be $true
    }
  }
}
