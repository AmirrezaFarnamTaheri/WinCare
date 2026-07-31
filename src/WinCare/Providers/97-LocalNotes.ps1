function Get-WinCareLocalNotesRoot {
    Ensure-WinCareState
    Get-WinCareBridgeStateRoot 'Notes'
}

function Get-WinCareLocalNotePaths {
    param([Parameter(Mandatory)][string]$Id)
    if($Id -notmatch '^[a-f0-9]{32}$'){throw 'Local note ID is invalid.'}
    $root=Get-WinCareLocalNotesRoot
    [pscustomobject]@{Metadata=Assert-WinCareSafePath -LiteralPath (Join-Path $root "$Id.json") -AllowedRoots @($root) -AllowMissing;Body=Assert-WinCareSafePath -LiteralPath (Join-Path $root "$Id.md") -AllowedRoots @($root) -AllowMissing}
}

function Get-WinCareLocalNoteSnapshot {
    param([Parameter(Mandatory)][string]$Id)
    $paths=Get-WinCareLocalNotePaths -Id $Id
    $hasMetadata=Test-Path -LiteralPath $paths.Metadata -PathType Leaf;$hasBody=Test-Path -LiteralPath $paths.Body -PathType Leaf
    if($hasMetadata -ne $hasBody){throw 'Local note state is incomplete; metadata and Markdown body must either both exist or both be absent.'}
    $metadata=$null;$body=''
    if($hasMetadata){
        $metadata=Read-WinCareJsonHashtable -LiteralPath $paths.Metadata;$null=Test-WinCareLocalNoteMetadata -Metadata $metadata
        if([string]$metadata.Id -ne $Id){throw 'Local note metadata identity does not match its file name.'}
        $bodyItem=Get-Item -LiteralPath $paths.Body -Force -ErrorAction Stop
        if(($bodyItem.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0 -or $bodyItem.Length -gt 1MB){throw 'Local note body is unsafe or exceeds 1 MiB.'}
        $body=Read-WinCareBoundedText -LiteralPath $paths.Body -MaximumBytes 1048576
    }
    $snapshot=[ordered]@{Exists=[bool]$hasMetadata;Metadata=$metadata;Body=$body}
    $snapshot.Hash=Get-WinCareCanonicalObjectHash -InputObject $snapshot
    $snapshot
}

function Test-WinCareLocalNoteMetadata {
    param([Parameter(Mandatory)][Collections.IDictionary]$Metadata)
    $null=Test-WinCareStrictObjectKeys -InputObject $Metadata -AllowedKeys @('SchemaVersion','Id','Title','Tags','Private','Pinned','CreatedAt','UpdatedAt','LinkedOperationIds','SourceRecords') -Context 'local note metadata'
    if([int]$Metadata.SchemaVersion -ne 1 -or [string]$Metadata.Id -notmatch '^[a-f0-9]{32}$'){throw 'Local note schema or identity is invalid.'}
    if([string]::IsNullOrWhiteSpace([string]$Metadata.Title) -or ([string]$Metadata.Title).Length -gt 240){throw 'Local note title is invalid.'}
    if($Metadata.Private -isnot [bool] -or $Metadata.Pinned -isnot [bool]){throw 'Local note flags must be Boolean.'}
    $tags=@($Metadata.Tags);if($tags.Count -gt 32 -or @($tags|Where-Object{$_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) -or ([string]$_).Length -gt 64}).Count -or @($tags|Sort-Object -Unique).Count -ne $tags.Count){throw 'Local note tags are invalid or duplicated.'}
    $links=@($Metadata.LinkedOperationIds);if($links.Count -gt 32 -or @($links|Where-Object{[string]$_ -notmatch '^[a-f0-9]{32}$'}).Count -or @($links|Sort-Object -Unique).Count -ne $links.Count){throw 'Local note operation links are invalid or duplicated.'}
    $sources=@($Metadata.SourceRecords);if($sources.Count -gt 64 -or @($sources|Where-Object{$_ -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$_) -or ([string]$_).Length -gt 160}).Count -or @($sources|Sort-Object -Unique).Count -ne $sources.Count){throw 'Local note source records are invalid or duplicated.'}
    $created=[datetime]::MinValue;$updated=[datetime]::MinValue
    if(-not[datetime]::TryParse([string]$Metadata.CreatedAt,[ref]$created) -or -not[datetime]::TryParse([string]$Metadata.UpdatedAt,[ref]$updated) -or $updated.ToUniversalTime() -lt $created.ToUniversalTime()){throw 'Local note timestamps are invalid.'}
    $true
}

function Get-WinCareLocalNote {
    [CmdletBinding()]param([string]$Id,[string]$Query,[switch]$IncludeBody,[switch]$IncludePrivateBody)
    $root=Get-WinCareLocalNotesRoot;$paths=if($Id){@((Get-WinCareLocalNotePaths -Id $Id).Metadata)}else{@(Get-ChildItem -LiteralPath $root -Filter '*.json' -File -ErrorAction SilentlyContinue|Select-Object -ExpandProperty FullName)}
    $items=[Collections.Generic.List[object]]::new()
    foreach($path in $paths){
        try{
            $metadata=Read-WinCareJsonHashtable -LiteralPath $path;$null=Test-WinCareLocalNoteMetadata -Metadata $metadata
            $snapshot=Get-WinCareLocalNoteSnapshot -Id ([string]$metadata.Id);$body=[string]$snapshot.Body
            if($Query){$haystack="$( $metadata.Title )`n$(@($metadata.Tags)-join ' ')`n$body";if($haystack.IndexOf($Query,[StringComparison]::OrdinalIgnoreCase) -lt 0){continue}}
            $canShow=$IncludeBody -and (-not [bool]$metadata.Private -or $IncludePrivateBody)
            $items.Add([pscustomobject]@{Id=$metadata.Id;Title=$metadata.Title;Tags=@($metadata.Tags);Private=[bool]$metadata.Private;Pinned=[bool]$metadata.Pinned;CreatedAt=$metadata.CreatedAt;UpdatedAt=$metadata.UpdatedAt;LinkedOperationIds=@($metadata.LinkedOperationIds);SourceRecords=@($metadata.SourceRecords);Body=if($canShow){$body}elseif($IncludeBody){'[PRIVATE NOTE BODY REDACTED]'}else{''};Bytes=[Text.Encoding]::UTF8.GetByteCount($body);Hash=$snapshot.Hash})
        }catch{Write-WinCareLog -Level Warning -Message 'Ignoring an invalid local note.' -Data @{path=$path;error=$_.Exception.Message}}
    }
    @($items|Sort-Object @{Expression='Pinned';Descending=$true},UpdatedAt -Descending)
}

function New-WinCareLocalNotePlan {
    [CmdletBinding()]
    param(
        [string]$Id,
        [Parameter(Mandatory)][string]$Title,
        [AllowEmptyString()][string]$Body='',
        [string[]]$Tags=@(),
        [bool]$Private=$false,
        [bool]$Pinned=$false,
        [string[]]$LinkedOperationIds=@(),
        [string[]]$SourceRecords=@('SRC:9ff8a9bd56')
    )
    if([Text.Encoding]::UTF8.GetByteCount($Body) -gt 1MB){throw 'Local note body exceeds 1 MiB.'}
    if(-not $Id){$Id=[guid]::NewGuid().ToString('N')}
    $before=Get-WinCareLocalNoteSnapshot -Id $Id;$created=if($before.Exists -and $before.Metadata){[string]$before.Metadata.CreatedAt}else{[datetime]::UtcNow.ToString('o')}
    $metadata=[ordered]@{SchemaVersion=1;Id=$Id;Title=$Title.Trim();Tags=@($Tags|Sort-Object -Unique);Private=$Private;Pinned=$Pinned;CreatedAt=$created;UpdatedAt=[datetime]::UtcNow.ToString('o');LinkedOperationIds=@($LinkedOperationIds|Sort-Object -Unique);SourceRecords=@($SourceRecords|Sort-Object -Unique)};$null=Test-WinCareLocalNoteMetadata -Metadata $metadata
    $after=[ordered]@{Exists=$true;Metadata=$metadata;Body=$Body};$after.Hash=Get-WinCareCanonicalObjectHash -InputObject $after
    $action=New-WinCareAction -Type 'WriteLocalNote' -Label "Save local note: $Title" -Risk Low -Parameters @{Id=$Id;Metadata=$metadata;BodyBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Body));ExpectedBeforeHash=$before.Hash} -RequiresAdmin $false -Reversible $true -Compensator @{Type='RestoreLocalNote';Parameters=@{Id=$Id;Snapshot=$before;ExpectedCurrentHash=$after.Hash}} -TimeoutSeconds 30 -Tags @('Notes','LocalFirst','Markdown') -SourceRecords $SourceRecords -RecoveryDescription 'Restore the exact previous note metadata and Markdown body, or remove a newly created note.'
    New-WinCarePlan -Title 'Save local note' -Actions @($action) -SourceRecords $SourceRecords
}

function New-WinCareLocalNoteRemovePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $before=Get-WinCareLocalNoteSnapshot -Id $Id;if(-not $before.Exists){throw 'Local note does not exist.'}
    $after=[ordered]@{Exists=$false;Metadata=$null;Body=''};$after.Hash=Get-WinCareCanonicalObjectHash -InputObject $after
    $action=New-WinCareAction -Type 'RemoveLocalNote' -Label "Remove local note: $($before.Metadata.Title)" -Risk Moderate -Parameters @{Id=$Id;ExpectedBeforeHash=$before.Hash} -RequiresAdmin $false -Reversible $true -Compensator @{Type='RestoreLocalNote';Parameters=@{Id=$Id;Snapshot=$before;ExpectedCurrentHash=$after.Hash}} -TimeoutSeconds 30 -Tags @('Notes','LocalFirst','Markdown') -SourceRecords @('SRC:9ff8a9bd56') -RecoveryDescription 'Restore the exact removed Markdown note and metadata.'
    New-WinCarePlan -Title 'Remove local note' -Actions @($action) -SourceRecords @('SRC:9ff8a9bd56')
}

function Set-WinCareLocalNoteSnapshotInternal {
    param([Parameter(Mandatory)][string]$Id,[Parameter(Mandatory)][Collections.IDictionary]$Snapshot)
    $null=Test-WinCareStrictObjectKeys -InputObject $Snapshot -AllowedKeys @('Exists','Metadata','Body','Hash') -Context 'local note snapshot'
    if($Snapshot.Exists -isnot [bool]){throw 'Local note snapshot existence flag must be Boolean.'}
    $body=[string]$Snapshot.Body
    if([Text.Encoding]::UTF8.GetByteCount($body) -gt 1MB){throw 'Local note snapshot body exceeds 1 MiB.'}
    $metadata=$null
    if([bool]$Snapshot.Exists){
        $metadata=ConvertTo-WinCareParameterDictionary $Snapshot.Metadata
        $null=Test-WinCareLocalNoteMetadata -Metadata $metadata
        if([string]$metadata.Id -ne $Id){throw 'Local note snapshot identity mismatch.'}
    }elseif($Snapshot.Metadata){throw 'An absent local note snapshot cannot contain metadata.'}
    $canonical=[ordered]@{Exists=[bool]$Snapshot.Exists;Metadata=$metadata;Body=$body}
    $expectedHash=Get-WinCareCanonicalObjectHash -InputObject $canonical
    if([string]$Snapshot.Hash -ne $expectedHash){throw 'Local note snapshot hash is invalid.'}
    $paths=Get-WinCareLocalNotePaths -Id $Id
    if([bool]$Snapshot.Exists){
        Write-WinCareAtomicText -LiteralPath $paths.Body -Text $body
        Write-WinCareAtomicJson -LiteralPath $paths.Metadata -InputObject $metadata
    }else{
        foreach($path in @($paths.Metadata,$paths.Body)){if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force -ErrorAction Stop}}
    }
    $actual=Get-WinCareLocalNoteSnapshot -Id $Id
    if([string]$actual.Hash -ne $expectedHash){throw 'Local note snapshot verification failed.'}
    $actual
}

function Invoke-WinCareWriteLocalNoteAction {
    param([object]$Action)
    $p=$Action.Parameters;$id=[string]$p.Id;$before=Get-WinCareLocalNoteSnapshot -Id $id
    if([string]$before.Hash -ne [string]$p.ExpectedBeforeHash){return New-WinCareResult -Success $false -Status Blocked -Code 'LocalNoteChanged' -Message 'Local note changed after preview.' -ExitCode 74}
    try{
        $metadata=ConvertTo-WinCareParameterDictionary $p.Metadata;$null=Test-WinCareLocalNoteMetadata -Metadata $metadata
        if([string]$metadata.Id -ne $id){throw 'Local note identity mismatch.'}
        $body=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$p.BodyBase64))
        $target=[ordered]@{Exists=$true;Metadata=$metadata;Body=$body};$target.Hash=Get-WinCareCanonicalObjectHash -InputObject $target
        $after=Set-WinCareLocalNoteSnapshotInternal -Id $id -Snapshot $target
        New-WinCareResult -Success $true -Message 'Local note saved and verified.' -Data @{Id=$id;Title=$metadata.Title;Hash=$after.Hash}
    }catch{
        $failure=$_.Exception.Message;$warnings=@()
        try{$null=Set-WinCareLocalNoteSnapshotInternal -Id $id -Snapshot $before;$warnings+= 'The previous local-note state was restored after the failed write.'}
        catch{$warnings+="The failed local-note write could not be fully restored: $($_.Exception.Message)"}
        New-WinCareResult -Success $false -Message "Local note could not be saved: $failure" -Warnings $warnings -ExitCode 1
    }
}

function Invoke-WinCareRemoveLocalNoteAction {
    param([object]$Action)
    $p=$Action.Parameters;$id=[string]$p.Id;$before=Get-WinCareLocalNoteSnapshot -Id $id
    if([string]$before.Hash -ne [string]$p.ExpectedBeforeHash){return New-WinCareResult -Success $false -Status Blocked -Code 'LocalNoteChanged' -Message 'Local note changed after preview.' -ExitCode 74}
    $target=[ordered]@{Exists=$false;Metadata=$null;Body=''};$target.Hash=Get-WinCareCanonicalObjectHash -InputObject $target
    try{$after=Set-WinCareLocalNoteSnapshotInternal -Id $id -Snapshot $target;New-WinCareResult -Success $true -Message 'Local note removed and verified.' -Data @{Id=$id;Hash=$after.Hash}}
    catch{
        $failure=$_.Exception.Message;$warnings=@()
        try{$null=Set-WinCareLocalNoteSnapshotInternal -Id $id -Snapshot $before;$warnings+='The removed local note was restored after the failed operation.'}
        catch{$warnings+="The failed note removal could not be fully restored: $($_.Exception.Message)"}
        New-WinCareResult -Success $false -Message "Local note could not be removed: $failure" -Warnings $warnings -ExitCode 1
    }
}

function Invoke-WinCareRestoreLocalNoteAction {
    param([object]$Action)
    $p=$Action.Parameters;$id=[string]$p.Id;$current=Get-WinCareLocalNoteSnapshot -Id $id
    if([string]$current.Hash -ne [string]$p.ExpectedCurrentHash){return New-WinCareResult -Success $false -Status Blocked -Code 'LocalNoteRecoveryConflict' -Message 'Local note recovery refused because the current state changed.' -ExitCode 74}
    try{$target=ConvertTo-WinCareParameterDictionary $p.Snapshot;$after=Set-WinCareLocalNoteSnapshotInternal -Id $id -Snapshot $target;New-WinCareResult -Success $true -Message 'Local note state restored and verified.' -Data @{Id=$id;Hash=$after.Hash}}
    catch{
        $failure=$_.Exception.Message;$warnings=@()
        try{$null=Set-WinCareLocalNoteSnapshotInternal -Id $id -Snapshot $current;$warnings+='The pre-recovery note state was restored after the failed recovery.'}
        catch{$warnings+="The failed note recovery could not restore its starting state: $($_.Exception.Message)"}
        New-WinCareResult -Success $false -Message "Local note recovery failed: $failure" -Warnings $warnings -ExitCode 1
    }
}

function Search-WinCareLocalNote {
    [CmdletBinding()]param([Parameter(Mandatory)][ValidateLength(1,500)][string]$Query,[ValidateRange(1,10000)][int]$Limit=500,[switch]$IncludePrivateBody)
    $results=[Collections.Generic.List[object]]::new()
    foreach($note in @(Get-WinCareLocalNote -IncludeBody -IncludePrivateBody)){
        $haystack=([string]$note.Title+"`n"+(@($note.Tags)-join ' ')+"`n"+[string]$note.Body)
        if($haystack.IndexOf($Query,[StringComparison]::OrdinalIgnoreCase) -lt 0){continue}
        if([bool]$note.Private -and -not $IncludePrivateBody){$note.Body='[PRIVATE NOTE BODY REDACTED]'}
        $results.Add($note);if($results.Count -ge $Limit){break}
    }
    @($results)
}

