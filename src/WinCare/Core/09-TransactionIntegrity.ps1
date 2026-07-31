#requires -Version 7.2

function Get-WinCarePlanSummary {
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
function Get-WinCarePlanStableHash {
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
function Get-WinCareOperationRecordStableHash {
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
function Complete-WinCareOperationReceipt {
    param([Parameter(Mandatory)][object]$Journal)
    Write-WinCareAtomicJson -LiteralPath $Journal.RecordPath -InputObject $Journal.Record
    $record=Read-WinCareBoundedJson -LiteralPath $Journal.RecordPath -MaximumBytes 16777216 -Depth 80 -AsHashtable
    $recordHash=Get-WinCareOperationRecordStableHash $record
    $receipt=[ordered]@{SchemaVersion=2;
        OperationId=$record.OperationId;
        PlanHash=$record.PlanHash;
        RecordHash=$recordHash;
        ModuleTreeHash=$record.ModuleTreeHash;
        FinalState=$record.State;
        Success=$record.Success;
        StartedAt=$record.StartedAt;
        FinishedAt=$record.FinishedAt;
        LastEventHash=$record.LastEventHash;
        ResultCount=@($record.Results).Count;
        WarningCount=@($record.Warnings).Count}
    $receipt.ReceiptHash=Get-WinCareOperationReceiptStableHash $receipt
    Write-WinCareProtectedJson -LiteralPath (Join-Path $Journal.Root 'receipt.json') -InputObject $receipt -Purpose 'WinCare.OperationReceipt'
    $Journal.Record.ReceiptHash=$receipt.ReceiptHash
    Write-WinCareAtomicJson -LiteralPath $Journal.RecordPath -InputObject $Journal.Record
    $receipt.ReceiptHash
}
function Test-WinCareOperationJournalIntegrity {
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
        $terminalStates=@('Succeeded','Failed','Cancelled','Interrupted','FailedWithRollbackWarnings')
        if([string]$record.State -in $terminalStates -and -not $receiptPresent){
            throw 'Terminal operation journal is missing its authenticated receipt.'
        }
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
