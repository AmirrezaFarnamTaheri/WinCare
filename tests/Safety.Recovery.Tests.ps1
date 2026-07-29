# Pester 5 executes Describe bodies -- and therefore InModuleScope -- during
# Discovery, which runs before any BeforeAll. The module must already be loaded
# at that point, otherwise InModuleScope throws "No modules named 'WinCare' are
# currently loaded" and the entire container fails without running one test.
# This import is deliberately at file scope; the BeforeAll imports below remain
# for run-time state. $PSScriptRoot is used so the path never depends on the
# caller's working directory.
Import-Module (Join-Path $PSScriptRoot '../src/WinCare/WinCare.psd1') -Force -Global


Describe 'Filesystem confinement and integrity' {
    #requires -Version 7.2
BeforeAll { Import-Module "$((Get-Location).Path)\src\WinCare\WinCare.psd1" -Force -Global }
  InModuleScope WinCare {
    BeforeEach { $script:WinCareState=@{Root=$TestDrive;Config=Get-WinCareDefaultConfig;Policy=Get-WinCareDefaultPolicy;SessionId='0123456789abcdef0123456789abcdef';IsAdmin=$false;Capabilities=@{}};foreach($d in 'Cache','Journal','Quarantine','Backups'){New-Item -ItemType Directory -Path (Join-Path $TestDrive $d) -Force|Out-Null} }
    It 'recognizes contained and escaping paths' {
      Test-WinCarePathWithin -Child (Join-Path $TestDrive 'a\b') -Parent $TestDrive | Should -Be $true
      Test-WinCarePathWithin -Child (Join-Path (Split-Path $TestDrive -Parent) 'escape') -Parent $TestDrive | Should -Be $false
    }
    It 'hashes a directory deterministically and detects changes' {
      $root=Join-Path $TestDrive tree;New-Item -ItemType Directory $root|Out-Null;Set-Content (Join-Path $root a.txt) alpha
      $a=Get-WinCarePathContentHash $root;$a|Should -Match '^[a-f0-9]{64}$';Set-Content (Join-Path $root a.txt) beta
      Get-WinCarePathContentHash $root | Should -Not -Be $a
    }
    It 'moves a path without overwriting and verifies hash' {
      $source=Join-Path $TestDrive source.txt;$destination=Join-Path $TestDrive 'out\destination.txt';Set-Content $source value
      $hash=Get-WinCarePathContentHash $source
      $action=New-WinCareAction -Type MoveFile -Label Move -Risk Moderate -Parameters @{Source=$source;Destination=$destination;ExpectedSourceHash=$hash;AllowedRoots=@($TestDrive)}
      (Invoke-WinCareMoveFileAction $action).Success | Should -Be $true
      Test-Path $destination | Should -Be $true
    }
    It 'enforces declared roots for move actions' {
      $source=Join-Path $TestDrive source.txt;Set-Content $source value
      $outside=Join-Path (Split-Path $TestDrive -Parent) ('outside-'+[guid]::NewGuid().ToString('N')+'.txt')
      $hash=Get-WinCarePathContentHash $source
      $action=New-WinCareAction -Type MoveFile -Label Escape -Risk Moderate -Parameters @{Source=$source;Destination=$outside;ExpectedSourceHash=$hash;AllowedRoots=@($TestDrive)}
      {Invoke-WinCareMoveFileAction $action} | Should -Throw
      Test-Path $source | Should -Be $true
      Test-Path $outside | Should -Be $false
    }
    It 'rejects a missing destination below a reparse-point ancestor on Windows' -Skip:(-not $IsWindows) {
      $target=Join-Path $TestDrive target;$link=Join-Path $TestDrive link
      New-Item -ItemType Directory -Path $target|Out-Null
      New-Item -ItemType Junction -Path $link -Target $target|Out-Null
      {Assert-WinCareSafePath -LiteralPath (Join-Path $link 'missing\child.txt') -AllowedRoots @($TestDrive) -AllowMissing}| Should -Throw
    }
    It 'creates a cancellation request only for an active journal' {
      $operationId=[guid]::NewGuid().ToString('N');$root=Join-Path (Join-Path $TestDrive Journal) $operationId
      New-Item -ItemType Directory -Path $root -Force|Out-Null
      @{OperationId=$operationId;State='Running'}|ConvertTo-Json|Set-Content (Join-Path $root operation.json)
      $result=Request-WinCareOperationCancellation -OperationId $operationId
      $result.Success|Should -Be $true
      Test-Path (Join-Path $root cancel)|Should -Be $true
      (Request-WinCareOperationCancellation -OperationId $operationId).Code|Should -Be 'CancellationAlreadyRequested'
    }
    It 'authenticates protected records and rejects tampering' {
      $path=Join-Path $TestDrive Cache\record.json;Write-WinCareProtectedJson -LiteralPath $path -InputObject @{Value=1} -Purpose Test
      (Read-WinCareProtectedJson -LiteralPath $path -Purpose Test -AsHashtable).Value | Should -Be 1
      $doc=Get-Content $path -Raw|ConvertFrom-Json -AsHashtable;$doc.Signature='0'*64;$doc|ConvertTo-Json|Set-Content $path
      {Read-WinCareProtectedJson -LiteralPath $path -Purpose Test}|Should -Throw
    }
    It 'requires an authenticated receipt for every terminal journal' {
      $action=New-WinCareAction -Type ClearClipboard -Label Clear -Risk Moderate
      $plan=New-WinCarePlan -Title Terminal -Actions @($action)
      $journal=New-WinCareOperationJournal -Plan $plan -OperationId ([guid]::NewGuid().ToString('N'))
      Update-WinCareOperationJournal -Journal $journal -State Failed -Patch @{FinishedAt=[datetime]::UtcNow.ToString('o');Success=$false} -Event Failed
      (Test-WinCareOperationJournalIntegrity $journal.Root).Valid|Should -Be $false
      $null=Complete-WinCareOperationReceipt $journal
      (Test-WinCareOperationJournalIntegrity $journal.Root).Valid|Should -Be $true
    }
    It 'reconciles interrupted active journals through the event chain and terminal receipt' {
      $action=New-WinCareAction -Type ClearClipboard -Label Clear -Risk Moderate
      $plan=New-WinCarePlan -Title Interrupted -Actions @($action)
      $journal=New-WinCareOperationJournal -Plan $plan -OperationId ([guid]::NewGuid().ToString('N'))
      Update-WinCareOperationJournal -Journal $journal -State Running -Patch @{CurrentActionId=$action.Id;CurrentActionType=$action.Type} -Event ActionStarted
      @(Repair-WinCareInterruptedOperation).Count|Should -Be 1
      $integrity=Test-WinCareOperationJournalIntegrity $journal.Root
      $integrity.Valid|Should -Be $true
      $integrity.State|Should -Be 'Interrupted'
      Test-Path (Join-Path $journal.Root 'receipt.json')|Should -Be $true
    }
    It 'binds the final operation record to its authenticated receipt' {
      $action=New-WinCareAction -Type ClearClipboard -Label Clear -Risk Moderate
      $plan=New-WinCarePlan -Title Receipt -Actions @($action)
      $operationId=[guid]::NewGuid().ToString('N');$journal=New-WinCareOperationJournal -Plan $plan -OperationId $operationId
      $result=New-WinCareResult -Success $true -Message Done
      Update-WinCareOperationJournal -Journal $journal -State Succeeded -Patch @{FinishedAt=[datetime]::UtcNow.ToString('o');Success=$true;Results=@([pscustomobject]@{Action=$action;Result=$result;Rollback=$false});Warnings=@()} -Event Completed
      $null=Complete-WinCareOperationReceipt $journal
      (Test-WinCareOperationJournalIntegrity $journal.Root).Valid|Should -Be $true
      $record=Get-Content $journal.RecordPath -Raw|ConvertFrom-Json -Depth 60;$record.Results[0].Result.Message='tampered';$record|ConvertTo-Json -Depth 60|Set-Content $journal.RecordPath
      (Test-WinCareOperationJournalIntegrity $journal.Root).Valid|Should -Be $false
    }
  }
}

Describe 'Condition composition' {
  InModuleScope WinCare {
    BeforeEach {$script:WinCareState=@{Config=Get-WinCareDefaultConfig;Policy=Get-WinCareDefaultPolicy;IsAdmin=$false;Capabilities=@{}}}
    It 'supports all, any, only-one, and not' {
      $t=New-WinCareCondition -Kind Context -Parameters @{Name='x'} -Expected 1
      $f=New-WinCareCondition -Kind Context -Parameters @{Name='x'} -Expected 2
      Test-WinCareCondition (New-WinCareCondition -Kind All -Children @($t,$t)) @{x=1} | Should -Be $true
      Test-WinCareCondition (New-WinCareCondition -Kind Any -Children @($f,$t)) @{x=1} | Should -Be $true
      Test-WinCareCondition (New-WinCareCondition -Kind OnlyOne -Children @($f,$t)) @{x=1} | Should -Be $true
      Test-WinCareCondition (New-WinCareCondition -Kind Not -Children @($f)) @{x=1} | Should -Be $true
    }
  }
}
