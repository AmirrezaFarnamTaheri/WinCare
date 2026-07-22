function Get-WinCareOperationRecord {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$OperationPath)
    $path=Assert-WinCareSafePath -LiteralPath $OperationPath
    $root=if((Get-Item -LiteralPath $path -Force).PSIsContainer){$path}else{Split-Path -Parent $path}
    $journalRoot=Join-Path $script:WinCareState.Root 'Journal'
    if(-not(Test-WinCarePathWithin -Child $root -Parent $journalRoot)){throw 'Operation journal is outside the WinCare journal root.'}
    $recordPath=Join-Path $root 'operation.json'
    if(-not(Test-Path -LiteralPath $recordPath -PathType Leaf)){throw 'Operation record is missing.'}
    $integrity=Test-WinCareOperationJournalIntegrity -OperationPath $root
    if(-not $integrity.Valid){throw "Operation journal failed integrity validation: $($integrity.Error)"}
    $record=Get-Content -LiteralPath $recordPath -Raw -ErrorAction Stop|ConvertFrom-Json -Depth 60
    if([int]$record.SchemaVersion -ne 4){throw 'Unsupported operation journal schema.'}
    if([string]$record.OperationId -ne (Split-Path -Leaf $root)){throw 'Operation identifier does not match its journal directory.'}
    [pscustomobject]@{Root=$root;RecordPath=$recordPath;Record=$record;Integrity=$integrity}
}

function Get-WinCareUndoMarkerPath {
    param([Parameter(Mandatory)][string]$OperationId)
    if($OperationId -notmatch '^[a-f0-9]{32}$'){throw 'Invalid operation identifier.'}
    Join-Path (Join-Path $script:WinCareState.Root 'Journal') ("undo-{0}.json" -f $OperationId)
}

function Test-WinCareOperationUndone {
    param([Parameter(Mandatory)][string]$OperationId)
    $path=Get-WinCareUndoMarkerPath $OperationId
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){return $false}
    try{
        $record=Read-WinCareProtectedJson -LiteralPath $path -Purpose 'WinCare.UndoMarker'
        return [int]$record.SchemaVersion -eq 1 -and [string]$record.OperationId -eq $OperationId -and [string]$record.UndoOperationId -match '^[a-f0-9]{32}$'
    }catch{
        Write-WinCareLog -Level Warning -Message 'Ignoring an invalid undo marker.' -Data @{operationId=$OperationId;error=$_.Exception.Message}
        return $false
    }
}

function Set-WinCareOperationUndone {
    param(
        [Parameter(Mandatory)][string]$OperationId,
        [Parameter(Mandatory)][string]$UndoJournalPath
    )
    $original=Get-WinCareOperationRecord -OperationPath (Join-Path (Join-Path $script:WinCareState.Root 'Journal') $OperationId)
    $undo=Get-WinCareOperationRecord -OperationPath $UndoJournalPath
    if(-not [bool]$undo.Record.Success -or [string]$undo.Record.State -ne 'Succeeded'){throw 'Undo operation did not complete successfully.'}
    if([string]$undo.Record.Plan.Metadata.OriginalOperationId -ne $OperationId){throw 'Undo journal is not bound to the original operation.'}
    $marker=[ordered]@{
        SchemaVersion=1
        OperationId=$OperationId
        OriginalReceiptHash=[string]$original.Record.ReceiptHash
        UndoOperationId=[string]$undo.Record.OperationId
        UndoReceiptHash=[string]$undo.Record.ReceiptHash
        UndoneAt=[datetime]::UtcNow.ToString('o')
    }
    $path=Get-WinCareUndoMarkerPath $OperationId
    Write-WinCareProtectedJson -LiteralPath $path -InputObject $marker -Purpose 'WinCare.UndoMarker'
    return $path
}

function Get-WinCareUndoableOperation {
    [CmdletBinding()]
    param([ValidateRange(1,500)][int]$Limit=50)
    $folder=Join-Path $script:WinCareState.Root 'Journal'
    if(-not(Test-Path -LiteralPath $folder -PathType Container)){return @()}
    $operations=[Collections.Generic.List[object]]::new()
    foreach($directory in Get-ChildItem -LiteralPath $folder -Directory -ErrorAction SilentlyContinue|Sort-Object LastWriteTimeUtc -Descending){
        if($operations.Count -ge $Limit){break}
        if($directory.Name -notmatch '^[a-f0-9]{32}$'){continue}
        try{
            $container=Get-WinCareOperationRecord -OperationPath $directory.FullName
            $record=$container.Record
            if([string]$record.State -ne 'Succeeded' -or -not [bool]$record.Success -or (Test-WinCareOperationUndone -OperationId ([string]$record.OperationId))){continue}
            $undoable=[Collections.Generic.List[object]]::new()
            foreach($entry in @($record.Results)){
                if([bool](Get-WinCarePropertyValue $entry 'Rollback' $false) -or -not [bool]$entry.Result.Success -or -not [bool]$entry.Action.Reversible){continue}
                try{$descriptor=Resolve-WinCareCompensatorDescriptor -Action $entry.Action -Result $entry.Result;if($descriptor){$undoable.Add($entry)}}catch{Write-WinCareLog -Level Warning -Message 'Skipping an invalid recovery descriptor.' -Data @{operationId=$record.OperationId;actionId=$entry.Action.Id;error=$_.Exception.Message}}
            }
            if($undoable.Count -eq 0){continue}
            $operations.Add([pscustomobject]@{
                Id=[string]$record.OperationId
                Name=[string]$record.Title
                StartedAt=[datetime]$record.StartedAt
                FinishedAt=[datetime]$record.FinishedAt
                ActionCount=$undoable.Count
                Path=$directory.FullName
                FullName=$directory.FullName
                ReceiptHash=[string]$record.ReceiptHash
                Record=$record
            })
        }catch{
            Write-WinCareLog -Level Warning -Message 'Skipping an invalid recovery journal.' -Data @{path=$directory.FullName;error=$_.Exception.Message}
        }
    }
    @($operations)
}

function New-WinCareUndoPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Operation)
    $path=if($Operation.Path){[string]$Operation.Path}else{Join-Path (Join-Path $script:WinCareState.Root 'Journal') ([string]$Operation.Id)}
    $container=Get-WinCareOperationRecord -OperationPath $path
    $record=$container.Record
    if([string]$record.State -ne 'Succeeded' -or -not [bool]$record.Success){throw 'Only successfully completed operations can be undone.'}
    if(Test-WinCareOperationUndone -OperationId ([string]$record.OperationId)){throw 'This operation has already been undone.'}
    $plan=New-WinCarePlan -Title "Undo: $($record.Title)" -Description "Reverse supported actions from operation $($record.OperationId)." -SourceRecords @($record.SourceRecords)
    $plan.Metadata=[ordered]@{
        Kind='Undo'
        OriginalOperationId=[string]$record.OperationId
        OriginalReceiptHash=[string]$record.ReceiptHash
        OriginalPlanHash=[string]$record.PlanHash
    }
    $entries=@($record.Results)
    [array]::Reverse($entries)
    foreach($entry in $entries){
        if([bool](Get-WinCarePropertyValue $entry 'Rollback' $false) -or -not [bool]$entry.Result.Success -or -not [bool]$entry.Action.Reversible){continue}
        $descriptor=Resolve-WinCareCompensatorDescriptor -Action $entry.Action -Result $entry.Result
        if(-not $descriptor){continue}
        $action=New-WinCareAction -Type ([string]$descriptor.Type) -Label ("Undo: {0}" -f [string]$entry.Action.Label) -Risk ([string]$descriptor.Contract.MinimumRisk) -Parameters $descriptor.Parameters -RequiresAdmin ([bool]$descriptor.RequiresAdmin) -Reversible $false -TimeoutSeconds ([Math]::Min([int]$entry.Action.TimeoutSeconds,[int]$descriptor.Contract.MaximumTimeout)) -Tags @('Undo','Recovery') -SourceRecords @($entry.Action.SourceRecords) -RecoveryDescription 'This action reverses a previously completed operation and is itself not automatically reversible.'
        $null=Add-WinCarePlanAction -Plan $plan -Action $action
    }
    if($plan.Actions.Count -eq 0){throw 'No valid compensating actions remain available for this operation.'}
    $validation=Test-WinCarePlanContract -Plan $plan
    if(-not $validation.Success){throw "Undo plan failed validation: $($validation.Message)"}
    return $plan
}

function Get-WinCareInterruptedOperation {
    [CmdletBinding()]
    param([ValidateRange(1,500)][int]$Limit=100)
    $root=Join-Path $script:WinCareState.Root 'Journal'
    if(-not(Test-Path -LiteralPath $root -PathType Container)){return @()}
    $items=[Collections.Generic.List[object]]::new()
    foreach($directory in Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue|Sort-Object LastWriteTimeUtc -Descending){
        if($items.Count -ge $Limit){break}
        $recordPath=Join-Path $directory.FullName 'operation.json'
        if(-not(Test-Path -LiteralPath $recordPath -PathType Leaf)){continue}
        try{
            $record=Get-Content -LiteralPath $recordPath -Raw|ConvertFrom-Json -Depth 60
            if([string]$record.State -notin @('Interrupted','FailedWithRollbackWarnings')){continue}
            $integrity=Test-WinCareOperationJournalIntegrity -OperationPath $directory.FullName
            $items.Add([pscustomobject]@{OperationId=$record.OperationId;Title=$record.Title;State=$record.State;StartedAt=$record.StartedAt;FinishedAt=$record.FinishedAt;RollbackState=$record.RollbackState;IntegrityValid=$integrity.Valid;Path=$directory.FullName;Warnings=@($record.Warnings)})
        }catch { Write-Verbose 'A best-effort operation was unavailable.' }
    }
    @($items)
}


