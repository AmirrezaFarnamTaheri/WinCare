
Describe 'Governed expert-operation contracts' {
    #requires -Version 7.2
BeforeAll { Import-Module "$((Get-Location).Path)\src\WinCare\WinCare.psd1" -Force -Global }
  InModuleScope WinCare {
    BeforeEach {
      $script:WinCareState=@{Config=Get-WinCareDefaultConfig;Policy=Get-WinCareDefaultPolicy;Root=$TestDrive;SessionId='0123456789abcdef0123456789abcdef';IsAdmin=$true;Capabilities=@{};ActionContracts=$null}
      foreach($name in @('Reports','SecurityMaintenance','NetworkExperiments','Instrumentation','Journal','Backups','Logs')){New-Item -ItemType Directory -Path (Join-Path $TestDrive $name) -Force|Out-Null}
    }
    It 'registers every expert action in both contracts and dispatcher' {
      $required=@('SetPageFileConfiguration','RestorePageFileConfiguration','SetSecurityControlState','RestoreSecurityControlState','RunNetworkExperiment','RestoreTcpGlobalSetting','CaptureEtwTrace','QuarantineInjectionSurface','RestoreInjectionSurface')
      $contracts=Get-WinCareActionContractTable
      $dispatch=Get-Content (Join-Path $script:WinCareModuleRoot 'Core\09-Transactions.ps1') -Raw
      foreach($type in $required){$contracts.Keys|Should Contain $type;$dispatch|Should Match ("'"+[regex]::Escape($type)+"'\s*\{")}
    }
    It 'fails closed for authority-bearing expert mutations by default' {
      $p=Get-WinCareDefaultPolicy
      $p.AllowPageFileDisable|Should Be $false
      $p.AllowSecurityControlReduction|Should Be $false
      $p.AllowUacDisable|Should Be $false
      $p.AllowWindowsUpdateServiceDisable|Should Be $false
      $p.AllowNetworkExperiments|Should Be $false
      $p.AllowInjectionSurfaceRemediation|Should Be $false
      $p.DeniedActionTypes|Should Contain 'InjectProcess'
      $p.DeniedActionTypes|Should Contain 'CreateRemoteThread'
      $p.DeniedActionTypes|Should Contain 'WriteProcessMemory'
      $p.DeniedActionTypes|Should Contain 'InstallGlobalHook'
    }
    It 'admits only bounded page-file records' {
      {Test-WinCarePageFileSettingRecord @{Name='C:\pagefile.sys';InitialSizeMB=4096;MaximumSizeMB=2048}}|Should Throw
      {Test-WinCarePageFileSettingRecord @{Name='C:\temp\pagefile.sys';InitialSizeMB=1024;MaximumSizeMB=2048}}|Should Throw
      $body=(Get-Command New-WinCarePageFilePlan).ScriptBlock.ToString()
      $body|Should Match 'CrashDump.PageFileRequired'
      $body|Should Match 'SystemManaged'
    }
    It 'limits UAC disable to disposable labs and all reductions to expiry records' {
      {New-WinCareSecurityMaintenancePlan -Control Uac -DurationMinutes 30 -Reason 'Compatibility test' -EnvironmentClass Workstation}| Should Throw
      $body=(Get-Command Invoke-WinCareSetSecurityControlStateAction).ScriptBlock.ToString()
      $body|Should Match 'Register-WinCareSecurityRestoreTask'
      $body|Should Match 'WinCare.SecurityMaintenance'
      (Get-WinCareSecurityMaintenanceTaskName ('a'*32))|Should Be ('WinCare-SecurityRestore-'+('a'*32))
      (Get-Command Register-WinCareSecurityRestoreTask).ScriptBlock.ToString()|Should Not -Match "TaskPath\s+'\\WinCare\\'"
      $body|Should Match 'Compensator'
      $auto=(Get-Command Invoke-WinCareSecurityMaintenanceAutoRestore).ScriptBlock.ToString()
      $auto|Should Match 'Invoke-WinCareRestoreSecurityControlStateAction'
      $auto|Should Match "Prepared','Armed','Active"
      $auto|Should Match 'SecurityMaintenanceUnexpectedPreparedMutation'
      $activation=(Get-Command Invoke-WinCareSetSecurityControlStateAction).ScriptBlock.ToString()
      $activation|Should Match "Status='Armed'"
      $activation|Should Match '\$mutationStarted=\$true'
      (Get-Command Set-WinCareSecurityControlInternal).ScriptBlock.ToString()|Should Match 'Tamper Protection'
    }
    It 'allows only documented TCP global tokens and measures before mutation' {
      $keys=@((Get-WinCareTcpSettingTokenMap).Keys);$keys.Count|Should Be 4;foreach($key in @('AutoTuningLevel','EcnCapability','ReceiveSideScaling','ReceiveSegmentCoalescing')){$keys|Should Contain $key}
      Get-WinCareTcpAllowedValue AutoTuningLevel|Should Contain normal
      Get-WinCareTcpAllowedValue AutoTuningLevel|Should Not -Contain default
      Get-WinCareTcpAllowedValue Unknown|Should BeNullOrEmpty
      $body=(Get-Command New-WinCareNetworkExperimentPlan).ScriptBlock.ToString()
      $body|Should Match 'Measure-WinCareNetworkEndpoint'
      $body|Should Match 'Baseline'
      (Get-Command Invoke-WinCareRunNetworkExperimentAction).ScriptBlock.ToString()|Should Match 'Automatic rollback'
    }
    It 'absorbs injection value through supported observation and defensive remediation only' {
      $contracts=Get-WinCareActionContractTable
      foreach($forbidden in @('InjectProcess','CreateRemoteThread','WriteProcessMemory','InstallGlobalHook')){$contracts.Keys|Should Not -Contain $forbidden}
      $contracts.Keys|Should Contain CaptureEtwTrace
      $contracts.Keys|Should Contain QuarantineInjectionSurface
      $text=(Get-Content (Join-Path $script:WinCareModuleRoot 'Providers\93-ProcessInstrumentation.ps1') -Raw)
      $text|Should Not -Match 'VirtualAllocEx\s*\('
      $text|Should Not -Match 'WriteProcessMemory\s*\('
      $text|Should Not -Match 'CreateRemoteThread\s*\('
      $text|Should Not -Match 'SetWindowsHookEx\s*\('
      $text|Should Match 'wpr.exe'
      $text|Should Match 'Sysmon/Operational'
      $text|Should Match 'Get-WinCareInjectionSurfaceValueStateHash'
      (Get-Command Invoke-WinCareCaptureEtwTraceAction).ScriptBlock.ToString()|Should Match 'Test-WinCareOperationCancellationRequested'
    }
    It 'requires Critical risk for page-file and UAC reductions' {
      $page=New-WinCareAction -Type SetPageFileConfiguration -Label 'Disable pagefile' -Risk High -Parameters @{Mode='Disabled';Settings=@();BeforeHash=('a'*64)} -RequiresAdmin $true -Reversible $true -TimeoutSeconds 600
      (Test-WinCareActionContract -Action $page).Success|Should Be $false
      (Test-WinCareActionContract -Action (New-WinCareAction -Type SetPageFileConfiguration -Label 'Disable pagefile' -Risk Critical -Parameters @{Mode='Disabled';Settings=@();BeforeHash=('a'*64)} -RequiresAdmin $true -Reversible $true -TimeoutSeconds 600)).Success|Should Be $true
      $uac=New-WinCareAction -Type SetSecurityControlState -Label 'Reduce UAC' -Risk High -Parameters @{Control='Uac';Enabled=$false;DurationMinutes=30;Reason='Disposable compatibility test';EnvironmentClass='DisposableLab';BeforeHash=('b'*64)} -RequiresAdmin $true -Reversible $true -TimeoutSeconds 600
      (Test-WinCareActionContract -Action $uac).Success|Should Be $false
      $uac.Risk='Critical';(Test-WinCareActionContract -Action $uac).Success|Should Be $true
      $uac.Parameters.Enabled=$true;(Test-WinCareActionContract -Action $uac).Success|Should Be $false
    }
    It 'rejects security recovery snapshots that target unexpected authority paths' {
      $payload=[ordered]@{Control='Uac';Available=$true;Enabled=$false;Registry=@(
        [pscustomobject]@{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System';Name='EnableLUA';Exists=$true;Value=1;Kind='DWord';ValueType='System.Int32'},
        [pscustomobject]@{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System';Name='ConsentPromptBehaviorAdmin';Exists=$true;Value=5;Kind='DWord';ValueType='System.Int32'},
        [pscustomobject]@{Path='HKLM:\SOFTWARE\Unexpected';Name='PromptOnSecureDesktop';Exists=$true;Value=1;Kind='DWord';ValueType='System.Int32'}
      );RestartRequired=$true}
      $payload.Hash=Get-WinCareSha256Text -Text (ConvertTo-WinCareCanonicalJson -InputObject $payload -Depth 30)
      {Test-WinCareSecurityControlSnapshot -Snapshot $payload -ExpectedControl Uac}| Should Throw
    }
    It 'binds security and injection recovery to authenticated provider records' {
      $securityBody=(Get-Command Invoke-WinCareRestoreSecurityControlStateAction).ScriptBlock.ToString()
      $securityBody|Should Match 'SecurityMaintenanceSnapshotMismatch'
      $securityBody|Should Match 'record.Before'
      $injectionContract=Get-WinCareActionContract -Type RestoreInjectionSurface
      $injectionContract.Required|Should Contain RecordId
      $injectionContract.Required|Should Not -Contain Snapshot
      $injectionBody=(Get-Command Invoke-WinCareRestoreInjectionSurfaceAction).ScriptBlock.ToString()
      $injectionBody|Should Match 'Get-WinCareInjectionQuarantineRecord'
      $injectionBody|Should Match 'InjectionSurfaceRecordIdentityMismatch'
      (Get-Command Invoke-WinCareQuarantineInjectionSurfaceAction).ScriptBlock.ToString()|Should Match 'Write-WinCareProtectedJson'
    }
    It 'rejects injection recovery descriptors outside exact remediable roots' {
      $snapshot=@{Surface=@{Kind='IFEO.Debugger';Path='HKLM:\SOFTWARE\Unexpected\victim.exe';Name='Debugger'};Values=@(@{Path='HKLM:\SOFTWARE\Unexpected\victim.exe';Name='Debugger';Exists=$true;Value='debugger.exe';Kind='String';ValueType='String'})}
      {Test-WinCareInjectionSurfaceSnapshot -Snapshot $snapshot}| Should Throw
    }
    It 'bounds network experiments and checks cancellation between measurements' {
      $provider=(Get-Content (Join-Path $script:WinCareModuleRoot 'Providers\92-NetworkExperiments.ps1') -Raw)
      $provider|Should Match 'Test-WinCareOperationCancellationRequested'
      $provider|Should Match 'Automatic rollback'
      $contract=(Get-WinCareActionContract -Type RunNetworkExperiment)
      $contract.MaximumTimeout|Should Be 1800
      $policy=Get-WinCareDefaultPolicy
      $policy.MaximumNetworkExperimentMinutes|Should -BeLessOrEqual 25
    }
    It 'keeps modifying expert commands outside the read-only broker' {
      foreach($command in @('pagefile-set','security-control-reduce','security-control-restore','network-experiment','etw-capture','injection-surface-quarantine')){Get-WinCareBrokerReadOnlyCommandName|Should Not -Contain $command}
      foreach($command in @('pagefile','security-controls','tcp-global','network-measure','injection-surfaces','remote-thread-events','etw-sessions')){Get-WinCareBrokerReadOnlyCommandName|Should Contain $command}
    }
    It 'routes the expert laboratory and exposes all headless operations' {
      @(Get-WinCareMainMenuItems|ForEach-Object Action)|Should Contain ExpertLab
      foreach($command in @('pagefile','pagefile-set','security-controls','security-control-reduce','security-control-restore','tcp-global','network-measure','network-experiment','injection-surfaces','process-modules','remote-thread-events','etw-capture','injection-surface-quarantine')){Get-WinCareHeadlessCommandName|Should Contain $command}
    }
  }
}
