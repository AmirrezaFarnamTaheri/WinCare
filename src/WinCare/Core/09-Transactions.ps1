function Get-WinCareRiskRank {
    param([string]$Risk)
    switch ($Risk) {'ReadOnly'{0};'Low'{1};'Moderate'{2};'High'{3};'Critical'{4};default{99}}
}


function Get-WinCareActionStableHash {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Action)
    $stable=[ordered]@{
        SchemaVersion=$Action.SchemaVersion;Id=$Action.Id;Type=$Action.Type;Label=$Action.Label;Risk=$Action.Risk;Parameters=$Action.Parameters
        RequiresAdmin=$Action.RequiresAdmin;Reversible=$Action.Reversible;EstimatedBytes=$Action.EstimatedBytes;Verification=$Action.Verification
        Preconditions=@($Action.Preconditions);Postconditions=@($Action.Postconditions);Compensator=$Action.Compensator;TimeoutSeconds=$Action.TimeoutSeconds
        IdempotencyKey=$Action.IdempotencyKey;Tags=@($Action.Tags);Compatibility=$Action.Compatibility;SourceRecords=@($Action.SourceRecords)
        RecoveryDescription=$Action.RecoveryDescription;RestartPossible=$Action.RestartPossible
    }
    Get-WinCareSha256Text -Text (ConvertTo-WinCareCanonicalJson -InputObject $stable -Depth 40)
}

function Get-WinCareOperationReceiptStableHash {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Receipt)
    $stable=[ordered]@{
        SchemaVersion=$Receipt.SchemaVersion;OperationId=$Receipt.OperationId;PlanHash=$Receipt.PlanHash;RecordHash=$Receipt.RecordHash;ModuleTreeHash=$Receipt.ModuleTreeHash
        FinalState=$Receipt.FinalState;Success=$Receipt.Success;StartedAt=$Receipt.StartedAt;FinishedAt=$Receipt.FinishedAt;LastEventHash=$Receipt.LastEventHash
        ResultCount=$Receipt.ResultCount;WarningCount=$Receipt.WarningCount
    }
    Get-WinCareSha256Text -Text (ConvertTo-WinCareCanonicalJson -InputObject $stable -Depth 30)
}

function Enter-WinCareOperationLock {
    [CmdletBinding()]param([ValidateRange(0,300)][int]$WaitSeconds=0)
    $journalRoot=Join-Path $script:WinCareState.Root 'Journal';$null=New-Item -ItemType Directory -Path $journalRoot -Force
    $path=Join-Path $journalRoot '.operation.lock';$deadline=[datetime]::UtcNow.AddSeconds($WaitSeconds)
    do{
        try{return [IO.File]::Open($path,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch [IO.IOException]{if([datetime]::UtcNow -ge $deadline){throw 'Another WinCare modifying operation is already active.'};Start-Sleep -Milliseconds 250}
    }while($true)
}

function New-WinCareOperationJournal {
    param([object]$Plan,[string]$OperationId)
    $root=Join-Path (Join-Path $script:WinCareState.Root 'Journal') $OperationId
    if(Test-Path -LiteralPath $root){throw "Operation journal already exists: $OperationId"}
    $null=New-Item -ItemType Directory -Path $root
    $record=[ordered]@{
        SchemaVersion=4;OperationId=$OperationId;SessionId=$script:WinCareState.SessionId;PlanId=$Plan.Id;PlanHash=Get-WinCarePlanStableHash $Plan
        Title=$Plan.Title;Description=$Plan.Description;State='Planned';Revision=0;StartedAt=[datetime]::UtcNow.ToString('o')
        FinishedAt=$null;Success=$null;RollbackState='NotRequired';ModuleTreeHash=Get-WinCareModuleTreeHash
        Plan=$Plan;Results=@();Warnings=@();SourceRecords=@($Plan.SourceRecords);LastEventHash=('0'*64);ReceiptHash=$null
    }
    $journal=[pscustomobject]@{Root=$root;Record=$record;RecordPath=(Join-Path $root 'operation.json');EventsPath=(Join-Path $root 'events.jsonl');CancelPath=(Join-Path $root 'cancel')}
    Write-WinCareAtomicJson -LiteralPath $journal.RecordPath -InputObject $record
    Update-WinCareOperationJournal -Journal $journal -State 'Planned' -Event 'PlanCreated' -Patch @{PlanHash=$record.PlanHash}
    return $journal
}

function Add-WinCareJournalEvent {
    param([Parameter(Mandatory)][object]$Journal,[Parameter(Mandatory)][string]$Event,[Parameter(Mandatory)][string]$State,[hashtable]$Data=@{})
    $previous=[string]$Journal.Record.LastEventHash
    $body=[ordered]@{schemaVersion=2;operationId=$Journal.Record.OperationId;revision=[int]$Journal.Record.Revision;timestamp=[datetime]::UtcNow.ToString('o');event=$Event;state=$State;previousEventHash=$previous;data=$Data}
    $bodyJson=ConvertTo-WinCareCanonicalJson -InputObject $body -Depth 40;$hash=Get-WinCareSha256Text -Text ($previous+"`n"+$bodyJson)
    $eventRecord=[ordered]@{};foreach($key in $body.Keys){$eventRecord[$key]=$body[$key]};$eventRecord.eventHash=$hash
    $line=$eventRecord|ConvertTo-Json -Compress -Depth 40
    $stream=[IO.File]::Open($Journal.EventsPath,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try{$bytes=[Text.Encoding]::UTF8.GetBytes($line+"`n");$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)} finally {$stream.Dispose()}
    $Journal.Record.LastEventHash=$hash
    return $hash
}

function Update-WinCareOperationJournal {
    param([Parameter(Mandatory)][object]$Journal,[Parameter(Mandatory)][string]$State,[hashtable]$Patch=@{},[string]$Event='StateChanged')
    $record=$Journal.Record;$record.State=$State;$record.Revision=[int]$record.Revision+1
    foreach($key in $Patch.Keys){$record[$key]=$Patch[$key]}
    $null=Add-WinCareJournalEvent -Journal $Journal -Event $Event -State $State -Data $Patch
    Write-WinCareAtomicJson -LiteralPath $Journal.RecordPath -InputObject $record
}

function Repair-WinCareInterruptedOperation {
    [CmdletBinding()]param()
    $root=Join-Path $script:WinCareState.Root 'Journal';if(-not(Test-Path -LiteralPath $root)){return @()}
    $repaired=[Collections.Generic.List[object]]::new()
    foreach($directory in Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue){
        $path=Join-Path $directory.FullName 'operation.json';if(-not(Test-Path -LiteralPath $path)){continue}
        try{
            $record=Read-WinCareBoundedJson -LiteralPath $path -MaximumBytes 16777216 -Depth 50 -AsHashtable
            if([string]$record.State -notin @('Authorized','Running','RollingBack')){continue}
            $integrity=Test-WinCareOperationJournalIntegrity -OperationPath $directory.FullName
            if(-not $integrity.Valid){throw "Active journal failed pre-reconciliation integrity validation: $($integrity.Error)"}
            $prior=[string]$record.State
            $journal=[pscustomobject]@{Root=$directory.FullName;Record=$record;RecordPath=$path;EventsPath=(Join-Path $directory.FullName 'events.jsonl');CancelPath=(Join-Path $directory.FullName 'cancel')}
            Update-WinCareOperationJournal -Journal $journal -State 'Interrupted' -Patch @{FinishedAt=[datetime]::UtcNow.ToString('o');Success=$false;Warnings=@($record.Warnings)+"Operation was interrupted while in state $prior.";CurrentActionId=$null;CurrentActionType=$null;CurrentActionHash=$null} -Event 'OperationInterrupted'
            $null=Complete-WinCareOperationReceipt -Journal $journal
            $post=Test-WinCareOperationJournalIntegrity -OperationPath $directory.FullName
            if(-not $post.Valid){throw "Reconciled journal failed integrity validation: $($post.Error)"}
            $repaired.Add([pscustomobject]@{OperationId=$record.OperationId;PriorState=$prior;Path=$path})
        }catch{Write-WinCareLog -Level Warning -Message 'Could not reconcile an interrupted operation journal.' -Data @{path=$path;error=$_.Exception.Message}}
    }
    @($repaired)
}

function Request-WinCareOperationCancellation {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$OperationId)
    if($OperationId -notmatch '^[a-f0-9]{32}$'){return New-WinCareResult -Success $false -Status Blocked -Code 'InvalidOperationId' -Message 'Invalid operation ID.' -ExitCode 22}
    $journalRoot=Join-Path $script:WinCareState.Root 'Journal'
    $operationRoot=Assert-WinCareSafePath -LiteralPath (Join-Path $journalRoot $OperationId) -AllowedRoots @($journalRoot)
    $recordPath=Join-Path $operationRoot 'operation.json'
    if(-not(Test-Path -LiteralPath $recordPath -PathType Leaf)){return New-WinCareResult -Success $false -Status Failed -Code 'OperationNotFound' -Message 'The operation journal was not found.' -ExitCode 2}
    try{$record=Read-WinCareBoundedJson -LiteralPath $recordPath -MaximumBytes 16777216 -Depth 50 -AsHashtable}catch{return New-WinCareResult -Success $false -Status Failed -Code 'OperationJournalInvalid' -Message $_.Exception.Message -ExitCode 74}
    if([string]$record.OperationId -ne $OperationId -or [string]$record.State -notin @('Authorized','Running','RollingBack')){return New-WinCareResult -Success $false -Status Blocked -Code 'OperationNotCancellable' -Message "Operation state is not cancellable: $($record.State)" -ExitCode 5}
    $path=Assert-WinCareSafePath -LiteralPath (Join-Path $operationRoot 'cancel') -AllowedRoots @($journalRoot) -AllowMissing
    try { $stream=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read); $bytes=[Text.Encoding]::ASCII.GetBytes([datetime]::UtcNow.ToString("o")); $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true); $stream.Dispose() }
    catch [System.IO.IOException]{return New-WinCareResult -Success $true -Status Succeeded -Code 'CancellationAlreadyRequested' -Message 'Cancellation was already requested.' -Data @{OperationId=$OperationId;CancelPath=$path}}
    New-WinCareResult -Success $true -Status Succeeded -Code 'CancellationRequested' -Message 'Cancellation will be observed at the next safe checkpoint.' -Data @{OperationId=$OperationId;CancelPath=$path}
}

function Test-WinCareOperationCancellationRequested {
    param([string]$OperationId)
    if([string]::IsNullOrWhiteSpace($OperationId) -or $OperationId -notmatch '^[a-f0-9]{32}$'){return $false}
    $journalRoot=Join-Path $script:WinCareState.Root 'Journal'
    $operationRoot=Join-Path $journalRoot $OperationId
    if(-not(Test-WinCarePathWithin -Child $operationRoot -Parent $journalRoot)){return $false}
    Test-Path -LiteralPath (Join-Path $operationRoot 'cancel') -PathType Leaf
}

function Test-WinCareActionConditions {
    param([object[]]$Conditions,[hashtable]$Context=@{})
    foreach($condition in @($Conditions)){if(-not(Test-WinCareCondition -Condition $condition -Context $Context)){$message=if($condition.Message){[string]$condition.Message}else{"Condition failed: $($condition.Kind)"};return New-WinCareResult -Success $false -Status Blocked -Code 'ConditionFailed' -Message $message -ExitCode 5}}
    New-WinCareResult -Success $true -Message 'Conditions passed.'
}

function Get-WinCareIdempotencyPath {param([string]$Key);Join-Path (Join-Path $script:WinCareState.Root 'Journal') ("idempotency-{0}.json" -f $Key)}

function Test-WinCareActionAlreadyApplied {
    param([object]$Action)
    if($Action.Risk -eq 'ReadOnly' -or @($Action.Tags)-notcontains 'Idempotent' -or @($Action.Postconditions).Count -eq 0){return $false}
    $path=Get-WinCareIdempotencyPath $Action.IdempotencyKey;if(-not(Test-Path -LiteralPath $path)){return $false}
    try{$receipt=Read-WinCareProtectedJson -LiteralPath $path -Purpose 'WinCare.IdempotencyReceipt';if([string]$receipt.ActionHash -ne (Get-WinCareActionStableHash $Action)){return $false};$post=Test-WinCareActionConditions -Conditions @($Action.Postconditions);return $post.Success}catch{Write-WinCareLog -Level Warning -Message 'Ignoring an invalid idempotency receipt.' -Data @{path=$path;error=$_.Exception.Message};return $false}
}

function Set-WinCareActionApplied {
    param([object]$Action,[string]$OperationId,[object]$Result)
    if($Action.Risk -eq 'ReadOnly' -or @($Action.Tags)-notcontains 'Idempotent'){return}
    $receipt=[ordered]@{SchemaVersion=2;Key=$Action.IdempotencyKey;ActionType=$Action.Type;ActionHash=Get-WinCareActionStableHash $Action;OperationId=$OperationId;AppliedAt=[datetime]::UtcNow.ToString('o');ResultCode=$Result.Code;ModuleTreeHash=Get-WinCareModuleTreeHash}
    Write-WinCareProtectedJson -LiteralPath (Get-WinCareIdempotencyPath $Action.IdempotencyKey) -InputObject $receipt -Purpose 'WinCare.IdempotencyReceipt'
}

function Invoke-WinCareActionInternal { [CmdletBinding()]param([Parameter(Mandatory)][object]$Action,[Parameter(Mandatory)][string]$OperationId)
    switch([string]$Action.Type){
        'UninstallRegistryApp'{Invoke-WinCareRegistryUninstallAction -Action $Action}
        'RemoveAppxPackage'{Invoke-WinCareAppxRemoveAction -Action $Action}
        'RemoveAppxPackageAllUsers'{Invoke-WinCareRemoveAppxPackageAllUsersAction -Action $Action}
        'ResetAppxPackage'{Invoke-WinCareAppxResetAction -Action $Action}
        'RepairApplication'{Invoke-WinCareRepairAction -Action $Action}
        'DeleteCleanupTarget'{Invoke-WinCareCleanupAction -Action $Action}
        'ClearDeliveryOptimization'{Invoke-WinCareDeliveryOptimizationCleanupAction -Action $Action}
        'ClearWindowsUpdateDownloadCache'{Invoke-WinCareClearWindowsUpdateDownloadCacheAction -Action $Action}
        'StartComponentCleanup'{Invoke-WinCareStartComponentCleanupAction -Action $Action}
        'ClearRecycleBin'{Invoke-WinCareRecycleBinAction -Action $Action}
        'ResetStoreCache'{Invoke-WinCareStoreCacheAction -Action $Action}
        'SetStorageSense'{Invoke-WinCareStorageSenseAction -Action $Action}
        'DisableStartupItem'{Invoke-WinCareDisableStartupAction -Action $Action}
        'EnableStartupItem'{Invoke-WinCareEnableStartupAction -Action $Action}
        'RunHealthCommand'{Invoke-WinCareHealthAction -Action $Action}
        'RunDefenderScan'{Invoke-WinCareDefenderScanAction -Action $Action}
        'UpdateDefenderSignatures'{Invoke-WinCareDefenderSignatureUpdateAction -Action $Action}
        'UpgradeWingetPackage'{Invoke-WinCareWingetUpgradeAction -Action $Action}
        'InstallWingetPackage'{Invoke-WinCareWingetInstallAction -Action $Action}
        'MoveFile'{Invoke-WinCareMoveFileAction -Action $Action}
        'DeleteAdmittedCleanupFiles'{Invoke-WinCareDeleteAdmittedCleanupFilesAction -Action $Action}
        'RelocateDirectoryWithJunction'{Invoke-WinCareRelocateDirectoryAction -Action $Action}
        'RestoreRelocatedDirectory'{Invoke-WinCareRestoreRelocatedDirectoryAction -Action $Action}
        'RunProcess'{Invoke-WinCareGenericProcessAction -Action $Action}
        'SetRegistryValue'{Invoke-WinCareSetRegistryValueAction -Action $Action}
        'RemoveRegistryValue'{Invoke-WinCareRemoveRegistryValueAction -Action $Action}
        'ApplyRegistryTree'{Invoke-WinCareApplyRegistryTreeAction -Action $Action}
        'RemoveRegistryTree'{Invoke-WinCareRemoveRegistryTreeAction -Action $Action}
        'RestoreRegistryTree'{Invoke-WinCareRestoreRegistryTreeAction -Action $Action}
        'SetServiceStartMode'{Invoke-WinCareServiceStartModeAction -Action $Action}
        'SetServiceRuntimeState'{Invoke-WinCareServiceRuntimeStateAction -Action $Action}
        'SetScheduledTaskState'{Invoke-WinCareScheduledTaskStateAction -Action $Action}
        'SetOptionalFeatureState'{Invoke-WinCareOptionalFeatureAction -Action $Action}
        'SetCapabilityState'{Invoke-WinCareCapabilityAction -Action $Action}
        'RemoveProvisionedAppx'{Invoke-WinCareProvisionedAppxAction -Action $Action}
        'SetPowerScheme'{Invoke-WinCarePowerSchemeAction -Action $Action}
        'SetHostsEntries'{Invoke-WinCareHostsEntriesAction -Action $Action}
        'SetFirewallRuleState'{Invoke-WinCareFirewallRuleAction -Action $Action}
        'SetFirewallProfileState'{Invoke-WinCareFirewallProfileStateAction -Action $Action}
        'SetLocalUserState'{Invoke-WinCareLocalUserStateAction -Action $Action}
        'SetDefenderPreference'{Invoke-WinCareDefenderPreferenceAction -Action $Action}
        'SetProcessTuning'{Invoke-WinCareProcessTuningAction -Action $Action}
        'TrimProcessWorkingSet'{Invoke-WinCareTrimProcessWorkingSetAction -Action $Action}
        'ResetNetworkAdapter'{Invoke-WinCareResetNetworkAdapterAction -Action $Action}
        'SetMasterVolume'{Invoke-WinCareMasterVolumeAction -Action $Action}
        'SetBrightness'{Invoke-WinCareBrightnessAction -Action $Action}
        'ClearClipboard'{Invoke-WinCareClearClipboardAction -Action $Action}
        'WuaSetHidden'{Invoke-WinCareWuaSetHiddenAction -Action $Action}
        'WuaDownload'{Invoke-WinCareWuaDownloadAction -Action $Action -OperationId $OperationId}
        'WuaInstall'{Invoke-WinCareWuaInstallAction -Action $Action -OperationId $OperationId}
        'WuaUninstall'{Invoke-WinCareWuaUninstallAction -Action $Action -OperationId $OperationId}
        'DeployWdacPolicy'{Invoke-WinCareWdacDeployAction -Action $Action}
        'RemoveWdacPolicy'{Invoke-WinCareWdacRemoveAction -Action $Action}
        'SetShellExtensionState'{Invoke-WinCareShellExtensionAction -Action $Action}
        'BcdExport'{Invoke-WinCareBcdExportAction -Action $Action}
        'RestoreRegistryBackup'{Invoke-WinCareRestoreRegistryBackupAction -Action $Action}
        'RegisterAutomationTask'{Invoke-WinCareRegisterAutomationTaskAction -Action $Action}
        'RemoveAutomationTask'{Invoke-WinCareRemoveAutomationTaskAction -Action $Action}
        'InstallDeclarativeExtension'{Invoke-WinCareInstallExtensionAction -Action $Action}
        'RemoveDeclarativeExtension'{Invoke-WinCareRemoveExtensionAction -Action $Action}
        'UpsertMaintenanceWindow'{Invoke-WinCareUpsertMaintenanceWindowAction -Action $Action}
        'SetMaintenanceWindowState'{Invoke-WinCareSetMaintenanceWindowStateAction -Action $Action}
        'RestoreMaintenanceWindowRecord'{Invoke-WinCareRestoreMaintenanceWindowRecordAction -Action $Action}
        'OfflineImageAddDriver'{Invoke-WinCareOfflineImageAddDriverAction -Action $Action}
        'OfflineImageRemoveDriver'{Invoke-WinCareOfflineImageRemoveDriverAction -Action $Action}
        'OfflineImageRemoveDriverBatch'{Invoke-WinCareOfflineImageRemoveDriverBatchAction -Action $Action}
        'OfflineImageAddPackage'{Invoke-WinCareOfflineImageAddPackageAction -Action $Action}
        'OfflineImageSetFeature'{Invoke-WinCareOfflineImageSetFeatureAction -Action $Action}
        'WriteManagedFile'{Invoke-WinCareWriteManagedFileAction -Action $Action}
        'RemoveManagedFile'{Invoke-WinCareRemoveManagedFileAction -Action $Action}
        'ImportLocalGroupPolicyBackup'{Invoke-WinCareImportLocalGroupPolicyBackupAction -Action $Action}
        'ConfigureSysmon'{Invoke-WinCareConfigureSysmonAction -Action $Action}
        'UninstallSysmon'{Invoke-WinCareUninstallSysmonAction -Action $Action}
        'SetPageFileConfiguration'{Invoke-WinCareSetPageFileConfigurationAction -Action $Action}
        'RestorePageFileConfiguration'{Invoke-WinCareRestorePageFileConfigurationAction -Action $Action}
        'SetSecurityControlState'{Invoke-WinCareSetSecurityControlStateAction -Action $Action}
        'RestoreSecurityControlState'{Invoke-WinCareRestoreSecurityControlStateAction -Action $Action}
        'SetLegacySecurityControlState'{Invoke-WinCareSetLegacySecurityControlStateAction -Action $Action}
        'RestoreLegacySecurityControlState'{Invoke-WinCareRestoreLegacySecurityControlStateAction -Action $Action}
        'ResetDisplayOverrides'{Invoke-WinCareResetDisplayOverridesAction -Action $Action}
        'RestoreDisplayOverrides'{Invoke-WinCareRestoreDisplayOverridesAction -Action $Action}
        'SetHibernateState'{Invoke-WinCareSetHibernateStateAction -Action $Action}
        'SetFolderAppearance'{Invoke-WinCareSetFolderAppearanceAction -Action $Action}
        'RestoreFolderAppearance'{Invoke-WinCareRestoreFolderAppearanceAction -Action $Action}
        'RunVerifiedPeerTool'{Invoke-WinCareRunVerifiedPeerToolAction -Action $Action}
        'SetMonitorVcpFeature'{Invoke-WinCareSetMonitorVcpFeatureAction -Action $Action}
        'OpenExplorerLocation'{Invoke-WinCareOpenExplorerLocationAction -Action $Action}
        'LaunchAppxApplication'{Invoke-WinCareLaunchAppxApplicationAction -Action $Action}
        'RunNetworkExperiment'{Invoke-WinCareRunNetworkExperimentAction -Action $Action -OperationId $OperationId}
        'RestoreTcpGlobalSetting'{Invoke-WinCareRestoreTcpGlobalSettingAction -Action $Action}
        'CaptureEtwTrace'{Invoke-WinCareCaptureEtwTraceAction -Action $Action -OperationId $OperationId}
        'QuarantineInjectionSurface'{Invoke-WinCareQuarantineInjectionSurfaceAction -Action $Action}
        'RestoreInjectionSurface'{Invoke-WinCareRestoreInjectionSurfaceAction -Action $Action}
        'StartPowerSession'{Invoke-WinCareStartPowerSessionAction -Action $Action}
        'StopPowerSession'{Invoke-WinCareStopPowerSessionAction -Action $Action}
        'SetWindowBounds'{Invoke-WinCareSetWindowBoundsAction -Action $Action}
        'SetWindowTopmost'{Invoke-WinCareSetWindowTopmostAction -Action $Action}
        'ActivateWindow'{Invoke-WinCareActivateWindowAction -Action $Action}
        'ReleaseLocalInput'{Invoke-WinCareReleaseLocalInputAction -Action $Action}
        'ReplaceColorPaletteStore'{Invoke-WinCareReplaceColorPaletteStoreAction -Action $Action}
        'WriteLocalNote'{Invoke-WinCareWriteLocalNoteAction -Action $Action}
        'RemoveLocalNote'{Invoke-WinCareRemoveLocalNoteAction -Action $Action}
        'RestoreLocalNote'{Invoke-WinCareRestoreLocalNoteAction -Action $Action}
        'ReplaceRemoteConsentStore'{Invoke-WinCareReplaceRemoteConsentStoreAction -Action $Action}
        'TerminateRemoteSupportProcess'{Invoke-WinCareTerminateRemoteSupportProcessAction -Action $Action}
        'ReplaceDownloadStore'{Invoke-WinCareReplaceDownloadStoreAction -Action $Action}
        'StartDownloadTransfer'{Invoke-WinCareStartDownloadTransferAction -Action $Action}
        'SetDownloadTransferState'{Invoke-WinCareSetDownloadTransferStateAction -Action $Action}
        'ReconcileDownloadTransfer'{Invoke-WinCareReconcileDownloadTransferAction -Action $Action}
        'CancelDownloadTransfer'{Invoke-WinCareCancelDownloadTransferAction -Action $Action}
        'CaptureTelemetrySeries'{Invoke-WinCareCaptureTelemetrySeriesAction -Action $Action -OperationId $OperationId}
        'OpenLaunchTarget'{Invoke-WinCareOpenLaunchTargetAction -Action $Action}
        'CreateSteamCloudBackup'{Invoke-WinCareCreateSteamCloudBackupAction -Action $Action}
        'RemoveGameStateBackup'{Invoke-WinCareRemoveGameStateBackupAction -Action $Action}
        'RestoreSteamCloudBackup'{Invoke-WinCareRestoreSteamCloudBackupAction -Action $Action}
        'OfflineImageRemoveProvisionedAppx'{Invoke-WinCareOfflineImageRemoveProvisionedAppxAction -Action $Action}
        'OfflineImageStartComponentCleanup'{Invoke-WinCareOfflineImageStartComponentCleanupAction -Action $Action}
        'ReplaceWorkspaceLayoutStore'{Invoke-WinCareReplaceWorkspaceLayoutStoreAction -Action $Action}
        'CreateSystemRestorePoint'{Invoke-WinCareVssRestorePointAction -Action $Action}
        default{New-WinCareResult -Success $false -Status Failed -Code 'UnknownActionType' -Message "Unknown action type: $($Action.Type)" -ExitCode 2}
    }
}

function Test-WinCareActionResultEvidence {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action,[Parameter(Mandatory)][object]$Result)
    if ($null -eq $Result) { return New-WinCareResult -Success $false -Status Failed -Code 'ProviderReturnedNull' -Message 'The action provider returned no result.' -ExitCode 70 }
    if ($null -eq $Result.PSObject.Properties['Success'] -or $null -eq $Result.PSObject.Properties['Status'] -or $null -eq $Result.PSObject.Properties['Code']) {
        return New-WinCareResult -Success $false -Status Failed -Code 'InvalidProviderResult' -Message 'The action provider did not return the WinCare result contract.' -ExitCode 70 -Data @{ObservedType=$Result.GetType().FullName}
    }
    if (-not [bool]$Result.Success) { return New-WinCareResult -Success $true -Code 'FailedResultAccepted' -Message 'The provider returned an explicit failure result.' -Data @{EvidenceType='ProviderFailureResult'} }
    $postconditions = @($Action.Postconditions)
    $data = Get-WinCarePropertyValue $Result 'Data' $null
    $hasData = $false
    $evidenceType = $null
    if ($null -ne $data) {
        if ($data -is [Collections.IDictionary]) {
            $hasData = $data.Count -gt 0
            if ($data.Contains('EvidenceType')) { $evidenceType = [string]$data['EvidenceType'] }
        } else {
            $properties = @($data.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' })
            $hasData = $properties.Count -gt 0 -or $data -is [string] -or $data.GetType().IsValueType
            $evidenceProperty = $data.PSObject.Properties['EvidenceType']
            if ($evidenceProperty) { $evidenceType = [string]$evidenceProperty.Value }
        }
    }
    $readOnly = [string]$Action.Risk -eq 'ReadOnly'
    $alreadyVerified = [string]$Result.Code -eq 'AlreadyAppliedAndVerified'
    if (-not $readOnly -and -not $alreadyVerified -and -not $hasData -and $postconditions.Count -eq 0) {
        return New-WinCareResult -Success $false -Status Failed -Code 'MutationEvidenceMissing' -Message 'The mutating provider reported success without observed result data or executable postconditions.' -ExitCode 70 -Data @{ProviderCode=$Result.Code;ActionType=$Action.Type}
    }
    New-WinCareResult -Success $true -Code 'ProviderEvidenceAccepted' -Message 'Provider evidence satisfied the transaction evidence floor.' -Data @{EvidenceType=$(if($evidenceType){$evidenceType}elseif($postconditions.Count){'ActionPostconditions'}elseif($alreadyVerified){'IdempotencyPostconditions'}else{'ProviderResultData'});ProviderCode=$Result.Code;HasResultData=$hasData;PostconditionCount=$postconditions.Count}
}

function Invoke-WinCareAction {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Action,[Parameter(Mandatory)][string]$OperationId,[switch]$Rollback)
    $contract=Test-WinCareActionContract -Action $Action;if(-not $contract.Success){return $contract}
    $policy=Test-WinCareActionPolicy -Action $Action;if(-not $policy.Success -and -not $Rollback){return $policy}
    $pre=Test-WinCareActionConditions -Conditions @($Action.Preconditions);if(-not $pre.Success){return $pre}
    if(-not $Rollback -and (Test-WinCareActionAlreadyApplied -Action $Action)){return New-WinCareResult -Success $true -Status Succeeded -Code 'AlreadyAppliedAndVerified' -Message 'The idempotent action is already applied and its postconditions were verified.' -Data @{EvidenceType='IdempotencyPostconditions';PostconditionCount=@($Action.Postconditions).Count} -OperationId $OperationId -ActionId $Action.Id}
    if($Action.RequiresAdmin -and -not $script:WinCareState.IsAdmin){$elevatedResult=Invoke-WinCareElevatedAction -Action $Action -OperationId $OperationId -TimeoutSeconds ([int]$Action.TimeoutSeconds);$elevatedEvidence=Test-WinCareActionResultEvidence -Action $Action -Result $elevatedResult;if(-not $elevatedEvidence.Success){return New-WinCareResult -Success $false -Status Failed -Code $elevatedEvidence.Code -Message $elevatedEvidence.Message -ExitCode $elevatedEvidence.ExitCode -Data @{ActionResult=$elevatedResult;EvidenceCheck=$elevatedEvidence} -OperationId $OperationId -ActionId $Action.Id};return $elevatedResult}
    Write-WinCareLog -Level Audit -Message 'Executing typed action.' -Data @{operationId=$OperationId;actionId=$Action.Id;type=$Action.Type;rollback=[bool]$Rollback;actionHash=Get-WinCareActionStableHash $Action}
    $started=[datetime]::UtcNow
    try{
        $result=Invoke-WinCareActionInternal -Action $Action -OperationId $OperationId
        $evidenceCheck=Test-WinCareActionResultEvidence -Action $Action -Result $result
        if(-not $evidenceCheck.Success){return New-WinCareResult -Success $false -Status Failed -Code $evidenceCheck.Code -Message $evidenceCheck.Message -ExitCode $evidenceCheck.ExitCode -Data @{ActionResult=$result;EvidenceCheck=$evidenceCheck} -OperationId $OperationId -ActionId $Action.Id}
        if($result.Success){
            if([bool]$Action.Reversible){
                try{$runtimeCompensator=Resolve-WinCareCompensatorDescriptor -Action $Action -Result $result}
                catch{return New-WinCareResult -Success $false -Status Failed -Code 'InvalidRuntimeCompensator' -Message "Action succeeded but its recovery contract is invalid: $($_.Exception.Message)" -Data @{ActionResult=$result} -OperationId $OperationId -ActionId $Action.Id}
                if(-not $runtimeCompensator){return New-WinCareResult -Success $false -Status Failed -Code 'MissingRuntimeCompensator' -Message 'Action succeeded but did not produce the required executable recovery contract.' -Data @{ActionResult=$result} -OperationId $OperationId -ActionId $Action.Id}
            }
            $post=Test-WinCareActionConditions -Conditions @($Action.Postconditions)
            if(-not $post.Success){
                $immediateRollback=$null
                if([bool]$Action.Reversible){$immediateRollback=Invoke-WinCareCompensation -Action $Action -Result $result -OperationId $OperationId}
                $rolledBack=$immediateRollback -and [bool]$immediateRollback.Success
                $warnings=if($immediateRollback -and -not $rolledBack){@("Immediate rollback failed: $($immediateRollback.Message)")}else{@()}
                return New-WinCareResult -Success $false -Status Failed -Code $(if($rolledBack){'PostconditionFailedRolledBack'}elseif($immediateRollback){'PostconditionFailedRollbackFailed'}else{'PostconditionFailed'}) -Message $(if($rolledBack){"Action verification failed and the action was rolled back: $($post.Message)"}elseif($immediateRollback){"Action verification failed and rollback also failed: $($post.Message)"}else{"Action ran but verification failed: $($post.Message)"}) -Data @{ActionResult=$result;ImmediateRollback=$immediateRollback} -Warnings $warnings -OperationId $OperationId -ActionId $Action.Id
            }
            if(-not $Rollback){Set-WinCareActionApplied -Action $Action -OperationId $OperationId -Result $result}
        }
        $result.OperationId=$OperationId;$result.ActionId=$Action.Id
        if(([datetime]::UtcNow-$started).TotalSeconds -gt [int]$Action.TimeoutSeconds){$result.Warnings=@($result.Warnings)+"Action exceeded its declared timeout of $($Action.TimeoutSeconds) seconds; the provider completed before control returned."}
        return $result
    }catch{Write-WinCareLog -Level Error -Message 'Typed action raised an exception.' -Data @{operationId=$OperationId;actionId=$Action.Id;type=$Action.Type;error=$_.Exception.Message};New-WinCareResult -Success $false -Status Failed -Code 'ActionException' -Message $_.Exception.Message -ExitCode 1 -OperationId $OperationId -ActionId $Action.Id}
}

function Resolve-WinCareCompensatorDescriptor {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Action,[object]$Result)
    if(-not [bool]$Action.Reversible){return $null}
    $descriptor=Get-WinCarePropertyValue $Action 'Compensator' $null
    if(($null -eq $descriptor -or [string]::IsNullOrWhiteSpace([string](Get-WinCarePropertyValue $descriptor 'Type' ''))) -and $Result){
        $data=Get-WinCarePropertyValue $Result 'Data' $null
        if($data){$descriptor=Get-WinCarePropertyValue $data 'Compensator' $null}
    }
    if($null -eq $descriptor -or [string]::IsNullOrWhiteSpace([string](Get-WinCarePropertyValue $descriptor 'Type' ''))){return $null}
    $null=Test-WinCareStrictObjectKeys -InputObject $descriptor -AllowedKeys @('Type','Parameters','RequiresAdmin') -Context 'compensator descriptor'
    $type=[string]$descriptor.Type;$contract=Get-WinCareActionContract -Type $type
    if(-not $contract){throw "Compensator type is unknown: $type"}
    $parameters=ConvertTo-WinCareParameterDictionary (Get-WinCarePropertyValue $descriptor 'Parameters' @{})
    $requiresAdmin=[bool](Get-WinCarePropertyValue $descriptor 'RequiresAdmin' $Action.RequiresAdmin)
    $candidate=New-WinCareAction -Type $type -Label ("Compensate: {0}" -f $Action.Label) -Risk ([string]$contract.MinimumRisk) -Parameters $parameters -RequiresAdmin $requiresAdmin -Reversible $false -TimeoutSeconds ([Math]::Min([int]$Action.TimeoutSeconds,[int]$contract.MaximumTimeout)) -Tags @('Compensation') -SourceRecords @($Action.SourceRecords)
    $validation=Test-WinCareActionContract -Action $candidate -SkipCompensatorValidation
    if(-not $validation.Success){throw "Compensator is invalid: $($validation.Message)"}
    [pscustomobject]@{Type=$type;Parameters=$parameters;RequiresAdmin=$requiresAdmin;Contract=$contract}
}

function Invoke-WinCareCompensation {
    param([object]$Action,[object]$Result,[string]$OperationId)
    try{$descriptor=Resolve-WinCareCompensatorDescriptor -Action $Action -Result $Result}
    catch{return New-WinCareResult -Success $false -Status Blocked -Code 'InvalidCompensator' -Message $_.Exception.Message}
    if(-not $descriptor){return New-WinCareResult -Success $false -Status Blocked -Code 'NoCompensator' -Message 'No compensator is available.'}
    $comp=New-WinCareAction -Type $descriptor.Type -Label ("Rollback: {0}" -f $Action.Label) -Risk ([string]$descriptor.Contract.MinimumRisk) -Parameters $descriptor.Parameters -RequiresAdmin $descriptor.RequiresAdmin -Tags @('Rollback') -SourceRecords @($Action.SourceRecords)
    Invoke-WinCareAction -Action $comp -OperationId $OperationId -Rollback
}

function Register-WinCarePlanPreview {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Plan,[ValidateRange(1,60)][int]$LifetimeMinutes=15)
    if(-not $script:WinCareState.ContainsKey('PlanPreviews')){$script:WinCareState.PlanPreviews=@{}}
    $now=[datetime]::UtcNow
    foreach($key in @($script:WinCareState.PlanPreviews.Keys)){if([datetime]$script:WinCareState.PlanPreviews[$key] -le $now){$script:WinCareState.PlanPreviews.Remove($key)}}
    $hash=Get-WinCarePlanStableHash $Plan
    $script:WinCareState.PlanPreviews[$hash]=$now.AddMinutes($LifetimeMinutes)
    return $hash
}

function Test-WinCarePlanPreviewReceipt {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Plan)
    if(-not $script:WinCareState.ContainsKey('PlanPreviews')){return $false}
    $hash=Get-WinCarePlanStableHash $Plan
    if(-not $script:WinCareState.PlanPreviews.ContainsKey($hash)){return $false}
    if([datetime]$script:WinCareState.PlanPreviews[$hash] -le [datetime]::UtcNow){$script:WinCareState.PlanPreviews.Remove($hash);return $false}
    return $true
}

function Remove-WinCarePlanPreviewReceipt {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Plan)
    if($script:WinCareState.ContainsKey('PlanPreviews')){$script:WinCareState.PlanPreviews.Remove((Get-WinCarePlanStableHash $Plan))}
}

function Invoke-WinCarePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Plan,[switch]$Approved,[switch]$PreviewOnly)
    $contract=Test-WinCarePlanContract -Plan $Plan;if(-not $contract.Success){return $contract}
    $summary=Get-WinCarePlanSummary $Plan;$policy=Test-WinCarePlanPolicy -Plan $Plan -PreviewOnly:$PreviewOnly;if(-not $policy.Success){return $policy}
    $modifies=(Get-WinCareRiskRank $summary.HighestRisk)-gt 0
    if($script:WinCareState.Config.ReadOnly -and -not $PreviewOnly -and $modifies){return New-WinCareResult -Success $false -Status Blocked -Code 'ReadOnlyMode' -Message 'Read-only mode blocks modifying actions.' -ExitCode 5}
    if($PreviewOnly){$planHash=Register-WinCarePlanPreview -Plan $Plan;return New-WinCareResult -Success $true -Status Preview -Code 'Preview' -Message 'Preview generated, policy-checked, and bound to the exact plan hash.' -Data @{Summary=$summary;Plan=$Plan;PlanHash=$planHash;ExpiresAt=$script:WinCareState.PlanPreviews[$planHash]}}
    if(-not $Approved){return New-WinCareResult -Success $false -Status Blocked -Code 'ApprovalRequired' -Message 'The operation plan was not explicitly approved.' -ExitCode 5}
    $previewRequired=$modifies -and ([bool](Get-WinCareConfig 'RequirePreviewForChanges') -or [bool](Get-WinCarePolicy 'RequirePreview'))
    if($previewRequired -and -not(Test-WinCarePlanPreviewReceipt -Plan $Plan)){return New-WinCareResult -Success $false -Status Blocked -Code 'PreviewRequired' -Message 'This exact plan must be previewed before it can be applied.' -ExitCode 5}
    if($previewRequired){Remove-WinCarePlanPreviewReceipt -Plan $Plan}
    $operationLock=$null;$journal=$null
    try{
        $operationLock=Enter-WinCareOperationLock
        $operationId=[guid]::NewGuid().ToString('N');$journal=New-WinCareOperationJournal -Plan $Plan -OperationId $operationId
        $results=[Collections.Generic.List[object]]::new();$completed=[Collections.Generic.List[object]]::new();$warnings=[Collections.Generic.List[string]]::new()
        Update-WinCareOperationJournal -Journal $journal -State 'Authorized' -Event 'PlanAuthorized';Update-WinCareOperationJournal -Journal $journal -State 'Running' -Event 'PlanStarted'
        $failed=$false;$cancelled=$false
        foreach($action in @($Plan.Actions)){
            if(Test-Path -LiteralPath $journal.CancelPath){$cancelled=$true;break}
            Update-WinCareOperationJournal -Journal $journal -State 'Running' -Patch @{CurrentActionId=$action.Id;CurrentActionType=$action.Type;CurrentActionHash=Get-WinCareActionStableHash $action} -Event 'ActionStarted'
            $result=Invoke-WinCareAction -Action $action -OperationId $operationId
            $journalItem=ConvertTo-WinCareRedactedObject ([pscustomobject]@{Action=$action;Result=$result;FinishedAt=[datetime]::UtcNow.ToString('o')});$results.Add($journalItem)
            if($result.Success){$completed.Add([pscustomobject]@{Action=$action;Result=$result})}else{$failed=$true}
            Update-WinCareOperationJournal -Journal $journal -State 'Running' -Patch @{Results=@($results)} -Event $(if($result.Success){'ActionSucceeded'}elseif($result.Status -eq 'Cancelled'){'ActionCancelled'}else{'ActionFailed'})
            if($failed -and $Plan.StopOnFailure){break}
        }
        $rollbackState='NotRequired'
        if(($failed -or $cancelled)-and $Plan.RollbackOnFailure -and $completed.Count -gt 0){
            $rollbackState='Running';Update-WinCareOperationJournal -Journal $journal -State 'RollingBack' -Patch @{RollbackState=$rollbackState} -Event 'RollbackStarted'
            for($i=$completed.Count-1;$i-ge 0;$i--){$completedItem=$completed[$i];$action=$completedItem.Action;if(-not $action.Reversible){continue};$rollback=Invoke-WinCareCompensation -Action $action -Result $completedItem.Result -OperationId $operationId;$results.Add((ConvertTo-WinCareRedactedObject ([pscustomobject]@{Action=$action;Result=$rollback;Rollback=$true;FinishedAt=[datetime]::UtcNow.ToString('o')})));if(-not $rollback.Success){$warnings.Add("Rollback failed for $($action.Label): $($rollback.Message)")}}
            $rollbackState=if($warnings.Count-eq 0){'Succeeded'}else{'Failed'}
        }
        $success=-not $failed -and -not $cancelled;$state=if($cancelled){'Cancelled'}elseif($success){'Succeeded'}elseif($warnings.Count-gt 0){'FailedWithRollbackWarnings'}else{'Failed'}
        Update-WinCareOperationJournal -Journal $journal -State $state -Patch @{FinishedAt=[datetime]::UtcNow.ToString('o');Success=$success;RollbackState=$rollbackState;Results=@($results);Warnings=@($warnings);CurrentActionId=$null;CurrentActionType=$null;CurrentActionHash=$null} -Event 'PlanFinished'
        $receiptHash=Complete-WinCareOperationReceipt -Journal $journal
        New-WinCareResult -Success $success -Status $(if($cancelled){'Cancelled'}elseif($success -and $warnings.Count-eq 0){'Succeeded'}elseif($success){'SucceededWithWarnings'}else{'Failed'}) -Code $state -Message $(if($success){'Plan completed with provider evidence, postcondition checks, and a finalized operation receipt.'}elseif($cancelled){'Plan was cancelled.'}else{'Plan failed; rollback was attempted where supported.'}) -Data @{Results=@($results);JournalPath=$journal.RecordPath;OperationId=$operationId;RollbackState=$rollbackState;ReceiptHash=$receiptHash;EvidenceType='TransactionJournalAndReceipt'} -Warnings @($warnings) -OperationId $operationId
    }catch{if($journal){try{Update-WinCareOperationJournal -Journal $journal -State 'Failed' -Patch @{FinishedAt=[datetime]::UtcNow.ToString('o');Success=$false;Warnings=@("Transaction engine failure: $($_.Exception.Message)")} -Event 'TransactionEngineFailed';$null=Complete-WinCareOperationReceipt -Journal $journal}catch { Write-Verbose 'A best-effort operation was unavailable.' }};New-WinCareResult -Success $false -Status Failed -Code 'TransactionEngineFailure' -Message $_.Exception.Message -ExitCode 1}
    finally{if($operationLock){$operationLock.Dispose()}}
}

function Invoke-WinCareMoveFileAction {
    param([object]$Action)
    $allowedRoots=@(Get-WinCarePropertyValue $Action.Parameters 'AllowedRoots' @())
    $source=Assert-WinCareSafePath -LiteralPath ([string]$Action.Parameters.Source) -AllowedRoots $allowedRoots
    $destination=Assert-WinCareSafePath -LiteralPath ([string]$Action.Parameters.Destination) -AllowedRoots $allowedRoots -AllowMissing
    if((Test-WinCarePathWithin -Child $destination -Parent $source -AllowEqual) -or (Test-WinCarePathWithin -Child $source -Parent $destination -AllowEqual)){return New-WinCareResult -Success $false -Status Blocked -Code 'NestedMove' -Message 'Source and destination cannot contain one another.' -ExitCode 22}
    $expectedHash=[string]$Action.Parameters.ExpectedSourceHash
    $sourceHash=Get-WinCarePathContentHash -LiteralPath $source
    if($sourceHash -ne $expectedHash){return New-WinCareResult -Success $false -Code 'SourceChanged' -Message 'Source content changed after preview.' -ExitCode 74}
    if(Test-Path -LiteralPath $destination){return New-WinCareResult -Success $false -Code 'DestinationExists' -Message 'Destination already exists; WinCare will not overwrite it.' -ExitCode 17}
    $sourceMeasurement=Measure-WinCarePathTree -LiteralPath $source
    $maximum=[long](Get-WinCarePolicy 'MaximumEstimatedBytes')
    if($maximum -gt 0 -and $sourceMeasurement.Bytes -gt $maximum){return New-WinCareResult -Success $false -Status Blocked -Code 'MoveSizeExceedsPolicy' -Message "Source size exceeds the policy limit of $maximum bytes." -ExitCode 5}
    $parent=Split-Path -Parent $destination
    if(-not $parent){return New-WinCareResult -Success $false -Message 'Destination must include a parent directory.' -ExitCode 22}
    $parent=Assert-WinCareSafePath -LiteralPath $parent -AllowedRoots $allowedRoots -AllowMissing
    $null=New-Item -ItemType Directory -Path $parent -Force
    $parent=Assert-WinCareSafePath -LiteralPath $parent -AllowedRoots $allowedRoots
    $sourceRoot=[IO.Path]::GetPathRoot($source);$destinationRoot=[IO.Path]::GetPathRoot($destination)
    $comparison=if($IsWindows){[StringComparison]::OrdinalIgnoreCase}else{[StringComparison]::Ordinal}
    $sameVolume=[string]::Equals($sourceRoot,$destinationRoot,$comparison)
    if(-not $sameVolume){
        try{
            $drive=[IO.DriveInfo]::new($destinationRoot)
            $required=[long]($sourceMeasurement.Bytes+[Math]::Max(16MB,[Math]::Ceiling($sourceMeasurement.Bytes*0.05)))
            if($drive.AvailableFreeSpace -lt $required){return New-WinCareResult -Success $false -Status Blocked -Code 'InsufficientDestinationSpace' -Message "Destination requires at least $required available bytes." -ExitCode 28}
        }catch{return New-WinCareResult -Success $false -Status Blocked -Code 'DestinationCapacityUnknown' -Message "Destination capacity could not be established: $($_.Exception.Message)" -ExitCode 5}
    }
    if($sameVolume){
        Move-Item -LiteralPath $source -Destination $destination -ErrorAction Stop
        $destinationHash=if(Test-Path -LiteralPath $destination){Get-WinCarePathContentHash -LiteralPath $destination}else{$null}
        if($destinationHash -ne $sourceHash -or (Test-Path -LiteralPath $source)){
            $warnings=[Collections.Generic.List[string]]::new()
            try{if((Test-Path -LiteralPath $destination)-and -not(Test-Path -LiteralPath $source)){Move-Item -LiteralPath $destination -Destination $source -ErrorAction Stop}}catch{$warnings.Add("Automatic move rollback failed: $($_.Exception.Message)")}
            return New-WinCareResult -Success $false -Status Failed -Code 'MoveVerificationFailed' -Message 'Same-volume move failed content verification and rollback was attempted.' -Warnings @($warnings) -ExitCode 74
        }
    }else{
        $temporary=Join-Path $parent ('.wincare-moving-'+[guid]::NewGuid().ToString('N'))
        try{
            Copy-Item -LiteralPath $source -Destination $temporary -Recurse -Force -ErrorAction Stop
            if((Get-WinCarePathContentHash -LiteralPath $temporary)-ne $sourceHash){throw 'Cross-volume copy verification failed.'}
            Move-Item -LiteralPath $temporary -Destination $destination -ErrorAction Stop
            try{Remove-Item -LiteralPath $source -Recurse -Force -ErrorAction Stop}
            catch{
                $removeError=$_.Exception.Message
                $warnings=[Collections.Generic.List[string]]::new()
                try{Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction Stop}catch{$warnings.Add("Verified destination cleanup failed: $($_.Exception.Message)")}
                return New-WinCareResult -Success $false -Status Failed -Code 'SourceRemovalFailed' -Message "Source removal failed after verified copy: $removeError" -Warnings @($warnings) -ExitCode 5
            }
        } finally {if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue}}
    }
    $destinationHash=if(Test-Path -LiteralPath $destination){Get-WinCarePathContentHash -LiteralPath $destination}else{$null}
    $success=$destinationHash -eq $sourceHash -and -not(Test-Path -LiteralPath $source)
    New-WinCareResult -Success $success -Status $(if($success){'Succeeded'}else{'Failed'}) -Code $(if($success){'MoveVerified'}else{'MoveVerificationFailed'}) -Message $(if($success){'Path moved and content-verified.'}else{'Path move did not satisfy final verification.'}) -Data @{Source=$source;Destination=$destination;DestinationHash=$destinationHash;Bytes=$sourceMeasurement.Bytes;Files=$sourceMeasurement.Files;SameVolume=$sameVolume} -ExitCode $(if($success){0}else{74})
}

function Invoke-WinCareGenericProcessAction {
    param([object]$Action)
    Invoke-WinCareProcess -FilePath ([string]$Action.Parameters.FilePath) -ArgumentList @($Action.Parameters.Arguments) -RequireAdmin:$Action.RequiresAdmin -TimeoutSeconds ([int]$Action.TimeoutSeconds) -SuccessExitCodes @((Get-WinCarePropertyValue $Action.Parameters 'SuccessExitCodes' @(0)))
}
