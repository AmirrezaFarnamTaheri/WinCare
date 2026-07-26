function Get-WinCareStartupRegistryLocation {
    @(
        @{ Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'; Scope='User' },
        @{ Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce'; Scope='User' },
        @{ Path='HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'; Scope='Machine' },
        @{ Path='HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnce'; Scope='Machine' },
        @{ Path='HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'; Scope='Machine' }
    )
}

function Test-WinCareStartupRegistryLocation {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$Scope)
    [bool](Get-WinCareStartupRegistryLocation|Where-Object{$_.Path -eq $Path -and $_.Scope -eq $Scope}|Select-Object -First 1)
}

function Get-WinCareStartupItem {
    [CmdletBinding()]
    param()
    if (-not $IsWindows) { return @() }
    $items=[Collections.Generic.List[object]]::new()
    foreach($location in Get-WinCareStartupRegistryLocation){
        $property=Get-ItemProperty -Path $location.Path -ErrorAction SilentlyContinue;$registryKey=Get-Item -Path $location.Path -ErrorAction SilentlyContinue
        if(-not $property -or -not $registryKey){continue}
        foreach($entry in $property.PSObject.Properties){
            if($entry.Name -like 'PS*'){continue}
            $items.Add([pscustomobject]@{PSTypeName='WinCare.StartupItem';Id="registry:$($location.Path):$($entry.Name)";Name=$entry.Name;Command=[string]$entry.Value;Scope=$location.Scope;Source='Registry';Location=$location.Path;ValueKind=[string]$registryKey.GetValueKind($entry.Name);Enabled=$true;RequiresAdmin=($location.Scope -eq 'Machine');FilePath=$null})
        }
    }
    $folders=@(@{Path=[Environment]::GetFolderPath('Startup');Scope='User'},@{Path=[Environment]::GetFolderPath('CommonStartup');Scope='Machine'})
    foreach($folder in $folders){
        if(-not $folder.Path -or -not(Test-Path -LiteralPath $folder.Path)){continue}
        foreach($file in Get-ChildItem -LiteralPath $folder.Path -File -Force -ErrorAction SilentlyContinue){
            if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){continue}
            $items.Add([pscustomobject]@{PSTypeName='WinCare.StartupItem';Id="file:$($file.FullName)";Name=$file.BaseName;Command=$file.FullName;Scope=$folder.Scope;Source='StartupFolder';Location=$folder.Path;ValueKind=$null;Enabled=$true;RequiresAdmin=($folder.Scope -eq 'Machine');FilePath=$file.FullName})
        }
    }
    @($items|Sort-Object Name,Scope)
}

function New-WinCareDisableStartupPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][object[]]$Items)
    $plan=New-WinCarePlan -Title "Disable $($Items.Count) startup item(s)" -Description 'Captures and authenticates the original entry before disabling it.' -SourceRecords @('SRC:d4218ff43e')
    foreach($item in $Items){
        $action=New-WinCareAction -Type DisableStartupItem -Label "Disable $($item.Name)" -Risk Moderate -Parameters @{Id=$item.Id;Name=$item.Name;Command=$item.Command;Source=$item.Source;Location=$item.Location;FilePath=$item.FilePath;Scope=$item.Scope;ValueKind=$item.ValueKind} -RequiresAdmin:$item.RequiresAdmin -Reversible $true -Verification 'Startup entry is absent from its active location.' -RecoveryDescription 'Restore the startup item from the authenticated provider-issued recovery record.' -Tags @('Startup') -SourceRecords @('SRC:d4218ff43e')
        $null=Add-WinCarePlanAction -Plan $plan -Action $action
    }
    $plan
}

function Get-WinCareDisabledStartupItem {
    [CmdletBinding()]param()
    $folder=Join-Path (Join-Path $script:WinCareState.Root 'Backups') 'Startup';if(-not(Test-Path -LiteralPath $folder)){return @()}
    foreach($file in Get-ChildItem -LiteralPath $folder -Filter '*.json' -File -ErrorAction SilentlyContinue){
        try{$data=Read-WinCareProtectedJson -LiteralPath $file.FullName -Purpose 'WinCare.StartupBackup';[pscustomobject]@{Name=$data.Name;Source=$data.Source;Scope=$data.Scope;BackupPath=$file.FullName;DisabledAt=$data.DisabledAt;Data=$data;Valid=$true}}catch{Write-WinCareLog -Level Warning -Message 'Ignoring an invalid startup backup.' -Data @{path=$file.FullName;error=$_.Exception.Message}}
    }
}

function New-WinCareEnableStartupPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][object[]]$Items)
    $plan=New-WinCarePlan -Title "Restore $($Items.Count) startup item(s)" -SourceRecords @('SRC:d4218ff43e')
    foreach($item in $Items){$action=New-WinCareAction -Type EnableStartupItem -Label "Restore $($item.Name)" -Risk Moderate -Parameters @{BackupPath=$item.BackupPath;Name=$item.Name;Scope=$item.Scope} -RequiresAdmin:($item.Scope -eq 'Machine') -Reversible $false -Verification 'Startup entry is restored to its authenticated original location.' -Tags @('Startup','Recovery') -SourceRecords @('SRC:d4218ff43e');$null=Add-WinCarePlanAction -Plan $plan -Action $action}
    $plan
}

function Invoke-WinCareDisableStartupAction {
    param([object]$Action)
    $p=$Action.Parameters
    if([string]$p.Source -notin @('Registry','StartupFolder')){return New-WinCareResult -Success $false -Message 'Unsupported startup source.' -ExitCode 22}
    if([string]$p.Scope -notin @('User','Machine')){return New-WinCareResult -Success $false -Message 'Unsupported startup scope.' -ExitCode 22}
    $folder=Join-Path (Join-Path $script:WinCareState.Root 'Backups') 'Startup';$null=New-Item -ItemType Directory -Path $folder -Force
    $backupPath=Join-Path $folder ("{0}-{1}.json" -f (ConvertTo-WinCareSafeFileName $p.Name),[guid]::NewGuid().ToString('N'))
    $backup=[ordered]@{SchemaVersion=2;RecordType='StartupBackup';Id=[string]$p.Id;Name=[string]$p.Name;Command=[string]$p.Command;Source=[string]$p.Source;Location=[string]$p.Location;FilePath=[string]$p.FilePath;Scope=[string]$p.Scope;ValueKind=[string]$p.ValueKind;FileBackupPath=$null;FileBackupSha256=$null;DisabledAt=[datetime]::UtcNow.ToString('o');DisabledBySid=if($IsWindows){[Security.Principal.WindowsIdentity]::GetCurrent().User.Value}else{[Environment]::UserName}}
    if($p.Source -eq 'Registry'){
        if(-not(Test-WinCareStartupRegistryLocation -Path ([string]$p.Location) -Scope ([string]$p.Scope))){return New-WinCareResult -Success $false -Message 'Startup registry location is outside the allowlist.' -ExitCode 5}
        $key=Get-Item -Path $p.Location -ErrorAction SilentlyContinue
        if(-not $key -or $key.GetValueNames() -notcontains [string]$p.Name){return New-WinCareResult -Success $false -Message 'Startup registry value no longer exists.' -ExitCode 2}
        $supportedKinds=@('String','ExpandString','Binary','DWord','MultiString','QWord')
        if([string]$p.ValueKind -notin $supportedKinds){return New-WinCareResult -Success $false -Message 'Startup registry value type is unsupported.' -ExitCode 22}
        Write-WinCareProtectedJson -LiteralPath $backupPath -InputObject $backup -Purpose 'WinCare.StartupBackup'
        try{Remove-ItemProperty -Path $p.Location -Name $p.Name -ErrorAction Stop}
        catch{
            $removeError=$_.Exception.Message
            $after=Get-Item -Path $p.Location -ErrorAction SilentlyContinue
            if($after -and $after.GetValueNames() -contains [string]$p.Name){Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue;return New-WinCareResult -Success $false -Message "Startup registry value was not removed: $removeError" -ExitCode 5}
            try{$null=New-ItemProperty -Path $p.Location -Name $p.Name -Value $p.Command -PropertyType ([string]$p.ValueKind) -ErrorAction Stop;Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue;return New-WinCareResult -Success $false -Status Failed -Code 'DisableSelfRolledBack' -Message "Startup removal raised an error and the original value was restored: $removeError" -ExitCode 5}
            catch{return New-WinCareResult -Success $false -Status Failed -Code 'DisablePartialFailure' -Message "Startup removal raised an error and automatic restoration failed: $removeError" -Warnings @("Authenticated recovery record retained at $backupPath") -ExitCode 5}
        }
        $after=Get-Item -Path $p.Location -ErrorAction SilentlyContinue
        if($after -and $after.GetValueNames() -contains [string]$p.Name){Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue;return New-WinCareResult -Success $false -Message 'Startup registry value still exists after removal.' -ExitCode 1}
    }else{
        $allowedFolder=if($p.Scope -eq 'Machine'){[Environment]::GetFolderPath('CommonStartup')}else{[Environment]::GetFolderPath('Startup')}
        $source=Assert-WinCareSafePath -LiteralPath ([string]$p.FilePath) -AllowedRoots @($allowedFolder)
        $disabledFolder=Join-Path $folder 'Files';$null=New-Item -ItemType Directory -Path $disabledFolder -Force
        $destination=Join-Path $disabledFolder ("{0}-{1}{2}" -f (ConvertTo-WinCareSafeFileName $p.Name),[guid]::NewGuid().ToString('N'),[IO.Path]::GetExtension($source))
        if(Test-Path -LiteralPath $destination){return New-WinCareResult -Success $false -Message 'Startup backup destination already exists.' -ExitCode 17}
        $backup.FileBackupPath=$destination;$backup.FileBackupSha256=Get-WinCareSha256 -LiteralPath $source
        Write-WinCareProtectedJson -LiteralPath $backupPath -InputObject $backup -Purpose 'WinCare.StartupBackup'
        try{Move-Item -LiteralPath $source -Destination $destination -ErrorAction Stop}
        catch{Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue;return New-WinCareResult -Success $false -Message "Startup file could not be disabled: $($_.Exception.Message)" -ExitCode 5}
        $verified=(-not(Test-Path -LiteralPath $source)) -and (Test-Path -LiteralPath $destination) -and ((Get-WinCareSha256 -LiteralPath $destination) -eq [string]$backup.FileBackupSha256)
        if(-not $verified){
            $warnings=[Collections.Generic.List[string]]::new()
            try{if((Test-Path -LiteralPath $destination)-and -not(Test-Path -LiteralPath $source)){Move-Item -LiteralPath $destination -Destination $source -ErrorAction Stop};if(Test-Path -LiteralPath $source){Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue}}catch{$warnings.Add("Automatic startup-file rollback failed: $($_.Exception.Message)")}
            return New-WinCareResult -Success $false -Status Failed -Code 'DisableVerificationFailed' -Message 'Startup file move could not be verified and rollback was attempted.' -Warnings @($warnings) -ExitCode 74
        }
    }
    $compensator=@{Type='EnableStartupItem';RequiresAdmin=[bool]$Action.RequiresAdmin;Parameters=@{BackupPath=$backupPath;Name=[string]$p.Name;Scope=[string]$p.Scope}}
    New-WinCareResult -Success $true -Message 'Startup item disabled with an authenticated recovery record.' -Data @{BackupPath=$backupPath;BackupSha256=Get-WinCareSha256 -LiteralPath $backupPath;Compensator=$compensator}
}

function Invoke-WinCareEnableStartupAction {
    param([object]$Action)
    $backupRoot=Join-Path (Join-Path $script:WinCareState.Root 'Backups') 'Startup'
    $backupPath=Assert-WinCareSafePath -LiteralPath ([string]$Action.Parameters.BackupPath) -AllowedRoots @($backupRoot)
    $backup=Read-WinCareProtectedJson -LiteralPath $backupPath -Purpose 'WinCare.StartupBackup'
    if([int]$backup.SchemaVersion -ne 2 -or [string]$backup.RecordType -ne 'StartupBackup'){return New-WinCareResult -Success $false -Message 'Startup backup schema is invalid.' -ExitCode 22}
    if([string]$backup.Name -ne [string]$Action.Parameters.Name -or [string]$backup.Scope -ne [string]$Action.Parameters.Scope){return New-WinCareResult -Success $false -Message 'Startup backup identity does not match the approved action.' -ExitCode 74}
    if($backup.Source -eq 'Registry'){
        if(-not(Test-WinCareStartupRegistryLocation -Path ([string]$backup.Location) -Scope ([string]$backup.Scope))){return New-WinCareResult -Success $false -Message 'Startup registry recovery location is outside the allowlist.' -ExitCode 5}
        $null=New-Item -Path $backup.Location -Force;$key=Get-Item -Path $backup.Location -ErrorAction Stop
        if($key.GetValueNames() -contains [string]$backup.Name){return New-WinCareResult -Success $false -Message 'A startup value with the same name already exists; it was not overwritten.' -ExitCode 17}
        $supportedKinds=@('String','ExpandString','Binary','DWord','MultiString','QWord');if([string]$backup.ValueKind -notin $supportedKinds){return New-WinCareResult -Success $false -Message 'Startup registry value type is unsupported.' -ExitCode 22}
        $null=New-ItemProperty -Path $backup.Location -Name $backup.Name -Value $backup.Command -PropertyType ([string]$backup.ValueKind) -ErrorAction Stop
        $key=Get-Item -Path $backup.Location -ErrorAction Stop;if($key.GetValueNames() -notcontains [string]$backup.Name){return New-WinCareResult -Success $false -Message 'Restored startup registry value could not be verified.' -ExitCode 1}
    }elseif($backup.Source -eq 'StartupFolder'){
        $allowedFolder=if($backup.Scope -eq 'Machine'){[Environment]::GetFolderPath('CommonStartup')}else{[Environment]::GetFolderPath('Startup')}
        $destination=Assert-WinCareSafePath -LiteralPath ([string]$backup.FilePath) -AllowedRoots @($allowedFolder) -AllowMissing
        $source=Assert-WinCareSafePath -LiteralPath ([string]$backup.FileBackupPath) -AllowedRoots @(Join-Path $backupRoot 'Files')
        if((Get-WinCareSha256 -LiteralPath $source) -ne [string]$backup.FileBackupSha256){return New-WinCareResult -Success $false -Message 'Disabled startup file failed integrity verification.' -ExitCode 74}
        if(Test-Path -LiteralPath $destination){return New-WinCareResult -Success $false -Message 'Original startup path is occupied; it was not overwritten.' -ExitCode 17}
        $null=New-Item -ItemType Directory -Path $allowedFolder -Force;Move-Item -LiteralPath $source -Destination $destination
        if(-not(Test-Path -LiteralPath $destination)){return New-WinCareResult -Success $false -Message 'Restored startup file could not be verified.' -ExitCode 1}
    }else{return New-WinCareResult -Success $false -Message 'Startup backup source is invalid.' -ExitCode 22}
    Remove-Item -LiteralPath $backupPath -Force
    New-WinCareResult -Success $true -Message 'Startup item restored from an authenticated recovery record.'
}


