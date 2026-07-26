#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: Page-file discovery, crash-dump-aware planning, mutation, and restoration.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function Get-WinCarePageFileConfiguration {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return [pscustomobject]@{Supported=$false;Mode='Unsupported';AutomaticManagedPagefile=$false;Settings=@();CrashDump=[pscustomobject]@{Mode='Unknown';PageFileRequired=$null};Hash=''}}
    $system=Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue
    $os=Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
    $settings=@(Get-CimInstance Win32_PageFileSetting -ErrorAction SilentlyContinue|ForEach-Object{[pscustomobject]@{Name=[string]$_.Name;InitialSizeMB=[int]$_.InitialSize;MaximumSizeMB=[int]$_.MaximumSize}})
    $dumpValue=0
    try{$dumpValue=[int](Get-ItemPropertyValue -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl' -Name CrashDumpEnabled -ErrorAction Stop)}catch{}
    $dumpMode=switch($dumpValue){0{'None'}1{'Complete'}2{'Kernel'}3{'Small'}7{'Automatic'}10{'Active'}default{'Unknown'}}
    $pageFileRequired=$dumpValue -in @(1,2,7,10)
    $mode=if($system -and [bool]$system.AutomaticManagedPagefile){'SystemManaged'}elseif($settings.Count){'Custom'}else{'Disabled'}
    $record=[ordered]@{
        Supported=$true
        Mode=$mode
        AutomaticManagedPagefile=if($system){[bool]$system.AutomaticManagedPagefile}else{$false}
        Settings=$settings
        TotalPhysicalMemoryMB=if($system){[math]::Ceiling([double]$system.TotalPhysicalMemory/1MB)}else{0}
        TotalVirtualMemoryMB=if($os){[math]::Ceiling([double]$os.TotalVirtualMemorySize/1KB)}else{0}
        FreeVirtualMemoryMB=if($os){[math]::Ceiling([double]$os.FreeVirtualMemory/1KB)}else{0}
        CrashDump=[pscustomobject]@{Mode=$dumpMode;Value=$dumpValue;PageFileRequired=$pageFileRequired}
    }
    $record.Hash=Get-WinCareCanonicalObjectHash $record
    [pscustomobject]$record
}
function Get-WinCarePageFileRecommendation {
    [CmdletBinding()]
    param()
    $summary=Get-WinCareSystemSummary;$ramMB=[math]::Ceiling($summary.MemoryTotal/1MB);$minimum=[math]::Max(1024,[math]::Ceiling($ramMB*0.5));$maximum=[math]::Max($minimum,[math]::Min(32768,[math]::Ceiling($ramMB*1.5)))
    [pscustomobject]@{Mode='SystemManaged';RecommendedInitialMB=[int]$minimum;RecommendedMaximumMB=[int]$maximum;PhysicalMemoryMB=[int]$ramMB;Rationale='System-managed paging is preferred; manual values are provided only for constrained environments.'}
}
function Test-WinCarePageFileSettingRecord {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('Record')][object]$Setting)
    $name=[string](Get-WinCareBridgeProperty $Setting 'Name' '')
    $initial=[int64](Get-WinCareBridgeProperty $Setting 'InitialSizeMB' -1)
    $maximum=[int64](Get-WinCareBridgeProperty $Setting 'MaximumSizeMB' -1)
    if($name -notmatch '^[A-Za-z]:\\pagefile\.sys$'){throw 'Page-file path must be a drive-root pagefile.sys path.'}
    if($initial -lt 16 -or $maximum -lt $initial -or $maximum -gt 1048576){throw 'Page-file sizes are outside the supported range.'}
    [pscustomobject]@{Name=([IO.Path]::GetFullPath($name));InitialSizeMB=[int]$initial;MaximumSizeMB=[int]$maximum}
}
function New-WinCarePageFilePlan {
    [CmdletBinding()]
    param([ValidateSet('SystemManaged','Custom','Disabled')][string]$Mode='SystemManaged',[object[]]$Settings=@())
    $before=Get-WinCarePageFileConfiguration
    $validated=[Collections.Generic.List[object]]::new()
    foreach($setting in @($Settings)){$validated.Add((Test-WinCarePageFileSettingRecord -Setting $setting))}
    if($Mode -eq 'Custom' -and $validated.Count -eq 0){throw 'Custom page-file mode requires at least one setting.'}
    if($Mode -ne 'Custom' -and $validated.Count -gt 0){throw 'Only custom page-file mode accepts explicit settings.'}
    if([bool]$before.CrashDump.PageFileRequired){
        if($Mode -eq 'Disabled'){throw 'CrashDump.PageFileRequired prevents disabling all page files.'}
        if($Mode -eq 'Custom'){
            $systemDrive=[IO.Path]::GetPathRoot($env:SystemRoot).TrimEnd('\\')
            $systemSetting=@($validated|Where-Object{[IO.Path]::GetPathRoot($_.Name).TrimEnd('\\') -ieq $systemDrive}|Select-Object -First 1)
            if(-not $systemSetting){throw 'CrashDump.PageFileRequired requires a page file on the Windows system drive.'}
            $requiredMB=if([string]$before.CrashDump.Mode -eq 'Complete'){[math]::Ceiling([double]$before.TotalPhysicalMemoryMB+257)}else{1024}
            if([int64]$systemSetting.MaximumSizeMB -lt $requiredMB){throw "CrashDump.PageFileRequired needs at least $requiredMB MB on the system drive for $($before.CrashDump.Mode) dumps."}
        }
    }
    $risk=if($Mode -eq 'Disabled'){'Critical'}else{'High'}
    $action=New-WinCareBridgeAction -Type 'SetPageFileConfiguration' -Label "Configure page file: $Mode" -Risk $risk -RequiresAdmin $true -Reversible $true -Parameters @{Mode=$Mode;Settings=@($validated);BeforeHash=[string]$before.Hash} -Compensator @{Type='RestorePageFileConfiguration';Parameters=@{Snapshot=$before;ExpectedCurrentHash=''};RequiresAdmin=$true} -TimeoutSeconds 600 -Verification 'CIM page-file state must match the requested mode and settings without violating crash-dump requirements.'
    New-WinCareBridgePlan -Title 'Configure Windows page file' -Description 'Uses Win32_ComputerSystem and Win32_PageFileSetting, preserves crash-dump requirements, and re-queries state for verification.' -Actions @($action)
}
function Invoke-WinCareSetPageFileConfigurationAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Page-file configuration';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;$mode=[string]$p.Mode;$before=Get-WinCarePageFileConfiguration
    if($p.BeforeHash -and [string]$p.BeforeHash -ne [string]$before.Hash){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'PageFileStateChanged' -Message 'Page-file configuration changed after preview.' -ExitCode 74 -Data @{Expected=$p.BeforeHash;Actual=$before.Hash}}
    try{
        $computer=Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
        if($mode -eq 'SystemManaged'){Set-CimInstance -InputObject $computer -Property @{AutomaticManagedPagefile=$true} -ErrorAction Stop|Out-Null}
        else{
            Set-CimInstance -InputObject $computer -Property @{AutomaticManagedPagefile=$false} -ErrorAction Stop|Out-Null
            foreach($existing in @(Get-CimInstance Win32_PageFileSetting -ErrorAction SilentlyContinue)){Remove-CimInstance -InputObject $existing -ErrorAction Stop}
            if($mode -eq 'Custom'){foreach($setting in @($p.Settings)){New-CimInstance -ClassName Win32_PageFileSetting -Property @{Name=[string]$setting.Name;InitialSize=[uint32]$setting.InitialSizeMB;MaximumSize=[uint32]$setting.MaximumSizeMB} -ErrorAction Stop|Out-Null}}
        }
        $after=Get-WinCarePageFileConfiguration;$success=if($mode -eq 'SystemManaged'){$after.AutomaticManagedPagefile}elseif($mode -eq 'Disabled'){(-not $after.AutomaticManagedPagefile) -and @($after.Settings).Count -eq 0}else{(-not $after.AutomaticManagedPagefile) -and @($after.Settings).Count -eq @($p.Settings).Count}
        New-WinCareBridgeResult -Success $success -Code $(if($success){'PageFileConfigured'}else{'PageFileVerificationFailed'}) -Message $(if($success){'Page-file configuration was applied and observed.'}else{'Page-file configuration did not match the requested state.'}) -Data @{Before=$before;After=$after;EvidenceType='CimPageFileState';Compensator=@{Type='RestorePageFileConfiguration';Parameters=@{Snapshot=$before;ExpectedCurrentHash=$after.Hash};RequiresAdmin=$true}} -ExitCode $(if($success){0}else{74}) -RestartRequired $true
    }catch{New-WinCareBridgeResult -Success $false -Code 'PageFileConfigurationFailed' -Message $_.Exception.Message -ExitCode 1 -Data @{Before=$before}}
}
function Invoke-WinCareRestorePageFileConfigurationAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$snapshot=$p.Snapshot;$mode=if([bool]$snapshot.AutomaticManagedPagefile){'SystemManaged'}elseif(@($snapshot.Settings).Count -eq 0){'Disabled'}else{'Custom'}
    $synthetic=[pscustomobject]@{Parameters=@{Mode=$mode;Settings=@($snapshot.Settings);BeforeHash=''}};Invoke-WinCareSetPageFileConfigurationAction -Action $synthetic
}
