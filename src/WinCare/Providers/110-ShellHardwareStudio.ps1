function Initialize-WinCareShellHardwareInterop {
    if('WinCare.Native.ShellHardware' -as [type]){return $true}
    $path=Join-Path $script:WinCareModuleRoot 'Native\WinCare.ShellHardware.cs'
    try{Add-Type -Path $path -ErrorAction Stop;return $true}
    catch{Write-WinCareLog -Level Warning -Message 'Shell and hardware interop could not be loaded.' -Data @{error=$_.Exception.Message};return $false}
}

function Get-WinCareShellHardwareRoot {
    [CmdletBinding()]param()
    Ensure-WinCareState
    Get-WinCareBridgeStateRoot 'ShellHardwareStudio'
}

function ConvertTo-WinCareHardwareIdentifier {
    param([AllowNull()][object]$Value,[switch]$IncludeSensitive)
    $text=[string]$Value
    if(-not $text){return ''}
    if($IncludeSensitive){return $text}
    'sha256:'+(Get-WinCareSha256Text -Text $text)
}

function Get-WinCareHardwareInventory {
    [CmdletBinding()]param([switch]$IncludeSensitiveIdentifiers,[ValidateRange(1,512)][int]$MaximumItemsPerClass=128)
    if(-not $IsWindows){return [pscustomobject]@{Available=$false;Reason='WindowsRequired';SourceRecords=@('SRC:1461504853')}}
    $errors=[Collections.Generic.List[string]]::new()
    function Read-Cim([string]$Class,[string[]]$Properties){
        try{@(Get-CimInstance -ClassName $Class -ErrorAction Stop|Select-Object -First $MaximumItemsPerClass -Property $Properties)}catch{$errors.Add("${Class}: $($_.Exception.Message)");@()}
    }
    $computerRaw=Read-Cim 'Win32_ComputerSystem' @('Manufacturer','Model','TotalPhysicalMemory','SystemType','Domain','PartOfDomain')|Select-Object -First 1
    $computer=if($computerRaw){[pscustomobject]@{Manufacturer=$computerRaw.Manufacturer;Model=$computerRaw.Model;TotalPhysicalMemory=[long]$computerRaw.TotalPhysicalMemory;SystemType=$computerRaw.SystemType;PartOfDomain=[bool]$computerRaw.PartOfDomain;Domain=ConvertTo-WinCareHardwareIdentifier $computerRaw.Domain -IncludeSensitive:$IncludeSensitiveIdentifiers}}else{$null}
    $os=Read-Cim 'Win32_OperatingSystem' @('Caption','Version','BuildNumber','OSArchitecture','InstallDate','LastBootUpTime','TotalVisibleMemorySize','FreePhysicalMemory')|Select-Object -First 1
    $bios=@(Read-Cim 'Win32_BIOS' @('Manufacturer','SMBIOSBIOSVersion','ReleaseDate','SerialNumber')|ForEach-Object{[pscustomobject]@{Manufacturer=$_.Manufacturer;Version=$_.SMBIOSBIOSVersion;ReleaseDate=$_.ReleaseDate;SerialNumber=ConvertTo-WinCareHardwareIdentifier $_.SerialNumber -IncludeSensitive:$IncludeSensitiveIdentifiers}})
    $baseboard=@(Read-Cim 'Win32_BaseBoard' @('Manufacturer','Product','Version','SerialNumber')|ForEach-Object{[pscustomobject]@{Manufacturer=$_.Manufacturer;Product=$_.Product;Version=$_.Version;SerialNumber=ConvertTo-WinCareHardwareIdentifier $_.SerialNumber -IncludeSensitive:$IncludeSensitiveIdentifiers}})
    $processors=@(Read-Cim 'Win32_Processor' @('Name','Manufacturer','NumberOfCores','NumberOfLogicalProcessors','MaxClockSpeed','Architecture','ProcessorId')|ForEach-Object{[pscustomobject]@{Name=$_.Name;Manufacturer=$_.Manufacturer;Cores=$_.NumberOfCores;LogicalProcessors=$_.NumberOfLogicalProcessors;MaxClockMHz=$_.MaxClockSpeed;Architecture=$_.Architecture;ProcessorId=ConvertTo-WinCareHardwareIdentifier $_.ProcessorId -IncludeSensitive:$IncludeSensitiveIdentifiers}})
    $memory=@(Read-Cim 'Win32_PhysicalMemory' @('Manufacturer','PartNumber','Capacity','Speed','ConfiguredClockSpeed','DeviceLocator','SerialNumber')|ForEach-Object{[pscustomobject]@{Manufacturer=$_.Manufacturer;PartNumber=([string]$_.PartNumber).Trim();Capacity=[long]$_.Capacity;SpeedMHz=$_.Speed;ConfiguredClockMHz=$_.ConfiguredClockSpeed;Locator=$_.DeviceLocator;SerialNumber=ConvertTo-WinCareHardwareIdentifier $_.SerialNumber -IncludeSensitive:$IncludeSensitiveIdentifiers}})
    $graphics=@(Read-Cim 'Win32_VideoController' @('Name','AdapterRAM','DriverVersion','VideoModeDescription','CurrentHorizontalResolution','CurrentVerticalResolution','CurrentRefreshRate','PNPDeviceID')|ForEach-Object{[pscustomobject]@{Name=$_.Name;AdapterRam=$_.AdapterRAM;DriverVersion=$_.DriverVersion;VideoMode=$_.VideoModeDescription;Width=$_.CurrentHorizontalResolution;Height=$_.CurrentVerticalResolution;RefreshHz=$_.CurrentRefreshRate;DeviceId=ConvertTo-WinCareHardwareIdentifier $_.PNPDeviceID -IncludeSensitive:$IncludeSensitiveIdentifiers}})
    $disks=@(Read-Cim 'Win32_DiskDrive' @('Model','InterfaceType','MediaType','Size','SerialNumber','FirmwareRevision','PNPDeviceID')|ForEach-Object{[pscustomobject]@{Model=$_.Model;Interface=$_.InterfaceType;MediaType=$_.MediaType;Size=[long]$_.Size;Firmware=$_.FirmwareRevision;SerialNumber=ConvertTo-WinCareHardwareIdentifier $_.SerialNumber -IncludeSensitive:$IncludeSensitiveIdentifiers;DeviceId=ConvertTo-WinCareHardwareIdentifier $_.PNPDeviceID -IncludeSensitive:$IncludeSensitiveIdentifiers}})
    $network=@(Read-Cim 'Win32_NetworkAdapter' @('Name','Manufacturer','MACAddress','NetEnabled','Speed','PhysicalAdapter','PNPDeviceID')|Where-Object{$_.PhysicalAdapter}|ForEach-Object{[pscustomobject]@{Name=$_.Name;Manufacturer=$_.Manufacturer;Enabled=$_.NetEnabled;Speed=[long]$_.Speed;MacAddress=ConvertTo-WinCareHardwareIdentifier $_.MACAddress -IncludeSensitive:$IncludeSensitiveIdentifiers;DeviceId=ConvertTo-WinCareHardwareIdentifier $_.PNPDeviceID -IncludeSensitive:$IncludeSensitiveIdentifiers}})
    $battery=@(Read-Cim 'Win32_Battery' @('Name','Status','EstimatedChargeRemaining','BatteryStatus','DesignVoltage','Chemistry'))
    $tpm=@();try{$tpm=@(Get-Tpm -ErrorAction Stop|Select-Object TpmPresent,TpmReady,TpmEnabled,TpmActivated,ManufacturerIdTxt,ManufacturerVersion)}catch{$errors.Add("TPM: $($_.Exception.Message)")}
    [pscustomobject]@{
        SchemaVersion=1;CapturedAt=[datetime]::UtcNow.ToString('o');Available=$true;SensitiveIdentifiersIncluded=[bool]$IncludeSensitiveIdentifiers
        Computer=$computer;OperatingSystem=$os;Bios=$bios;Baseboard=$baseboard;Processors=$processors;MemoryModules=$memory;Graphics=$graphics;Disks=$disks;NetworkAdapters=$network;Battery=$battery;Tpm=$tpm
        Errors=@($errors);Bounds=[pscustomobject]@{MaximumItemsPerClass=$MaximumItemsPerClass};SourceRecords=@('SRC:1461504853')
    }
}

function New-WinCareHardwareReportPlan {
    [CmdletBinding()]param(
        [ValidatePattern('^[A-Za-z0-9._-]{3,100}\.(json|md)$')][string]$Name='hardware-report.json',
        [switch]$IncludeSensitiveIdentifiers
    )
    $inventory=Get-WinCareHardwareInventory -IncludeSensitiveIdentifiers:$IncludeSensitiveIdentifiers
    $root=Join-Path (Get-WinCareShellHardwareRoot) 'Reports';$null=New-Item -ItemType Directory -Path $root -Force
    $path=Join-Path $root $Name
    if($Name.EndsWith('.md',[StringComparison]::OrdinalIgnoreCase)){
        $lines=[Collections.Generic.List[string]]::new();$lines.Add('# WinCare hardware report');$lines.Add('');$lines.Add("Captured: $($inventory.CapturedAt)");$lines.Add("Sensitive identifiers included: $($inventory.SensitiveIdentifiersIncluded)");$lines.Add('')
        foreach($section in @('Computer','OperatingSystem','Bios','Baseboard','Processors','MemoryModules','Graphics','Disks','NetworkAdapters','Battery','Tpm')){$lines.Add("## $section");$lines.Add('```json');$lines.Add((ConvertTo-WinCareCanonicalJson -InputObject $inventory.$section -Depth 40));$lines.Add('```');$lines.Add('')}
        $content=[Text.Encoding]::UTF8.GetBytes(($lines -join "`n")+"`n")
    }else{$content=[Text.Encoding]::UTF8.GetBytes((ConvertTo-WinCareCanonicalJson -InputObject $inventory -Depth 60)+"`n")}
    if($content.Length -gt 8388608){throw 'Hardware report exceeds the 8 MiB output limit.'}
    $action=New-WinCareManagedFileWriteAction -Path $path -Content $content -AllowedRoots @($root) -Label 'Write bounded hardware report' -SourceRecords @('SRC:1461504853')
    New-WinCarePlan -Title 'Export hardware inventory' -Description 'Writes a bounded local report. Hardware identifiers are hashed unless explicitly included.' -Actions @($action) -SourceRecords @($action.SourceRecords)
}

function Get-WinCareMonitorControlState {
    [CmdletBinding()]param()
    if(-not $IsWindows -or -not(Initialize-WinCareShellHardwareInterop)){return @()}
    @([WinCare.Native.ShellHardware]::EnumerateMonitorVcpFeatures()|ForEach-Object{
        [pscustomobject]@{DeviceName=[string]$_.DeviceName;PhysicalIndex=[int]$_.PhysicalIndex;Description=[string]$_.Description;Feature=if([byte]$_.FeatureCode -eq 0x10){'Brightness'}elseif([byte]$_.FeatureCode -eq 0x12){'Contrast'}else{('0x{0:X2}' -f [byte]$_.FeatureCode)};FeatureCode=[int]$_.FeatureCode;Current=[uint32]$_.CurrentValue;Maximum=[uint32]$_.MaximumValue;Supported=[bool]$_.Supported;Win32Error=[int]$_.Win32Error;SourceRecords=@('SRC:1461504853')}
    })
}

function New-WinCareMonitorControlPlan {
    [CmdletBinding()]param(
        [Parameter(Mandatory)][string]$DeviceName,
        [Parameter(Mandatory)][ValidateRange(0,63)][int]$PhysicalIndex,
        [Parameter(Mandatory)][ValidateSet('Brightness','Contrast')][string]$Feature,
        [Parameter(Mandatory)][ValidateRange(0,65535)][int]$Value
    )
    $code=if($Feature -eq 'Brightness'){0x10}else{0x12}
    $before=Get-WinCareMonitorControlState|Where-Object{$_.DeviceName -eq $DeviceName -and $_.PhysicalIndex -eq $PhysicalIndex -and $_.FeatureCode -eq $code}|Select-Object -First 1
    if(-not $before -or -not $before.Supported){throw 'The requested monitor VCP feature is unavailable.'}
    if($Value -gt [int]$before.Maximum){throw "Value exceeds the monitor maximum of $($before.Maximum)."}
    $parameters=@{DeviceName=[string]$before.DeviceName;PhysicalIndex=[int]$before.PhysicalIndex;Description=[string]$before.Description;FeatureCode=[int]$before.FeatureCode;Value=[int]$Value;ExpectedCurrent=[int]$before.Current;ExpectedMaximum=[int]$before.Maximum}
    $recovery=@{Type='SetMonitorVcpFeature';Parameters=@{DeviceName=[string]$before.DeviceName;PhysicalIndex=[int]$before.PhysicalIndex;Description=[string]$before.Description;FeatureCode=[int]$before.FeatureCode;Value=[int]$before.Current;ExpectedCurrent=[int]$Value;ExpectedMaximum=[int]$before.Maximum}}
    $action=New-WinCareAction -Type SetMonitorVcpFeature -Label "Set $Feature on $($before.Description) to $Value" -Risk Low -Parameters $parameters -RequiresAdmin $false -Reversible $true -Compensator $recovery -TimeoutSeconds 60 -Tags @('Display','DDC-CI','MonitorControl') -SourceRecords @('SRC:1461504853') -RecoveryDescription 'Restore the exact prior monitor VCP value if the same monitor and maximum remain present.'
    New-WinCarePlan -Title "Set monitor $Feature" -Description 'Uses DDC/CI with logical-device, physical-index, current-value, and maximum-value compare-and-swap checks.' -Actions @($action) -SourceRecords @($action.SourceRecords)
}

function Invoke-WinCareSetMonitorVcpFeatureAction {
    param([object]$Action)
    if(-not $IsWindows -or -not(Initialize-WinCareShellHardwareInterop)){return New-WinCareResult -Success $false -Message 'Monitor DDC/CI interop is unavailable.' -ExitCode 127 -Code 'MonitorInteropUnavailable'}
    $p=$Action.Parameters
    try{
        $after=[WinCare.Native.ShellHardware]::SetMonitorVcpFeature([string]$p.DeviceName,[int]$p.PhysicalIndex,[string]$p.Description,[byte]$p.FeatureCode,[uint32]$p.Value,[uint32]$p.ExpectedCurrent,[uint32]$p.ExpectedMaximum)
        New-WinCareResult -Success $true -Message 'Monitor VCP feature was updated and verified.' -Code 'MonitorVcpUpdated' -Data @{DeviceName=$after.DeviceName;PhysicalIndex=$after.PhysicalIndex;Description=$after.Description;FeatureCode=[int]$after.FeatureCode;Current=[int]$after.CurrentValue;Maximum=[int]$after.MaximumValue}
    }catch{
        $code=if($_.Exception.Message -match 'changed after preview'){'MonitorStateChanged'}else{'MonitorVcpFailed'}
        New-WinCareResult -Success $false -Status $(if($code -eq 'MonitorStateChanged'){'Blocked'}else{'Failed'}) -Code $code -Message $_.Exception.Message -ExitCode $(if($code -eq 'MonitorStateChanged'){74}else{1})
    }
}

function Get-WinCareWindowSearch {
    [CmdletBinding()]param([Parameter(Mandatory)][ValidateLength(1,240)][string]$Query,[ValidateRange(1,200)][int]$Limit=50)
    $needle=$Query.Trim().ToLowerInvariant();$tokens=@($needle -split '\s+'|Where-Object{$_})
    $rows=[Collections.Generic.List[object]]::new()
    foreach($window in @(Get-WinCareWindowInventory -IncludeUntitled)){
        $title=([string]$window.Title).ToLowerInvariant();$process=([string]$window.ProcessName).ToLowerInvariant();$hay="$process $title";$score=0
        if($process -eq $needle){$score+=120};if($title -eq $needle){$score+=110};if($process.StartsWith($needle)){$score+=80};if($title.StartsWith($needle)){$score+=70};if($process.Contains($needle)){$score+=55};if($title.Contains($needle)){$score+=45}
        foreach($token in $tokens){if($process.Contains($token)){$score+=20};if($title.Contains($token)){$score+=12}}
        if($score -gt 0){$rows.Add([pscustomobject]@{Score=$score;Handle=$window.Handle;ProcessId=$window.ProcessId;ProcessName=$window.ProcessName;Title=$window.Title;Path=$window.Path;Signature=$window.Signature;SourceRecords=@('SRC:1461504853')})}
    }
    @($rows|Sort-Object @{Expression='Score';Descending=$true},ProcessName,Title|Select-Object -First $Limit)
}

function Get-WinCareExplorerSession {
    [CmdletBinding()]param()
    if(-not $IsWindows){return @()}
    $rows=[Collections.Generic.List[object]]::new();$shell=$null
    try{
        $shell=New-Object -ComObject Shell.Application
        foreach($window in @($shell.Windows())){
            try{
                $fullName=[string]$window.FullName;if([IO.Path]::GetFileName($fullName) -ne 'explorer.exe'){continue}
                $url=[string]$window.LocationURL;if(-not $url -or -not $url.StartsWith('file:',[StringComparison]::OrdinalIgnoreCase)){continue}
                $uri=[Uri]$url;$path=Resolve-WinCareCanonicalPath -LiteralPath $uri.LocalPath
                if(-not(Test-Path -LiteralPath $path -PathType Container)){continue}
                $rows.Add([pscustomobject]@{Path=$path;PathHash=Get-WinCareSha256Text -Text $path;LocationName=[string]$window.LocationName;Hwnd=[long]$window.HWND;SourceRecords=@('SRC:1461504853')})
            }catch{Write-Verbose 'An Explorer window could not be inspected.'}
        }
    }finally{if($shell -and [Runtime.InteropServices.Marshal]::IsComObject($shell)){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}}
    @($rows|Sort-Object Path -Unique)
}

function Get-WinCareExplorerSessionRoot { Get-WinCareBridgeStateRoot 'ShellHardwareStudio/ExplorerSessions' }

function New-WinCareExplorerSessionSavePlan {
    [CmdletBinding()]param([ValidatePattern('^[a-z0-9][a-z0-9-]{2,60}$')][string]$Id=('session-'+[datetime]::UtcNow.ToString('yyyyMMdd-HHmmss')))
    $locations=@(Get-WinCareExplorerSession|Select-Object Path,PathHash,LocationName)
    if($locations.Count -gt 128){throw 'Explorer session exceeds 128 locations.'}
    $record=[ordered]@{SchemaVersion=1;Id=$Id;CapturedAt=[datetime]::UtcNow.ToString('o');Locations=$locations;SourceRecords=@('SRC:1461504853')}
    $root=Get-WinCareExplorerSessionRoot;$path=Join-Path $root "$Id.json";$content=[Text.Encoding]::UTF8.GetBytes((ConvertTo-WinCareCanonicalJson $record -Depth 40)+"`n")
    $action=New-WinCareManagedFileWriteAction -Path $path -Content $content -AllowedRoots @($root) -Label "Save Explorer session $Id" -SourceRecords @($record.SourceRecords)
    New-WinCarePlan -Title 'Save Explorer session' -Description 'Stores a bounded list of canonical local folder locations under protected WinCare state.' -Actions @($action) -SourceRecords @($record.SourceRecords)
}

function Get-WinCareExplorerSessionRecord {
    [CmdletBinding()]param([string]$Id='')
    $root=Get-WinCareExplorerSessionRoot;$paths=if($Id){@(Join-Path $root "$Id.json")}else{@(Get-ChildItem -LiteralPath $root -Filter '*.json' -File -ErrorAction SilentlyContinue|Select-Object -ExpandProperty FullName)}
    $rows=[Collections.Generic.List[object]]::new()
    foreach($path in $paths){
        if(-not(Test-Path -LiteralPath $path -PathType Leaf)){continue}
        try{
            $record=Read-WinCareBoundedJson -LiteralPath $path -MaximumBytes 4194304 -AsHashtable -Depth 30
            $null=Test-WinCareStrictObjectKeys -InputObject $record -AllowedKeys @('SchemaVersion','Id','CapturedAt','Locations','SourceRecords') -Context 'Explorer session'
            if([int]$record.SchemaVersion -ne 1 -or [string]$record.Id -notmatch '^[a-z0-9][a-z0-9-]{2,60}$' -or @($record.Locations).Count -gt 128){throw 'Explorer session record is invalid.'}
            foreach($location in @($record.Locations)){$null=Test-WinCareStrictObjectKeys -InputObject $location -AllowedKeys @('Path','PathHash','LocationName') -Context 'Explorer session location';if([string]$location.PathHash -notmatch '^[a-f0-9]{64}$'){throw 'Explorer session path hash is invalid.'}}
            $rows.Add([pscustomobject]@{Id=$record.Id;CapturedAt=$record.CapturedAt;Locations=@($record.Locations);LocationCount=@($record.Locations).Count;Path=$path;Sha256=Get-WinCareSha256 -LiteralPath $path;SourceRecords=@($record.SourceRecords)})
        }catch{Write-WinCareLog -Level Warning -Message 'Ignoring an invalid Explorer session record.' -Data @{path=$path;error=$_.Exception.Message}}
    }
    @($rows|Sort-Object CapturedAt -Descending)
}

function New-WinCareExplorerSessionRestorePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $record=Get-WinCareExplorerSessionRecord -Id $Id|Select-Object -First 1;if(-not $record){throw 'Explorer session does not exist or is invalid.'}
    $actions=[Collections.Generic.List[object]]::new()
    foreach($location in @($record.Locations)){
        $path=Resolve-WinCareCanonicalPath -LiteralPath ([string]$location.Path)
        if(-not(Test-Path -LiteralPath $path -PathType Container)){continue}
        if((Get-WinCareSha256Text -Text $path) -ne [string]$location.PathHash){throw 'Explorer location identity changed after capture.'}
        $actions.Add((New-WinCareAction -Type OpenExplorerLocation -Label "Open Explorer: $path" -Risk Low -Parameters @{Path=$path;ExpectedPathHash=[string]$location.PathHash} -RequiresAdmin $false -Reversible $false -TimeoutSeconds 60 -Tags @('Explorer','Session','Restore') -SourceRecords @('SRC:1461504853') -RecoveryDescription 'Opening an Explorer window is transient and is not automatically reversed.'))
    }
    New-WinCarePlan -Title "Restore Explorer session $Id" -Description 'Opens only canonical local directories that still match their captured path identities.' -Actions @($actions) -RollbackOnFailure $false -SourceRecords @('SRC:1461504853')
}

function Invoke-WinCareOpenExplorerLocationAction {
    param([object]$Action)
    $p=$Action.Parameters
    try{
        $path=Resolve-WinCareCanonicalPath -LiteralPath ([string]$p.Path)
        if(-not(Test-Path -LiteralPath $path -PathType Container)){throw 'Explorer target is not a local directory.'}
        if((Get-WinCareSha256Text -Text $path) -ne [string]$p.ExpectedPathHash){return New-WinCareResult -Success $false -Status Blocked -Code 'ExplorerPathChanged' -Message 'Explorer target identity changed after preview.' -ExitCode 74}
        $result=Invoke-WinCareProcess -FilePath explorer.exe -ArgumentList @($path) -TimeoutSeconds 30 -SuccessExitCodes @(0,1)
        if(-not $result.Success){return $result}
        New-WinCareResult -Success $true -Code 'ExplorerLocationOpened' -Message 'Explorer location was opened.' -Data @{Path=$path}
    }catch{New-WinCareResult -Success $false -Message $_.Exception.Message -Code 'ExplorerOpenFailed' -ExitCode 1}
}

function Get-WinCareUiAutomationSnapshot {
    [CmdletBinding()]param([ValidateRange(0,2147483647)][int]$ProcessId=0,[ValidateRange(1,8)][int]$MaximumDepth=5,[ValidateRange(1,2000)][int]$MaximumNodes=500,[switch]$IncludeText)
    if(-not $IsWindows){return [pscustomobject]@{Available=$false;Nodes=@();SourceRecords=@('SRC:1461504853')}}
    Add-Type -AssemblyName UIAutomationClient -ErrorAction Stop;Add-Type -AssemblyName UIAutomationTypes -ErrorAction Stop
    $root=if($ProcessId -gt 0){$process=Get-Process -Id $ProcessId -ErrorAction Stop;if($process.MainWindowHandle -eq [IntPtr]::Zero){throw 'Process has no main window.'};[Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)}else{[Windows.Automation.AutomationElement]::RootElement}
    $queue=[Collections.Generic.Queue[object]]::new();$queue.Enqueue([pscustomobject]@{Element=$root;Depth=0;Parent=-1});$nodes=[Collections.Generic.List[object]]::new();$walker=[Windows.Automation.TreeWalker]::RawViewWalker
    while($queue.Count -gt 0 -and $nodes.Count -lt $MaximumNodes){
        $entry=$queue.Dequeue();$element=$entry.Element;$index=$nodes.Count
        try{
            $current=$element.Current;$name=[string]$current.Name;$safeName=if($IncludeText){$name}elseif($name){"[redacted length=$($name.Length) sha256=$((Get-WinCareSha256Text $name).Substring(0,12))]"}else{''}
            $rect=$current.BoundingRectangle
            $nodes.Add([pscustomobject]@{Index=$index;ParentIndex=[int]$entry.Parent;Depth=[int]$entry.Depth;Name=$safeName;AutomationId=[string]$current.AutomationId;ClassName=[string]$current.ClassName;ControlType=[string]$current.ControlType.ProgrammaticName;ProcessId=[int]$current.ProcessId;Enabled=[bool]$current.IsEnabled;Offscreen=[bool]$current.IsOffscreen;Left=[double]$rect.Left;Top=[double]$rect.Top;Width=[double]$rect.Width;Height=[double]$rect.Height})
            if([int]$entry.Depth -lt $MaximumDepth){$child=$walker.GetFirstChild($element);while($child -and ($queue.Count+$nodes.Count) -lt ($MaximumNodes*2)){$queue.Enqueue([pscustomobject]@{Element=$child;Depth=[int]$entry.Depth+1;Parent=$index});$child=$walker.GetNextSibling($child)}}
        }catch{Write-Verbose 'A UI Automation node could not be read.'}
    }
    [pscustomobject]@{Available=$true;ProcessId=$ProcessId;MaximumDepth=$MaximumDepth;MaximumNodes=$MaximumNodes;TextIncluded=[bool]$IncludeText;Truncated=($queue.Count -gt 0);NodeCount=$nodes.Count;Nodes=@($nodes);SourceRecords=@('SRC:1461504853','PRODUCT-XAML-AUDIT')}
}

function Get-WinCareAppxRuntimeInventory {
    [CmdletBinding()]param([switch]$IncludeUnpackaged,[ValidateRange(1,2048)][int]$Limit=512)
    if(-not $IsWindows -or -not(Initialize-WinCareShellHardwareInterop)){return @()}
    $rows=[Collections.Generic.List[object]]::new()
    foreach($process in @(Get-Process -ErrorAction SilentlyContinue|Sort-Object Id|Select-Object -First $Limit)){
        try{$record=[WinCare.Native.ShellHardware]::GetProcessPackage([int]$process.Id);if(-not $IncludeUnpackaged -and -not [string]$record.PackageFullName){continue};$rows.Add([pscustomobject]@{ProcessId=[int]$process.Id;ProcessName=[string]$process.ProcessName;PackageFullName=[string]$record.PackageFullName;Packaged=[bool][string]$record.PackageFullName;Win32Error=[int]$record.Win32Error;SourceRecords=@('SRC:1461504853')})}catch{Write-Verbose 'A process package identity could not be inspected.'}
    }
    @($rows)
}

function Get-WinCareAppxLaunchTarget {
    [CmdletBinding()]param([string]$Aumid='',[ValidateRange(1,2000)][int]$Limit=1000)
    if(-not $IsWindows){return @()}
    $rows=[Collections.Generic.List[object]]::new()
    foreach($package in @(Get-AppxPackage -ErrorAction SilentlyContinue|Select-Object -First $Limit)){
        try{
            $manifestPath=Join-Path ([string]$package.InstallLocation) 'AppxManifest.xml';if(-not(Test-Path -LiteralPath $manifestPath -PathType Leaf)){continue}
            $manifest=Get-AppxPackageManifest -Package $package -ErrorAction Stop
            foreach($app in @($manifest.Package.Applications.Application)){
                $id=[string]$app.Id;if(-not $id -or $id.Length -gt 256){continue};$target="$($package.PackageFamilyName)!$id"
                if($Aumid -and $target -ne $Aumid){continue}
                $rows.Add([pscustomobject]@{Aumid=$target;ApplicationId=$id;PackageName=[string]$package.Name;PackageFullName=[string]$package.PackageFullName;PackageFamilyName=[string]$package.PackageFamilyName;ManifestPath=$manifestPath;ManifestSha256=Get-WinCareSha256 -LiteralPath $manifestPath;DisplayName=[string]$app.VisualElements.DisplayName;SourceRecords=@('SRC:1461504853')})
            }
        }catch{Write-Verbose 'An AppX manifest could not be inspected.'}
    }
    @($rows|Sort-Object PackageName,ApplicationId)
}

function New-WinCareAppxLaunchPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][ValidateLength(3,1024)][string]$Aumid)
    $target=Get-WinCareAppxLaunchTarget -Aumid $Aumid|Select-Object -First 1;if(-not $target){throw 'AppX launch target was not found.'}
    $action=New-WinCareAction -Type LaunchAppxApplication -Label "Launch $($target.PackageName): $($target.ApplicationId)" -Risk Low -Parameters @{Aumid=$target.Aumid;PackageFullName=$target.PackageFullName;ManifestSha256=$target.ManifestSha256} -RequiresAdmin $false -Reversible $false -TimeoutSeconds 60 -Tags @('AppX','Launch','ExactManifest') -SourceRecords @('SRC:1461504853') -RecoveryDescription 'Application activation is transient and is not automatically reversed.'
    New-WinCarePlan -Title 'Launch packaged application' -Description 'Revalidates the package full name and manifest hash before activation through the supported Windows activation manager.' -Actions @($action) -RollbackOnFailure $false -SourceRecords @($action.SourceRecords)
}

function Invoke-WinCareLaunchAppxApplicationAction {
    param([object]$Action)
    if(-not $IsWindows -or -not(Initialize-WinCareShellHardwareInterop)){return New-WinCareResult -Success $false -Message 'AppX activation interop is unavailable.' -ExitCode 127 -Code 'AppxActivationUnavailable'}
    $p=$Action.Parameters;$target=Get-WinCareAppxLaunchTarget -Aumid ([string]$p.Aumid)|Select-Object -First 1
    if(-not $target -or [string]$target.PackageFullName -ne [string]$p.PackageFullName -or [string]$target.ManifestSha256 -ne [string]$p.ManifestSha256){return New-WinCareResult -Success $false -Status Blocked -Code 'AppxTargetChanged' -Message 'AppX package or manifest changed after preview.' -ExitCode 74}
    try{$pid=[WinCare.Native.ShellHardware]::ActivateApplication([string]$p.Aumid);New-WinCareResult -Success ($pid -gt 0) -Code 'AppxActivated' -Message 'Packaged application was activated.' -Data @{ProcessId=$pid;Aumid=$p.Aumid}}
    catch{New-WinCareResult -Success $false -Code 'AppxActivationFailed' -Message $_.Exception.Message -ExitCode 1}
}

function Get-WinCareArchiveInspection {
    [CmdletBinding()]param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,200000)][int]$MaximumMembers=50000,
        [ValidateRange(1,1099511627776)][long]$MaximumExpandedBytes=10737418240,
        [ValidateRange(1,10000)][int]$MaximumCompressionRatio=200
    )
    Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath;$extension=[IO.Path]::GetExtension($path).ToLowerInvariant();if($extension -notin @('.zip','.appx','.msix','.appxbundle','.msixbundle','.nupkg')){throw 'Only ZIP-container formats are supported.'}
    $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read);$archive=$null
    try{
        $archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Read,$false);$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$seenCase=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$violations=[Collections.Generic.List[string]]::new();$members=[Collections.Generic.List[object]]::new();$expanded=0L;$compressed=0L;$index=0
        foreach($entry in $archive.Entries){
            $index++;if($index -gt $MaximumMembers){$violations.Add("Member count exceeds $MaximumMembers.");break}
            $name=([string]$entry.FullName).Replace('\','/');$expanded+=[long]$entry.Length;$compressed+=[long]$entry.CompressedLength
            $absolute=$name.StartsWith('/') -or $name -match '^[A-Za-z]:';$segments=@($name -split '/'|Where-Object{$_ -ne ''});$traversal=$absolute -or '..' -in $segments
            $duplicate=-not $seen.Add($name);$caseCollision=(-not $duplicate) -and -not $seenCase.Add($name)
            $unixMode=([int64]$entry.ExternalAttributes -shr 16) -band 0xFFFF;$kind=$unixMode -band 0xF000;$symlink=$kind -eq 0xA000
            if($traversal){$violations.Add("Unsafe path: $name")};if($duplicate){$violations.Add("Duplicate member: $name")};if($caseCollision){$violations.Add("Case-colliding member: $name")};if($symlink){$violations.Add("Symlink member: $name")}
            if($expanded -gt $MaximumExpandedBytes){$violations.Add("Expanded bytes exceed $MaximumExpandedBytes.")}
            $ratio=if($entry.CompressedLength -gt 0){[double]$entry.Length/[double]$entry.CompressedLength}elseif($entry.Length -gt 0){[double]::PositiveInfinity}else{1.0};if($ratio -gt $MaximumCompressionRatio){$violations.Add("Compression ratio exceeds $MaximumCompressionRatio for $name")}
            if($members.Count -lt 2000){$members.Add([pscustomobject]@{Name=$name;Length=[long]$entry.Length;CompressedLength=[long]$entry.CompressedLength;Directory=$name.EndsWith('/');Symlink=$symlink;UnsafePath=$traversal;Duplicate=$duplicate;CaseCollision=$caseCollision})}
        }
        [pscustomobject]@{Path=$path;Sha256=Get-WinCareSha256 -LiteralPath $path;Bytes=[long](Get-Item -LiteralPath $path).Length;Format=$extension.TrimStart('.');MemberCount=$index;ExpandedBytes=$expanded;CompressedMemberBytes=$compressed;Safe=($violations.Count-eq 0);Violations=@($violations|Select-Object -Unique);Members=@($members);MembersTruncated=($index -gt $members.Count);ExtractionPerformed=$false;Bounds=[pscustomobject]@{MaximumMembers=$MaximumMembers;MaximumExpandedBytes=$MaximumExpandedBytes;MaximumCompressionRatio=$MaximumCompressionRatio};SourceRecords=@('SRC:1461504853')}
    }finally{if($archive){$archive.Dispose()};$stream.Dispose()}
}

function Get-WinCareServicingMediaInfo {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath,[ValidateRange(1,3600)][int]$TimeoutSeconds=300)
    if(-not $IsWindows){throw 'Servicing-media inspection requires Windows DISM.'}
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath;$extension=[IO.Path]::GetExtension($path).ToLowerInvariant();if($extension -notin @('.wim','.esd','.swm')){throw 'Servicing-media path must be a WIM, ESD, or SWM file.'}
    $item=Get-Item -LiteralPath $path -ErrorAction Stop;if($item.Length -gt 274877906944){throw 'Servicing-media file exceeds the 256 GiB inspection limit.'}
    $result=Invoke-WinCareProcess -FilePath dism.exe -ArgumentList @('/English','/Get-WimInfo',"/WimFile:$path") -TimeoutSeconds $TimeoutSeconds
    $images=[Collections.Generic.List[object]]::new();$current=$null
    foreach($line in @(([string]$result.Data.StdOut) -split "`r?`n")){
        if($line -match '^Index\s*:\s*(\d+)'){$current=[ordered]@{Index=[int]$Matches[1];Name='';Description='';Size='';Architecture=''};$images.Add([pscustomobject]$current);continue}
        if(-not $current){continue};if($line -match '^Name\s*:\s*(.+)$'){$images[$images.Count-1].Name=$Matches[1].Trim()}elseif($line -match '^Description\s*:\s*(.+)$'){$images[$images.Count-1].Description=$Matches[1].Trim()}elseif($line -match '^Size\s*:\s*(.+)$'){$images[$images.Count-1].Size=$Matches[1].Trim()}elseif($line -match '^Architecture\s*:\s*(.+)$'){$images[$images.Count-1].Architecture=$Matches[1].Trim()}
    }
    [pscustomobject]@{Path=$path;Sha256=Get-WinCareSha256 -LiteralPath $path;Bytes=[long]$item.Length;Success=$result.Success;ExitCode=$result.ExitCode;Images=@($images);RawOutput=if($images.Count){''}else{[string]$result.Data.StdOut};ReadOnly=$true;SourceRecords=@('SRC:1461504853')}
}

function ConvertTo-WinCareTerminalProfile {
    param([object]$Profile)
    $commandLine=[string](Get-WinCarePropertyValue $Profile 'commandline' '')
    $startingDirectory=[string](Get-WinCarePropertyValue $Profile 'startingDirectory' '')
    $executable=''
    if($commandLine){
        $trimmed=$commandLine.Trim()
        if($trimmed.StartsWith('"')){$closing=$trimmed.IndexOf('"',1);if($closing -gt 1){$executable=$trimmed.Substring(1,$closing-1)}}
        if(-not $executable){$executable=($trimmed -split '\s+',2)[0].Trim('"')}
        try{$executable=[IO.Path]::GetFileName($executable)}catch{$executable=''}
    }
    [pscustomobject]@{
        Name=[string](Get-WinCarePropertyValue $Profile 'name' '')
        Guid=[string](Get-WinCarePropertyValue $Profile 'guid' '')
        Source=[string](Get-WinCarePropertyValue $Profile 'source' '')
        Executable=$executable
        HasCommandLine=[bool]$commandLine
        CommandLineSha256=if($commandLine){Get-WinCareSha256Text -Text $commandLine}else{''}
        HasStartingDirectory=[bool]$startingDirectory
        StartingDirectorySha256=if($startingDirectory){Get-WinCareSha256Text -Text $startingDirectory}else{''}
        Hidden=[bool](Get-WinCarePropertyValue $Profile 'hidden' $false)
    }
}

function Get-WinCareTerminalEnvironment {
    [CmdletBinding()]param()
    if(-not $IsWindows){return [pscustomobject]@{Available=$false;SourceRecords=@('SRC:1461504853')}}
    $candidates=[Collections.Generic.List[string]]::new();$local=[Environment]::GetFolderPath('LocalApplicationData')
    if(-not $local){return [pscustomobject]@{Available=$false;Reason='LocalApplicationDataUnavailable';SourceRecords=@('SRC:1461504853')}}
    foreach($path in @((Join-Path $local 'Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json'),(Join-Path $local 'Packages\Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe\LocalState\settings.json'))){if(Test-Path -LiteralPath $path -PathType Leaf){$candidates.Add($path)}}
    $terminal=[Collections.Generic.List[object]]::new()
    foreach($path in $candidates){
        try{if((Get-Item -LiteralPath $path).Length -gt 4194304){throw 'Terminal settings exceed 4 MiB.'};$doc=Read-WinCareBoundedJson -LiteralPath $path -MaximumBytes 4194304 -Depth 80;$profiles=@();if($doc.profiles -and $doc.profiles.list){$profiles=@($doc.profiles.list|ForEach-Object{ConvertTo-WinCareTerminalProfile $_})};$terminal.Add([pscustomobject]@{FileName=[IO.Path]::GetFileName($path);PathSha256=Get-WinCareSha256Text -Text $path;Sha256=Get-WinCareSha256 $path;DefaultProfile=[string]$doc.defaultProfile;ProfileCount=$profiles.Count;Profiles=$profiles;ActionsCount=@($doc.actions).Count})}catch{$terminal.Add([pscustomobject]@{FileName=[IO.Path]::GetFileName($path);PathSha256=Get-WinCareSha256Text -Text $path;Error=$_.Exception.Message})}
    }
    $conEmuCandidates=@()
    if($env:APPDATA){$conEmuCandidates+=@(Join-Path $env:APPDATA 'ConEmu.xml';Join-Path $env:APPDATA 'ConEmu\.ConEmu.xml')}
    if($env:USERPROFILE){$conEmuCandidates+=@(Join-Path $env:USERPROFILE '.ConEmu.xml')}
    $conEmuCandidates=@($conEmuCandidates|Where-Object{$_ -and (Test-Path -LiteralPath $_ -PathType Leaf)})
    $conEmu=@($conEmuCandidates|ForEach-Object{[pscustomobject]@{FileName=[IO.Path]::GetFileName($_);PathSha256=Get-WinCareSha256Text -Text $_;Sha256=Get-WinCareSha256 $_;Bytes=[long](Get-Item -LiteralPath $_).Length}})
    [pscustomobject]@{Available=$true;WindowsTerminal=@($terminal);ConEmu=@($conEmu);SecretsIncluded=$false;Execution='None';SourceRecords=@('SRC:1461504853')}
}

function New-WinCareTerminalProfileExportPlan {
    [CmdletBinding()]param([ValidatePattern('^[A-Za-z0-9._-]{3,100}\.json$')][string]$Name='terminal-environment.json')
    $environment=Get-WinCareTerminalEnvironment;$root=Join-Path (Get-WinCareShellHardwareRoot) 'Terminal';$null=New-Item -ItemType Directory -Path $root -Force;$path=Join-Path $root $Name
    $content=[Text.Encoding]::UTF8.GetBytes((ConvertTo-WinCareCanonicalJson -InputObject $environment -Depth 60)+"`n")
    $action=New-WinCareManagedFileWriteAction -Path $path -Content $content -AllowedRoots @($root) -Label 'Export sanitized terminal environment' -SourceRecords @('SRC:1461504853')
    New-WinCarePlan -Title 'Export terminal environment' -Description 'Exports bounded local terminal metadata without executing profiles or copying terminal history.' -Actions @($action) -SourceRecords @($action.SourceRecords)
}

function Get-WinCareGuiResourceAudit {
    [CmdletBinding()]param([string]$LiteralPath='')
    if(-not $LiteralPath){$LiteralPath=Join-Path $script:WinCareModuleRoot 'Data\Gui\WinCare.MainWindow.xaml'}
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath -AllowedRoots @($script:WinCareModuleRoot);$raw=Read-WinCareBoundedText -LiteralPath $path -MaximumBytes 2097152
    $definitions=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);foreach($match in [regex]::Matches($raw,'x:Key\s*=\s*["'']([^"'']+)["'']')){$null=$definitions.Add($match.Groups[1].Value)}
    $references=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);foreach($match in [regex]::Matches($raw,'\{(?:StaticResource|DynamicResource)\s+([^},\s]+)')){$null=$references.Add($match.Groups[1].Value)}
    $missing=@($references|Where-Object{$_ -notin $definitions}|Sort-Object);$unused=@($definitions|Where-Object{$_ -notin $references}|Sort-Object)
    [pscustomobject]@{Path=$path;Sha256=Get-WinCareSha256 -LiteralPath $path;DefinitionCount=$definitions.Count;ReferenceCount=$references.Count;Missing=@($missing);Unused=@($unused);Success=($missing.Count-eq 0);SourceRecords=@('PRODUCT-XAML-AUDIT','PRODUCT-CAPABILITY-NAVIGATION')}
}

function Get-WinCareFullscreenShellAssessment {
    [CmdletBinding()]param()
    if(-not $IsWindows){return $null}
    $shell=Get-WinCareRegistryValueState -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name 'Shell';$deviceForm=Get-WinCareRegistryValueState -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\OEM' -Name 'DeviceForm'
    $shellValue=if($shell.Exists){[string]$shell.Value}else{'explorer.exe'};$custom=$shellValue.Trim() -notmatch '^(?i)explorer(\.exe)?$'
    $deviceFormValue=if($deviceForm.Exists){$deviceForm.Value}else{$null};$deviceFormCode=$null
    if($null -ne $deviceFormValue){$parsed=0;if([int]::TryParse([string]$deviceFormValue,[ref]$parsed)){$deviceFormCode=$parsed}}
    [pscustomobject]@{Shell=$shellValue;CustomShell=$custom;DeviceForm=$deviceFormValue;ExplorerRunning=[bool](Get-Process explorer -ErrorAction SilentlyContinue);Risk=if($custom){'Critical'}elseif($deviceFormCode -eq 46){'High'}else{'Low'};Admission='AssessmentOnly';Reason='Custom shell replacement is not admitted as an automatic WinCare mutation because startup failure can remove the primary recovery surface.';SourceRecords=@('SRC:1461504853')}
}

function Get-WinCareShellHardwareCapabilityCard {
    [CmdletBinding()]param([string]$Query='')
    $cards=@(
        [pscustomobject]@{Id='hardware-inventory';Title='Hardware inventory';Summary='Bounded CIM inventory with sensitive identifiers hashed by default.';Risk='ReadOnly';SourceRecords=@('SRC:1461504853')},
        [pscustomobject]@{Id='monitor-control';Title='DDC/CI monitor controls';Summary='Brightness and contrast with physical-monitor compare-and-swap and recovery.';Risk='Low';SourceRecords=@('SRC:1461504853')},
        [pscustomobject]@{Id='window-search';Title='Window and Explorer search';Summary='Fuzzy title/process search plus bounded Explorer session capture and restore.';Risk='Low';SourceRecords=@('SRC:1461504853')},
        [pscustomobject]@{Id='ui-automation';Title='UI Automation snapshot';Summary='Bounded accessibility-tree inspection with text redacted by default.';Risk='ReadOnly';SourceRecords=@('SRC:1461504853')},
        [pscustomobject]@{Id='appx-runtime';Title='AppX runtime';Summary='Process-package mapping and exact manifest-verified activation.';Risk='Low';SourceRecords=@('SRC:1461504853')},
        [pscustomobject]@{Id='archive-inspection';Title='Archive inspection';Summary='No-extraction ZIP/AppX/MSIX member and expansion safety audit.';Risk='ReadOnly';SourceRecords=@('SRC:1461504853')},
        [pscustomobject]@{Id='servicing-media';Title='Servicing media';Summary='Read-only DISM metadata for local WIM, ESD, and SWM images.';Risk='ReadOnly';SourceRecords=@('SRC:1461504853')},
        [pscustomobject]@{Id='terminal-environment';Title='Terminal environment';Summary='Sanitized Windows Terminal and ConEmu profile inventory and export.';Risk='ReadOnly';SourceRecords=@('SRC:1461504853')},
        [pscustomobject]@{Id='fullscreen-shell';Title='Full-screen shell assessment';Summary='Detects custom shell replacement without granting mutation authority.';Risk='ReadOnly';SourceRecords=@('SRC:1461504853')},
        [pscustomobject]@{Id='winhance-maximum';Title='Winhance maximum';Summary='Critical one-click hardening, debloat, privacy, and shell-reduction profile.';Risk='Critical';SourceRecords=@('SRC:1461504853')}
    )
    if($Query){$q=$Query.ToLowerInvariant();return @($cards|Where-Object{([string]$_.Title).ToLowerInvariant().Contains($q) -or ([string]$_.Summary).ToLowerInvariant().Contains($q) -or ([string]$_.Id).Contains($q)})}
    $cards
}



