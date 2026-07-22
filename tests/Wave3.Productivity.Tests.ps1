

Describe 'Wave-three productivity convergence contracts' {
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
      foreach($name in @('PowerSessions','ColorStudio','Notes','RemoteSupport','Browser','Cache','Backups','Journal','Logs','Reports','Extensions')){
        New-Item -ItemType Directory -Path (Join-Path $TestDrive $name) -Force|Out-Null
      }
    }

    It 'registers and dispatches every wave-three typed action' {
      $expected=@(
        'StartPowerSession','StopPowerSession','SetWindowBounds','SetWindowTopmost','ActivateWindow','ReleaseLocalInput',
        'ReplaceColorPaletteStore','WriteLocalNote','RemoveLocalNote','RestoreLocalNote','ReplaceRemoteConsentStore','TerminateRemoteSupportProcess'
      )
      $contracts=Get-WinCareActionContractTable
      foreach($type in $expected){$contracts.Keys|Should Contain $type}
      $dispatch=(Get-Command Invoke-WinCareActionInternal).ScriptBlock.ToString()
      foreach($type in $expected){$dispatch|Should Match ("'"+[regex]::Escape($type)+"'")}
    }

    It 'keeps sensitive productivity policy denied by default' {
      $policy=Get-WinCareDefaultPolicy
      $policy.AllowAwayModePowerSession|Should Be $false
      $policy.AllowIndefinitePowerSession|Should Be $false
      $policy.AllowRemoteSupportTermination|Should Be $false
      $policy.MaximumPowerSessionMinutes|Should -BeGreaterOrEqual 5
      $policy.MaximumRemoteConsentMinutes|Should -BeGreaterOrEqual 5
    }

    It 'requires a runtime-bound power-session compensator' {
      $action=New-WinCareAction -Type StartPowerSession -Label Awake -Risk Low -Parameters @{Id='0123456789abcdef0123456789abcdef';Mode='System';PreventDisplay=$false;AwayMode=$false;ExpiresAt=[datetime]::UtcNow.AddMinutes(30).ToString('o');RefreshSeconds=30;Reason='test'} -Reversible $true
      (Test-WinCareActionContract $action).Success|Should Be $true
      $action.Compensator|Should BeNullOrEmpty
      $result=New-WinCareResult -Success $true -Message Started -Data @{Compensator=@{Type='StopPowerSession';Parameters=@{Id=$action.Parameters.Id;ExpectedHostProcessId=123;ExpectedHostProcessStartTime=[datetime]::UtcNow.ToString('o')};RequiresAdmin=$false}}
      (Resolve-WinCareCompensatorDescriptor -Action $action -Result $result).Type|Should Be StopPowerSession
    }

    It 'uses revisioned and integrity-bound power-session records' {
      $provider=(Get-Command Write-WinCarePowerSessionRecord).ScriptBlock.ToString()
      $provider|Should Match 'ExpectedRevision'
      $provider|Should Match 'PowerSession.*Mutex|WinCare\.PowerSession'
      $host=Get-Content (Join-Path $script:WinCareModuleRoot 'Host\PowerSessionHost.ps1') -Raw
      $host|Should Match 'ExpectedModuleTreeHash'
      $host|Should Match 'SHA256'
      $sessionHost=(Get-Command Invoke-WinCarePowerSessionHost).ScriptBlock.ToString()
      $sessionHost|Should Match "Status='Active'"
      $sessionHost|Should Match 'HostProcessId -ne \$PID'
      $starter=(Get-Command Invoke-WinCareStartPowerSessionAction).ScriptBlock.ToString()
      $starter|Should Match 'execution-state handshake'
      $starter|Should Match "Status -eq 'Active'"
      $native=Get-Content (Join-Path $script:WinCareModuleRoot 'Native\WinCare.Productivity.cs') -Raw
      $native|Should Match 'SetThreadExecutionState'
      $native|Should Match 'SWP_NOZORDER'
      $native|Should Match 'MOUSEINPUT'
    }

    It 'keeps the standalone power host independent of private module functions' {
      $host=Get-Content -LiteralPath (Join-Path $script:WinCareModuleRoot 'Host\PowerSessionHost.ps1') -Raw
      $host|Should Match 'function Get-PowerSessionModuleTreeHash'
      $host|Should Match 'function Assert-PowerSessionNoReparse'
      $host|Should Match 'Set-StrictMode -Version Latest'
      $host|Should Not -Match '(?m)^if\(\(Get-WinCareModuleTreeHash\)'
      $host|Should Match 'Get-PowerSessionModuleTreeHash -Root \$moduleRoot'
    }

    It 'keeps power-session discovery read-only while deriving effective terminal state' {
      $source=(Get-Command Get-WinCarePowerSession).ScriptBlock.ToString()
      $source|Should Not -Match 'Write-WinCarePowerSessionRecord'
      $source|Should Match 'StoredStatus'
      $source|Should Match "effectiveStatus='Failed'"
      $source|Should Match "effectiveStatus='Expired'"
    }

    It 'binds window mutations to exact preview state and verified recovery' {
      $contracts=Get-WinCareActionContractTable
      foreach($name in @('ExpectedCurrentLeft','ExpectedCurrentTop','ExpectedCurrentWidth','ExpectedCurrentHeight')){
        $contracts.SetWindowBounds.RequiredParameters|Should Contain $name
      }
      $contracts.SetWindowTopmost.RequiredParameters|Should Contain 'ExpectedCurrentTopmost'
      $bounds=(Get-Command Invoke-WinCareSetWindowBoundsAction).ScriptBlock.ToString()
      $bounds|Should Match 'WindowStateChanged'
      $bounds|Should Match 'MonitorStateChanged'
      $bounds|Should Match 'restored and verified'
      $topmost=(Get-Command Invoke-WinCareSetWindowTopmostAction).ScriptBlock.ToString()
      $topmost|Should Match 'WindowStateChanged'
      $topmost|Should Match 'restored and verified'
    }

    It 'bounds remote-support configuration traversal without following reparse points' {
      $walker=(Get-Command Get-WinCareBoundedRemoteSupportConfigFile).ScriptBlock.ToString()
      $walker|Should Match 'MaximumFiles'
      $walker|Should Match 'MaximumDepth'
      $walker|Should Match 'MaximumDirectories'
      $walker|Should Match 'ReparsePoint'
      $surface=(Get-Command Get-WinCareRemoteSupportSurface).ScriptBlock.ToString()
      $surface|Should Match 'Get-WinCareBoundedRemoteSupportConfigFile'
      $surface|Should Not -Match 'Get-ChildItem[^\r\n]*-Recurse'
    }

    It 'preserves local-note privacy while allowing explicit local search' {
      $plan=New-WinCareLocalNotePlan -Title 'Private test' -Body 'private searchable phrase' -Tags @('test') -Private $true -Pinned $true
      $result=Invoke-WinCareWriteLocalNoteAction -Action $plan.Actions[0]
      $result.Success|Should Be $true
      $default=Get-WinCareLocalNote -Id $plan.Actions[0].Parameters.Id -IncludeBody|Select-Object -First 1
      $default.Body|Should Be '[PRIVATE NOTE BODY REDACTED]'
      $explicit=Get-WinCareLocalNote -Id $default.Id -IncludeBody -IncludePrivateBody|Select-Object -First 1
      $explicit.Body|Should Be 'private searchable phrase'
      @(Search-WinCareLocalNote -Query 'searchable').Count|Should Be 1
      $remove=New-WinCareLocalNoteRemovePlan -Id $default.Id
      (Invoke-WinCareRemoveLocalNoteAction -Action $remove.Actions[0]).Success|Should Be $true
    }

    It 'rejects tampered local-note recovery snapshots' {
      $snapshot=[ordered]@{Exists=$false;Metadata=$null;Body='';Hash=('0'*64)}
      {Set-WinCareLocalNoteSnapshotInternal -Id '0123456789abcdef0123456789abcdef' -Snapshot $snapshot}| Should Throw
    }

    It 'uses stable empty-store hashes for first palette and consent mutations' {
      (Get-WinCareColorPaletteStoreHash (Get-WinCareDefaultColorPaletteStore))|Should Be (Get-WinCareColorPaletteStoreHash (Get-WinCareDefaultColorPaletteStore))
      (Get-WinCareRemoteConsentStoreHash (Get-WinCareDefaultRemoteConsentStore))|Should Be (Get-WinCareRemoteConsentStoreHash (Get-WinCareDefaultRemoteConsentStore))
    }

    It 'accepts governed palette additions and exact rollback descriptors' {
      $color=ConvertTo-WinCareColorRecord -Red 18 -Green 52 -Blue 86 -Name 'Test'
      $plan=New-WinCareColorPaletteAddPlan -Color $color -Tags @('test')
      $plan.Actions[0].Type|Should Be ReplaceColorPaletteStore
      $plan.Actions[0].Compensator.Type|Should Be ReplaceColorPaletteStore
      (Invoke-WinCareReplaceColorPaletteStoreAction -Action $plan.Actions[0]).Success|Should Be $true
      @(Get-WinCareColorPalette).Count|Should Be 1
    }

    It 'classifies broad browser permissions without installing a counter extension' {
      Get-WinCareBrowserPermissionRisk @('tabs')|Should Be Moderate
      Get-WinCareBrowserPermissionRisk @('*://*/*')|Should Be High
      Get-WinCareBrowserPermissionRisk @('nativeMessaging')|Should Be Critical
      (Get-Command Get-WinCareBrowserWorkspaceSummary).ScriptBlock.ToString()|Should Match 'TabCountAvailable=\$false'
    }

    It 'models remote-support consent as a closed expiring lifecycle' {
      $plan=New-WinCareRemoteSupportConsentPlan -ToolId rustdesk -Reason 'Support session' -DurationMinutes 30
      $plan.Actions[0].Type|Should Be ReplaceRemoteConsentStore
      (Invoke-WinCareReplaceRemoteConsentStoreAction -Action $plan.Actions[0]).Success|Should Be $true
      $consent=Get-WinCareRemoteSupportConsent|Select-Object -First 1
      $consent.Status|Should Be Prepared
      $active=New-WinCareRemoteSupportConsentStatePlan -Id $consent.Id -State Active
      (Invoke-WinCareReplaceRemoteConsentStoreAction -Action $active.Actions[0]).Success|Should Be $true
      $raw=Get-WinCareRemoteConsentStore;$raw.Records[0].ExpiresAt=[datetime]::UtcNow.AddMinutes(-1).ToString('o');$raw.UpdatedAt=[datetime]::UtcNow.ToString('o');$raw.Revision=[long]$raw.Revision+1
      $null=Set-WinCareRemoteConsentStoreInternal -Store $raw
      (Get-WinCareRemoteSupportConsent -IncludeTerminal|Select-Object -First 1).Status|Should Be Expired
      {New-WinCareRemoteSupportConsentStatePlan -Id $consent.Id -State Closed}| Should Throw
      (New-WinCareRemoteSupportExpiryReconciliationPlan).Actions[0].Type|Should Be ReplaceRemoteConsentStore
    }

    It 'does not admit state-changing productivity commands to the read-only broker' {
      $blocked=@('power-start','power-stop','window-zone-set','window-topmost','window-activate','input-release','color-add','color-remove','note-save','note-remove','remote-consent-create','remote-consent-state','remote-consent-expire','remote-emergency')
      foreach($command in $blocked){Get-WinCareBrokerReadOnlyCommandName|Should Not -Contain $command}
      foreach($command in @('power-sessions','windows','monitors','window-zones','color-capture','color-palette','image-metadata','browsers','browser-extensions','remote-support','remote-consent')){Get-WinCareBrokerReadOnlyCommandName|Should Contain $command}
      Get-WinCareBrokerReadOnlyCommandName|Should Not -Contain 'notes'
    }

    It 'routes every wave-three workspace and headless command' {
      $actions=@(Get-WinCareMainMenuItems|ForEach-Object Action)
      foreach($action in @('Power','Windows','Color','Notes','Browser','RemoteSupport')){$actions|Should Contain $action}
      $commands=Get-WinCareHeadlessCommandName
      foreach($command in @('power-sessions','power-start','window-zone-set','color-capture','note-save','browser-extensions','remote-consent-expire','remote-emergency')){$commands|Should Contain $command}
    }

    It 'admits only data-only extension command aliases to approved routes' {
      $manifest=[ordered]@{schemaVersion=1;id='test.commands';name='Test Commands';version='1.0.0';publisher='Test';description='';minimumWinCareVersion='4.2.0';files=@(@{path='commands.json';sha256=('a'*64);bytes=10});catalogFiles=@();knowledgeFiles=@();commandFiles=@('commands.json');sourceRecords=@('W3-CONTEXT-MODE-ALIASES')}
      Test-WinCareExtensionManifest $manifest|Should Be $true
      $manifest.commandFiles=@('commands.ps1')
      {Test-WinCareExtensionManifest $manifest}| Should Throw
    }

    It 'fails closed for mutations when durable palette state is corrupt' {
      $path=Get-WinCareColorPalettePath
      Write-WinCareProtectedJson -LiteralPath $path -InputObject ([ordered]@{SchemaVersion=99;Revision=0;UpdatedAt='invalid';Entries=@()}) -Purpose 'WinCare.ColorPalette'
      @(Get-WinCareColorPalette).Count|Should Be 0
      $color=ConvertTo-WinCareColorRecord -Red 1 -Green 2 -Blue 3
      {New-WinCareColorPaletteAddPlan -Color $color}|Should Throw
    }

    It 'fails closed for mutations when durable consent state is corrupt' {
      $path=Get-WinCareRemoteConsentPath
      New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force|Out-Null
      Write-WinCareProtectedJson -LiteralPath $path -InputObject ([ordered]@{SchemaVersion=99;Revision=0;UpdatedAt='invalid';Records=@()}) -Purpose 'WinCare.RemoteConsent'
      @(Get-WinCareRemoteSupportConsent).Count|Should Be 0
      {New-WinCareRemoteSupportConsentPlan -ToolId rustdesk -Reason 'test' -DurationMinutes 30}|Should Throw
    }

    It 'bounds image metadata before decoder allocation' {
      $path=Join-Path $TestDrive 'oversized.png'
      $stream=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
      try{$stream.SetLength(257MB)}finally{$stream.Dispose()}
      {Get-WinCareImageMetadata -LiteralPath $path}| Should Throw
    }

    It 'confines browser extension traversal and refuses arbitrary Firefox package hashing' {
      $source=(Get-Command Get-WinCareBrowserExtensionInventory).ScriptBlock.ToString()
      $source|Should Match 'ReparsePoint'
      $source|Should Match 'Assert-WinCareSafePath'
      $source|Should Match 'PathConfined'
      $source|Should Match 'scanLimit'
      $source|Should Not -Match 'Get-ChildItem[^\r\n]*-Recurse'
    }

    It 'fails closed on incomplete note pairs and literalizes note queries' {
      $id='0123456789abcdef0123456789abcdef';$paths=Get-WinCareLocalNotePaths -Id $id
      Set-Content -LiteralPath $paths.Body -Value 'orphan' -Encoding utf8
      {Get-WinCareLocalNoteSnapshot -Id $id}| Should Throw
      Remove-Item -LiteralPath $paths.Body -Force
      $source=(Get-Command Get-WinCareLocalNote).ScriptBlock.ToString()
      $source|Should Match 'IndexOf'
      $source|Should Not -Match '-notlike'
    }

    It 'keeps private notes outside authenticated read-only broker reports' {
      Get-WinCareBrokerReadOnlyCommandName|Should Not -Contain 'notes'
      $report=(Get-Command New-WinCareSystemReportData).ScriptBlock.ToString()
      $report|Should Match 'BodiesIncluded=\$false'
      $report|Should Not -Match 'RemoteConsent = @\(Get-WinCareRemoteSupportConsent -IncludeTerminal\)\s*$'
    }

    It 'uses the configured browser extension inventory bound in summaries' {
      $source=(Get-Command Get-WinCareBrowserWorkspaceSummary).ScriptBlock.ToString()
      $source|Should Match "Get-WinCareConfig 'BrowserExtensionInventoryLimit'"
      $source|Should Not -Match '-Limit 10000'
    }

    It 'keeps report data body-free and remote configuration value-free' {
      $reportBody=(Get-Command New-WinCareSystemReportData).ScriptBlock.ToString()
      $reportBody|Should Match 'BodiesIncluded=\$false'
      $reportBody|Should Not -Match 'IncludePrivateBody'
      (Get-Command Get-WinCareRemoteSupportSurface).ScriptBlock.ToString()|Should Match 'ValuesCollected=\$false'
    }
  }
}
