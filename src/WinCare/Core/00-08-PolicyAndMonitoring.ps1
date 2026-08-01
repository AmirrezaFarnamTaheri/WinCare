#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: Playbooks, local policy, and Sysmon stateful operations.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function New-WinCarePlaybookPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('PlaybookId')][string]$Id)
    $playbook=Get-WinCarePlaybook -Id $Id|Select-Object -First 1;if(-not $playbook){throw "Playbook not found: $Id"};if(-not (Test-WinCarePlaybookCompatibility $playbook)){throw "Playbook is incompatible with this workstation: $Id"};$actions=[Collections.Generic.List[object]]::new();foreach($definition in @($playbook.Actions)){$actions.Add((New-WinCareBridgeAction -Type ([string]$definition.Type) -Label ([string](Get-WinCareBridgeProperty $definition 'Label' $definition.Type)) -Risk ([string](Get-WinCareBridgeProperty $definition 'Risk' 'Low')) -Parameters (ConvertTo-WinCareBridgeHashtable $definition.Parameters) -RequiresAdmin ([bool](Get-WinCareBridgeProperty $definition 'RequiresAdmin' $false)) -Reversible ([bool](Get-WinCareBridgeProperty $definition 'Reversible' $false)) -TimeoutSeconds ([int](Get-WinCareBridgeProperty $definition 'TimeoutSeconds' 3600)) -Verification ([string](Get-WinCareBridgeProperty $definition 'Verification' 'The provider must return mutation evidence.'))))};New-WinCareBridgePlan -Title ([string]$playbook.Title) -Description ([string](Get-WinCareBridgeProperty $playbook 'Description' '')) -Actions @($actions) -Metadata @{PlaybookId=$Id;SourcePath=$playbook.SourcePath;SourceSha256=$playbook.SourceSha256}
}
function Get-WinCareLocalGroupPolicyState {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return [pscustomobject]@{Supported=$false;MachinePolicyExists=$false;UserPolicyExists=$false;Sha256=''}}
    $machine=Join-Path $env:windir 'System32\GroupPolicy\Machine\Registry.pol';$user=Join-Path $env:windir 'System32\GroupPolicy\User\Registry.pol'
    $record=[ordered]@{Supported=$true;MachinePolicyExists=Test-Path $machine;MachinePolicySha256=if(Test-Path $machine){Get-WinCareSha256 -LiteralPath $machine}else{$null};UserPolicyExists=Test-Path $user;UserPolicySha256=if(Test-Path $user){Get-WinCareSha256 -LiteralPath $user}else{$null};LastAppliedEvents=@(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-GroupPolicy/Operational';StartTime=[datetime]::Now.AddDays(-7)} -MaxEvents 20 -ErrorAction SilentlyContinue|Select-Object TimeCreated,Id,LevelDisplayName,Message)}
    $record.Sha256=Get-WinCareCanonicalObjectHash ([ordered]@{MachinePolicyExists=$record.MachinePolicyExists;MachinePolicySha256=$record.MachinePolicySha256;UserPolicyExists=$record.UserPolicyExists;UserPolicySha256=$record.UserPolicySha256})
    [pscustomobject]$record
}
function New-WinCareLocalGroupPolicyImportPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('BackupPath')][string]$Path,[string]$LgpoPath='')
    $backup=(Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path;if(-not $LgpoPath){$candidate=Get-Command LGPO.exe -ErrorAction SilentlyContinue;if($candidate){$LgpoPath=$candidate.Source}};if(-not $LgpoPath){throw 'LGPO.exe path is required.'};$lgpo=(Resolve-Path -LiteralPath $LgpoPath -ErrorAction Stop).Path;$action=New-WinCareBridgeAction -Type 'ImportLocalGroupPolicyBackup' -Label 'Import local Group Policy backup' -Risk Critical -RequiresAdmin $true -Parameters @{BackupPath=$backup;BackupHash=Get-WinCarePathContentHash -LiteralPath $backup;LgpoPath=$lgpo;LgpoHash=(Get-FileHash -LiteralPath $lgpo -Algorithm SHA256).Hash.ToLowerInvariant()} -TimeoutSeconds 21600 -Verification 'LGPO must return success and gpupdate must complete.';New-WinCareBridgePlan -Title 'Import local Group Policy backup' -Actions @($action) -RollbackOnFailure $false
}
function Invoke-WinCareImportLocalGroupPolicyBackupAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Local Group Policy import';if($blocked){return $blocked}
    $p=Get-WinCareActionParameters $Action;$rollbackRoot=Join-Path (Get-WinCareBridgeStateRoot 'Policy') ('lgpo-rollback-'+[guid]::NewGuid().ToString('N'))
    try{
        $backup=(Resolve-Path -LiteralPath ([string]$p.BackupPath) -ErrorAction Stop).Path;$lgpo=(Resolve-Path -LiteralPath ([string]$p.LgpoPath) -ErrorAction Stop).Path
        if((Get-WinCarePathContentHash $backup) -ne [string]$p.BackupHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'PolicyBackupChanged' -Message 'Policy backup changed after preview.' -ExitCode 74}
        if((Get-WinCareSha256 -LiteralPath $lgpo) -ne [string]$p.LgpoHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'LgpoBinaryChanged' -Message 'LGPO.exe changed after preview.' -ExitCode 74}
        $before=Get-WinCareLocalGroupPolicyState
        $null=New-Item -ItemType Directory -Path $rollbackRoot -Force
        $capture=Invoke-WinCareBridgeProcess -FilePath $lgpo -ArgumentList @('/b',$rollbackRoot) -TimeoutSeconds 21600 -RequireAdmin
        if(-not $capture.Success){return New-WinCareBridgeResult -Success $false -Code 'LocalGroupPolicyRollbackCaptureFailed' -Message 'Current Local Group Policy could not be backed up before import.' -ExitCode $capture.ExitCode -Data @{Capture=$capture.Data;Before=$before}}
        $import=Invoke-WinCareBridgeProcess -FilePath $lgpo -ArgumentList @('/g',$backup) -TimeoutSeconds 21600 -RequireAdmin
        $refresh=if($import.Success){Invoke-WinCareBridgeProcess -FilePath 'gpupdate.exe' -ArgumentList @('/force') -TimeoutSeconds 1800 -RequireAdmin}else{$null}
        if($import.Success -and $refresh.Success){return New-WinCareBridgeResult -Success $true -Code 'LocalPolicyImported' -Message 'Local Group Policy backup was imported and policy refresh completed.' -Data @{Import=$import.Data;Refresh=$refresh.Data;Before=$before;After=Get-WinCareLocalGroupPolicyState;RollbackBackup=$rollbackRoot;EvidenceType='LgpoBackupImportAndGpupdate'}}
        $rollback=Invoke-WinCareBridgeProcess -FilePath $lgpo -ArgumentList @('/g',$rollbackRoot) -TimeoutSeconds 21600 -RequireAdmin
        $rollbackRefresh=if($rollback.Success){Invoke-WinCareBridgeProcess -FilePath 'gpupdate.exe' -ArgumentList @('/force') -TimeoutSeconds 1800 -RequireAdmin}else{$null}
        $rollbackState=Get-WinCareLocalGroupPolicyState
        $rollbackOk=$rollback.Success -and $rollbackRefresh.Success -and $rollbackState.Sha256 -eq $before.Sha256
        if($rollbackOk){return New-WinCareBridgeResult -Success $false -Code 'LocalGroupPolicyImportFailedRolledBack' -Message 'Local Group Policy import failed; the captured prior policy was restored and verified.' -ExitCode 1 -Data @{Import=$import.Data;Refresh=if($refresh){$refresh.Data}else{$null};Rollback=$rollback.Data;RollbackRefresh=$rollbackRefresh.Data;Before=$before;After=$rollbackState;EvidenceType='ProviderRollback'}}
        New-WinCareBridgeResult -Success $false -Code 'LocalGroupPolicyImportRollbackFailed' -Message 'Local Group Policy import failed and the prior policy could not be fully restored or verified.' -ExitCode 1 -Data @{Import=$import.Data;Refresh=if($refresh){$refresh.Data}else{$null};Rollback=$rollback.Data;RollbackRefresh=if($rollbackRefresh){$rollbackRefresh.Data}else{$null};Before=$before;After=$rollbackState;EvidenceType='ProviderRollbackFailure'}
    }catch{New-WinCareBridgeResult -Success $false -Code 'LocalPolicyImportFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Get-WinCareSysmonState {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return [pscustomobject]@{Supported=$false;Installed=$false}};$service=Get-Service Sysmon,Sysmon64 -ErrorAction SilentlyContinue|Select-Object -First 1;$driver=Get-CimInstance Win32_SystemDriver -Filter "Name='SysmonDrv'" -ErrorAction SilentlyContinue;$configHash=$null;$configEvent=Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Sysmon/Operational';Id=16} -MaxEvents 1 -ErrorAction SilentlyContinue;[pscustomobject]@{Supported=$true;Installed=$null -ne $service;ServiceName=if($service){$service.Name}else{$null};ServiceStatus=if($service){$service.Status.ToString()}else{'Absent'};DriverState=if($driver){$driver.State}else{'Absent'};ConfigurationEvent=if($configEvent){[pscustomobject]@{TimeCreated=$configEvent.TimeCreated;Message=$configEvent.Message}}else{$null};OperationalLogExists=[Diagnostics.Eventing.Reader.EventLogSession]::GlobalSession.GetLogNames() -contains 'Microsoft-Windows-Sysmon/Operational'}
}
function New-WinCareSysmonConfigurationPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('ConfigPath')][string]$ConfigFile,[string]$ExecutablePath='',[string]$RollbackConfigFile='')
    $config=(Resolve-Path -LiteralPath $ConfigFile -ErrorAction Stop).Path
    if(-not $ExecutablePath){$cmd=Get-Command Sysmon64.exe,Sysmon.exe -ErrorAction SilentlyContinue|Select-Object -First 1;if($cmd){$ExecutablePath=$cmd.Source}}
    if(-not $ExecutablePath){throw 'Sysmon executable path is required.'}
    $exe=(Resolve-Path -LiteralPath $ExecutablePath -ErrorAction Stop).Path;$state=Get-WinCareSysmonState;$mode=if($state.Installed){'Update'}else{'Install'}
    $rollbackPath='';$rollbackHash=''
    if($mode -eq 'Update'){
        if(-not $RollbackConfigFile){throw 'Updating Sysmon requires a trusted prior configuration file for provider rollback.'}
        $rollbackPath=(Resolve-Path -LiteralPath $RollbackConfigFile -ErrorAction Stop).Path;$rollbackHash=Get-WinCareSha256 -LiteralPath $rollbackPath
    }
    $action=New-WinCareBridgeAction -Type 'ConfigureSysmon' -Label "$mode Sysmon" -Risk High -RequiresAdmin $true -Parameters @{ExecutablePath=$exe;ExecutableHash=Get-WinCareSha256 -LiteralPath $exe;ConfigPath=$config;ConfigHash=Get-WinCareSha256 -LiteralPath $config;Mode=$mode;RollbackConfigPath=$rollbackPath;RollbackConfigHash=$rollbackHash} -TimeoutSeconds 3600 -Verification 'Sysmon service and driver must be running and the operational log must exist; failed changes must be rolled back.'
    New-WinCareBridgePlan -Title "$mode Sysmon" -Actions @($action) -RollbackOnFailure $false
}
function New-WinCareSysmonUninstallPlan {
    [CmdletBinding()]
    param([string]$ExecutablePath='')
    if(-not $ExecutablePath){$cmd=Get-Command Sysmon64.exe,Sysmon.exe -ErrorAction SilentlyContinue|Select-Object -First 1;if($cmd){$ExecutablePath=$cmd.Source}};if(-not $ExecutablePath){throw 'Sysmon executable path is required.'};$exe=(Resolve-Path -LiteralPath $ExecutablePath -ErrorAction Stop).Path;$action=New-WinCareBridgeAction -Type 'UninstallSysmon' -Label 'Uninstall Sysmon' -Risk Critical -RequiresAdmin $true -Parameters @{ExecutablePath=$exe;ExecutableHash=(Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()} -TimeoutSeconds 3600 -Verification 'Sysmon service and driver must be absent after uninstall.';New-WinCareBridgePlan -Title 'Uninstall Sysmon' -Actions @($action) -RollbackOnFailure $false
}
function Invoke-WinCareConfigureSysmonAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$mode=[string]$p.Mode
    try{
        $exe=(Resolve-Path $p.ExecutablePath -ErrorAction Stop).Path;$config=(Resolve-Path $p.ConfigPath -ErrorAction Stop).Path
        if((Get-WinCareSha256 -LiteralPath $exe) -ne [string]$p.ExecutableHash -or (Get-WinCareSha256 -LiteralPath $config) -ne [string]$p.ConfigHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'SysmonInputChanged' -Message 'Sysmon executable or configuration changed after preview.' -ExitCode 74}
        $rollbackConfig=$null
        if($mode -eq 'Update'){
            if(-not $p.RollbackConfigPath -or -not $p.RollbackConfigHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'SysmonRollbackConfigRequired' -Message 'Sysmon update requires a trusted prior configuration.' -ExitCode 5}
            $rollbackConfig=(Resolve-Path $p.RollbackConfigPath -ErrorAction Stop).Path
            if((Get-WinCareSha256 -LiteralPath $rollbackConfig) -ne [string]$p.RollbackConfigHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'SysmonRollbackConfigChanged' -Message 'Sysmon rollback configuration changed after preview.' -ExitCode 74}
        }
        $args=if($mode -eq 'Install'){@('-accepteula','-i',$config)}else{@('-c',$config)}
        $result=Invoke-WinCareBridgeProcess -FilePath $exe -ArgumentList $args -TimeoutSeconds 3600 -RequireAdmin
        Start-Sleep -Seconds 2;$state=Get-WinCareSysmonState;$success=$result.Success -and $state.Installed -and $state.ServiceStatus -eq 'Running'
        if($success){return New-WinCareBridgeResult -Success $true -Code 'SysmonConfigured' -Message 'Sysmon was configured and service state verified.' -Data @{Process=$result.Data;State=$state;EvidenceType='SysmonServiceAndLog'}}
        $rollback=if($mode -eq 'Install'){Invoke-WinCareBridgeProcess -FilePath $exe -ArgumentList @('-u','force') -TimeoutSeconds 3600 -RequireAdmin}else{Invoke-WinCareBridgeProcess -FilePath $exe -ArgumentList @('-c',$rollbackConfig) -TimeoutSeconds 3600 -RequireAdmin}
        Start-Sleep -Seconds 2;$rollbackState=Get-WinCareSysmonState
        $rollbackOk=if($mode -eq 'Install'){$rollback.Success -and -not $rollbackState.Installed}else{$rollback.Success -and $rollbackState.Installed -and $rollbackState.ServiceStatus -eq 'Running'}
        if($rollbackOk){return New-WinCareBridgeResult -Success $false -Code 'SysmonConfigurationFailedRolledBack' -Message 'Sysmon configuration failed; the prior provider state was restored and verified.' -ExitCode 1 -Data @{Attempt=$result.Data;Rollback=$rollback.Data;State=$rollbackState;EvidenceType='ProviderRollback'}}
        New-WinCareBridgeResult -Success $false -Code 'SysmonConfigurationRollbackFailed' -Message 'Sysmon configuration failed and provider rollback could not be verified.' -ExitCode 1 -Data @{Attempt=$result.Data;Rollback=$rollback.Data;State=$rollbackState;EvidenceType='ProviderRollbackFailure'}
    }catch{New-WinCareBridgeResult -Success $false -Code 'SysmonConfigureFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareUninstallSysmonAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;try{$exe=(Resolve-Path $p.ExecutablePath -ErrorAction Stop).Path;if((Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$p.ExecutableHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'SysmonBinaryChanged' -Message 'Sysmon executable changed after preview.' -ExitCode 74};$result=Invoke-WinCareBridgeProcess -FilePath $exe -ArgumentList @('-u','force') -TimeoutSeconds 3600 -RequireAdmin;if(-not $result.Success){return $result};Start-Sleep -Seconds 2;$state=Get-WinCareSysmonState;$success=-not $state.Installed;New-WinCareBridgeResult -Success $success -Code $(if($success){'SysmonUninstalled'}else{'SysmonUninstallVerificationFailed'}) -Message $(if($success){'Sysmon was uninstalled and service absence verified.'}else{'Sysmon service remains present.'}) -Data @{Process=$result.Data;State=$state;EvidenceType='SysmonServiceAbsence'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'SysmonUninstallFailed' -Message $_.Exception.Message -ExitCode 1}
}
