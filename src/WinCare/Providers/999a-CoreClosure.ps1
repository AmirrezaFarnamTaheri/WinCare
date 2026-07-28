#requires -Version 7.2
# Target-native final convergence closure.

$manifestVersion=(Import-PowerShellDataFile -LiteralPath (Join-Path $script:WinCareModuleRoot 'WinCare.psd1')).ModuleVersion.ToString()
if([string]$script:WinCareVersion -in @('0.0','0.0.0')){$script:WinCareVersion=$manifestVersion}

${function:Write-WinCareLog} = {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Debug', 'Info', 'Warning', 'Error', 'Audit')]
        [string]$Level,
        [Parameter(Mandatory)][string]$Message,
        [hashtable]$Data = @{}
    )

    if($script:WinCareState -is [Collections.IDictionary] -and
        $script:WinCareState.Contains('ReadOnlyLocked') -and
        [bool]$script:WinCareState.ReadOnlyLocked) {
        return
    }
    $safeMessage = ConvertTo-WinCareRedactedScalar -Value $Message
    $safeData = ConvertTo-WinCareRedactedValue -Value $Data
    $record = [ordered]@{
        schemaVersion = 1
        timestamp = [datetime]::UtcNow.ToString('o')
        level = $Level
        sessionId = [string](Get-WinCarePropertyValue $script:WinCareState 'SessionId' '')
        processId = $PID
        message = $safeMessage
        data = $safeData
    }
    $json = $record | ConvertTo-Json -Compress -Depth 20
    Write-WinCareLogLine -LiteralPath (Get-WinCareLogPath) -Line $json
}

${function:Get-WinCarePlanSummary} = {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Plan)
    $actions=@(Get-WinCarePropertyValue $Plan 'Actions' @());$highest=$actions|Sort-Object {Get-WinCareRiskRank $_.Risk} -Descending|Select-Object -First 1
    $sources=[Collections.Generic.List[string]]::new()
    foreach($source in @(Get-WinCarePropertyValue $Plan 'SourceRecords' @())){if($source){$sources.Add([string]$source)}}
    foreach($action in $actions){foreach($source in @(Get-WinCarePropertyValue $action 'SourceRecords' @())){if($source){$sources.Add([string]$source)}}}
    [pscustomobject]@{
        Count=$actions.Count;HighestRisk=if($highest){$highest.Risk}else{'ReadOnly'}
        RequiresAdmin=[bool]($actions|Where-Object RequiresAdmin|Select-Object -First 1)
        Reversible=@($actions|Where-Object Reversible).Count
        EstimatedBytes=[long](($actions|Measure-Object EstimatedBytes -Sum).Sum)
        RestartPossible=[bool]($actions|Where-Object RestartPossible|Select-Object -First 1)
        SourceRecords=@($sources|Sort-Object -Unique)
    }
}

${function:Get-WinCarePlanStableHash} = {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Plan)
    $stable=[ordered]@{
        SchemaVersion=Get-WinCarePropertyValue $Plan 'SchemaVersion' 0
        Id=Get-WinCarePropertyValue $Plan 'Id' ''
        Title=Get-WinCarePropertyValue $Plan 'Title' ''
        Description=Get-WinCarePropertyValue $Plan 'Description' ''
        Actions=@(@(Get-WinCarePropertyValue $Plan 'Actions' @())|ForEach-Object{Get-WinCareActionStableHash $_})
        StopOnFailure=[bool](Get-WinCarePropertyValue $Plan 'StopOnFailure' $true)
        RollbackOnFailure=[bool](Get-WinCarePropertyValue $Plan 'RollbackOnFailure' $true)
        Metadata=Get-WinCarePropertyValue $Plan 'Metadata' @{}
        SourceRecords=@(Get-WinCarePropertyValue $Plan 'SourceRecords' @())
    }
    Get-WinCareSha256Text -Text (ConvertTo-WinCareCanonicalJson -InputObject $stable -Depth 40)
}

${function:Get-WinCareOperationRecordStableHash} = {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Record)
    $stable=[ordered]@{
        SchemaVersion=Get-WinCarePropertyValue $Record 'SchemaVersion' 0
        OperationId=Get-WinCarePropertyValue $Record 'OperationId' ''
        SessionId=Get-WinCarePropertyValue $Record 'SessionId' ''
        PlanId=Get-WinCarePropertyValue $Record 'PlanId' ''
        PlanHash=Get-WinCarePropertyValue $Record 'PlanHash' ''
        Title=Get-WinCarePropertyValue $Record 'Title' ''
        Description=Get-WinCarePropertyValue $Record 'Description' ''
        State=Get-WinCarePropertyValue $Record 'State' ''
        Revision=Get-WinCarePropertyValue $Record 'Revision' 0
        StartedAt=Get-WinCarePropertyValue $Record 'StartedAt' $null
        FinishedAt=Get-WinCarePropertyValue $Record 'FinishedAt' $null
        Success=Get-WinCarePropertyValue $Record 'Success' $null
        RollbackState=Get-WinCarePropertyValue $Record 'RollbackState' ''
        ModuleTreeHash=Get-WinCarePropertyValue $Record 'ModuleTreeHash' ''
        Plan=Get-WinCarePropertyValue $Record 'Plan' $null
        Results=@(Get-WinCarePropertyValue $Record 'Results' @())
        Warnings=@(Get-WinCarePropertyValue $Record 'Warnings' @())
        SourceRecords=@(Get-WinCarePropertyValue $Record 'SourceRecords' @())
        LastEventHash=Get-WinCarePropertyValue $Record 'LastEventHash' ''
    }
    Get-WinCareSha256Text -Text (ConvertTo-WinCareCanonicalJson -InputObject $stable -Depth 80)
}

${function:Test-WinCareOperationJournalIntegrity} = {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$OperationPath)
    try{
        $root=if((Get-Item -LiteralPath $OperationPath).PSIsContainer){$OperationPath}else{Split-Path -Parent $OperationPath}
        $recordPath=Join-Path $root 'operation.json';$eventsPath=Join-Path $root 'events.jsonl';$receiptPath=Join-Path $root 'receipt.json'
        $record=Read-WinCareBoundedJson -LiteralPath $recordPath -MaximumBytes 16777216 -Depth 50 -AsHashtable
        $eventsItem=Get-Item -LiteralPath $eventsPath -Force -ErrorAction Stop
        $unsafeEvents=$eventsItem.PSIsContainer -or ($eventsItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -or [long]$eventsItem.Length -gt 67108864
        if($unsafeEvents){throw 'Operation event log is not a bounded regular file.'}
        $previous='0'*64;$count=0
        foreach($line in [IO.File]::ReadLines($eventsPath,[Text.UTF8Encoding]::new($false,$true))){
            if([string]::IsNullOrWhiteSpace($line)){continue}
            $event=$line|ConvertFrom-Json -AsHashtable -Depth 50;$hash=[string]$event.eventHash;$event.Remove('eventHash')
            $bodyJson=ConvertTo-WinCareCanonicalJson -InputObject $event -Depth 40;$expected=Get-WinCareSha256Text -Text ($previous+"`n"+$bodyJson)
            if($expected -ne $hash -or [string]$event.previousEventHash -ne $previous){throw "Event chain mismatch at event $count."}
            $previous=$hash;$count++
        }
        if($previous -ne [string]$record.LastEventHash){throw 'Operation record does not match the event chain.'}
        if([string]$record.PlanHash -ne (Get-WinCarePlanStableHash $record.Plan)){throw 'Operation plan no longer matches its recorded hash.'}
        $receiptPresent=Test-Path -LiteralPath $receiptPath -PathType Leaf
        if([string]$record.State -in @('Succeeded','Failed','Cancelled','Interrupted','FailedWithRollbackWarnings') -and -not $receiptPresent){throw 'Terminal operation journal is missing its authenticated receipt.'}
        if($receiptPresent){
            $receipt=Read-WinCareProtectedJson -LiteralPath $receiptPath -Purpose 'WinCare.OperationReceipt' -AsHashtable
            if([int]$receipt.SchemaVersion -ne 2){throw 'Unsupported operation receipt schema.'}
            if([string]$receipt.LastEventHash -ne $previous -or [string]$receipt.OperationId -ne [string]$record.OperationId){throw 'Operation receipt does not match the journal.'}
            if([string]$receipt.PlanHash -ne [string]$record.PlanHash){throw 'Operation receipt plan hash does not match the journal.'}
            if([string]$receipt.RecordHash -ne (Get-WinCareOperationRecordStableHash $record)){throw 'Operation record was modified after receipt creation.'}
            if([string]$receipt.ReceiptHash -ne (Get-WinCareOperationReceiptStableHash $receipt)){throw 'Operation receipt content hash is invalid.'}
            if([string]$record.ReceiptHash -ne [string]$receipt.ReceiptHash){throw 'Operation record receipt reference is invalid.'}
            if([int]$receipt.ResultCount -ne @($record.Results).Count -or [int]$receipt.WarningCount -ne @($record.Warnings).Count){throw 'Operation receipt counts do not match the journal.'}
        }
        [pscustomobject]@{Valid=$true;OperationId=$record.OperationId;Events=$count;LastEventHash=$previous;State=$record.State;ReceiptPresent=$receiptPresent}
    }catch{[pscustomobject]@{Valid=$false;OperationId=$null;Events=0;Error=$_.Exception.Message}}
}

${function:Assert-WinCareRolePermission} = {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$RequestedRole,[Parameter(Mandatory)][string]$ActionContractName,[Parameter(Mandatory)][string]$UserIdentity)
    $validRoles=@('HelpdeskAdmin','SecOpsAdmin','FleetLead');if($RequestedRole -notin $validRoles){return New-WinCareResult -Success $false -Status Blocked -Code 'InvalidRole' -Message "Role '$RequestedRole' is not recognized." -ExitCode 78}
    $roleInfo=(Get-WinCareRbacMatrix).$RequestedRole
    $highRiskActions=@('DisableWdac','UnloadKernelDriver','ModifyHvci','ForceSystemReboot')
    $mediumRiskActions=@('OptimizeStorage','TrimMemoryWorkingSets','ApplyGroupPolicy')
    $actionRiskLevel=if($ActionContractName -in $highRiskActions){3}elseif($ActionContractName -in $mediumRiskActions){2}else{1}
    if($actionRiskLevel -gt $roleInfo.AllowedRiskCap){
        $auditEntry=[pscustomobject]@{Timestamp=[datetime]::UtcNow.ToString('o');EventType='UnauthorizedRoleActionAttempt';RequestedRole=$RequestedRole;ActionContractName=$ActionContractName;UserIdentity=$UserIdentity;ActionRiskLevel=$actionRiskLevel;AllowedRiskCap=$roleInfo.AllowedRiskCap;Status='BlockedByPolicy';EvidenceType='UnauthorizedRoleAttemptAuditRecord'}
        Write-WinCareLog -Level Audit -Message 'Role permission denied.' -Data @{requestedRole=$RequestedRole;action=$ActionContractName;user=$UserIdentity;risk=$actionRiskLevel;cap=$roleInfo.AllowedRiskCap}
        return New-WinCareResult -Success $false -Status Blocked -Code 'BlockedByPolicy' -Message "Role '$RequestedRole' is unauthorized for action '$ActionContractName'." -ExitCode 78 -Data $auditEntry
    }
    New-WinCareResult -Success $true -Code 'RolePermissionGranted' -Message "Role '$RequestedRole' authorized for action '$ActionContractName'." -Data @{RequestedRole=$RequestedRole;ActionContractName=$ActionContractName;UserIdentity=$UserIdentity;ActionRiskLevel=$actionRiskLevel;EvidenceType='RolePermissionAuthorizationRecord'}
}
