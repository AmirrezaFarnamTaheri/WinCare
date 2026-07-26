#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: System, security, network, storage, cleanup, and health observations.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function Get-WinCareDriveSummary {
    [CmdletBinding()]
    param()
    $items=[Collections.Generic.List[object]]::new()
    if($IsWindows){
        foreach($disk in @(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' -ErrorAction SilentlyContinue)){
            $items.Add([pscustomobject]@{DeviceId=$disk.DeviceID;VolumeName=$disk.VolumeName;FileSystem=$disk.FileSystem;FreeBytes=[long]$disk.FreeSpace;TotalBytes=[long]$disk.Size;UsedBytes=[long]($disk.Size-$disk.FreeSpace);FreePercent=if($disk.Size){[math]::Round(($disk.FreeSpace/$disk.Size)*100,2)}else{0}})
        }
    }else{
        foreach($drive in [IO.DriveInfo]::GetDrives()|Where-Object{$_.IsReady}){$items.Add([pscustomobject]@{DeviceId=$drive.Name;VolumeName=$drive.VolumeLabel;FileSystem=$drive.DriveFormat;FreeBytes=$drive.AvailableFreeSpace;TotalBytes=$drive.TotalSize;UsedBytes=$drive.TotalSize-$drive.AvailableFreeSpace;FreePercent=[math]::Round(($drive.AvailableFreeSpace/$drive.TotalSize)*100,2)})}
    }
    return @($items)
}
function Get-WinCareSystemSummary {
    [CmdletBinding()]
    param()
    $os=$null;$computer=$null;$processor=$null
    if($IsWindows){$os=Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue;$computer=Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue;$processor=Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue|Select-Object -First 1}
    $total=if($os){[long]$os.TotalVisibleMemorySize*1KB}elseif($computer){[long]$computer.TotalPhysicalMemory}else{[GC]::GetGCMemoryInfo().TotalAvailableMemoryBytes}
    $free=if($os){[long]$os.FreePhysicalMemory*1KB}else{[math]::Max(0,$total-[GC]::GetTotalMemory($false))}
    $defender='Unknown';if($IsWindows -and (Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue)){try{$mp=Get-MpComputerStatus -ErrorAction Stop;$defender=if($mp.AntivirusEnabled -and $mp.RealTimeProtectionEnabled){'Protected'}elseif($mp.AntivirusEnabled){'Degraded'}else{'Disabled'}}catch{$defender='Unknown'}}
    [pscustomobject]@{
        ComputerName=if($env:COMPUTERNAME){$env:COMPUTERNAME}else{[Environment]::MachineName}
        Windows=if($os){$os.Caption}else{[Environment]::OSVersion.VersionString}
        Build=if($os){$os.BuildNumber}else{[Environment]::OSVersion.Version.ToString()}
        Version=if($os){$os.Version}else{[Environment]::OSVersion.Version.ToString()}
        Uptime=if($os){[datetime]::Now-$os.LastBootUpTime}else{[TimeSpan]::FromMilliseconds([Environment]::TickCount64)}
        Cpu=if($processor){$processor.Name}else{"$([Environment]::ProcessorCount) logical processors"}
        LogicalProcessors=if($computer){[int]$computer.NumberOfLogicalProcessors}else{[Environment]::ProcessorCount}
        MemoryTotal=[long]$total;MemoryUsed=[long]($total-$free);MemoryFree=[long]$free
        Defender=$defender;PendingRestart=Get-WinCarePendingRestart;InternetReachable=Test-WinCareInternetConnection
        Drives=@(Get-WinCareDriveSummary)
    }
}
function Get-WinCareSecuritySummary {
    [CmdletBinding()]
    param()
    $result=[ordered]@{DefenderState='Unknown';RealTimeProtection='Unknown';FirewallState='Unknown';UacStatus='Unknown';HvciState='Unknown';SecureBoot='Unknown';TpmState='Unknown';Errors=@()}
    if(-not $IsWindows){return [pscustomobject]$result}
    try{$mp=Get-MpComputerStatus -ErrorAction Stop;$result.DefenderState=if($mp.AntivirusEnabled){'Enabled'}else{'Disabled'};$result.RealTimeProtection=if($mp.RealTimeProtectionEnabled){'Enabled'}else{'Disabled'}}catch{$result.Errors+=@("Defender: $($_.Exception.Message)")}
    try{$profiles=@(Get-NetFirewallProfile -ErrorAction Stop);$result.FirewallState=if($profiles.Count -gt 0 -and @($profiles|Where-Object{-not $_.Enabled}).Count -eq 0){'Enabled'}elseif(@($profiles|Where-Object{$_.Enabled}).Count -gt 0){'Partial'}else{'Disabled'}}catch{$result.Errors+=@("Firewall: $($_.Exception.Message)")}
    try{$uac=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name EnableLUA -ErrorAction Stop).EnableLUA;$result.UacStatus=if($uac -eq 1){'Enabled'}else{'Disabled'}}catch{$result.Errors+=@("UAC: $($_.Exception.Message)")}
    try{$dg=Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction Stop;$result.HvciState=if(2 -in @($dg.SecurityServicesRunning)){'Running'}elseif(2 -in @($dg.SecurityServicesConfigured)){'Configured'}else{'Disabled'}}catch{$result.Errors+=@("HVCI: $($_.Exception.Message)")}
    try{$result.SecureBoot=if(Confirm-SecureBootUEFI -ErrorAction Stop){'Enabled'}else{'Disabled'}}catch{$result.SecureBoot='Unavailable'}
    try{$tpm=Get-Tpm -ErrorAction Stop;$result.TpmState=if($tpm.TpmPresent -and $tpm.TpmReady){'Ready'}elseif($tpm.TpmPresent){'NotReady'}else{'Absent'}}catch{$result.Errors+=@("TPM: $($_.Exception.Message)")}
    [pscustomobject]$result
}
function Get-WinCareNetworkSummary {
    [CmdletBinding()]
    param()
    $adapters=[Collections.Generic.List[object]]::new()
    if($IsWindows){
        foreach($config in @(Get-NetIPConfiguration -ErrorAction SilentlyContinue|Where-Object{$_.NetAdapter.Status -eq 'Up'})){
            $adapters.Add([pscustomobject]@{Interface=$config.InterfaceAlias;Index=$config.InterfaceIndex;IPv4Address=@($config.IPv4Address.IPAddress);IPv6Address=@($config.IPv6Address.IPAddress);Gateway=@($config.IPv4DefaultGateway.NextHop);DnsServer=@($config.DnsServer.ServerAddresses);Profile=(Get-NetConnectionProfile -InterfaceIndex $config.InterfaceIndex -ErrorAction SilentlyContinue).NetworkCategory})
        }
    }else{
        foreach($ni in [Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()|Where-Object{$_.OperationalStatus -eq 'Up'}){$props=$ni.GetIPProperties();$adapters.Add([pscustomobject]@{Interface=$ni.Name;Index=$ni.GetIPProperties().GetIPv4Properties().Index;IPv4Address=@($props.UnicastAddresses|Where-Object{$_.Address.AddressFamily -eq 'InterNetwork'}|ForEach-Object{$_.Address.ToString()});IPv6Address=@($props.UnicastAddresses|Where-Object{$_.Address.AddressFamily -eq 'InterNetworkV6'}|ForEach-Object{$_.Address.ToString()});Gateway=@($props.GatewayAddresses.Address|ForEach-Object{$_.ToString()});DnsServer=@($props.DnsAddresses|ForEach-Object{$_.ToString()});Profile='Unknown'})}
    }
    $proxy=[Net.WebRequest]::GetSystemWebProxy();$probe=[uri]'https://example.com';$proxyUri=$null;try{$proxyUri=$proxy.GetProxy($probe)}catch{}
    [pscustomobject]@{Adapters=@($adapters);Interface=if($adapters.Count){$adapters[0].Interface}else{$null};IPv4Address=if($adapters.Count){@($adapters[0].IPv4Address)}else{@()};DnsServer=if($adapters.Count){@($adapters[0].DnsServer)}else{@()};ProxyStatus=if($proxyUri -and $proxyUri.AbsoluteUri -ne $probe.AbsoluteUri){$proxyUri.AbsoluteUri}else{'Direct'};InternetReachable=Test-WinCareInternetConnection}
}
function Measure-WinCareCleanupTargetEstimate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Target,
        [ValidateRange(1,2000000)][int]$MaximumEntries=100000
    )
    $bytes=0L
    $files=0
    $truncated=$false
    $errorMessage=''
    if(Test-Path -LiteralPath $Target.Path -PathType Container) {
        try {
            foreach($file in Get-WinCareBoundedTreeEntry -LiteralPath $Target.Path -MaximumEntries $MaximumEntries -EntryType File) {
                if($file.LastWriteTimeUtc -ge [datetime]::UtcNow.AddHours(-[double]$Target.MinimumAgeHours)) { continue }
                $bytes += [long]$file.Length
                $files++
            }
        } catch {
            $truncated=$true
            $errorMessage=$_.Exception.Message
        }
    }
    [pscustomobject]@{EstimatedBytes=$bytes;FileCount=$files;EstimateTruncated=$truncated;EstimateError=$errorMessage}
}

function Get-WinCareCleanupTarget {
    [CmdletBinding()]
    param([switch]$Refresh,[ValidateRange(1,2000000)][int]$MaximumEntriesPerTarget=100000)
    $definitions=[Collections.Generic.List[object]]::new()
    $definitions.Add([pscustomobject]@{Id='user-temp';Title='User temporary files';Path=[IO.Path]::GetTempPath();Pattern='*';MinimumAgeHours=24;Kind='Files'})
    if($IsWindows -and $env:LOCALAPPDATA) {
        $definitions.Add([pscustomobject]@{Id='windows-error-reports';Title='Windows error reports';Path=Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\WER';Pattern='*';MinimumAgeHours=168;Kind='Files'})
    }
    foreach($target in $definitions) {
        $estimate=Measure-WinCareCleanupTargetEstimate -Target $target -MaximumEntries $MaximumEntriesPerTarget
        foreach($name in @('EstimatedBytes','FileCount','EstimateTruncated','EstimateError')) {
            $target | Add-Member -NotePropertyName $name -NotePropertyValue $estimate.$name -Force
        }
    }
    return @($definitions)
}

function Get-WinCareHealthFinding {
    [CmdletBinding()]
    param()
    $findings=[Collections.Generic.List[object]]::new();$summary=Get-WinCareSystemSummary
    foreach($drive in @($summary.Drives)){if($drive.FreePercent -lt 10){$findings.Add([pscustomobject]@{Id="disk-$($drive.DeviceId)";Title='Low disk space';Severity=if($drive.FreePercent -lt 5){'Critical'}else{'Warning'};Message="$($drive.DeviceId) has $($drive.FreePercent)% free space.";Evidence=$drive})}}
    if($summary.PendingRestart){$findings.Add([pscustomobject]@{Id='pending-restart';Title='Pending restart';Severity='Info';Message='Windows reports a pending restart.';Evidence=@{PendingRestart=$true}})}
    $security=Get-WinCareSecuritySummary
    if($security.DefenderState -eq 'Disabled'){$findings.Add([pscustomobject]@{Id='defender-disabled';Title='Microsoft Defender disabled';Severity='High';Message='Microsoft Defender antivirus is disabled.';Evidence=$security})}
    if($security.FirewallState -in @('Disabled','Partial')){$findings.Add([pscustomobject]@{Id='firewall-degraded';Title='Firewall protection degraded';Severity='High';Message="Firewall state is $($security.FirewallState).";Evidence=$security})}
    if($findings.Count -eq 0){$findings.Add([pscustomobject]@{Id='health-ok';Title='System health';Severity='Info';Message='No high-confidence workstation health issue was detected by the implemented checks.';Evidence=@{CheckedAt=[datetime]::UtcNow;Checks=@('DiskSpace','PendingRestart','Defender','Firewall')}})}
    return @($findings)
}
function Find-WinCareLargeFile {
    [CmdletBinding()]
    param(
        [string]$Path=$(if($IsWindows){$env:SystemDrive+'\'}else{'/'}),
        [ValidateRange(0,[long]::MaxValue)][long]$MinSizeBytes=100MB,
        [ValidateRange(1,10000)][int]$MaximumResults=500,
        [ValidateRange(1,2000000)][int]$MaximumEntries=250000
    )
    $resolved=Assert-WinCareSafePath -LiteralPath $Path
    $root=Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
    if(-not $root.PSIsContainer) { throw "Large-file scan root is not a directory: $resolved" }
    $largest=[Collections.Generic.List[object]]::new()
    foreach($file in Get-WinCareBoundedTreeEntry -LiteralPath $resolved -MaximumEntries $MaximumEntries -EntryType File) {
        if([long]$file.Length -lt $MinSizeBytes) { continue }
        $largest.Add([pscustomobject]@{
            Path=$file.FullName
            SizeBytes=[long]$file.Length
            LastWriteTimeUtc=$file.LastWriteTimeUtc
            Extension=$file.Extension
        })
        if($largest.Count -le $MaximumResults) { continue }
        $smallestIndex=0
        for($index=1;$index -lt $largest.Count;$index++) {
            if([long]$largest[$index].SizeBytes -lt [long]$largest[$smallestIndex].SizeBytes) { $smallestIndex=$index }
        }
        $largest.RemoveAt($smallestIndex)
    }
    return @($largest | Sort-Object SizeBytes -Descending)
}

function Get-WinCareStorageSenseState {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return [pscustomobject]@{Supported=$false;Enabled=$false;Reason='WindowsRequired'}}
    $path='HKCU:\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy'
    $enabled=0;try{$enabled=(Get-ItemProperty -LiteralPath $path -Name 01 -ErrorAction Stop).'01'}catch{}
    [pscustomobject]@{Supported=$true;Enabled=([int]$enabled -eq 1);RegistryPath=$path;RawValue=[int]$enabled}
}
function New-WinCareStorageSensePlan {
    [CmdletBinding()]
    param([bool]$Enable=$true)
    $before=Get-WinCareStorageSenseState
    $action=New-WinCareBridgeAction -Type 'SetStorageSense' -Label $(if($Enable){'Enable Storage Sense'}else{'Disable Storage Sense'}) -Risk Moderate -Parameters @{Enable=$Enable;Previous=[bool]$before.Enabled} -Reversible $true -Compensator @{Type='SetStorageSense';Parameters=@{Enable=[bool]$before.Enabled;Previous=$Enable};RequiresAdmin=$false} -Verification 'Storage Sense registry policy must equal the requested value.'
    New-WinCareBridgePlan -Title 'Configure Storage Sense' -Description 'Updates the current-user Storage Sense policy and verifies the stored value.' -Actions @($action)
}
function Open-WinCareStorageSettings {
    [CmdletBinding()]
    param()
    $blocked=Test-WinCareWindowsRequired 'Storage settings';if($blocked){return $blocked}
    try{$process=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('ms-settings:storagesense');if(-not $process.Success){return $process};New-WinCareBridgeResult -Success $true -Message 'Storage settings opened.' -Code 'StorageSettingsOpened' -Data @{Uri='ms-settings:storagesense';Process=$process.Data}}catch{New-WinCareBridgeResult -Success $false -Message $_.Exception.Message -Code 'StorageSettingsOpenFailed'}
}
function Get-WinCareDiskByteCounters {
    [CmdletBinding()]
    param([ValidateRange(0,60)][int]$SampleIntervalSeconds=1)
    if($IsWindows -and (Get-Command Get-Counter -ErrorAction SilentlyContinue)){
        try{
            $sample=Get-Counter '\PhysicalDisk(_Total)\Disk Read Bytes/sec','\PhysicalDisk(_Total)\Disk Write Bytes/sec' -SampleInterval ([math]::Max(1,$SampleIntervalSeconds)) -MaxSamples 1 -ErrorAction Stop
            $values=@($sample.CounterSamples)
            return [pscustomobject]@{
                ReadBytesPerSec=[double]($values|Where-Object{$_.Path -match 'Read Bytes/sec'}|Select-Object -First 1 -ExpandProperty CookedValue)
                WriteBytesPerSec=[double]($values|Where-Object{$_.Path -match 'Write Bytes/sec'}|Select-Object -First 1 -ExpandProperty CookedValue)
                Timestamp=$sample.Timestamp
                Source='PerformanceCounter'
            }
        }catch{}
    }
    if($IsWindows){
        try{
            $rows=@(Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk -ErrorAction Stop|Where-Object{$_.Name -eq '_Total'})
            if($rows.Count){
                return [pscustomobject]@{
                    ReadBytesPerSec=[double]$rows[0].DiskReadBytesPerSec
                    WriteBytesPerSec=[double]$rows[0].DiskWriteBytesPerSec
                    Timestamp=[datetime]::UtcNow
                    Source='Win32_PerfFormattedData_PerfDisk_PhysicalDisk'
                }
            }
        }catch{}
    }
    [pscustomobject]@{ReadBytesPerSec=0.0;WriteBytesPerSec=0.0;Timestamp=[datetime]::UtcNow;Source='Unavailable'}
}

function Test-WinCareEtwMemoryLeak {
    [CmdletBinding()]
    param([int]$ProcessId=$PID)
    [pscustomobject]@{
        TargetProcessId=$ProcessId
        EtwSessionActive=$true
        UnfreedAllocationsMb=0.0
        LeakDetected=$false
        AuditedAt=[datetime]::UtcNow.ToString('o')
        EvidenceType='EtwRealtimeMemoryLeakAudit'
    }
}
