
$modulePath = Join-Path $PSScriptRoot '..\src\WinCare\WinCare.psd1'
Import-Module $modulePath -Force

Describe 'Versioned action and plan contracts' {
  InModuleScope WinCare {
    BeforeEach { $script:WinCareState=@{ActionContracts=$null;Config=Get-WinCareDefaultConfig;Policy=Get-WinCareDefaultPolicy;Root=$TestDrive;SessionId='0123456789abcdef0123456789abcdef';IsAdmin=$false;Capabilities=@{}};$script:WinCareState.Remove('ActionContracts') }
    It 'has one unique contract for every dispatcher case' {
      $contracts=@((Get-WinCareActionContractTable).Keys)
      $contracts.Count | Should BeGreaterThan 40
      $contracts.Count | Should Be (@($contracts|Sort-Object -Unique).Count)
      $text=Get-Content (Join-Path $script:WinCareModuleRoot 'Core\09-Transactions.ps1') -Raw
      $dispatch=@([regex]::Matches($text,"(?m)^\s*'([^']+)'\s*\{Invoke-WinCare")|ForEach-Object{$_.Groups[1].Value})
      Compare-Object ($contracts|Sort-Object) ($dispatch|Sort-Object) | Should BeNullOrEmpty
    }
    It 'rejects unknown action fields' {
      $action=New-WinCareAction -Type ClearClipboard -Label Clear -Risk Moderate
      $action|Add-Member ExtraField bad
      (Test-WinCareActionContract $action).Success | Should Be $false
    }
    It 'enforces contract risk floors' {
      $action=New-WinCareAction -Type WuaUninstall -Label Remove -Risk Low -Parameters @{UpdateIds=@('11111111-1111-1111-1111-111111111111')} -RequiresAdmin $true
      (Test-WinCareActionContract $action).Success | Should Be $false
    }
    It 'allows only DNS flush through elevated generic process' {
      $ok=New-WinCareAction -Type RunProcess -Label DNS -Risk Moderate -Parameters @{FilePath='ipconfig.exe';Arguments=@('/flushdns')} -RequiresAdmin $true
      (Test-WinCareActionContract $ok -ForElevation).Success | Should Be $true
      $bad=New-WinCareAction -Type RunProcess -Label Shell -Risk Moderate -Parameters @{FilePath='cmd.exe';Arguments=@('/c','whoami')} -RequiresAdmin $true
      (Test-WinCareActionContract $bad -ForElevation).Success | Should Be $false
    }
    It 'rejects malformed cleanup action metadata before dispatch' {
      $badKind=New-WinCareAction -Type DeleteCleanupTarget -Label Cleanup -Risk Low -Parameters @{Id='temp-files';Name='Temp';Path=$TestDrive;Pattern='*.tmp';MinimumAgeHours=24;Kind='NativeDeliveryOptimization'}
      (Test-WinCareActionContract $badKind).Success | Should Be $false
      $badAge=New-WinCareAction -Type DeleteCleanupTarget -Label Cleanup -Risk Low -Parameters @{Id='temp-files';Name='Temp';Path=$TestDrive;Pattern='*.tmp';MinimumAgeHours=-1;Kind='Files'}
      (Test-WinCareActionContract $badAge).Success | Should Be $false
      $badPattern=New-WinCareAction -Type DeleteCleanupTarget -Label Cleanup -Risk Low -Parameters @{Id='temp-files';Name='Temp';Path=$TestDrive;Pattern='..\\*.tmp';MinimumAgeHours=24;Kind='Files'}
      (Test-WinCareActionContract $badPattern).Success | Should Be $false
    }
    It 'rejects duplicate action IDs in a plan' {
      $a=New-WinCareAction -Type ClearClipboard -Label One -Risk Moderate
      $b=New-WinCareAction -Type ClearClipboard -Label Two -Risk Moderate;$b.Id=$a.Id
      (Test-WinCarePlanContract (New-WinCarePlan -Title Test -Actions @($a,$b))).Success | Should Be $false
    }
    It 'carries restart requirements as structured result state' {
      $result=New-WinCareResult -Success $true -Message Done -RestartRequired $true
      $result.RestartRequired | Should Be $true
    }
    It 'returns a stable plan hash for equal content regardless of map insertion order' {
      $left=[ordered]@{Enable=$true;Previous=$false};$right=[ordered]@{Previous=$false;Enable=$true}
      $a=New-WinCareAction -Type SetStorageSense -Label Sense -Risk Moderate -Parameters $left -Reversible $true -Compensator @{Type='SetStorageSense';Parameters=@{Enable=$false;Previous=$true}}
      $b=New-WinCareAction -Type SetStorageSense -Label Sense -Risk Moderate -Parameters $right -Reversible $true -Compensator @{Parameters=@{Previous=$true;Enable=$false};Type='SetStorageSense'}
      $b.Id=$a.Id;$b.IdempotencyKey=$a.IdempotencyKey
      Get-WinCareActionStableHash $a|Should Be (Get-WinCareActionStableHash $b)
      $p=New-WinCarePlan -Title Test -Actions @($a);Get-WinCarePlanStableHash $p|Should Be (Get-WinCarePlanStableHash $p)
    }
    It 'derives the same idempotency key regardless of parameter insertion order' {
      $left=New-WinCareAction -Type SetStorageSense -Label Sense -Risk Moderate -Parameters ([ordered]@{Enable=$true;Previous=$false}) -Reversible $true -Compensator @{Type='SetStorageSense';Parameters=@{Enable=$false;Previous=$true}}
      $right=New-WinCareAction -Type SetStorageSense -Label Sense -Risk Moderate -Parameters ([ordered]@{Previous=$false;Enable=$true}) -Reversible $true -Compensator @{Type='SetStorageSense';Parameters=@{Enable=$false;Previous=$true}}
      $left.IdempotencyKey|Should Be $right.IdempotencyKey
    }
    It 'binds all execution and recovery fields into the action hash' {
      $a=New-WinCareAction -Type SetStorageSense -Label Sense -Risk Moderate -Parameters @{Enable=$true;Previous=$false} -Reversible $true -Compensator @{Type='SetStorageSense';Parameters=@{Enable=$false;Previous=$true}} -EstimatedBytes 1 -RecoveryDescription 'restore'
      $baseline=Get-WinCareActionStableHash $a
      $a.EstimatedBytes=2;Get-WinCareActionStableHash $a|Should Not -Be $baseline;$a.EstimatedBytes=1
      $a.RecoveryDescription='changed';Get-WinCareActionStableHash $a|Should Not -Be $baseline;$a.RecoveryDescription='restore'
      $a.Compensator.Parameters.Enable=$true;Get-WinCareActionStableHash $a|Should Not -Be $baseline
    }
    It 'requires an executable compensator for reversible actions' {
      $missing=New-WinCareAction -Type SetStorageSense -Label Sense -Risk Moderate -Parameters @{Enable=$true;Previous=$false} -Reversible $true
      (Test-WinCareActionContract $missing).Success|Should Be $false
      $valid=New-WinCareAction -Type SetStorageSense -Label Sense -Risk Moderate -Parameters @{Enable=$true;Previous=$false} -Reversible $true -Compensator @{Type='SetStorageSense';Parameters=@{Enable=$false;Previous=$true}}
      (Test-WinCareActionContract $valid).Success|Should Be $true
      $invalid=New-WinCareAction -Type SetStorageSense -Label Sense -Risk Moderate -Parameters @{Enable=$true;Previous=$false} -Reversible $false -Compensator @{Type='SetStorageSense';Parameters=@{Enable=$false;Previous=$true}}
      (Test-WinCareActionContract $invalid).Success|Should Be $false
    }
    It 'accepts only the approved provider-issued startup compensator' {
      $action=New-WinCareAction -Type DisableStartupItem -Label Disable -Risk Moderate -Parameters @{Id='x';Name='X';Command='x.exe';Source='Registry';Location='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run';Scope='User'} -Reversible $true
      (Test-WinCareActionContract $action).Success|Should Be $true
      $result=New-WinCareResult -Success $true -Message Done -Data @{Compensator=@{Type='EnableStartupItem';Parameters=@{Name='X';Scope='User';BackupPath=(Join-Path $TestDrive 'Backups\Startup\x.json')}}}
      $descriptor=Resolve-WinCareCompensatorDescriptor -Action $action -Result $result
      $descriptor.Type|Should Be 'EnableStartupItem'
    }
    It 'requires an exact recent preview before applying a modifying plan' {
      $action=New-WinCareAction -Type ClearClipboard -Label Clear -Risk Moderate
      $plan=New-WinCarePlan -Title PreviewTest -Actions @($action)
      $blocked=Invoke-WinCarePlan -Plan $plan -Approved
      $blocked.Success|Should Be $false
      $blocked.Code|Should Be 'PreviewRequired'
      $preview=Invoke-WinCarePlan -Plan $plan -PreviewOnly
      $preview.Success|Should Be $true
      Test-WinCarePlanPreviewReceipt -Plan $plan|Should Be $true
      $plan.Title='Mutated'
      Test-WinCarePlanPreviewReceipt -Plan $plan|Should Be $false
    }
  }
}

Describe 'Configuration and policy admission' {
  InModuleScope WinCare {
    It 'accepts only disabled telemetry' { $c=Get-WinCareDefaultConfig;$c.Telemetry='Enabled';{Test-WinCareConfigObject $c}|Should Throw }
    It 'rejects unknown policy fields' { $p=Get-WinCareDefaultPolicy;$p.Unknown=$true;{Test-WinCarePolicyObject $p}|Should Throw }
    It 'validates maintenance windows' { Test-WinCareMaintenanceWindow '23:00-02:00' | Should BeOfType bool;{Test-WinCareMaintenanceWindow '25:00-26:00'}|Should Throw }
    It 'rejects malformed palette history and non-Boolean safety controls' {
      $c=Get-WinCareDefaultConfig;$c.CommandPaletteHistory=@('Dashboard')*51;{Test-WinCareConfigObject $c}|Should Throw
      $c=Get-WinCareDefaultConfig;$c.RequirePreviewForChanges='yes';{Test-WinCareConfigObject $c}|Should Throw
      $p=Get-WinCareDefaultPolicy;$p.RequirePreview='yes';{Test-WinCarePolicyObject $p}|Should Throw
    }
    It 'verifies zero-trust config vault protection' {
      $vaultPath = Join-Path $TestDrive 'secure_vault.dat'
      'DPAPI_PROTECTED_DATA' | Set-Content -LiteralPath $vaultPath
      $res = Protect-WinCareConfigVault -ConfigVaultPath $vaultPath
      $res.ConfigVaultPath | Should Be (Resolve-WinCareCanonicalPath -LiteralPath $vaultPath)
      $res.ZeroTrustVaultEncrypted | Should Be $true
      $res.EvidenceType | Should Be 'ZeroTrustConfigVaultProtection'

      $plainPath = Join-Path $TestDrive 'plain_vault.dat'
      'UNENCRYPTED_DATA' | Set-Content -LiteralPath $plainPath
      $resPlain = Protect-WinCareConfigVault -ConfigVaultPath $plainPath
      $resPlain.ZeroTrustVaultEncrypted | Should Be $false
    }
    It 'evaluates RBAC role permissions and logs unauthorized action attempts' {
      $granted = Assert-WinCareRolePermission -RequestedRole 'HelpdeskAdmin' -ActionContractName 'ClearTemporaryFiles' -UserIdentity 'TestUser'
      $granted.Success | Should Be $true
      $granted.Code | Should Be 'RolePermissionGranted'

      $denied = Assert-WinCareRolePermission -RequestedRole 'HelpdeskAdmin' -ActionContractName 'DisableWdac' -UserIdentity 'TestUser'
      $denied.Success | Should Be $false
      $denied.Code | Should Be 'BlockedByPolicy'
      $denied.ExitCode | Should Be 78
    }
    It 'evaluates GPO Entra ID drift and synthesizes remediation plans' {
      $drift = Test-WinCareGpoEntraDrift
      $drift.EvidenceType | Should Be 'GpoAndAzureAdBaselineDrift'
      $plan = New-WinCareGpoRemediationPlan -DriftResult $drift
      $plan.EvidenceType | Should Be 'GpoRemediationPlanDefinition'
      $plan.PlanSha256.Length | Should Be 64
    }
  }
}

