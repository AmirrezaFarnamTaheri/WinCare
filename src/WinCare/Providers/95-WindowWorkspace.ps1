function Get-WinCareWindowTitleHash {
    param([AllowEmptyString()][string]$Title)
    Get-WinCareSha256Text -Text ([string]$Title)
}

function Get-WinCareWindowInventory {
    [CmdletBinding()]param([switch]$IncludeUntitled,[switch]$IncludeCloaked)
    if(-not $IsWindows -or -not(Initialize-WinCareProductivityInterop)){return @()}
    $items=[Collections.Generic.List[object]]::new()
    foreach($window in [WinCare.Native.Productivity]::EnumerateWindows([bool]$IncludeUntitled)){
        if(-not $IncludeCloaked -and [bool]$window.Cloaked){continue}
        $process=$null;$path='';$start='';$name='';$signature='Unknown'
        try{$process=Get-Process -Id ([int]$window.ProcessId) -ErrorAction Stop;$name=$process.ProcessName;$start=$process.StartTime.ToUniversalTime().ToString('o');$path=$process.Path;if($path){$signature=(Get-AuthenticodeSignature -LiteralPath $path -ErrorAction SilentlyContinue).Status.ToString()}}catch{Write-Verbose 'Window process metadata was unavailable.'}
        $items.Add([pscustomobject]@{Handle=[long]$window.Handle;ProcessId=[int]$window.ProcessId;ProcessName=$name;ProcessStartTime=$start;Title=[string]$window.Title;TitleHash=Get-WinCareWindowTitleHash ([string]$window.Title);ClassName=[string]$window.ClassName;Left=[int]$window.Left;Top=[int]$window.Top;Width=[int]$window.Width;Height=[int]$window.Height;Topmost=[bool]$window.Topmost;Cloaked=[bool]$window.Cloaked;Path=$path;Signature=$signature;ZOrder=$items.Count})
    }
    @($items)
}

function Get-WinCareMonitorInventory {
    [CmdletBinding()]param()
    if(-not $IsWindows -or -not(Initialize-WinCareProductivityInterop)){return @()}
    @([WinCare.Native.Productivity]::EnumerateMonitors()|ForEach-Object{[pscustomobject]@{Handle=[long]$_.Handle;DeviceName=[string]$_.DeviceName;Left=[int]$_.Left;Top=[int]$_.Top;Width=[int]$_.Width;Height=[int]$_.Height;WorkLeft=[int]$_.WorkLeft;WorkTop=[int]$_.WorkTop;WorkWidth=[int]$_.WorkWidth;WorkHeight=[int]$_.WorkHeight;Primary=[bool]$_.Primary}})
}

function Get-WinCareWindowZoneDefinition {
    [CmdletBinding()]param([string]$LayoutId,[string]$ZoneId)
    $path=Join-Path $script:WinCareModuleRoot 'Data\Catalog\window-zones.json'
    $doc=Read-WinCareBoundedJson -LiteralPath $path -MaximumBytes 4194304 -AsHashtable -Depth 20
    $null=Test-WinCareStrictObjectKeys -InputObject $doc -AllowedKeys @('schemaVersion','layouts') -Context 'window-zone catalog'
    if([int]$doc.schemaVersion -ne 1){throw 'Unsupported window-zone catalog schema.'}
    $result=[Collections.Generic.List[object]]::new();$layoutIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($layout in @($doc.layouts)){
        $null=Test-WinCareStrictObjectKeys -InputObject $layout -AllowedKeys @('id','title','zones') -Context 'window-zone layout'
        if([string]$layout.id -notmatch '^[a-z0-9][a-z0-9-]{1,40}$' -or -not $layoutIds.Add([string]$layout.id)){throw 'Window-zone layout ID is invalid or duplicated.'}
        $zoneIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($zone in @($layout.zones)){
            $null=Test-WinCareStrictObjectKeys -InputObject $zone -AllowedKeys @('id','title','x','y','width','height','zIndex') -Context 'window zone'
            if([string]$zone.id -notmatch '^[a-z0-9][a-z0-9-]{1,40}$' -or -not $zoneIds.Add([string]$zone.id)){throw 'Window-zone ID is invalid or duplicated.'}
            foreach($field in @('x','y','width','height')){if([double]$zone[$field] -lt 0 -or [double]$zone[$field] -gt 1){throw "Window-zone $field is outside 0..1."}}
            if([double]$zone.width -le 0 -or [double]$zone.height -le 0 -or ([double]$zone.x+[double]$zone.width) -gt 1.000001 -or ([double]$zone.y+[double]$zone.height) -gt 1.000001){throw 'Window zone exceeds the normalized monitor work area.'}
            $item=[pscustomobject]@{LayoutId=[string]$layout.id;LayoutTitle=[string]$layout.title;ZoneId=[string]$zone.id;ZoneTitle=[string]$zone.title;X=[double]$zone.x;Y=[double]$zone.y;Width=[double]$zone.width;Height=[double]$zone.height;ZIndex=[int]$zone.zIndex}
            if((-not $LayoutId -or $LayoutId -eq $item.LayoutId) -and (-not $ZoneId -or $ZoneId -eq $item.ZoneId)){$result.Add($item)}
        }
    }
    @($result)
}

function Get-WinCareWindowByIdentity {
    param([Parameter(Mandatory)][long]$Handle,[Parameter(Mandatory)][int]$ProcessId,[string]$ProcessStartTime,[string]$ExpectedTitleHash)
    $window=Get-WinCareWindowInventory -IncludeUntitled -IncludeCloaked|Where-Object Handle -eq $Handle|Select-Object -First 1
    if(-not $window){throw 'Target window no longer exists.'}
    if([int]$window.ProcessId -ne $ProcessId){throw 'Target window process changed after preview.'}
    if($ProcessStartTime -and [string]$window.ProcessStartTime -ne $ProcessStartTime){throw 'Target window process start time changed after preview.'}
    if($ExpectedTitleHash -and [string]$window.TitleHash -ne $ExpectedTitleHash){throw 'Target window title changed after preview.'}
    $window
}

function New-WinCareWindowZonePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][long]$WindowHandle,[Parameter(Mandatory)][string]$LayoutId,[Parameter(Mandatory)][string]$ZoneId,[string]$MonitorDevice='')
    $window=Get-WinCareWindowInventory -IncludeUntitled|Where-Object Handle -eq $WindowHandle|Select-Object -First 1;if(-not $window){throw 'Window was not found.'};if(-not $window.ProcessStartTime -or -not $window.ProcessName){throw 'Window process identity could not be established.'}
    $monitor=if($MonitorDevice){Get-WinCareMonitorInventory|Where-Object DeviceName -eq $MonitorDevice|Select-Object -First 1}else{Get-WinCareMonitorInventory|Where-Object Primary|Select-Object -First 1};if(-not $monitor){throw 'Monitor was not found.'}
    $zone=Get-WinCareWindowZoneDefinition -LayoutId $LayoutId -ZoneId $ZoneId|Select-Object -First 1;if(-not $zone){throw 'Window zone was not found.'}
    $left=[int][math]::Round($monitor.WorkLeft+($monitor.WorkWidth*$zone.X));$top=[int][math]::Round($monitor.WorkTop+($monitor.WorkHeight*$zone.Y));$width=[math]::Max(1,[int][math]::Round($monitor.WorkWidth*$zone.Width));$height=[math]::Max(1,[int][math]::Round($monitor.WorkHeight*$zone.Height))
    $identity=@{WindowHandle=[long]$window.Handle;ProcessId=[int]$window.ProcessId;ProcessStartTime=[string]$window.ProcessStartTime;ExpectedTitleHash=[string]$window.TitleHash}
    $parameters=$identity.Clone();$parameters.ExpectedCurrentLeft=[int]$window.Left;$parameters.ExpectedCurrentTop=[int]$window.Top;$parameters.ExpectedCurrentWidth=[int]$window.Width;$parameters.ExpectedCurrentHeight=[int]$window.Height;$parameters.Left=$left;$parameters.Top=$top;$parameters.Width=$width;$parameters.Height=$height;$parameters.MonitorDevice=[string]$monitor.DeviceName;$parameters.ExpectedMonitorWorkLeft=[int]$monitor.WorkLeft;$parameters.ExpectedMonitorWorkTop=[int]$monitor.WorkTop;$parameters.ExpectedMonitorWorkWidth=[int]$monitor.WorkWidth;$parameters.ExpectedMonitorWorkHeight=[int]$monitor.WorkHeight
    $before=$identity.Clone();$before.ExpectedCurrentLeft=$left;$before.ExpectedCurrentTop=$top;$before.ExpectedCurrentWidth=$width;$before.ExpectedCurrentHeight=$height;$before.Left=[int]$window.Left;$before.Top=[int]$window.Top;$before.Width=[int]$window.Width;$before.Height=[int]$window.Height
    $action=New-WinCareAction -Type 'SetWindowBounds' -Label "Place $($window.ProcessName) in $($zone.LayoutTitle) / $($zone.ZoneTitle)" -Risk Low -Parameters $parameters -RequiresAdmin $false -Reversible $true -Compensator @{Type='SetWindowBounds';Parameters=$before} -TimeoutSeconds 30 -Tags @('Window','Zone','Idempotent') -SourceRecords @('SRC:97b6af9be1') -RecoveryDescription 'Restore the exact previous window rectangle while the same process and window remain alive.'
    New-WinCarePlan -Title 'Apply window zone' -Actions @($action) -SourceRecords @('SRC:97b6af9be1')
}

function New-WinCareWindowTopmostPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][long]$WindowHandle,[Parameter(Mandatory)][bool]$Topmost)
    $window=Get-WinCareWindowInventory -IncludeUntitled|Where-Object Handle -eq $WindowHandle|Select-Object -First 1;if(-not $window){throw 'Window was not found.'};if(-not $window.ProcessStartTime -or -not $window.ProcessName){throw 'Window process identity could not be established.'}
    $base=@{WindowHandle=[long]$window.Handle;ProcessId=[int]$window.ProcessId;ProcessStartTime=[string]$window.ProcessStartTime;ExpectedTitleHash=[string]$window.TitleHash}
    $parameters=$base.Clone();$parameters.ExpectedCurrentTopmost=[bool]$window.Topmost;$parameters.Topmost=$Topmost;$before=$base.Clone();$before.ExpectedCurrentTopmost=$Topmost;$before.Topmost=[bool]$window.Topmost
    $action=New-WinCareAction -Type 'SetWindowTopmost' -Label "Set always-on-top to $Topmost for $($window.ProcessName)" -Risk Low -Parameters $parameters -RequiresAdmin $false -Reversible $true -Compensator @{Type='SetWindowTopmost';Parameters=$before} -TimeoutSeconds 30 -Tags @('Window','Topmost','Idempotent') -SourceRecords @('SRC:97b6af9be1') -RecoveryDescription 'Restore the exact previous topmost state while the same window remains alive.'
    New-WinCarePlan -Title 'Change always-on-top state' -Actions @($action) -SourceRecords @('SRC:97b6af9be1')
}

function New-WinCareWindowActivatePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][long]$WindowHandle)
    $window=Get-WinCareWindowInventory -IncludeUntitled|Where-Object Handle -eq $WindowHandle|Select-Object -First 1;if(-not $window){throw 'Window was not found.'};if(-not $window.ProcessStartTime -or -not $window.ProcessName){throw 'Window process identity could not be established.'}
    $action=New-WinCareAction -Type 'ActivateWindow' -Label "Activate $($window.ProcessName): $($window.Title)" -Risk Low -Parameters @{WindowHandle=[long]$window.Handle;ProcessId=[int]$window.ProcessId;ProcessStartTime=[string]$window.ProcessStartTime;ExpectedTitleHash=[string]$window.TitleHash} -RequiresAdmin $false -Reversible $false -TimeoutSeconds 15 -Tags @('Window','Activation') -SourceRecords @('SRC:97b6af9be1') -RecoveryDescription 'Focus changes are transient and are not automatically reversed.'
    New-WinCarePlan -Title 'Activate window' -Actions @($action) -RollbackOnFailure $false -SourceRecords @('SRC:97b6af9be1')
}

function New-WinCareEmergencyInputReleasePlan {
    [CmdletBinding()]param()
    $action=New-WinCareAction -Type 'ReleaseLocalInput' -Label 'Release cursor clipping and pressed modifier state' -Risk Low -Parameters @{} -RequiresAdmin $false -Reversible $false -TimeoutSeconds 15 -Tags @('Input','EmergencyRelease') -SourceRecords @('SRC:97b6af9be1') -RecoveryDescription 'The action only releases local input constraints; no persistent state is changed.'
    New-WinCarePlan -Title 'Emergency local input release' -Actions @($action) -RollbackOnFailure $false -SourceRecords @('SRC:97b6af9be1')
}

function Invoke-WinCareSetWindowBoundsAction {
    param([object]$Action)
    if(-not(Initialize-WinCareProductivityInterop)){return New-WinCareResult -Success $false -Message 'Windows productivity interop is unavailable.' -ExitCode 2}
    $p=$Action.Parameters;$warnings=@()
    try{
        $current=Get-WinCareWindowByIdentity -Handle ([long]$p.WindowHandle) -ProcessId ([int]$p.ProcessId) -ProcessStartTime ([string]$p.ProcessStartTime) -ExpectedTitleHash ([string]$p.ExpectedTitleHash)
        $currentMatches=[math]::Abs([int]$current.Left-[int]$p.ExpectedCurrentLeft)-le 2 -and [math]::Abs([int]$current.Top-[int]$p.ExpectedCurrentTop)-le 2 -and [math]::Abs([int]$current.Width-[int]$p.ExpectedCurrentWidth)-le 4 -and [math]::Abs([int]$current.Height-[int]$p.ExpectedCurrentHeight)-le 4
        if(-not $currentMatches){return New-WinCareResult -Success $false -Status Blocked -Code 'WindowStateChanged' -Message 'Window bounds changed after preview.' -ExitCode 74}
        if($p.ContainsKey('MonitorDevice')){
            $monitor=Get-WinCareMonitorInventory|Where-Object DeviceName -eq [string]$p.MonitorDevice|Select-Object -First 1
            if(-not $monitor -or [int]$monitor.WorkLeft -ne [int]$p.ExpectedMonitorWorkLeft -or [int]$monitor.WorkTop -ne [int]$p.ExpectedMonitorWorkTop -or [int]$monitor.WorkWidth -ne [int]$p.ExpectedMonitorWorkWidth -or [int]$monitor.WorkHeight -ne [int]$p.ExpectedMonitorWorkHeight){return New-WinCareResult -Success $false -Status Blocked -Code 'MonitorStateChanged' -Message 'Monitor work area changed after preview.' -ExitCode 74}
        }
        if(-not[WinCare.Native.Productivity]::SetBounds([long]$p.WindowHandle,[int]$p.Left,[int]$p.Top,[int]$p.Width,[int]$p.Height)){throw 'Windows rejected the requested window rectangle.'}
        Start-Sleep -Milliseconds 100
        $after=Get-WinCareWindowInventory -IncludeUntitled -IncludeCloaked|Where-Object Handle -eq [long]$p.WindowHandle|Select-Object -First 1
        $verified=$after -and [math]::Abs([int]$after.Left-[int]$p.Left)-le 2 -and [math]::Abs([int]$after.Top-[int]$p.Top)-le 2 -and [math]::Abs([int]$after.Width-[int]$p.Width)-le 4 -and [math]::Abs([int]$after.Height-[int]$p.Height)-le 4
        if(-not $verified){throw 'Window bounds could not be verified.'}
        New-WinCareResult -Success $true -Message 'Window bounds updated and verified.' -Data $after
    }catch{
        $failure=$_.Exception.Message;$descriptor=Get-WinCarePropertyValue $Action 'Compensator' $null
        if($descriptor -and [string](Get-WinCarePropertyValue $descriptor 'Type' '') -eq 'SetWindowBounds'){
            try{
                $before=ConvertTo-WinCareParameterDictionary (Get-WinCarePropertyValue $descriptor 'Parameters' @{})
                $null=Get-WinCareWindowByIdentity -Handle ([long]$before.WindowHandle) -ProcessId ([int]$before.ProcessId) -ProcessStartTime ([string]$before.ProcessStartTime) -ExpectedTitleHash ([string]$before.ExpectedTitleHash)
                if(-not[WinCare.Native.Productivity]::SetBounds([long]$before.WindowHandle,[int]$before.Left,[int]$before.Top,[int]$before.Width,[int]$before.Height)){throw 'Windows rejected the recovery rectangle.'}
                Start-Sleep -Milliseconds 100
                $restored=Get-WinCareWindowInventory -IncludeUntitled -IncludeCloaked|Where-Object Handle -eq [long]$before.WindowHandle|Select-Object -First 1
                if(-not $restored -or [math]::Abs([int]$restored.Left-[int]$before.Left)-gt 2 -or [math]::Abs([int]$restored.Top-[int]$before.Top)-gt 2 -or [math]::Abs([int]$restored.Width-[int]$before.Width)-gt 4 -or [math]::Abs([int]$restored.Height-[int]$before.Height)-gt 4){throw 'Recovered window bounds could not be verified.'}
                $warnings+='The prior window rectangle was restored and verified after the failed update.'
            }catch{$warnings+="The failed window update could not restore its prior rectangle: $($_.Exception.Message)"}
        }
        New-WinCareResult -Success $false -Message "Window bounds update failed: $failure" -Warnings $warnings -ExitCode 1
    }
}

function Invoke-WinCareSetWindowTopmostAction {
    param([object]$Action)
    if(-not(Initialize-WinCareProductivityInterop)){return New-WinCareResult -Success $false -Message 'Windows productivity interop is unavailable.' -ExitCode 2}
    $p=$Action.Parameters;$warnings=@()
    try{
        $current=Get-WinCareWindowByIdentity -Handle ([long]$p.WindowHandle) -ProcessId ([int]$p.ProcessId) -ProcessStartTime ([string]$p.ProcessStartTime) -ExpectedTitleHash ([string]$p.ExpectedTitleHash)
        if([bool]$current.Topmost -ne [bool]$p.ExpectedCurrentTopmost){return New-WinCareResult -Success $false -Status Blocked -Code 'WindowStateChanged' -Message 'Window topmost state changed after preview.' -ExitCode 74}
        if(-not[WinCare.Native.Productivity]::SetTopmost([long]$p.WindowHandle,[bool]$p.Topmost)){throw 'Windows rejected the requested topmost state.'}
        Start-Sleep -Milliseconds 100
        $after=Get-WinCareWindowInventory -IncludeUntitled -IncludeCloaked|Where-Object Handle -eq [long]$p.WindowHandle|Select-Object -First 1
        if(-not $after -or [bool]$after.Topmost -ne [bool]$p.Topmost){throw 'Window topmost state could not be verified.'}
        New-WinCareResult -Success $true -Message 'Window topmost state updated and verified.' -Data $after
    }catch{
        $failure=$_.Exception.Message;$descriptor=Get-WinCarePropertyValue $Action 'Compensator' $null
        if($descriptor -and [string](Get-WinCarePropertyValue $descriptor 'Type' '') -eq 'SetWindowTopmost'){
            try{
                $before=ConvertTo-WinCareParameterDictionary (Get-WinCarePropertyValue $descriptor 'Parameters' @{})
                $null=Get-WinCareWindowByIdentity -Handle ([long]$before.WindowHandle) -ProcessId ([int]$before.ProcessId) -ProcessStartTime ([string]$before.ProcessStartTime) -ExpectedTitleHash ([string]$before.ExpectedTitleHash)
                if(-not[WinCare.Native.Productivity]::SetTopmost([long]$before.WindowHandle,[bool]$before.Topmost)){throw 'Windows rejected the recovery topmost state.'}
                Start-Sleep -Milliseconds 100
                $restored=Get-WinCareWindowInventory -IncludeUntitled -IncludeCloaked|Where-Object Handle -eq [long]$before.WindowHandle|Select-Object -First 1
                if(-not $restored -or [bool]$restored.Topmost -ne [bool]$before.Topmost){throw 'Recovered topmost state could not be verified.'}
                $warnings+='The previous topmost state was restored and verified after the failed update.'
            }catch{$warnings+="The failed topmost update could not restore its prior state: $($_.Exception.Message)"}
        }
        New-WinCareResult -Success $false -Message "Window topmost update failed: $failure" -Warnings $warnings -ExitCode 1
    }
}

function Invoke-WinCareActivateWindowAction {
    param([object]$Action)
    if(-not(Initialize-WinCareProductivityInterop)){return New-WinCareResult -Success $false -Message 'Windows productivity interop is unavailable.' -ExitCode 2}
    $p=$Action.Parameters;$window=Get-WinCareWindowByIdentity -Handle ([long]$p.WindowHandle) -ProcessId ([int]$p.ProcessId) -ProcessStartTime ([string]$p.ProcessStartTime) -ExpectedTitleHash ([string]$p.ExpectedTitleHash)
    $ok=[WinCare.Native.Productivity]::Activate([long]$p.WindowHandle)
    New-WinCareResult -Success $ok -Message $(if($ok){'Window activation requested.'}else{'Windows refused foreground activation.'}) -Data $window
}

function Invoke-WinCareReleaseLocalInputAction {
    param([object]$Action)
    if(-not(Initialize-WinCareProductivityInterop)){return New-WinCareResult -Success $false -Message 'Windows productivity interop is unavailable.' -ExitCode 2}
    $ok=[WinCare.Native.Productivity]::ReleaseLocalInput()
    New-WinCareResult -Success $ok -Message $(if($ok){'Cursor clipping and common modifier states were released.'}else{'Input release completed only partially.'}) -Warnings $(if($ok){@()}else{@('Some synthetic key-up events may have been rejected by Windows integrity boundaries.')})
}


