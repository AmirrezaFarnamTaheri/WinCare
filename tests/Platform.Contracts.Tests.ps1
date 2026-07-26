
Describe 'Platform integration contracts' {
    #requires -Version 7.2
BeforeAll { Import-Module "$((Get-Location).Path)\src\WinCare\WinCare.psd1" -Force -Global }
  InModuleScope WinCare {
    BeforeEach {
      $script:WinCareState=@{Config=Get-WinCareDefaultConfig;Policy=Get-WinCareDefaultPolicy;Root=$TestDrive;SessionId='0123456789abcdef0123456789abcdef';IsAdmin=$false;Capabilities=@{};ActionContracts=$null}
      foreach($name in @('Maintenance','Metrics','Widgets','Cache','Backups','Journal','Customization','Images')){New-Item -ItemType Directory -Path (Join-Path $TestDrive $name) -Force|Out-Null}
    }
    It 'loads every strict widget definition and rejects executable providers' {
      $widgets=@(Get-WinCareWidgetDefinition)
      $widgets.Count|Should -BeGreaterOrEqual 8
      foreach($widget in $widgets){Test-WinCareWidgetDefinition $widget|Should Be $true}
      $bad=@{id='bad-widget';title='Bad';description='';provider='PowerShellScript';minimumRefreshSeconds=1;sourceRecords=@('TEST')}
      {Test-WinCareWidgetDefinition $bad}| Should Throw
    }
    It 'classifies Bluetooth devices without driver mutation' {
      Get-WinCareBluetoothDeviceKind -Name 'AirPods Pro' -Manufacturer Apple|Should Be AppleAudio
      Get-WinCareBluetoothDeviceKind -Name 'Mechanical Keyboard' -Manufacturer Vendor|Should Be Keyboard
      Get-WinCareHeadlessCommandName|Should Contain bluetooth
      Get-WinCareHeadlessCommandName|Should Not -Contain bluetooth-pair
    }
    It 'enforces closed maintenance transitions and protected storage' {
      Test-WinCareMaintenanceTransition Scheduled Notice|Should Be $true
      Test-WinCareMaintenanceTransition Notice Completed|Should Be $false
      Test-WinCareMaintenanceTransition Completed Active|Should Be $false
      $record=New-WinCareMaintenanceWindowRecord -Name Test -StartAt ([datetimeoffset]::Now.AddHours(1)) -EndAt ([datetimeoffset]::Now.AddHours(2))
      Test-WinCareMaintenanceRecord $record|Should Be $true
      (New-WinCareMaintenanceUpsertPlan $record).Actions[0].Type|Should Be UpsertMaintenanceWindow
    }
    It 'uses data-only playbooks that compose existing plans' {
      $playbooks=@(Get-WinCarePlaybook);$playbooks.Count|Should BeGreaterThan 3
      foreach($playbook in $playbooks){Test-WinCarePlaybookDefinition $playbook|Should Be $true}
      $bad=@{schemaVersion=1;id='bad-playbook';title='Bad';description='';risk='High';minimumBuild=0;maximumBuild=0;steps=@(@{type='script';id='Invoke-Expression'});sourceRecords=@('TEST')}
      {Test-WinCarePlaybookDefinition $bad}|Should Throw
    }
    It 'keeps context-menu mutations inside approved registry roots' {
      foreach($rule in @(Get-WinCareContextMenuRule)){Test-WinCareContextMenuRule $rule|Should Be $true}
      $bad=@{id='bad.rule';title='Bad';description='';risk='High';requiresAdmin=$true;minimumBuild=0;maximumBuild=0;requiredExecutables=@();sourceRecords=@('TEST');trees=@(@{path='HKLM:\SYSTEM\CurrentControlSet\Services';values=@();deleteValues=@();deleteSubKeys=@()})}
      {Test-WinCareContextMenuRule $bad}| Should Throw
    }
    It 'blocks offline servicing under safe default policy' {
      (Get-WinCareDefaultPolicy).AllowOfflineImageServicing|Should Be $false
      {Test-WinCareOfflineImagePolicy}| Should Throw
    }
    It 'requires Microsoft signatures for LGPO and Sysmon pathways' {
      Get-WinCareHeadlessCommandName|Should Contain group-policy-import
      Get-WinCareHeadlessCommandName|Should Contain sysmon-configure
      $contracts=Get-WinCareActionContractTable
      $contracts.Keys|Should Contain ImportLocalGroupPolicyBackup
      $contracts.Keys|Should Contain ConfigureSysmon
      $contracts.Keys|Should Contain UninstallSysmon
    }
    It 'has no state-changing cumulative command in the read-only broker list' {
      $blocked=@('widget-export','maintenance-create','maintenance-transition','maintenance-export','context-menu-set','playbook','group-policy-import','sysmon-configure','sysmon-uninstall','offline-driver-add','offline-driver-remove','offline-package-add','offline-feature-set')
      foreach($command in $blocked){Get-WinCareBrokerReadOnlyCommandName|Should Not -Contain $command}
    }
    It 'routes all cumulative product workspaces' {
      $actions=@(Get-WinCareMainMenuItems|ForEach-Object Action)
      foreach($required in @('Customization','Widgets','Bluetooth','Baseline','Offline','Maintenance','Playbooks')){$actions|Should Contain $required}
    }
    It 'uses compare-and-swap and exact dynamic recovery for maintenance mutations' {
      $record=New-WinCareMaintenanceWindowRecord -Name 'Rollback test' -StartAt ([datetimeoffset]::Now.AddHours(1)) -EndAt ([datetimeoffset]::Now.AddHours(2))
      $upsert=New-WinCareMaintenanceUpsertPlan $record
      $upsert.Actions[0].Parameters.ExpectedRevision|Should Be 0
      $upsert.Actions[0].Parameters.ExpectedRecordHash|Should Be absent
      $upsertResult=Invoke-WinCareUpsertMaintenanceWindowAction -Action $upsert.Actions[0]
      $upsertResult.Success|Should Be $true
      $upsertResult.Data.Compensator.Type|Should Be RestoreMaintenanceWindowRecord
      $upsertResult.Data.Compensator.Parameters.ExpectedCurrentHash|Should Match '^[a-f0-9]{64}$'
      $transition=New-WinCareMaintenanceTransitionPlan -Id $record.Id -State Notice
      $transition.Actions[0].Parameters.ExpectedRevision|Should Be 1
      $transition.Actions[0].Parameters.ExpectedRecordHash|Should Match '^[a-f0-9]{64}$'
      $transitionResult=Invoke-WinCareSetMaintenanceWindowStateAction -Action $transition.Actions[0]
      $transitionResult.Success|Should Be $true
      $transitionResult.Data.Compensator.Type|Should Be RestoreMaintenanceWindowRecord
      $transitionResult.Data.Compensator.Parameters.Record.State|Should Be Scheduled
      $stale=Invoke-WinCareSetMaintenanceWindowStateAction -Action $transition.Actions[0]
      $stale.Success|Should Be $false
      $stale.Code|Should Be MaintenanceStoreChanged
    }

    It 'binds registry tree changes to exact snapshots and stale-safe restoration' {
      $contracts=Get-WinCareActionContractTable
      $contracts.ApplyRegistryTree.Required|Should Contain ExpectedSnapshotHash
      $contracts.RestoreRegistryTree.Required|Should Contain ExpectedCurrentHash
      (Get-Command Invoke-WinCareApplyRegistryTreeAction).ScriptBlock.ToString()|Should Match 'RegistryTreeChanged'
      (Get-Command Invoke-WinCareRestoreRegistryTreeAction).ScriptBlock.ToString()|Should Match 'RegistryTreeRecoveryConflict'
    }
    It 'uses correct widget paths and bounded Bluetooth payloads' {
      $widgetBody=(Get-Command New-WinCareWidgetSnapshotExportPlan).ScriptBlock.ToString()
      $widgetBody|Should Match 'Test-WinCarePathWithin -Child \$full -Parent \$root -AllowEqual'
      (Get-Command Get-WinCareWidgetSnapshot).ScriptBlock.ToString()|Should Match 'Sort-Object StartAt'
      $bluetoothBody=(Get-Command Get-WinCareBluetoothDevice).ScriptBlock.ToString()
      $bluetoothBody|Should Match 'Select-Object -First 512'
      (Get-Command Get-WinCareBluetoothEvent).ScriptBlock.ToString()|Should Match 'Substring\(0,4096\)'
    }
    It 'self-rolls back partial Sysmon and offline servicing failures' {
      $sysmon=(Get-Command Invoke-WinCareConfigureSysmonAction).ScriptBlock.ToString()
      $sysmon|Should Match 'SysmonConfigurationFailedRolledBack'
      $sysmon|Should Match 'SysmonConfigurationRollbackFailed'
      $driver=(Get-Command Invoke-WinCareOfflineImageAddDriverAction).ScriptBlock.ToString()
      $driver|Should Match 'OfflineDriverAddFailedRolledBack'
      $driver|Should Match 'OfflineDriverAddRollbackFailed'
      $feature=(Get-Command Invoke-WinCareOfflineImageSetFeatureAction).ScriptBlock.ToString()
      $feature|Should Match 'OfflineFeatureChangeFailedRolledBack'
      $feature|Should Match 'OfflineFeatureChangeRollbackFailed'
    }
    It 'requires exact LGPO refresh and store-hash rollback verification' {
      $body=(Get-Command Invoke-WinCareImportLocalGroupPolicyBackupAction).ScriptBlock.ToString()
      $body|Should Match 'rollbackRefresh.Success'
      $body|Should Match 'rollbackState.Sha256'
      $body|Should Match 'before.Sha256'
    }
    It 'contains provider-internal rollback for LGPO mutation failures' {
      $body=(Get-Command Invoke-WinCareImportLocalGroupPolicyBackupAction).ScriptBlock.ToString()
      $body|Should Match 'LocalGroupPolicyImportFailedRolledBack'
      $body|Should Match 'LocalGroupPolicyImportRollbackFailed'
      $body|Should Match 'ArgumentList @\(''/g'',\$captured\.Path\)'
    }
    It 'requires healthy read-write mounted images for mutation' {
      $body=(Get-Command Resolve-WinCareMountedImagePath).ScriptBlock.ToString()
      $body|Should Match 'MountStatus.*-ne.*Ok'
      $body|Should Match 'RequireReadWrite'
      $body|Should Match 'MountMode.*ReadWrite'
    }
    It 'uses staged verified managed-file replacement with internal rollback' {
      $body=(Get-Command Invoke-WinCareWriteManagedFileAction).ScriptBlock.ToString()
      $body|Should Match 'WriteThrough'
      $body|Should Match 'File\]::Replace'
      $body|Should Match 'ManagedFileWriteFailedRolledBack'
      $body|Should Match 'ManagedFileWriteRollbackFailed'
    }
  }
}
