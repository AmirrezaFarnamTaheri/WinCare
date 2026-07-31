function Restore-WinCareRegistryValueState {
    param([Parameter(Mandatory)][object]$State)
    if([bool]$State.Exists){
        $action=New-WinCareAction -Type SetRegistryValue -Label 'Restore security registry value' -Parameters @{Path=[string]$State.Path;Name=[string]$State.Name;Value=$State.Value;ValueType=[string]$State.ValueType} -RequiresAdmin ([string]$State.Path -match '^HKLM:')
        $result=Invoke-WinCareSetRegistryValueAction -Action $action
    }else{
        $action=New-WinCareAction -Type RemoveRegistryValue -Label 'Remove security registry value' -Parameters @{Path=[string]$State.Path;Name=[string]$State.Name} -RequiresAdmin ([string]$State.Path -match '^HKLM:')
        $result=Invoke-WinCareRemoveRegistryValueAction -Action $action
    }
    if(-not $result.Success){throw $result.Message}
}

function Get-WinCareServiceSecurityState {
    param([Parameter(Mandatory)][string[]]$Name)
    @($Name|ForEach-Object{
        $escapedName=([string]$_).Replace("'","''")
        $filter="Name='{0}'" -f $escapedName
        $service=Get-CimInstance Win32_Service -Filter $filter -ErrorAction SilentlyContinue
        if($service){
            $servicePath="HKLM:\SYSTEM\CurrentControlSet\Services\$([string]$service.Name)"
            $delayed=Get-WinCareRegistryValueState -Path $servicePath -Name 'DelayedAutoStart'
            [pscustomobject]@{Name=[string]$service.Name;Exists=$true;StartMode=[string]$service.StartMode;State=[string]$service.State;DelayedAutoStart=if($delayed.Exists){[int]$delayed.Value}else{0}}
        } else {[pscustomobject]@{Name=[string]$_;Exists=$false;StartMode='';State='';DelayedAutoStart=0}}
    })
}

function Get-WinCareSecurityControlState {
    [CmdletBinding()]
    param([ValidateSet('DefenderRealtime','SmartScreenShell','Uac','WindowsUpdateStack')][string]$Control)
    if(-not $IsWindows){return $null}
    $controls=if($Control){@($Control)}else{@('DefenderRealtime','SmartScreenShell','Uac','WindowsUpdateStack')}
    foreach($name in $controls){
        $snapshot=switch($name){
            'DefenderRealtime'{
                $preference=if(Get-Command Get-MpPreference -ErrorAction SilentlyContinue){Get-MpPreference -ErrorAction SilentlyContinue}else{$null}
                $status=if(Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue){Get-MpComputerStatus -ErrorAction SilentlyContinue}else{$null}
                [ordered]@{Control=$name;Available=[bool]$preference;Enabled=if($status){[bool](Get-WinCarePropertyValue $status 'RealTimeProtectionEnabled' $false)}else{$null};DisableRealtimeMonitoring=if($preference){[bool](Get-WinCarePropertyValue $preference 'DisableRealtimeMonitoring' $false)}else{$null};TamperProtected=if($status){[bool](Get-WinCarePropertyValue $status 'IsTamperProtected' $false)}else{$null}}
            }
            'SmartScreenShell'{
                $path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
                $enable=Get-WinCareRegistryValueState -Path $path -Name 'EnableSmartScreen'
                $level=Get-WinCareRegistryValueState -Path $path -Name 'ShellSmartScreenLevel'
                $effective=if($enable.Exists){[int]$enable.Value -ne 0}else{$null}
                [ordered]@{Control=$name;Available=$true;Enabled=$effective;Registry=@($enable,$level)}
            }
            'Uac'{
                $path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
                $values=@(
                    Get-WinCareRegistryValueState -Path $path -Name 'EnableLUA'
                    Get-WinCareRegistryValueState -Path $path -Name 'ConsentPromptBehaviorAdmin'
                    Get-WinCareRegistryValueState -Path $path -Name 'PromptOnSecureDesktop'
                )
                $enable=$values|Where-Object Name -eq 'EnableLUA'|Select-Object -First 1
                [ordered]@{Control=$name;Available=$true;Enabled=if($enable.Exists){[int]$enable.Value -ne 0}else{$true};Registry=$values;RestartRequired=$true}
            }
            'WindowsUpdateStack'{
                $services=Get-WinCareServiceSecurityState -Name @('wuauserv','UsoSvc','DoSvc')
                $enabled=-not [bool]($services|Where-Object{$_.Exists -and $_.StartMode -eq 'Disabled'}|Select-Object -First 1)
                [ordered]@{Control=$name;Available=[bool]($services|Where-Object Exists|Select-Object -First 1);Enabled=$enabled;Services=$services}
            }
        }
        $hashPayload=[ordered]@{};foreach($key in $snapshot.Keys){if($key -ne 'Hash'){$hashPayload[$key]=$snapshot[$key]}}
        $snapshot.Hash=Get-WinCareSha256Text -Text (ConvertTo-WinCareCanonicalJson -InputObject $hashPayload -Depth 30)
        [pscustomobject]$snapshot
    }
}


function Test-WinCareSecurityControlDesiredState {
    param(
        [Parameter(Mandatory)][object]$State,
        [Parameter(Mandatory)][bool]$Enabled
    )
    switch([string]$State.Control){
        'DefenderRealtime' {
            return [bool]$State.Available -and $null -ne $State.Enabled -and [bool]$State.Enabled -eq $Enabled
        }
        'SmartScreenShell' {
            $enable=@($State.Registry|Where-Object Name -eq 'EnableSmartScreen'|Select-Object -First 1)
            $level=@($State.Registry|Where-Object Name -eq 'ShellSmartScreenLevel'|Select-Object -First 1)
            if($Enabled){return $enable.Count -eq 1 -and [bool]$enable[0].Exists -and [int]$enable[0].Value -eq 1 -and $level.Count -eq 1 -and [bool]$level[0].Exists -and [string]$level[0].Value -eq 'Warn'}
            return $enable.Count -eq 1 -and [bool]$enable[0].Exists -and [int]$enable[0].Value -eq 0 -and $level.Count -eq 1 -and [bool]$level[0].Exists -and [string]$level[0].Value -eq 'Off'
        }
        'Uac' {
            $enableLua=@($State.Registry|Where-Object Name -eq 'EnableLUA'|Select-Object -First 1)
            return $enableLua.Count -eq 1 -and [bool]$enableLua[0].Exists -and (([int]$enableLua[0].Value -ne 0) -eq $Enabled)
        }
        'WindowsUpdateStack' {
            $existing=@($State.Services|Where-Object Exists)
            if($existing.Count -lt 1){return $false}
            if($Enabled){return -not [bool]($existing|Where-Object StartMode -eq 'Disabled'|Select-Object -First 1)}
            return @($existing|Where-Object StartMode -ne 'Disabled').Count -eq 0
        }
        default { return $false }
    }
}

function Test-WinCareSecurityControlSnapshot {
    param([Parameter(Mandatory)][object]$Snapshot,[Parameter(Mandatory)][string]$ExpectedControl)
    $state=ConvertTo-WinCareParameterDictionary $Snapshot
    if([string](Get-WinCarePropertyValue $state 'Control' '') -ne $ExpectedControl){throw 'Security snapshot control does not match the recovery action.'}
    if([string](Get-WinCarePropertyValue $state 'Hash' '') -notmatch '^[a-f0-9]{64}$'){throw 'Security snapshot hash is invalid.'}
    switch($ExpectedControl){
        'DefenderRealtime' {
            $null=Test-WinCareStrictObjectKeys -InputObject $state -AllowedKeys @('Control','Available','Enabled','DisableRealtimeMonitoring','TamperProtected','Hash') -Context 'Defender security snapshot'
        }
        'SmartScreenShell' {
            $null=Test-WinCareStrictObjectKeys -InputObject $state -AllowedKeys @('Control','Available','Enabled','Registry','Hash') -Context 'SmartScreen security snapshot'
            $expectedPath='HKLM:\SOFTWARE\Policies\Microsoft\Windows\System';$expectedNames=@('EnableSmartScreen','ShellSmartScreenLevel')
            $entries=@(Get-WinCarePropertyValue $state 'Registry' @());if($entries.Count -ne 2){throw 'SmartScreen snapshot must contain exactly two Registry values.'}
            foreach($entry in $entries){if([string]$entry.Path -ne $expectedPath -or [string]$entry.Name -notin $expectedNames){throw 'SmartScreen snapshot contains an unexpected Registry target.'}}
        }
        'Uac' {
            $null=Test-WinCareStrictObjectKeys -InputObject $state -AllowedKeys @('Control','Available','Enabled','Registry','RestartRequired','Hash') -Context 'UAC security snapshot'
            $expectedPath='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System';$expectedNames=@('EnableLUA','ConsentPromptBehaviorAdmin','PromptOnSecureDesktop')
            $entries=@(Get-WinCarePropertyValue $state 'Registry' @());if($entries.Count -ne 3){throw 'UAC snapshot must contain exactly three Registry values.'}
            foreach($entry in $entries){if([string]$entry.Path -ne $expectedPath -or [string]$entry.Name -notin $expectedNames){throw 'UAC snapshot contains an unexpected Registry target.'}}
        }
        'WindowsUpdateStack' {
            $null=Test-WinCareStrictObjectKeys -InputObject $state -AllowedKeys @('Control','Available','Enabled','Services','Hash') -Context 'Windows Update security snapshot'
            $services=@(Get-WinCarePropertyValue $state 'Services' @());if($services.Count -ne 3){throw 'Windows Update snapshot must contain exactly three service records.'}
            $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach($service in $services){if([string]$service.Name -notin @('wuauserv','UsoSvc','DoSvc') -or -not $seen.Add([string]$service.Name)){throw 'Windows Update snapshot contains an unexpected or duplicate service.'}}
        }
        default { throw 'Unsupported security snapshot control.' }
    }
    $hashPayload=[ordered]@{};foreach($key in $state.Keys){if($key -ne 'Hash'){$hashPayload[$key]=$state[$key]}}
    $computed=Get-WinCareSha256Text -Text (ConvertTo-WinCareCanonicalJson -InputObject $hashPayload -Depth 30)
    if($computed -ne [string]$state.Hash){throw 'Security snapshot integrity hash does not match its contents.'}
    return [pscustomobject]$state
}

function Set-WinCareServiceExactState {
    param([Parameter(Mandatory)][object]$State)
    if(-not [bool]$State.Exists){return}
    $mode=switch([string]$State.StartMode){'Auto'{'auto'}'Automatic'{'auto'}'Manual'{'demand'}'Disabled'{'disabled'}default{'demand'}}
    $result=Invoke-WinCareProcess -FilePath sc.exe -ArgumentList @('config',[string]$State.Name,'start=',$mode) -RequireAdmin -TimeoutSeconds 60
    if(-not $result.Success){throw $result.Message}
    $servicePath="HKLM:\SYSTEM\CurrentControlSet\Services\$([string]$State.Name)"
    if(Test-Path -LiteralPath $servicePath){
        New-ItemProperty -LiteralPath $servicePath -Name 'DelayedAutoStart' -Value ([int](Get-WinCarePropertyValue $State 'DelayedAutoStart' 0)) -PropertyType DWord -Force -ErrorAction Stop|Out-Null
    }
    if([string]$State.State -eq 'Running'){
        Start-Service -Name ([string]$State.Name) -ErrorAction Stop
    }else{
        Stop-Service -Name ([string]$State.Name) -Force -ErrorAction SilentlyContinue
    }
}

function Set-WinCareSecurityControlInternal {
    param([Parameter(Mandatory)][string]$Control,[Parameter(Mandatory)][bool]$Enabled,[object]$Snapshot)
    switch($Control){
        'DefenderRealtime'{
            if(-not(Get-Command Set-MpPreference -ErrorAction SilentlyContinue)){throw 'Microsoft Defender preference commands are unavailable.'}
            if(-not $Enabled){
                $status=Get-MpComputerStatus -ErrorAction SilentlyContinue
                if([bool](Get-WinCarePropertyValue $status 'IsTamperProtected' $false)){throw 'Tamper Protection is enabled; WinCare will not attempt to bypass it.'}
            }
            Set-MpPreference -DisableRealtimeMonitoring (-not $Enabled) -ErrorAction Stop
        }
        'SmartScreenShell'{
            $path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
            $null=New-Item -Path $path -Force -ErrorAction Stop
            New-ItemProperty -LiteralPath $path -Name 'EnableSmartScreen' -Value $(if($Enabled){1}else{0}) -PropertyType DWord -Force -ErrorAction Stop|Out-Null
            New-ItemProperty -LiteralPath $path -Name 'ShellSmartScreenLevel' -Value $(if($Enabled){'Warn'}else{'Off'}) -PropertyType String -Force -ErrorAction Stop|Out-Null
        }
        'Uac'{
            $path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
            $null=New-Item -Path $path -Force -ErrorAction Stop
            New-ItemProperty -LiteralPath $path -Name 'EnableLUA' -Value $(if($Enabled){1}else{0}) -PropertyType DWord -Force -ErrorAction Stop|Out-Null
            if($Enabled){
                New-ItemProperty -LiteralPath $path -Name 'ConsentPromptBehaviorAdmin' -Value 5 -PropertyType DWord -Force -ErrorAction Stop|Out-Null
                New-ItemProperty -LiteralPath $path -Name 'PromptOnSecureDesktop' -Value 1 -PropertyType DWord -Force -ErrorAction Stop|Out-Null
            }
        }
        'WindowsUpdateStack'{
            if($Enabled){
                if(-not $Snapshot){throw 'Exact Windows Update service state is required for restoration.'}
                foreach($service in @($Snapshot.Services)){Set-WinCareServiceExactState -State $service}
            }else{
                foreach($service in @('wuauserv','UsoSvc','DoSvc')){
                    Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
                    $result=Invoke-WinCareProcess -FilePath sc.exe -ArgumentList @('config',$service,'start=','disabled') -RequireAdmin -TimeoutSeconds 60
                    if(-not $result.Success -and (Get-Service -Name $service -ErrorAction SilentlyContinue)){throw $result.Message}
                }
            }
        }
        default{throw "Unsupported security control: $Control"}
    }
}

function Restore-WinCareSecurityControlSnapshotInternal {
    param([Parameter(Mandatory)][object]$Snapshot)
    $control=[string]$Snapshot.Control
    switch($control){
        'DefenderRealtime'{
            if(-not(Get-Command Set-MpPreference -ErrorAction SilentlyContinue)){throw 'Microsoft Defender preference commands are unavailable.'}
            Set-MpPreference -DisableRealtimeMonitoring ([bool]$Snapshot.DisableRealtimeMonitoring) -ErrorAction Stop
        }
        {$_ -in @('SmartScreenShell','Uac')}{foreach($entry in @($Snapshot.Registry)){Restore-WinCareRegistryValueState -State $entry}}
        'WindowsUpdateStack'{foreach($service in @($Snapshot.Services)){Set-WinCareServiceExactState -State $service}}
        default{throw "Unsupported security control: $control"}
    }
}

function Get-WinCareSecurityMaintenanceRoot { Get-WinCareBridgeStateRoot 'SecurityMaintenance' }
function Get-WinCareSecurityMaintenanceRecordPath {param([Parameter(Mandatory)][string]$Id);if($Id -notmatch '^[a-f0-9]{32}$'){throw 'Security-maintenance record ID is invalid.'};Join-Path (Get-WinCareSecurityMaintenanceRoot) "$Id.json"}
function Get-WinCareSecurityMaintenanceTaskName {param([Parameter(Mandatory)][string]$Id);"WinCare-SecurityRestore-$Id"}

function Register-WinCareSecurityRestoreTask {
    param([Parameter(Mandatory)][object]$Record)
    if(-not(Get-Command Register-ScheduledTask -ErrorAction SilentlyContinue)){throw 'Scheduled Tasks cmdlets are unavailable.'}
    $hostExe=(Get-Process -Id $PID).Path
    $helper=Assert-WinCareSafePath -LiteralPath (Join-Path $script:WinCareModuleRoot 'Host\SecurityControlRestoreHost.ps1') -AllowedRoots @($script:WinCareModuleRoot)
    $manifest=Assert-WinCareSafePath -LiteralPath (Join-Path $script:WinCareModuleRoot 'WinCare.psd1') -AllowedRoots @($script:WinCareModuleRoot)
    $arguments='-NoProfile -NonInteractive -File "{0}" -ModuleManifest "{1}" -RecordId {2}' -f $helper,$manifest,$Record.Id
    $action=New-ScheduledTaskAction -Execute $hostExe -Argument $arguments
    $expiry=[datetime]::Parse([string]$Record.ExpiresAt).ToLocalTime()
    $triggers=@(
        (New-ScheduledTaskTrigger -Once -At $expiry)
        (New-ScheduledTaskTrigger -AtLogOn)
    )
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name
    $principal=New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Highest
    $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 15) -StartWhenAvailable -MultipleInstances IgnoreNew
    $null=Register-ScheduledTask -TaskName (Get-WinCareSecurityMaintenanceTaskName $Record.Id) -Action $action -Trigger $triggers -Principal $principal -Settings $settings -Force -ErrorAction Stop
}

function Remove-WinCareSecurityRestoreTask {
    param([Parameter(Mandatory)][string]$Id)
    if(Get-Command Unregister-ScheduledTask -ErrorAction SilentlyContinue){Unregister-ScheduledTask -TaskName (Get-WinCareSecurityMaintenanceTaskName $Id) -Confirm:$false -ErrorAction SilentlyContinue}
}

function Get-WinCareSecurityMaintenanceRecord {
    [CmdletBinding()]
    param([string]$Id,[switch]$IncludeRestored)
    $files=if($Id){@(Get-WinCareSecurityMaintenanceRecordPath $Id)}else{@(Get-ChildItem -LiteralPath (Get-WinCareSecurityMaintenanceRoot) -Filter '*.json' -File -ErrorAction SilentlyContinue|Select-Object -ExpandProperty FullName)}
    foreach($file in $files){
        if(-not(Test-Path -LiteralPath $file -PathType Leaf)){continue}
        try{$record=Read-WinCareProtectedJson -LiteralPath $file -Purpose 'WinCare.SecurityMaintenance' -AsHashtable}catch{Write-WinCareLog -Level Warning -Message 'Security-maintenance record failed authentication.' -Data @{path=$file;error=$_.Exception.Message};continue}
        if(-not $IncludeRestored -and [string]$record.Status -eq 'Restored'){continue}
        [pscustomobject]$record
    }
}

function Invoke-WinCareSecurityMaintenanceAutoRestore {
    [CmdletBinding()]param([Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$RecordId)
    $record=Get-WinCareSecurityMaintenanceRecord -Id $RecordId -IncludeRestored|Select-Object -First 1
    if(-not $record){return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceRecordMissing' -Message 'The authenticated security-maintenance record is unavailable.' -ExitCode 74}
    if([string]$record.Status -eq 'Restored'){return New-WinCareResult -Success $true -Code 'SecurityMaintenanceAlreadyRestored' -Message 'The security control is already restored.' -Data @{Record=$record}}
    if([string]$record.Status -notin @('Prepared','Armed','Active')){return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceStateInvalid' -Message "Security-maintenance record is not recoverable: $($record.Status)" -ExitCode 74}
    try{$before=Test-WinCareSecurityControlSnapshot -Snapshot $record.Before -ExpectedControl ([string]$record.Control)}catch{return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceRecordSnapshotInvalid' -Message $_.Exception.Message -ExitCode 74}
    $current=Get-WinCareSecurityControlState -Control ([string]$record.Control)
    if(-not $current){return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceControlUnavailable' -Message 'The security control is unavailable for automatic recovery.' -ExitCode 127}
    if([string]$current.Hash -eq [string]$before.Hash){
        $record.Status='Restored';$record.RestoredAt=[datetime]::UtcNow.ToString('o');$record.RestoredHash=[string]$current.Hash;$record.RestoreReason='No mutation remained when the recovery task ran.'
        $warnings=[Collections.Generic.List[string]]::new()
        try{Write-WinCareProtectedJson -LiteralPath (Get-WinCareSecurityMaintenanceRecordPath $RecordId) -InputObject $record -Purpose 'WinCare.SecurityMaintenance'}catch{$warnings.Add("The control was already safe, but the maintenance record could not be finalized: $($_.Exception.Message)")}
        try{Remove-WinCareSecurityRestoreTask -Id $RecordId}catch{$warnings.Add("The control was already safe, but the recovery task could not be removed: $($_.Exception.Message)")}
        return New-WinCareResult -Success $true -Code 'SecurityMaintenanceReconciledNoMutation' -Message 'The control already matched its exact pre-maintenance state.' -Data @{Record=$record;After=$current} -Warnings @($warnings)
    }
    if([string]$record.Status -eq 'Prepared'){return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceUnexpectedPreparedMutation' -Message 'A prepared record observed changed control state before it was durably armed; automatic recovery refused the ambiguous state.' -ExitCode 74}
    $expectedHash=if([string]$record.Status -eq 'Active' -and [string]$record.AppliedHash -match '^[a-f0-9]{64}$'){[string]$record.AppliedHash}else{[string]$current.Hash}
    $action=New-WinCareAction -Type RestoreSecurityControlState -Label "Automatic restore of $($record.Control)" -Risk High -Parameters @{RecordId=[string]$record.Id;Control=[string]$record.Control;Snapshot=$record.Before;ExpectedCurrentHash=$expectedHash} -RequiresAdmin $true -Reversible $false -TimeoutSeconds 600 -Tags @('SecurityMaintenance','AutomaticRestore') -SourceRecords @($record.SourceRecords) -RestartPossible ([string]$record.Control -eq 'Uac')
    $contract=Test-WinCareActionContract -Action $action
    if(-not $contract.Success){return $contract}
    Invoke-WinCareRestoreSecurityControlStateAction -Action $action
}

function New-WinCareSecurityMaintenancePlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DefenderRealtime','SmartScreenShell','Uac','WindowsUpdateStack')][string]$Control,
        [ValidateRange(5,1440)][int]$DurationMinutes=60,
        [Parameter(Mandatory)][ValidateLength(3,500)][string]$Reason,
        [ValidateSet('Workstation','Test','DisposableLab')][string]$EnvironmentClass='Workstation'
    )
    if($Control -eq 'Uac' -and $EnvironmentClass -ne 'DisposableLab'){throw 'Full UAC disable is restricted to an explicitly declared DisposableLab environment.'}
    $before=Get-WinCareSecurityControlState -Control $Control
    if(-not $before -or -not [bool]$before.Available){throw "$Control is unavailable on this system."}
    if($before.Enabled -eq $false){throw "$Control is already reduced or disabled; use the existing recovery record instead of stacking reductions."}
    $risk=if($Control -eq 'Uac'){'Critical'}else{'High'}
    $action=New-WinCareAction -Type SetSecurityControlState -Label "Temporarily reduce $Control protection" -Risk $risk -Parameters @{Control=$Control;Enabled=$false;DurationMinutes=$DurationMinutes;Reason=$Reason;EnvironmentClass=$EnvironmentClass;BeforeHash=[string]$before.Hash} -RequiresAdmin $true -Reversible $true -TimeoutSeconds 600 -Tags @('SecurityMaintenance','Expiring','Expert') -SourceRecords @('SRC:3b617af8c7') -RecoveryDescription 'An authenticated scheduled task and journal compensator restore the exact previous control state.' -RestartPossible ($Control -eq 'Uac') -Verification 'The selected control matches the reduced state and a protected expiry record exists.'
    New-WinCarePlan -Title "Temporary $Control maintenance mode" -Description "Protection is reduced for at most $DurationMinutes minutes and restored at expiry or next logon. This is not an optimization." -Actions @($action) -SourceRecords @($action.SourceRecords)
}

function New-WinCareSecurityMaintenanceRestorePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$RecordId)
    $record=Get-WinCareSecurityMaintenanceRecord -Id $RecordId -IncludeRestored|Select-Object -First 1
    if(-not $record){throw 'Security-maintenance record was not found or could not be authenticated.'}
    if([string]$record.Status -eq 'Restored'){throw 'Security-maintenance record is already restored.'}
    if([string]$record.Status -notin @('Armed','Active')){throw "Security-maintenance record cannot be restored from state '$($record.Status)'."}
    $current=Get-WinCareSecurityControlState -Control ([string]$record.Control)
    if(-not $current){throw 'Security control is unavailable.'}
    if([string]$current.Hash -eq [string]$record.Before.Hash){throw 'The control already matches its exact pre-maintenance state; run automatic reconciliation to finalize the record.'}
    $expectedHash=if([string]$record.Status -eq 'Active' -and [string]$record.AppliedHash -match '^[a-f0-9]{64}$'){[string]$record.AppliedHash}else{[string]$current.Hash}
    $action=New-WinCareAction -Type RestoreSecurityControlState -Label "Restore $($record.Control) protection" -Risk High -Parameters @{RecordId=[string]$record.Id;Control=[string]$record.Control;Snapshot=$record.Before;ExpectedCurrentHash=$expectedHash} -RequiresAdmin $true -Reversible $false -TimeoutSeconds 600 -Tags @('SecurityMaintenance','Restore') -SourceRecords @($record.SourceRecords) -RestartPossible ([string]$record.Control -eq 'Uac')
    New-WinCarePlan -Title "Restore $($record.Control)" -Description 'Restores the exact authenticated pre-maintenance state and removes the scheduled recovery task.' -Actions @($action) -SourceRecords @($record.SourceRecords)
}

function Invoke-WinCareSetSecurityControlStateAction {
    param([object]$Action)
    $p=$Action.Parameters;$control=[string]$p.Control;$before=Get-WinCareSecurityControlState -Control $control
    if(-not $before){return New-WinCareResult -Success $false -Message 'Security control state is unavailable.' -ExitCode 127}
    if([string]$before.Hash -ne [string]$p.BeforeHash){return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityControlChangedAfterPreview' -Message 'Security control state changed after preview.' -ExitCode 74}
    $id=[guid]::NewGuid().ToString('N');$path=Get-WinCareSecurityMaintenanceRecordPath $id;$expires=[datetime]::UtcNow.AddMinutes([int]$p.DurationMinutes)
    $record=[ordered]@{SchemaVersion=1;Id=$id;Control=$control;Status='Prepared';CreatedAt=[datetime]::UtcNow.ToString('o');ExpiresAt=$expires.ToString('o');Reason=[string]$p.Reason;EnvironmentClass=[string]$p.EnvironmentClass;Before=$before;DesiredEnabled=[bool]$p.Enabled;AppliedHash='';CreatedByModuleVersion=[string](Get-WinCarePropertyValue $script:WinCareState 'ModuleVersion' 'unknown');SourceRecords=@($Action.SourceRecords)}
    $taskRegistered=$false;$mutationStarted=$false
    try{
        Write-WinCareProtectedJson -LiteralPath $path -InputObject $record -Purpose 'WinCare.SecurityMaintenance'
        Register-WinCareSecurityRestoreTask -Record $record;$taskRegistered=$true
        $record.Status='Armed';$record.ArmedAt=[datetime]::UtcNow.ToString('o')
        Write-WinCareProtectedJson -LiteralPath $path -InputObject $record -Purpose 'WinCare.SecurityMaintenance'
        $mutationStarted=$true
        Set-WinCareSecurityControlInternal -Control $control -Enabled ([bool]$p.Enabled) -Snapshot $before
        $after=Get-WinCareSecurityControlState -Control $control
        if(-not(Test-WinCareSecurityControlDesiredState -State $after -Enabled ([bool]$p.Enabled))){throw 'Security-control verification failed.'}
        $record.Status='Active';$record.AppliedHash=[string]$after.Hash;$record.ActivatedAt=[datetime]::UtcNow.ToString('o')
        Write-WinCareProtectedJson -LiteralPath $path -InputObject $record -Purpose 'WinCare.SecurityMaintenance'
        $compensator=@{Type='RestoreSecurityControlState';RequiresAdmin=$true;Parameters=@{RecordId=$id;Control=$control;Snapshot=$before;ExpectedCurrentHash=[string]$after.Hash}}
        New-WinCareResult -Success $true -Code 'SecurityMaintenanceActivated' -Message "$control protection was reduced temporarily; automatic restoration is scheduled for $($expires.ToString('u'))." -Data @{Record=$record;After=$after;Compensator=$compensator} -Warnings @('Reducing Windows security controls increases exposure. Keep the maintenance interval short and restore immediately after the blocked task is complete.') -RestartRequired ($control -eq 'Uac')
    }catch{
        $primary=$_.Exception.Message;$rollbackError=$null
        if($mutationStarted){try{Restore-WinCareSecurityControlSnapshotInternal -Snapshot $before}catch{$rollbackError=$_.Exception.Message}}
        if($taskRegistered){Remove-WinCareSecurityRestoreTask -Id $id}
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        $rolledBack=$mutationStarted -and -not $rollbackError -and (Get-WinCareSecurityControlState -Control $control).Hash -eq $before.Hash
        New-WinCareResult -Success $false -Status Failed -Code $(if(-not $mutationStarted){'SecurityMaintenanceFailedBeforeMutation'}elseif($rolledBack){'SecurityMaintenanceFailedRolledBack'}else{'SecurityMaintenanceRollbackFailed'}) -Message $(if($rolledBack){"$primary The prior protection state was restored."}elseif($rollbackError){"$primary Rollback failed: $rollbackError"}else{$primary}) -ExitCode 1 -Data @{RecordId=$id;RolledBack=$rolledBack}
    }
}

function Invoke-WinCareRestoreSecurityControlStateAction {
    param([object]$Action)
    $p=$Action.Parameters;$recordId=[string]$p.RecordId;$control=[string]$p.Control
    try{$validatedSnapshot=Test-WinCareSecurityControlSnapshot -Snapshot $p.Snapshot -ExpectedControl $control}catch{return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceSnapshotInvalid' -Message $_.Exception.Message -ExitCode 74}
    $record=Get-WinCareSecurityMaintenanceRecord -Id $recordId -IncludeRestored|Select-Object -First 1
    if(-not $record){return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceRecordMissing' -Message 'The authenticated security-maintenance record is unavailable.' -ExitCode 74}
    if([int](Get-WinCarePropertyValue $record 'SchemaVersion' 0) -ne 1 -or [string]$record.Control -ne $control -or [string]$record.Status -notin @('Armed','Active')){return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceIdentityMismatch' -Message 'Security-maintenance record schema, control identity, or lifecycle state is invalid.' -ExitCode 74}
    try{$recordBefore=Test-WinCareSecurityControlSnapshot -Snapshot $record.Before -ExpectedControl $control}catch{return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceRecordSnapshotInvalid' -Message $_.Exception.Message -ExitCode 74}
    if([string]$recordBefore.Hash -ne [string]$validatedSnapshot.Hash -or (ConvertTo-WinCareCanonicalJson -InputObject $recordBefore -Depth 30) -ne (ConvertTo-WinCareCanonicalJson -InputObject $validatedSnapshot -Depth 30)){return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceSnapshotMismatch' -Message 'The recovery snapshot does not match the authenticated maintenance record.' -ExitCode 74}
    $current=Get-WinCareSecurityControlState -Control $control
    if([string]$current.Hash -ne [string]$p.ExpectedCurrentHash -and [string]$current.Hash -ne [string]$record.AppliedHash){return New-WinCareResult -Success $false -Status Blocked -Code 'SecurityMaintenanceRecoveryConflict' -Message 'The control changed after maintenance activation; WinCare will not overwrite the newer state.' -ExitCode 74}
    try{
        Restore-WinCareSecurityControlSnapshotInternal -Snapshot $validatedSnapshot
        $after=Get-WinCareSecurityControlState -Control $control
        if([string]$after.Hash -ne [string]$validatedSnapshot.Hash){throw 'Security-control restore verification failed.'}
        $record.Status='Restored';$record.RestoredAt=[datetime]::UtcNow.ToString('o');$record.RestoredHash=[string]$after.Hash
        $warnings=[Collections.Generic.List[string]]::new()
        try{Write-WinCareProtectedJson -LiteralPath (Get-WinCareSecurityMaintenanceRecordPath $recordId) -InputObject $record -Purpose 'WinCare.SecurityMaintenance'}catch{$warnings.Add("Protection was restored, but the maintenance record could not be finalized: $($_.Exception.Message)")}
        try{Remove-WinCareSecurityRestoreTask -Id $recordId}catch{$warnings.Add("Protection was restored, but the scheduled recovery task could not be removed: $($_.Exception.Message)")}
        New-WinCareResult -Success $true -Code 'SecurityMaintenanceRestored' -Message "$control was restored to its exact pre-maintenance state." -Data @{Record=$record;After=$after} -Warnings @($warnings) -RestartRequired ($control -eq 'Uac')
    }catch{New-WinCareResult -Success $false -Status Failed -Code 'SecurityMaintenanceRestoreFailed' -Message $_.Exception.Message -ExitCode 1}
}


