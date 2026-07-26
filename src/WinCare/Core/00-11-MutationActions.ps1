#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: Registry, service, task, feature, firewall, account, cleanup, and managed-file action handlers.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function ConvertTo-WinCareRegistryValuePayload {
    [CmdletBinding()]
    param([AllowNull()][object]$Value,[string]$ValueType='')
    if($null -eq $Value){return [pscustomobject]@{Value=$null;ValueType=if($ValueType){$ValueType}else{'None'}}}
    $type=if($ValueType){$ValueType}elseif($Value -is [byte[]]){'Binary'}elseif($Value -is [int] -or $Value -is [uint32]){'DWord'}elseif($Value -is [long] -or $Value -is [uint64]){'QWord'}elseif($Value -is [string[]]){'MultiString'}else{'String'}
    [pscustomobject]@{Value=$Value;ValueType=$type}
}
function Get-WinCareRegistryKey {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    if(-not $IsWindows){return $null};if(-not (Test-Path -LiteralPath $Path)){return [pscustomobject]@{Path=$Path;Exists=$false;Values=@();SubKeyCount=0}}
    $key=Get-Item -LiteralPath $Path -ErrorAction Stop;$property=Get-ItemProperty -LiteralPath $Path -ErrorAction Stop;$values=[Collections.Generic.List[object]]::new();foreach($name in $key.GetValueNames()){$values.Add([pscustomobject]@{Name=$name;Value=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);ValueType=$key.GetValueKind($name).ToString()})};[pscustomobject]@{Path=$Path;Exists=$true;Values=@($values|Sort-Object Name);SubKeyCount=$key.SubKeyCount;LastWriteTime=$null}
}
function Get-WinCareRegistryTreeSnapshot {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[ValidateRange(1,10000)][int]$MaximumKeys=5000)
    if(-not $IsWindows){return [pscustomobject]@{Path=$Path;Exists=$false;Values=@();SubKeys=@()}};if(-not (Test-Path -LiteralPath $Path)){return [pscustomobject]@{Path=$Path;Exists=$false;Values=@();SubKeys=@()}}
    $count=0
    function Read-Node([string]$NodePath){$script:__WinCareRegistrySnapshotCount++;if($script:__WinCareRegistrySnapshotCount -gt $MaximumKeys){throw "Registry snapshot exceeds $MaximumKeys keys."};$key=Get-Item -LiteralPath $NodePath -ErrorAction Stop;$values=[Collections.Generic.List[object]]::new();foreach($name in $key.GetValueNames()){$values.Add([pscustomobject]@{Name=$name;Value=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);ValueType=$key.GetValueKind($name).ToString()})};$children=[Collections.Generic.List[object]]::new();foreach($child in Get-ChildItem -LiteralPath $NodePath -ErrorAction SilentlyContinue){$children.Add((Read-Node $child.PSPath))};[pscustomobject]@{Name=$key.PSChildName;Path=$NodePath;Exists=$true;Values=@($values|Sort-Object Name);SubKeys=@($children|Sort-Object Name)}}
    $script:__WinCareRegistrySnapshotCount=0;try{$snapshot=Read-Node $Path;return $snapshot}finally{Remove-Variable __WinCareRegistrySnapshotCount -Scope Script -ErrorAction SilentlyContinue}
}
function Test-WinCareRegistryTreeSnapshot {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Snapshot)
    if($null -eq (Get-WinCareBridgeProperty $Snapshot 'Exists' $null)){return $false};if([bool]$Snapshot.Exists -and [string]::IsNullOrWhiteSpace([string](Get-WinCareBridgeProperty $Snapshot 'Path' ''))){return $false};foreach($value in @(Get-WinCareBridgeProperty $Snapshot 'Values' @())){if($null -eq (Get-WinCareBridgeProperty $value 'Name' $null) -or [string]::IsNullOrWhiteSpace([string](Get-WinCareBridgeProperty $value 'ValueType' ''))){return $false}};foreach($child in @(Get-WinCareBridgeProperty $Snapshot 'SubKeys' @())){if(-not (Test-WinCareRegistryTreeSnapshot $child)){return $false}};return $true
}
function Get-WinCareRegistryTreeSnapshotHash {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $snapshot=Get-WinCareRegistryTreeSnapshot $Path;if(Get-Command Get-WinCareCanonicalObjectHash -ErrorAction SilentlyContinue){return Get-WinCareCanonicalObjectHash $snapshot};$json=$snapshot|ConvertTo-Json -Compress -Depth 100;[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($json))).ToLowerInvariant()
}
function Invoke-WinCareRestoreRegistrySnapshotDirect {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][object]$Snapshot)
    if(-not (Test-WinCareRegistryTreeSnapshot $Snapshot)){throw 'Registry snapshot is invalid.'};if(-not [bool]$Snapshot.Exists){if(Test-Path $Path){Remove-Item -LiteralPath $Path -Recurse -Force};return}
    if(Test-Path $Path){Remove-Item -LiteralPath $Path -Recurse -Force};$null=New-Item -Path $Path -Force
    function Restore-Node([string]$NodePath,[object]$Node){$null=New-Item -Path $NodePath -Force;foreach($value in @($Node.Values)){New-ItemProperty -LiteralPath $NodePath -Name ([string]$value.Name) -Value $value.Value -PropertyType ([Microsoft.Win32.RegistryValueKind]::$($value.ValueType)) -Force|Out-Null};foreach($child in @($Node.SubKeys)){$childPath=Join-Path $NodePath ([string]$child.Name);Restore-Node $childPath $child}}
    Restore-Node $Path $Snapshot
}
function Invoke-WinCareSetRegistryValueAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Registry mutation';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;$path=[string]$p.Path;$name=[string]$p.Name;try{$beforeExists=$false;$beforeValue=$null;$beforeType=$null;if(Test-Path $path){$key=Get-Item $path;try{$beforeValue=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);$beforeType=$key.GetValueKind($name).ToString();$beforeExists=$true}catch{}};$null=New-Item -Path $path -Force;$payload=ConvertTo-WinCareRegistryValuePayload -Value $p.Value -ValueType ([string]$p.ValueType);New-ItemProperty -LiteralPath $path -Name $name -Value $payload.Value -PropertyType ([Microsoft.Win32.RegistryValueKind]::$($payload.ValueType)) -Force -ErrorAction Stop|Out-Null;$key=Get-Item $path;$observed=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);$success=(Get-WinCareCanonicalObjectHash $observed) -eq (Get-WinCareCanonicalObjectHash $p.Value);New-WinCareBridgeResult -Success $success -Code $(if($success){'RegistryValueSet'}else{'RegistryValueVerificationFailed'}) -Message $(if($success){'Registry value was set and observed.'}else{'Registry value did not match the requested payload.'}) -Data @{Path=$path;Name=$name;Before=@{Exists=$beforeExists;Value=$beforeValue;ValueType=$beforeType};After=@{Value=$observed;ValueType=$key.GetValueKind($name).ToString()};EvidenceType='RegistryValueReadback';Compensator=@{Type='RestoreRegistryBackup';Parameters=@{Path=$path;Name=$name;Exists=$beforeExists;Value=$beforeValue;ValueType=$beforeType};RequiresAdmin=[bool](Get-WinCareBridgeProperty $Action 'RequiresAdmin' $false)}} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'RegistryValueSetFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareRemoveRegistryValueAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Registry mutation';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;$path=[string]$p.Path;$name=[string]$p.Name;try{$exists=$false;$value=$null;$kind=$null;if(Test-Path $path){$key=Get-Item $path;try{$value=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);$kind=$key.GetValueKind($name).ToString();$exists=$true}catch{}};if($exists){Remove-ItemProperty -LiteralPath $path -Name $name -ErrorAction Stop};$remaining=$false;try{$null=(Get-Item $path).GetValueKind($name);$remaining=$true}catch{};$success=-not $remaining;New-WinCareBridgeResult -Success $success -Code $(if($success){'RegistryValueRemoved'}else{'RegistryValueRemovalVerificationFailed'}) -Message $(if($success){'Registry value is absent.'}else{'Registry value remains present.'}) -Data @{Path=$path;Name=$name;Before=@{Exists=$exists;Value=$value;ValueType=$kind};EvidenceType='RegistryValueAbsence';Compensator=@{Type='RestoreRegistryBackup';Parameters=@{Path=$path;Name=$name;Exists=$exists;Value=$value;ValueType=$kind};RequiresAdmin=[bool](Get-WinCareBridgeProperty $Action 'RequiresAdmin' $false)}} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'RegistryValueRemovalFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareApplyRegistryTreeAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Registry tree mutation';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;$path=[string]$p.Path;$before=Get-WinCareRegistryTreeSnapshot $path;$beforeHash=Get-WinCareRegistryTreeSnapshotHash $path;if($p.ExpectedSnapshotHash -and $beforeHash -ne [string]$p.ExpectedSnapshotHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'RegistryTreeChanged' -Message 'Registry tree changed after preview.' -ExitCode 74};try{$null=New-Item -Path $path -Force;foreach($entry in @($p.Values)){New-ItemProperty -LiteralPath $path -Name ([string]$entry.Name) -Value $entry.Value -PropertyType ([Microsoft.Win32.RegistryValueKind]::$($entry.ValueType)) -Force|Out-Null};foreach($name in @($p.DeleteValues)){Remove-ItemProperty -LiteralPath $path -Name ([string]$name) -ErrorAction SilentlyContinue};foreach($sub in @($p.DeleteSubKeys)){if([string]$sub -match '^[^\\/:*?"<>|]+$'){$target=Join-Path $path $sub;if(Test-Path $target){Remove-Item $target -Recurse -Force}}else{throw "Invalid subkey name: $sub"}};$after=Get-WinCareRegistryTreeSnapshot $path;$afterHash=Get-WinCareRegistryTreeSnapshotHash $path;New-WinCareBridgeResult -Success $true -Code 'RegistryTreeApplied' -Message 'Registry tree changes were applied and snapshotted.' -Data @{BeforeHash=$beforeHash;AfterHash=$afterHash;After=$after;EvidenceType='RegistryTreeSnapshot';Compensator=@{Type='RestoreRegistryTree';Parameters=@{Path=$path;Snapshot=$before;ExpectedCurrentHash=$afterHash};RequiresAdmin=[bool](Get-WinCareBridgeProperty $Action 'RequiresAdmin' $false)}}}catch{New-WinCareBridgeResult -Success $false -Code 'RegistryTreeApplyFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareRemoveRegistryTreeAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Registry tree removal';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;$path=[string]$p.Path;$before=Get-WinCareRegistryTreeSnapshot $path;$hash=Get-WinCareRegistryTreeSnapshotHash $path;if($p.ExpectedSnapshotHash -and $hash -ne [string]$p.ExpectedSnapshotHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'RegistryTreeChanged' -Message 'Registry tree changed after preview.' -ExitCode 74};try{if(Test-Path $path){Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop};$success=-not (Test-Path $path);New-WinCareBridgeResult -Success $success -Code $(if($success){'RegistryTreeRemoved'}else{'RegistryTreeRemovalVerificationFailed'}) -Message $(if($success){'Registry tree was removed.'}else{'Registry tree remains present.'}) -Data @{Before=$before;BeforeHash=$hash;EvidenceType='RegistryKeyAbsence';Compensator=@{Type='RestoreRegistryTree';Parameters=@{Path=$path;Snapshot=$before;ExpectedCurrentHash=Get-WinCareRegistryTreeSnapshotHash $path};RequiresAdmin=[bool](Get-WinCareBridgeProperty $Action 'RequiresAdmin' $false)}} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'RegistryTreeRemovalFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareRestoreRegistryTreeAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$path=[string]$p.Path
    try{
        if($p.ExpectedCurrentHash){
            $current=Get-WinCareRegistryTreeSnapshotHash $path
            if($current -ne [string]$p.ExpectedCurrentHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'RegistryTreeRecoveryConflict' -Message 'Registry tree changed after the original operation; compare-and-swap recovery was refused.' -ExitCode 74 -Data @{ExpectedCurrentHash=$p.ExpectedCurrentHash;ActualCurrentHash=$current}}
        }
        Invoke-WinCareRestoreRegistrySnapshotDirect -Path $path -Snapshot $p.Snapshot
        $after=Get-WinCareRegistryTreeSnapshot $path;$expected=Get-WinCareCanonicalObjectHash $p.Snapshot;$actual=Get-WinCareCanonicalObjectHash $after;$success=$actual -eq $expected
        New-WinCareBridgeResult -Success $success -Code $(if($success){'RegistryTreeRestored'}else{'RegistryTreeRestoreVerificationFailed'}) -Message $(if($success){'Registry tree was restored and snapshot-verified.'}else{'Registry tree restore did not match the snapshot.'}) -Data @{Path=$path;ExpectedSnapshotHash=$expected;ActualSnapshotHash=$actual;After=$after;EvidenceType='RegistryTreeSnapshot'} -ExitCode $(if($success){0}else{74})
    }catch{New-WinCareBridgeResult -Success $false -Code 'RegistryTreeRestoreFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareRestoreRegistryBackupAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;if([bool]$p.Exists){$synthetic=[pscustomobject]@{Parameters=@{Path=$p.Path;Name=$p.Name;Value=$p.Value;ValueType=$p.ValueType};RequiresAdmin=(Get-WinCareBridgeProperty $Action 'RequiresAdmin' $false)};return Invoke-WinCareSetRegistryValueAction $synthetic};$synthetic=[pscustomobject]@{Parameters=@{Path=$p.Path;Name=$p.Name};RequiresAdmin=(Get-WinCareBridgeProperty $Action 'RequiresAdmin' $false)};Invoke-WinCareRemoveRegistryValueAction $synthetic
}
function Invoke-WinCareServiceStartModeAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Service configuration';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;try{$service=Get-CimInstance Win32_Service -Filter ("Name='{0}'" -f ([string]$p.Name).Replace("'","''")) -ErrorAction Stop;$before=$service.StartMode;$map=@{Automatic='Automatic';Auto='Automatic';Manual='Manual';Disabled='Disabled';Boot='Boot';System='System'};$requested=$map[[string]$p.StartMode];if(-not $requested){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InvalidServiceStartMode' -Message 'Invalid service start mode.' -ExitCode 22};Set-Service -Name $p.Name -StartupType $requested -ErrorAction Stop;$after=(Get-CimInstance Win32_Service -Filter ("Name='{0}'" -f ([string]$p.Name).Replace("'","''"))).StartMode;$expected=if($requested -eq 'Automatic'){'Auto'}else{$requested};$success=$after -eq $expected;New-WinCareBridgeResult -Success $success -Code $(if($success){'ServiceStartModeChanged'}else{'ServiceStartModeVerificationFailed'}) -Message $(if($success){'Service start mode was changed and observed.'}else{"Observed start mode is '$after'."}) -Data @{Name=$p.Name;Before=$before;After=$after;EvidenceType='Win32ServiceState'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'ServiceStartModeFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareServiceRuntimeStateAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;try{$before=(Get-Service -Name $p.Name -ErrorAction Stop).Status.ToString();switch([string]$p.State){'Running'{Start-Service -Name $p.Name -ErrorAction Stop}'Stopped'{Stop-Service -Name $p.Name -Force -ErrorAction Stop}'Paused'{Suspend-Service -Name $p.Name -ErrorAction Stop}default{return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InvalidServiceState' -Message 'Invalid service state.' -ExitCode 22}};$service=Get-Service $p.Name;$service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::$($p.State),[TimeSpan]::FromSeconds(60));$after=(Get-Service $p.Name).Status.ToString();$success=$after -eq [string]$p.State;New-WinCareBridgeResult -Success $success -Code $(if($success){'ServiceStateChanged'}else{'ServiceStateVerificationFailed'}) -Message $(if($success){'Service runtime state was changed and observed.'}else{"Observed state is '$after'."}) -Data @{Name=$p.Name;Before=$before;After=$after;EvidenceType='ServiceControllerState'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'ServiceStateFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareScheduledTaskStateAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;try{$before=(Get-ScheduledTask -TaskPath $p.TaskPath -TaskName $p.TaskName -ErrorAction Stop).State;if([bool]$p.Enabled){Enable-ScheduledTask -TaskPath $p.TaskPath -TaskName $p.TaskName -ErrorAction Stop|Out-Null}else{Disable-ScheduledTask -TaskPath $p.TaskPath -TaskName $p.TaskName -ErrorAction Stop|Out-Null};$task=Get-ScheduledTask -TaskPath $p.TaskPath -TaskName $p.TaskName;$afterEnabled=$task.State -ne 'Disabled';$success=$afterEnabled -eq [bool]$p.Enabled;New-WinCareBridgeResult -Success $success -Code $(if($success){'ScheduledTaskStateChanged'}else{'ScheduledTaskVerificationFailed'}) -Message $(if($success){'Scheduled task state was changed and observed.'}else{'Scheduled task state did not match the request.'}) -Data @{TaskPath=$p.TaskPath;TaskName=$p.TaskName;Before=$before;After=$task.State;EvidenceType='ScheduledTaskState'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'ScheduledTaskStateFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareOptionalFeatureAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$state=[string]$p.State;try{if($state -in @('Enabled','Enable')){$result=Enable-WindowsOptionalFeature -Online -FeatureName $p.Name -All -NoRestart -ErrorAction Stop}else{$result=Disable-WindowsOptionalFeature -Online -FeatureName $p.Name -NoRestart -ErrorAction Stop};$after=Get-WindowsOptionalFeature -Online -FeatureName $p.Name -ErrorAction Stop;$success=if($state -in @('Enabled','Enable')){$after.State -eq 'Enabled'}else{$after.State -eq 'Disabled'};New-WinCareBridgeResult -Success $success -Code $(if($success){'OptionalFeatureStateChanged'}else{'OptionalFeatureVerificationFailed'}) -Message $(if($success){'Optional feature state was changed and observed.'}else{"Observed feature state is '$($after.State)'."}) -Data @{Name=$p.Name;Before=$p.Before;After=$after.State;EvidenceType='DismFeatureState'} -ExitCode $(if($success){0}else{74}) -RestartRequired ([bool]$result.RestartNeeded)}catch{New-WinCareBridgeResult -Success $false -Code 'OptionalFeatureStateFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareCapabilityAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$state=[string]$p.State;try{if($state -in @('Installed','Present','Enable','Enabled')){$after=Add-WindowsCapability -Online -Name $p.Name -ErrorAction Stop}else{$after=Remove-WindowsCapability -Online -Name $p.Name -ErrorAction Stop};$observed=Get-WindowsCapability -Online -Name $p.Name -ErrorAction Stop;$success=if($state -in @('Installed','Present','Enable','Enabled')){$observed.State -eq 'Installed'}else{$observed.State -eq 'NotPresent'};New-WinCareBridgeResult -Success $success -Code $(if($success){'CapabilityStateChanged'}else{'CapabilityVerificationFailed'}) -Message $(if($success){'Windows capability state was changed and observed.'}else{"Observed capability state is '$($observed.State)'."}) -Data @{Name=$p.Name;Before=$p.Before;After=$observed.State;EvidenceType='DismCapabilityState'} -ExitCode $(if($success){0}else{74}) -RestartRequired ([bool]$after.RestartNeeded)}catch{New-WinCareBridgeResult -Success $false -Code 'CapabilityStateFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareProvisionedAppxAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;try{$before=@(Get-AppxProvisionedPackage -Online|Where-Object{$_.PackageName -eq $p.PackageName});if($before.Count -eq 0){return New-WinCareBridgeResult -Success $true -Code 'ProvisionedAppxAlreadyAbsent' -Message 'Provisioned AppX package is already absent.' -Data @{PackageName=$p.PackageName;EvidenceType='ProvisionedPackageAbsence'}};$result=Remove-AppxProvisionedPackage -Online -PackageName $p.PackageName -ErrorAction Stop;$after=@(Get-AppxProvisionedPackage -Online|Where-Object{$_.PackageName -eq $p.PackageName});$success=$after.Count -eq 0;New-WinCareBridgeResult -Success $success -Code $(if($success){'ProvisionedAppxRemoved'}else{'ProvisionedAppxVerificationFailed'}) -Message $(if($success){'Provisioned AppX package was removed.'}else{'Provisioned AppX package remains present.'}) -Data @{PackageName=$p.PackageName;Result=$result;EvidenceType='ProvisionedPackageAbsence'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'ProvisionedAppxRemovalFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCarePowerSchemeAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$scheme=[string]$p.Scheme;if($scheme -notmatch '^(?:[a-fA-F0-9-]{36}|SCHEME_(?:BALANCED|MIN|MAX))$'){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InvalidPowerScheme' -Message 'Power scheme identifier is invalid.' -ExitCode 22};$result=Invoke-WinCareBridgeProcess -FilePath 'powercfg.exe' -ArgumentList @('/setactive',$scheme) -TimeoutSeconds 60;if(-not $result.Success){return $result};$query=Invoke-WinCareBridgeProcess -FilePath 'powercfg.exe' -ArgumentList @('/getactivescheme') -TimeoutSeconds 30;$success=$query.Success -and $query.Data.StdOut -match [regex]::Escape($scheme.Replace('SCHEME_',''));New-WinCareBridgeResult -Success $success -Code $(if($success){'PowerSchemeChanged'}else{'PowerSchemeVerificationFailed'}) -Message $(if($success){'Power scheme was changed and queried.'}else{'Active power scheme could not be verified.'}) -Data @{Requested=$scheme;Output=$query.Data.StdOut;EvidenceType='PowerCfgActiveScheme'} -ExitCode $(if($success){0}else{74})
}
function Invoke-WinCareHostsEntriesAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action
    $root=Join-Path $env:windir 'System32\drivers\etc'
    $hosts=Join-Path $root 'hosts'
    $blockId=[string]$p.BlockId
    if($blockId -notmatch '^[A-Za-z0-9._-]{1,80}$') {
        return New-WinCareBridgeResult -Success $false -Status Blocked `
            -Code 'InvalidHostsBlockId' -Message 'Hosts block ID is invalid.' -ExitCode 22
    }
    if(@($p.Entries).Count -gt 10000) {
        return New-WinCareBridgeResult -Success $false -Status Blocked `
            -Code 'HostsEntryLimitExceeded' -Message 'Hosts entry count exceeds 10,000.' -ExitCode 77
    }
    $beforeBytes=$null
    $mutationStarted=$false
    try {
        $hosts=Assert-WinCareSafePath -LiteralPath $hosts -AllowedRoots @($root)
        $beforeBytes=Read-WinCareBoundedFileBytes -LiteralPath $hosts -MaximumBytes 4194304
        $before=[Text.UTF8Encoding]::new($false,$true).GetString($beforeBytes)
        $beforeHash=(Get-WinCareSha256 -LiteralPath $hosts)
        if($p.BeforeHash -and $beforeHash -ne [string]$p.BeforeHash) {
            return New-WinCareBridgeResult -Success $false -Status Blocked `
                -Code 'HostsFileChanged' -Message 'Hosts file changed after preview.' -ExitCode 74
        }
        $begin="# BEGIN WINCARE $blockId"
        $end="# END WINCARE $blockId"
        $clean=[regex]::Replace($before,"(?ms)^\Q$begin\E.*?^\Q$end\E\s*",'')
        $content=$clean.TrimEnd()+"`r`n"
        if([bool]$p.Enabled) {
            $lines=[Collections.Generic.List[string]]::new()
            foreach($entry in @($p.Entries)) {
                $ip=[string](Get-WinCareBridgeProperty $entry 'Address' '')
                $host=[string](Get-WinCareBridgeProperty $entry 'HostName' '')
                $parsed=$null
                if(-not [Net.IPAddress]::TryParse($ip,[ref]$parsed) -or -not (Test-WinCareDnsHostName $host)) {
                    throw "Invalid hosts entry: $ip $host"
                }
                $lines.Add("$ip`t$host")
            }
            $content += $begin+"`r`n"+(@($lines)-join "`r`n")+"`r`n"+$end+"`r`n"
        }
        if([Text.Encoding]::UTF8.GetByteCount($content) -gt 4194304) { throw 'Managed hosts content exceeds 4 MiB.' }
        $mutationStarted=$true
        Write-WinCareAtomicText -LiteralPath $hosts -Text $content
        $after=Read-WinCareBoundedText -LiteralPath $hosts -MaximumBytes 4194304
        $success=if([bool]$p.Enabled) { $after.Contains($begin) -and $after.Contains($end) } else { -not $after.Contains($begin) }
        if(-not $success) { throw 'Hosts entry markers did not match the requested state.' }
        return New-WinCareBridgeResult -Success $true -Code 'HostsEntriesChanged' `
            -Message 'Managed hosts entries were atomically updated and marker-verified.' `
            -Data @{Path=$hosts;BeforeHash=$beforeHash;AfterHash=(Get-WinCareSha256 -LiteralPath $hosts);EvidenceType='AtomicManagedHostsBlock'}
    } catch {
        $primary=$_.Exception.Message
        if($mutationStarted -and $beforeBytes) {
            try {
                Write-WinCareAtomicBytes -LiteralPath $hosts -Bytes $beforeBytes
                $restored=Get-WinCareSha256 -LiteralPath $hosts
                $expected=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($beforeBytes)).ToLowerInvariant()
                if($restored -ne $expected) { throw 'Hosts rollback hash verification failed.' }
                return New-WinCareBridgeResult -Success $false -Code 'HostsEntriesFailedRolledBack' `
                    -Message $primary -ExitCode 1 -Data @{Path=$hosts;RestoredSha256=$restored;EvidenceType='ProviderRollback'}
            } catch {
                return New-WinCareBridgeResult -Success $false -Code 'HostsEntriesRollbackFailed' `
                    -Message "$primary Rollback failed: $($_.Exception.Message)" -ExitCode 1 `
                    -Data @{Path=$hosts;EvidenceType='ProviderRollbackFailure'}
            }
        }
        return New-WinCareBridgeResult -Success $false -Code 'HostsEntriesFailed' -Message $primary -ExitCode 1
    } finally {
        if($beforeBytes -and $beforeBytes.Length) { [Array]::Clear($beforeBytes,0,$beforeBytes.Length) }
    }
}
function Invoke-WinCareFirewallRuleAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;try{$rules=@(Get-NetFirewallRule -DisplayName $p.Name -ErrorAction Stop);$before=@($rules|Select-Object DisplayName,Name,Enabled,Direction,Action,Profile);Set-NetFirewallRule -DisplayName $p.Name -Enabled $(if([bool]$p.Enabled){'True'}else{'False'}) -ErrorAction Stop;$after=@(Get-NetFirewallRule -DisplayName $p.Name -ErrorAction Stop);$success=@($after|Where-Object{$_.Enabled -ne $(if([bool]$p.Enabled){'True'}else{'False'})}).Count -eq 0;New-WinCareBridgeResult -Success $success -Code $(if($success){'FirewallRuleStateChanged'}else{'FirewallRuleVerificationFailed'}) -Message $(if($success){'Firewall rule state was changed and observed.'}else{'One or more firewall rules did not match the request.'}) -Data @{Before=$before;After=@($after|Select-Object DisplayName,Name,Enabled,Direction,Action,Profile);EvidenceType='FirewallRuleState'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'FirewallRuleStateFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareFirewallProfileStateAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;try{$names=@($p.Profiles);$before=@(Get-NetFirewallProfile -Name $names -ErrorAction Stop|Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction);Set-NetFirewallProfile -Name $names -Enabled ([bool]$p.Enabled) -ErrorAction Stop;$after=@(Get-NetFirewallProfile -Name $names -ErrorAction Stop);$success=@($after|Where-Object{$_.Enabled -ne [bool]$p.Enabled}).Count -eq 0;New-WinCareBridgeResult -Success $success -Code $(if($success){'FirewallProfileStateChanged'}else{'FirewallProfileVerificationFailed'}) -Message $(if($success){'Firewall profile state was changed and observed.'}else{'Firewall profile state did not match the request.'}) -Data @{Before=$before;After=@($after|Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction);EvidenceType='FirewallProfileState'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'FirewallProfileStateFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareLocalUserStateAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;try{$before=Get-LocalUser -Name $p.Name -ErrorAction Stop;if([bool]$p.Enabled){Enable-LocalUser -Name $p.Name -ErrorAction Stop}else{Disable-LocalUser -Name $p.Name -ErrorAction Stop};$after=Get-LocalUser -Name $p.Name;$success=$after.Enabled -eq [bool]$p.Enabled;New-WinCareBridgeResult -Success $success -Code $(if($success){'LocalUserStateChanged'}else{'LocalUserVerificationFailed'}) -Message $(if($success){'Local user state was changed and observed.'}else{'Local user state did not match the request.'}) -Data @{Name=$p.Name;Before=$before.Enabled;After=$after.Enabled;EvidenceType='LocalUserState'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'LocalUserStateFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareDefenderPreferenceAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$allowed=@('DisableRealtimeMonitoring','MAPSReporting','SubmitSamplesConsent','PUAProtection','EnableNetworkProtection','DisableIOAVProtection','DisableScriptScanning','CloudBlockLevel','CloudExtendedTimeout','DisableBehaviorMonitoring');if([string]$p.Name -notin $allowed){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'DefenderPreferenceNotAllowed' -Message 'Defender preference is not in the WinCare allowlist.' -ExitCode 22};try{$before=(Get-MpPreference -ErrorAction Stop).$($p.Name);$arguments=@{};$arguments[[string]$p.Name]=$p.Value;Set-MpPreference @arguments -ErrorAction Stop;$after=(Get-MpPreference -ErrorAction Stop).$($p.Name);$success=(Get-WinCareCanonicalObjectHash $after) -eq (Get-WinCareCanonicalObjectHash $p.Value);New-WinCareBridgeResult -Success $success -Code $(if($success){'DefenderPreferenceChanged'}else{'DefenderPreferenceVerificationFailed'}) -Message $(if($success){'Defender preference was changed and observed.'}else{'Defender preference did not match the request.'}) -Data @{Name=$p.Name;Before=$before;After=$after;EvidenceType='DefenderPreferenceState'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'DefenderPreferenceFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareDeliveryOptimizationCleanupAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $before=$null;try{if(Get-Command Get-DeliveryOptimizationStatus -ErrorAction SilentlyContinue){$before=@(Get-DeliveryOptimizationStatus)};if(Get-Command Delete-DeliveryOptimizationCache -ErrorAction SilentlyContinue){Delete-DeliveryOptimizationCache -Force -ErrorAction Stop}else{$service=Get-Service DoSvc -ErrorAction SilentlyContinue;if($service -and $service.Status -eq 'Running'){Stop-Service DoSvc -Force};$path=Join-Path $env:ProgramData 'Microsoft\Windows\DeliveryOptimization\Cache';if(Test-Path $path){Get-ChildItem $path -Force -ErrorAction SilentlyContinue|Remove-Item -Recurse -Force -ErrorAction Stop};if($service -and $service.StartType -ne 'Disabled'){Start-Service DoSvc -ErrorAction SilentlyContinue}};$after=if(Get-Command Get-DeliveryOptimizationStatus -ErrorAction SilentlyContinue){@(Get-DeliveryOptimizationStatus)}else{@()};New-WinCareBridgeResult -Success $true -Code 'DeliveryOptimizationCacheCleared' -Message 'Delivery Optimization cache cleanup completed.' -Data @{Before=$before;After=$after;EvidenceType='DeliveryOptimizationState'}}catch{New-WinCareBridgeResult -Success $false -Code 'DeliveryOptimizationCleanupFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareRecycleBinAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;try{$args=@{};if($p.Drive){$args.DriveLetter=[string]$p.Drive};Clear-RecycleBin @args -Force -Confirm:$false -ErrorAction Stop;New-WinCareBridgeResult -Success $true -Code 'RecycleBinCleared' -Message 'Recycle Bin was cleared.' -Data @{Drive=$p.Drive;EvidenceType='ClearRecycleBinCompleted'}}catch{New-WinCareBridgeResult -Success $false -Code 'RecycleBinCleanupFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareStoreCacheAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $result=Invoke-WinCareBridgeProcess -FilePath 'wsreset.exe' -ArgumentList @('-i') -TimeoutSeconds 600 -SuccessExitCodes @(0);if($result.Success){$result.Code='StoreCacheReset';$result.Message='Microsoft Store cache reset completed.';$result.Data.EvidenceType='WsresetExitCode'};return $result
}
function Invoke-WinCareStorageSenseAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$path='HKCU:\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy';try{$null=New-Item -Path $path -Force;New-ItemProperty -LiteralPath $path -Name 01 -Value $(if([bool]$p.Enable){1}else{0}) -PropertyType DWord -Force|Out-Null;$after=Get-WinCareStorageSenseState;$success=$after.Enabled -eq [bool]$p.Enable;New-WinCareBridgeResult -Success $success -Code $(if($success){'StorageSenseChanged'}else{'StorageSenseVerificationFailed'}) -Message $(if($success){'Storage Sense state was changed and observed.'}else{'Storage Sense state did not match the request.'}) -Data @{Before=$p.Previous;After=$after;EvidenceType='StorageSenseRegistry'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'StorageSenseFailed' -Message $_.Exception.Message -ExitCode 1}
}
function New-WinCareStoreCachePlan {
    [CmdletBinding()]
    param()
    $action=New-WinCareBridgeAction -Type 'ResetStoreCache' -Label 'Reset Microsoft Store cache' -Risk Low -Parameters @{} -TimeoutSeconds 600 -Verification 'wsreset.exe must exit with code 0.';New-WinCareBridgePlan -Title 'Reset Microsoft Store cache' -Actions @($action) -RollbackOnFailure $false
}
function Remove-WinCareEmptyDirectory {
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][string]$Path,[switch]$Recurse)
    $resolved=(Resolve-Path $Path -ErrorAction Stop).Path;$removed=[Collections.Generic.List[string]]::new();$directories=if($Recurse){@(Get-ChildItem $resolved -Directory -Recurse -Force|Sort-Object FullName -Descending)}else{@(Get-Item $resolved)};foreach($dir in $directories){if(@(Get-ChildItem $dir.FullName -Force -ErrorAction SilentlyContinue).Count -eq 0 -and $PSCmdlet.ShouldProcess($dir.FullName,'Remove empty directory')){Remove-Item $dir.FullName -Force;$removed.Add($dir.FullName)}};return @($removed)
}
function New-WinCareManagedFileWriteAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][AllowEmptyString()][string]$Content,[string[]]$AllowedRoots=@())
    $full=[IO.Path]::GetFullPath($Path)
    $contentBytes=[Text.Encoding]::UTF8.GetBytes($Content)
    if($contentBytes.Length -gt 67108864){throw 'Managed file content exceeds 64 MiB.'}
    if($AllowedRoots.Count -eq 0){$AllowedRoots=@((Split-Path -Parent $full))}
    $beforeHash=if(Test-Path $full){Get-WinCareSha256 -LiteralPath $full}else{''}
    New-WinCareBridgeAction -Type 'WriteManagedFile' `
        -Label "Write managed file $([IO.Path]::GetFileName($full))" -Risk High `
        -Parameters @{
            Path=$full
            ContentBase64=[Convert]::ToBase64String($contentBytes)
            ExpectedBeforeHash=$beforeHash
            AllowedRoots=@($AllowedRoots)
        } -Reversible $true -Verification 'The file SHA-256 must match the planned content.'
}
function Invoke-WinCareWriteManagedFileAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$path=$null;$temp=$null;$backup=$null;$beforeExists=$false;$beforeBytes=$null;$beforeHash='';$mutationStarted=$false
    try{
        $path=if(Get-Command Assert-WinCareSafePath -ErrorAction SilentlyContinue){Assert-WinCareSafePath -LiteralPath $p.Path -AllowedRoots @($p.AllowedRoots) -AllowMissing}else{[IO.Path]::GetFullPath($p.Path)}
        $beforeExists=Test-Path -LiteralPath $path;$beforeBytes=if($beforeExists){Read-WinCareBoundedFileBytes -LiteralPath $path -MaximumBytes 67108864}else{$null};$beforeHash=if($beforeExists){Get-WinCareSha256 -LiteralPath $path}else{''}
        if([string]$p.ExpectedBeforeHash -ne $beforeHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'ManagedFileChanged' -Message 'Managed file changed after preview.' -ExitCode 74}
        $bytes=[Convert]::FromBase64String([string]$p.ContentBase64);if($bytes.Length -gt 67108864){throw 'Managed file content exceeds 64 MiB.'};$parent=Split-Path -Parent $path;$null=New-Item -ItemType Directory -Path $parent -Force
        $leaf=[IO.Path]::GetFileName($path);$temp=Join-Path $parent ('.'+$leaf+'.'+[guid]::NewGuid().ToString('N')+'.tmp');$backup=Join-Path $parent ('.'+$leaf+'.'+[guid]::NewGuid().ToString('N')+'.bak')
        $stream=[IO.FileStream]::new($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::WriteThrough)
        try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
        $expected=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        if((Get-WinCareSha256 -LiteralPath $temp) -ne $expected){throw 'Managed file staging hash verification failed.'}
        $mutationStarted=$true
        if($beforeExists){[IO.File]::Replace($temp,$path,$backup,$true)}else{[IO.File]::Move($temp,$path)}
        $actual=Get-WinCareSha256 -LiteralPath $path
        if($actual -ne $expected){throw 'Managed file hash did not match the planned content.'}
        if($beforeExists -and (Test-Path -LiteralPath $backup)){Remove-Item -LiteralPath $backup -Force -ErrorAction Stop}
        return New-WinCareBridgeResult -Success $true -Code 'ManagedFileWritten' -Message 'Managed file was durably replaced and hash-verified.' -Data @{Path=$path;BeforeExists=$beforeExists;BeforeContentBase64=if($beforeExists){[Convert]::ToBase64String($beforeBytes)}else{''};BeforeHash=$beforeHash;AfterHash=$actual;EvidenceType='DurableAtomicFileSha256';Compensator=@{Type=if($beforeExists){'WriteManagedFile'}else{'RemoveManagedFile'};Parameters=if($beforeExists){@{Path=$path;ContentBase64=[Convert]::ToBase64String($beforeBytes);ExpectedBeforeHash=$actual;AllowedRoots=@($p.AllowedRoots)}}else{@{Path=$path;ExpectedBeforeHash=$actual;AllowedRoots=@($p.AllowedRoots)}};RequiresAdmin=[bool](Get-WinCareBridgeProperty $Action 'RequiresAdmin' $false)}}
    }catch{
        $primary=$_.Exception.Message
        if($mutationStarted){
            try{
                if($beforeExists){
                    if(Test-Path -LiteralPath $backup){if(Test-Path -LiteralPath $path){[IO.File]::Replace($backup,$path,$null,$true)}else{[IO.File]::Move($backup,$path)}}else{Write-WinCareAtomicBytes -LiteralPath $path -Bytes $beforeBytes -MaximumBytes 67108864}
                    if((Get-WinCareSha256 -LiteralPath $path) -ne $beforeHash){throw 'Restored file hash does not match the before-state.'}
                }elseif(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force -ErrorAction Stop}
                return New-WinCareBridgeResult -Success $false -Code 'ManagedFileWriteFailedRolledBack' -Message $primary -ExitCode 1 -Data @{Path=$path;BeforeHash=$beforeHash;EvidenceType='ProviderRollback'}
            }catch{return New-WinCareBridgeResult -Success $false -Code 'ManagedFileWriteRollbackFailed' -Message "$primary Rollback failed: $($_.Exception.Message)" -ExitCode 1 -Data @{Path=$path;BeforeHash=$beforeHash;EvidenceType='ProviderRollbackFailure'}}
        }
        New-WinCareBridgeResult -Success $false -Code 'ManagedFileWriteFailed' -Message $primary -ExitCode 1
    }finally{
        if($temp -and (Test-Path -LiteralPath $temp)){Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue}
        if($backup -and (Test-Path -LiteralPath $backup)){Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue}
    }
}
function Invoke-WinCareRemoveManagedFileAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;try{$path=if(Get-Command Assert-WinCareSafePath -ErrorAction SilentlyContinue){Assert-WinCareSafePath -LiteralPath $p.Path -AllowedRoots @($p.AllowedRoots) -AllowMissing}else{[IO.Path]::GetFullPath($p.Path)};if(-not (Test-Path $path)){return New-WinCareBridgeResult -Success $true -Code 'ManagedFileAlreadyAbsent' -Message 'Managed file is already absent.' -Data @{Path=$path;EvidenceType='FileAbsence'}};$hash=Get-WinCareSha256 -LiteralPath $path;if($p.ExpectedBeforeHash -and $hash -ne [string]$p.ExpectedBeforeHash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'ManagedFileChanged' -Message 'Managed file changed after preview.' -ExitCode 74};$bytes=Read-WinCareBoundedFileBytes -LiteralPath $path -MaximumBytes 67108864;Remove-Item $path -Force -ErrorAction Stop;$success=-not (Test-Path $path);New-WinCareBridgeResult -Success $success -Code $(if($success){'ManagedFileRemoved'}else{'ManagedFileRemovalFailed'}) -Message $(if($success){'Managed file was removed.'}else{'Managed file remains present.'}) -Data @{Path=$path;BeforeHash=$hash;EvidenceType='FileAbsence';Compensator=@{Type='WriteManagedFile';Parameters=@{Path=$path;ContentBase64=[Convert]::ToBase64String($bytes);ExpectedBeforeHash='';AllowedRoots=@($p.AllowedRoots)};RequiresAdmin=[bool](Get-WinCareBridgeProperty $Action 'RequiresAdmin' $false)}} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'ManagedFileRemovalFailed' -Message $_.Exception.Message -ExitCode 1}
}
