

Describe 'Wave-four operations convergence contracts' {
    #requires -Version 7.2
BeforeAll { Import-Module "$((Get-Location).Path)\src\WinCare\WinCare.psd1" -Force -Global }
  InModuleScope WinCare {
    BeforeEach {
      $script:WinCareState=@{
        Config=Get-WinCareDefaultConfig
        Policy=Get-WinCareDefaultPolicy
        Root=$TestDrive
        SessionId='0123456789abcdef0123456789abcdef'
        IsAdmin=$false
        Capabilities=@{}
        ActionContracts=$null
      }
      foreach($name in @('Downloads','TelemetrySnapshots','Launcher','GameState','WorkspaceLayouts','Cache','Backups','Journal','Logs','Reports','Extensions')){
        New-Item -ItemType Directory -Path (Join-Path $TestDrive $name) -Force|Out-Null
      }
      New-Item -ItemType Directory -Path (Join-Path $TestDrive 'Downloads\Completed') -Force|Out-Null
      New-Item -ItemType Directory -Path (Join-Path $TestDrive 'Downloads\Partial') -Force|Out-Null
      $script:WinCareState.Policy.AllowInsecureDownloads=$false
      $script:WinCareState.Policy.AllowOfflineImageReduction=$false
      $script:WinCareState.Policy.AllowGameStateRestore=$false
    }

    It 'registers and dispatches every wave-four typed action' {
      $expected=@(
        'ReplaceDownloadStore','StartDownloadTransfer','SetDownloadTransferState','ReconcileDownloadTransfer','CancelDownloadTransfer',
        'CaptureTelemetrySeries','OpenLaunchTarget','CreateSteamCloudBackup','RemoveGameStateBackup','RestoreSteamCloudBackup',
        'OfflineImageRemoveProvisionedAppx','OfflineImageStartComponentCleanup','ReplaceWorkspaceLayoutStore'
      )
      $contracts=Get-WinCareActionContractTable
      foreach($type in $expected){$contracts.Keys|Should Contain $type}
      $dispatch=(Get-Command Invoke-WinCareActionInternal).ScriptBlock.ToString()
      foreach($type in $expected){$dispatch|Should Match ("'"+[regex]::Escape($type)+"'")}
    }

    It 'keeps sensitive wave-four policy denied by default' {
      $policy=Get-WinCareDefaultPolicy
      $policy.AllowInsecureDownloads|Should Be $false
      $policy.AllowOfflineImageReduction|Should Be $false
      $policy.AllowGameStateRestore|Should Be $false
      $policy.MaximumDownloadJobs|Should BeGreaterThan 0
      $policy.MaximumDownloadBytes|Should BeGreaterThan 1MB
    }

    It 'rejects insecure download URLs by default' {
      {Test-WinCareDownloadUri 'http://example.test/file.bin'}| Should Throw
      Test-WinCareDownloadUri 'https://example.test/file.bin'|Should Be 'https://example.test/file.bin'
    }

    It 'rejects durable sensitive download headers' {
      {Test-WinCareDownloadHeaderMap @{Authorization='Bearer secret'}}| Should Throw
      {Test-WinCareDownloadHeaderMap @{Cookie='secret=value'}}| Should Throw
      Test-WinCareDownloadHeaderMap @{'User-Agent'='WinCare test'}|Should Be $true
    }

    It 'builds a protected download queue action with exact recovery' {
      Mock Get-WinCareDownloadDestinationRoots { @((Join-Path $TestDrive 'Downloads\Completed')) }
      $job=New-WinCareDownloadJobRecord -Url @('https://example.test/file.bin') -Destination (Join-Path $TestDrive 'Downloads\Completed\file.bin') -Name 'file' -HashAlgorithm SHA256 -ExpectedHash ('a'*64)
      $plan=New-WinCareDownloadJobPlan -Job $job
      $plan.Actions[0].Type|Should Be ReplaceDownloadStore
      $plan.Actions[0].Compensator.Type|Should Be ReplaceDownloadStore
      (Test-WinCareActionContract $plan.Actions[0]).Success|Should Be $true
    }

    It 'requires runtime-bound cancellation for started downloads' {
      $action=New-WinCareAction -Type StartDownloadTransfer -Label Start -Risk Low -Parameters @{Id='0123456789abcdef0123456789abcdef';ExpectedRecordHash=('a'*64)} -Reversible $true
      (Test-WinCareActionContract $action).Success|Should Be $true
      $result=New-WinCareResult -Success $true -Data @{Compensator=@{Type='CancelDownloadTransfer';RequiresAdmin=$false;Parameters=@{Id=$action.Parameters.Id;ExpectedRecordHash=('b'*64)}}}
      (Resolve-WinCareCompensatorDescriptor -Action $action -Result $result).Type|Should Be CancelDownloadTransfer
    }

    It 'bounds queue size and download bytes through policy' {
      (Get-Command Test-WinCareDownloadStore).ScriptBlock.ToString()|Should Match 'MaximumDownloadJobs'
      (Get-Command Invoke-WinCareStartDownloadTransferAction).ScriptBlock.ToString()|Should Match 'MaximumDownloadBytes'
      (Get-Command Invoke-WinCareReconcileDownloadTransferAction).ScriptBlock.ToString()|Should Match 'MaximumDownloadBytes'
    }

    It 'enforces the strictest download byte and concurrency limits' {
      (Get-Command Get-WinCareDownloadByteLimit).ScriptBlock.ToString()|Should Match 'Min'
      (Get-Command Invoke-WinCareStartDownloadTransferAction).ScriptBlock.ToString()|Should Match 'DownloadConcurrencyLimit'
      (Get-Command New-WinCareDueDownloadStartPlan).ScriptBlock.ToString()|Should Match 'DownloadMaximumConcurrent'
    }

    It 'finalizes downloads through verified same-directory staging and tombstone rollback' {
      $source=(Get-Command Complete-WinCareDownloadedFile).ScriptBlock.ToString()
      $source|Should Match 'wincare-stage'
      $source|Should Match 'wincare-old'
      $source|Should Match 'Final download hash verification failed'
      $source|Should Match 'Existing destination rollback failed'
      $source|Should Not -Match 'Remove-Item[^
]*\$destination[^
]*Move-Item'
    }

    It 'uses BITS state transitions and exact content verification' {
      $source=(Get-Command Invoke-WinCareReconcileDownloadTransferAction).ScriptBlock.ToString()
      $source|Should Match 'Complete-BitsTransfer'
      $source|Should Match 'Get-FileHash'
      $source|Should Match 'Download hash mismatch'
      $source|Should Match 'ReparsePoint'
    }

    It 'keeps telemetry local, bounded, cancellable, and history-limited' {
      $capture=(Get-Command Get-WinCareTelemetrySeries).ScriptBlock.ToString()
      $capture|Should Match 'Test-WinCareOperationCancellationRequested'
      $capture|Should Match 'Maximum|3600'
      (Get-Command Invoke-WinCareCaptureTelemetrySeriesAction).ScriptBlock.ToString()|Should Match 'TelemetryHistoryLimit'
      (Get-Command New-WinCareTelemetryExportPlan).ScriptBlock.ToString()|Should Match 'one-MiB'
    }

    It 'uses formatted disk rates and strips private telemetry counters' {
      (Get-Command Get-WinCareDiskByteCounters).ScriptBlock.ToString()|Should Match 'Win32_PerfFormattedData_PerfDisk_PhysicalDisk'
      $raw=[pscustomobject]@{Timestamp=[datetime]::UtcNow.ToString('o');CpuPercent=1;MemoryUsedBytes=1;MemoryTotalBytes=2;MemoryPercent=50;DiskReadBytesPerSecond=3;DiskWriteBytesPerSecond=4;NetworkReceivedBytesPerSecond=5;NetworkSentBytesPerSecond=6;ProcessCount=1;ThreadCount=1;HandleCount=1;TopCpu=@();TopMemory=@();Alerts=@();_NetworkReceivedTotal=100}
      $public=ConvertTo-WinCarePublicTelemetrySample $raw
      $public.PSObject.Properties.Name|Should Not -Contain '_NetworkReceivedTotal'
    }

    It 'rejects malformed telemetry series' {
      $sample=[ordered]@{Timestamp=[datetime]::UtcNow.ToString('o');CpuPercent=10;MemoryUsedBytes=1;MemoryTotalBytes=2;MemoryPercent=50;DiskReadBytesPerSecond=0;DiskWriteBytesPerSecond=0;NetworkReceivedBytesPerSecond=0;NetworkSentBytesPerSecond=0;ProcessCount=1;ThreadCount=1;HandleCount=1;TopCpu=@();TopMemory=@();Alerts=@()}
      $series=[ordered]@{SchemaVersion=1;Id='0123456789abcdef0123456789abcdef';Name='test';StartedAt=[datetime]::UtcNow.ToString('o');CompletedAt=[datetime]::UtcNow.ToString('o');IntervalSeconds=1;DurationSeconds=1;Samples=@($sample);SourceRecords=@('test')}
      Test-WinCareTelemetrySeries $series|Should Be $true
      $series.Samples[0].CpuPercent=101
      {Test-WinCareTelemetrySeries $series}| Should Throw
    }

    It 'evaluates arithmetic without Invoke-Expression or a shell' {
      Invoke-WinCareCalculator '2 + 3 * 4'|Should Be 14
      Invoke-WinCareCalculator '-5 + (2 * 3)'|Should Be 1
      {Invoke-WinCareCalculator 'Get-Process'}| Should Throw
      (Get-Command Invoke-WinCareCalculator).ScriptBlock.ToString()|Should Not -Match 'Invoke-Expression|ScriptBlock::Create|cmd.exe'
    }

    It 'binds launch execution to a current indexed target hash' {
      $source=(Get-Command Invoke-WinCareOpenLaunchTargetAction).ScriptBlock.ToString()
      $source|Should Match 'LaunchTargetChanged'
      $source|Should Match 'ExpectedHash'
      $source|Should Not -Match 'cmd.exe|powershell.exe'
    }

    It 'bounds Start-menu traversal and rejects reparse points' {
      $source=(Get-Command Get-WinCareBoundedLaunchShortcutFile).ScriptBlock.ToString()
      $source|Should Match 'MaximumFiles'
      $source|Should Match 'MaximumDepth'
      $source|Should Match 'ReparsePoint'
      $source|Should Not -Match 'Get-ChildItem[^\r\n]*-Recurse'
    }

    It 'uses exact Steam file manifests and archive hashes' {
      $validator=(Get-Command Test-WinCareSteamBackupArchive).ScriptBlock.ToString()
      $validator|Should Match 'Unsafe Steam backup member'
      $validator|Should Match 'Duplicate Steam backup member'
      $validator|Should Match 'size mismatch'
      $writer=(Get-Command Write-WinCareSteamBackupArchive).ScriptBlock.ToString()
      $writer|Should Match 'Get-WinCareSha256'
      $writer|Should Match 'wincare-game-backup.json'
    }

    It 'rejects hidden Steam backup members and live-client restore races' {
      (Get-Command Test-WinCareSteamBackupArchive).ScriptBlock.ToString()|Should Match 'unlisted entry'
      (Get-Command Invoke-WinCareRestoreSteamCloudBackupAction).ScriptBlock.ToString()|Should Match 'SteamRunning'
      (Get-Command Invoke-WinCareRestoreSteamCloudBackupAction).ScriptBlock.ToString()|Should Match 'Get-WinCareSteamProcess'
    }

    It 'requires policy before Steam restore and performs provider-internal rollback' {
      $source=(Get-Command Invoke-WinCareRestoreSteamCloudBackupAction).ScriptBlock.ToString()
      $source|Should Match 'AllowGameStateRestore'
      $source|Should Match 'Restore-WinCareSteamBackupArchiveInternal'
      $source|Should Match 'ProviderRollback'
      $source|Should Match 'Get-WinCareSteamCloudSnapshotHash'
    }

    It 'never executes trainers, injectors, or cheat payloads' {
      $source=(Get-Command Get-WinCareGameIntegrityFinding).ScriptBlock.ToString()
      $source|Should Match 'trainer'
      $source|Should Match 'injector'
      $source|Should Match 'Get-AuthenticodeSignature'
      $source|Should Not -Match 'Start-Process|WriteProcessMemory|CreateRemoteThread|VirtualAllocEx'
      foreach($type in @('InjectProcess','CreateRemoteThread','WriteProcessMemory','InstallGlobalHook')){(Get-WinCareDefaultPolicy).DeniedActionTypes|Should Contain $type}
    }

    It 'offers only exact supported offline reduction operations' {
      $profiles=@(Get-WinCareOfflineReductionProfile)
      $profiles.Count|Should BeGreaterThan 0
      foreach($profile in $profiles){@($profile.AppxDisplayNames|Where-Object{$_ -match '[*?]'}).Count|Should Be 0}
      (Get-Command New-WinCareOfflineReductionPlan).ScriptBlock.ToString()|Should Match 'AllowOfflineImageReduction'
      (Get-Command Invoke-WinCareOfflineImageStartComponentCleanupAction).ScriptBlock.ToString()|Should Match 'ResetBaseDenied'
    }

    It 'validates immutable offline reduction profile schemas' {
      foreach($profile in @(Get-WinCareOfflineReductionProfile)){Test-WinCareOfflineReductionProfile $profile|Should Be $true}
      $bad=ConvertTo-WinCareParameterDictionary (Get-WinCareOfflineReductionProfile -Id 'nano-safe')
      $bad.AppxDisplayNames=@('*')
      {Test-WinCareOfflineReductionProfile $bad}| Should Throw
    }

    It 'marks provisioned AppX removal as non-reversible' {
      $source=(Get-Command New-WinCareOfflineReductionPlan).ScriptBlock.ToString()
      $source|Should Match 'OfflineImageRemoveProvisionedAppx'
      $source|Should Match 'Reversible \$false'
      $source|Should Match 'not automatically reversible'
    }

    It 'validates workspace layout ratios and unique slots' {
      $layout=New-WinCareWorkspaceLayoutRecord -Name Test -Slot @(
        @{ProcessName='notepad';LeftRatio=0;TopRatio=0;WidthRatio=.5;HeightRatio=1;Required=$true},
        @{ProcessName='explorer';LeftRatio=.5;TopRatio=0;WidthRatio=.5;HeightRatio=1;Required=$true}
      )
      Test-WinCareWorkspaceLayoutRecord $layout|Should Be $true
      $layout.Slots[0].WidthRatio=1.1
      {Test-WinCareWorkspaceLayoutRecord $layout}| Should Throw
    }

    It 'bounds workspace title regex execution' {
      New-WinCareWorkspaceTitleRegex '^Notepad'|Should Not BeNullOrEmpty
      {New-WinCareWorkspaceTitleRegex '('}| Should Throw
      (Get-Command New-WinCareWorkspaceTitleRegex).ScriptBlock.ToString()|Should Match 'FromMilliseconds\(100\)'
      (Get-Command New-WinCareWorkspaceLayoutApplyPlan).ScriptBlock.ToString()|Should Match 'Test-WinCareWorkspaceTitleMatch'
    }

    It 'uses CAS for saved layout catalog mutations' {
      $source=(Get-Command Invoke-WinCareReplaceWorkspaceLayoutStoreAction).ScriptBlock.ToString()
      $source|Should Match 'LayoutStoreChanged'
      $source|Should Match 'ExpectedCurrentHash'
      $planSource=(Get-Command New-WinCareWorkspaceLayoutSavePlan).ScriptBlock.ToString()
      $planSource|Should Match 'Compensator'
    }

    It 'binds applied layouts to exact windows and monitor work areas' {
      $source=(Get-Command New-WinCareWorkspaceLayoutApplyPlan).ScriptBlock.ToString()
      $source|Should Match 'ProcessStartTime'
      $source|Should Match 'ExpectedTitleHash'
      $source|Should Match 'ExpectedMonitorWork'
      $source|Should Match 'SetWindowBounds'
    }

    It 'routes every wave-four workspace and headless command' {
      $actions=@(Get-WinCareMainMenuItems|ForEach-Object Action)
      foreach($action in @('Downloads','Telemetry','Launcher','Layouts','GameState')){$actions|Should Contain $action}
      $commands=Get-WinCareHeadlessCommandName
      foreach($command in @('downloads','download-create','telemetry-capture','launcher-search','calculator','steam-backup','steam-restore','offline-reduction-apply','workspace-layout-apply')){$commands|Should Contain $command}
    }

    It 'keeps every wave-four mutation outside the read-only broker' {
      $blocked=@('download-create','download-start','download-start-due','download-suspend','download-resume','download-reconcile','download-cancel','download-remove','telemetry-capture','telemetry-export','launcher-open','steam-backup','steam-restore','offline-reduction-apply','workspace-layout-save','workspace-layout-apply','workspace-layout-remove')
      foreach($command in $blocked){Get-WinCareBrokerReadOnlyCommandName|Should Not -Contain $command}
      foreach($command in @('downloads','downloads-due','telemetry-snapshot','telemetry-history','launcher-search','calculator','steam-games','steam-users','steam-cloud-files','game-integrity','offline-reduction-profiles','workspace-layouts')){Get-WinCareBrokerReadOnlyCommandName|Should Contain $command}
    }
  }
}
