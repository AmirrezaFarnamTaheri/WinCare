function Get-WinCareWorkspaceLayoutRoot { Ensure-WinCareState; Get-WinCareBridgeStateRoot 'WorkspaceLayouts' }
function Get-WinCareWorkspaceLayoutStorePath { Join-Path (Get-WinCareWorkspaceLayoutRoot) 'layouts.json' }
function Get-WinCareDefaultWorkspaceLayoutStore { [ordered]@{SchemaVersion=1;Revision=0;UpdatedAt='1970-01-01T00:00:00.0000000Z';Layouts=@()} }
function Get-WinCareWorkspaceLayoutStoreHash {param([object]$Store);Get-WinCareCanonicalObjectHash $Store}

function New-WinCareWorkspaceTitleRegex {
    param([AllowEmptyString()][string]$Pattern)
    if([string]::IsNullOrWhiteSpace($Pattern)){return $null}
    if($Pattern.Length -gt 200){throw 'Workspace slot title pattern is too long.'}
    try{[regex]::new($Pattern,[Text.RegularExpressions.RegexOptions]::CultureInvariant,[timespan]::FromMilliseconds(100))}
    catch{throw "Workspace title pattern is invalid or unsafe: $($_.Exception.Message)"}
}

function Test-WinCareWorkspaceTitleMatch {
    param([AllowEmptyString()][string]$Pattern,[AllowEmptyString()][string]$Title)
    $regex=New-WinCareWorkspaceTitleRegex $Pattern
    if($null -eq $regex){return $true}
    try{$regex.IsMatch($Title)}catch [System.Text.RegularExpressions.RegexMatchTimeoutException]{throw 'Workspace title matching exceeded the 100 ms safety limit.'}
}

function Test-WinCareWorkspaceLayoutRecord {
    param([Parameter(Mandatory)][object]$Layout)
    $map=ConvertTo-WinCareParameterDictionary $Layout
    $null=Test-WinCareStrictObjectKeys -InputObject $map -AllowedKeys @('SchemaVersion','Id','Name','Description','CreatedAt','UpdatedAt','Slots','SourceRecords') -Context 'workspace layout'
    if([int]$map.SchemaVersion -ne 1 -or [string]$map.Id -notmatch '^[a-f0-9]{32}$'){throw 'Workspace layout identity is invalid.'}
    if([string]::IsNullOrWhiteSpace([string]$map.Name) -or ([string]$map.Name).Length -gt 120){throw 'Workspace layout name is invalid.'}
    if(([string]$map.Description).Length -gt 500){throw 'Workspace layout description is too long.'}
    $slots=@($map.Slots);if($slots.Count -lt 1 -or $slots.Count -gt 32){throw 'Workspace layout must contain 1..32 slots.'}
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($slotValue in $slots){
        $slot=ConvertTo-WinCareParameterDictionary $slotValue
        $null=Test-WinCareStrictObjectKeys -InputObject $slot -AllowedKeys @('Id','ProcessName','TitlePattern','MonitorDevice','LeftRatio','TopRatio','WidthRatio','HeightRatio','Required') -Context 'workspace layout slot'
        if([string]$slot.Id -notmatch '^[a-f0-9]{16}$' -or -not $ids.Add([string]$slot.Id)){throw 'Workspace slot identity is invalid or duplicated.'}
        if([string]::IsNullOrWhiteSpace([string]$slot.ProcessName) -or [string]$slot.ProcessName -notmatch '^[A-Za-z0-9_.-]{1,128}$'){throw 'Workspace slot process name is invalid.'}
        $null=New-WinCareWorkspaceTitleRegex ([string]$slot.TitlePattern)
        foreach($name in @('LeftRatio','TopRatio','WidthRatio','HeightRatio')){if([double]$slot[$name] -lt 0 -or [double]$slot[$name] -gt 1){throw "Workspace slot ratio is invalid: $name"}}
        if([double]$slot.WidthRatio -le 0 -or [double]$slot.HeightRatio -le 0 -or ([double]$slot.LeftRatio+[double]$slot.WidthRatio) -gt 1.000001 -or ([double]$slot.TopRatio+[double]$slot.HeightRatio) -gt 1.000001){throw 'Workspace slot rectangle exceeds the monitor work area.'}
        if($slot.Required -isnot [bool]){throw 'Workspace slot Required must be Boolean.'}
    }
    $true
}

function Test-WinCareWorkspaceLayoutStore {
    param([Parameter(Mandatory)][object]$Store)
    $map=ConvertTo-WinCareParameterDictionary $Store
    $null=Test-WinCareStrictObjectKeys -InputObject $map -AllowedKeys @('SchemaVersion','Revision','UpdatedAt','Layouts') -Context 'workspace layout store'
    if([int]$map.SchemaVersion -ne 1 -or [long]$map.Revision -lt 0){throw 'Workspace layout store schema or revision is invalid.'}
    $layouts=@($map.Layouts);if($layouts.Count -gt [int](Get-WinCareConfig 'WorkspaceLayoutLimit')){throw 'Workspace layout count exceeds configuration.'}
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($layout in $layouts){$null=Test-WinCareWorkspaceLayoutRecord $layout;if(-not $ids.Add([string]$layout.Id)){throw 'Workspace layout IDs must be unique.'}}
    $true
}

function Get-WinCareWorkspaceLayoutStore {
    [CmdletBinding()]param([switch]$Strict)
    $path=Get-WinCareWorkspaceLayoutStorePath;if(-not(Test-Path -LiteralPath $path)){return Get-WinCareDefaultWorkspaceLayoutStore}
    try{$store=Read-WinCareProtectedJson -LiteralPath $path -Purpose 'WinCare.WorkspaceLayouts' -AsHashtable;$null=Test-WinCareWorkspaceLayoutStore $store;$store}catch{if($Strict){throw};Write-WinCareLog -Level Warning -Message 'Workspace layout store is invalid.' -Data @{error=$_.Exception.Message};Get-WinCareDefaultWorkspaceLayoutStore}
}
function Set-WinCareWorkspaceLayoutStoreInternal {param([object]$Store);$map=ConvertTo-WinCareParameterDictionary $Store;$null=Test-WinCareWorkspaceLayoutStore $map;Write-WinCareProtectedJson -LiteralPath (Get-WinCareWorkspaceLayoutStorePath) -InputObject $map -Purpose 'WinCare.WorkspaceLayouts';$map}
function Get-WinCareWorkspaceLayout {[CmdletBinding()]param([string]$Id='');$layouts=@((Get-WinCareWorkspaceLayoutStore).Layouts);if($Id){return $layouts|Where-Object Id -eq $Id};$layouts}

function New-WinCareWorkspaceLayoutRecord {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Name,[string]$Description='',[Parameter(Mandatory)][object[]]$Slot,[string]$Id='')
    if(-not $Id){$Id=[guid]::NewGuid().ToString('N')};$now=[datetime]::UtcNow.ToString('o')
    $slots=@($Slot|ForEach-Object{$map=ConvertTo-WinCareParameterDictionary $_;[ordered]@{Id=if($map.Id){[string]$map.Id}else{[guid]::NewGuid().ToString('N').Substring(0,16)};ProcessName=[string]$map.ProcessName;TitlePattern=[string](Get-WinCarePropertyValue $map 'TitlePattern' '');MonitorDevice=[string](Get-WinCarePropertyValue $map 'MonitorDevice' '');LeftRatio=[double]$map.LeftRatio;TopRatio=[double]$map.TopRatio;WidthRatio=[double]$map.WidthRatio;HeightRatio=[double]$map.HeightRatio;Required=[bool](Get-WinCarePropertyValue $map 'Required' $true)}})
    $record=[ordered]@{SchemaVersion=1;Id=$Id;Name=$Name;Description=$Description;CreatedAt=$now;UpdatedAt=$now;Slots=$slots;SourceRecords=@('SRC:62fff6504d')};$null=Test-WinCareWorkspaceLayoutRecord $record;[pscustomobject]$record
}

function New-WinCareWorkspaceLayoutSavePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Layout)
    $null=Test-WinCareWorkspaceLayoutRecord $Layout;$store=Get-WinCareWorkspaceLayoutStore -Strict;$before=(ConvertTo-WinCareCanonicalJson -InputObject $store -Depth 40|ConvertFrom-Json -AsHashtable -Depth 40)
    $layouts=[Collections.Generic.List[object]]::new();$replaced=$false
    foreach($item in @($store.Layouts)){if([string]$item.Id -eq [string]$Layout.Id){$record=ConvertTo-WinCareParameterDictionary $Layout;$record.CreatedAt=[string]$item.CreatedAt;$record.UpdatedAt=[datetime]::UtcNow.ToString('o');$layouts.Add($record);$replaced=$true}else{$layouts.Add($item)}}
    if(-not $replaced){$layouts.Add($Layout)}
    $after=[ordered]@{SchemaVersion=1;Revision=[long]$store.Revision+1;UpdatedAt=[datetime]::UtcNow.ToString('o');Layouts=@($layouts)};$null=Test-WinCareWorkspaceLayoutStore $after
    $action=New-WinCareAction -Type ReplaceWorkspaceLayoutStore -Label "Save workspace layout: $($Layout.Name)" -Risk Low -Parameters @{Store=$after;ExpectedCurrentHash=Get-WinCareWorkspaceLayoutStoreHash $store} -Reversible $true -Compensator @{Type='ReplaceWorkspaceLayoutStore';RequiresAdmin=$false;Parameters=@{Store=$before;ExpectedCurrentHash=Get-WinCareWorkspaceLayoutStoreHash $after}} -Tags @('Windows','Layouts','CAS') -SourceRecords @('SRC:62fff6504d') -RecoveryDescription 'Restore the exact previous layout catalog.'
    New-WinCarePlan -Title 'Save workspace layout' -Actions @($action) -SourceRecords @('SRC:62fff6504d')
}

function New-WinCareWorkspaceLayoutRemovePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $store=Get-WinCareWorkspaceLayoutStore -Strict;$record=@($store.Layouts)|Where-Object Id -eq $Id|Select-Object -First 1;if(-not $record){throw 'Workspace layout was not found.'};$before=(ConvertTo-WinCareCanonicalJson -InputObject $store -Depth 40|ConvertFrom-Json -AsHashtable -Depth 40)
    $after=[ordered]@{SchemaVersion=1;Revision=[long]$store.Revision+1;UpdatedAt=[datetime]::UtcNow.ToString('o');Layouts=@($store.Layouts|Where-Object Id -ne $Id)}
    $action=New-WinCareAction -Type ReplaceWorkspaceLayoutStore -Label "Remove workspace layout: $($record.Name)" -Risk Moderate -Parameters @{Store=$after;ExpectedCurrentHash=Get-WinCareWorkspaceLayoutStoreHash $store} -Reversible $true -Compensator @{Type='ReplaceWorkspaceLayoutStore';RequiresAdmin=$false;Parameters=@{Store=$before;ExpectedCurrentHash=Get-WinCareWorkspaceLayoutStoreHash $after}} -Tags @('Windows','Layouts','CAS') -SourceRecords @('SRC:62fff6504d') -RecoveryDescription 'Restore the exact removed layout.'
    New-WinCarePlan -Title 'Remove workspace layout' -Actions @($action) -SourceRecords @('SRC:62fff6504d')
}

function New-WinCareWorkspaceLayoutApplyPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $layout=Get-WinCareWorkspaceLayout -Id $Id|Select-Object -First 1;if(-not $layout){throw 'Workspace layout was not found.'}
    $windows=@(Get-WinCareWindowInventory -IncludeUntitled);$monitors=@(Get-WinCareMonitorInventory);$actions=[Collections.Generic.List[object]]::new();$matched=[Collections.Generic.HashSet[long]]::new()
    foreach($slot in @($layout.Slots)){
        $candidate=$windows|Where-Object{$_.ProcessName -eq [string]$slot.ProcessName -and -not $matched.Contains([long]$_.Handle) -and (Test-WinCareWorkspaceTitleMatch -Pattern ([string]$slot.TitlePattern) -Title ([string]$_.Title))}|Select-Object -First 1
        if(-not $candidate){if([bool]$slot.Required){throw "Required layout window is not available: $($slot.ProcessName)"};continue}
        $monitor=if([string]$slot.MonitorDevice){$monitors|Where-Object DeviceName -eq [string]$slot.MonitorDevice|Select-Object -First 1}else{$monitors|Where-Object Primary|Select-Object -First 1};if(-not $monitor){$monitor=$monitors|Select-Object -First 1};if(-not $monitor){throw 'No monitor work area is available.'}
        $left=[int][math]::Round([int]$monitor.WorkLeft+[int]$monitor.WorkWidth*[double]$slot.LeftRatio);$top=[int][math]::Round([int]$monitor.WorkTop+[int]$monitor.WorkHeight*[double]$slot.TopRatio);$width=[int][math]::Max(100,[math]::Round([int]$monitor.WorkWidth*[double]$slot.WidthRatio));$height=[int][math]::Max(100,[math]::Round([int]$monitor.WorkHeight*[double]$slot.HeightRatio))
        $comp=@{Type='SetWindowBounds';RequiresAdmin=$false;Parameters=@{WindowHandle=[long]$candidate.Handle;ProcessId=[int]$candidate.ProcessId;ProcessStartTime=[string]$candidate.ProcessStartTime;ExpectedTitleHash=[string]$candidate.TitleHash;ExpectedCurrentLeft=$left;ExpectedCurrentTop=$top;ExpectedCurrentWidth=$width;ExpectedCurrentHeight=$height;Left=[int]$candidate.Left;Top=[int]$candidate.Top;Width=[int]$candidate.Width;Height=[int]$candidate.Height}}
        $actions.Add((New-WinCareAction -Type SetWindowBounds -Label "Place $($candidate.ProcessName) in layout $($layout.Name)" -Risk Low -Parameters @{WindowHandle=[long]$candidate.Handle;ProcessId=[int]$candidate.ProcessId;ProcessStartTime=[string]$candidate.ProcessStartTime;ExpectedTitleHash=[string]$candidate.TitleHash;ExpectedCurrentLeft=[int]$candidate.Left;ExpectedCurrentTop=[int]$candidate.Top;ExpectedCurrentWidth=[int]$candidate.Width;ExpectedCurrentHeight=[int]$candidate.Height;Left=$left;Top=$top;Width=$width;Height=$height;MonitorDevice=[string]$monitor.DeviceName;ExpectedMonitorWorkLeft=[int]$monitor.WorkLeft;ExpectedMonitorWorkTop=[int]$monitor.WorkTop;ExpectedMonitorWorkWidth=[int]$monitor.WorkWidth;ExpectedMonitorWorkHeight=[int]$monitor.WorkHeight} -Reversible $true -Compensator $comp -Tags @('Windows','Layouts','Bounds') -SourceRecords @($layout.SourceRecords) -RecoveryDescription 'Restore the exact prior window rectangle.'))
        $null=$matched.Add([long]$candidate.Handle)
    }
    New-WinCarePlan -Title "Apply workspace layout: $($layout.Name)" -Description 'Match current windows, bind exact process/window identities, and apply monitor-relative rectangles.' -Actions @($actions) -SourceRecords @($layout.SourceRecords)
}

function Invoke-WinCareReplaceWorkspaceLayoutStoreAction {
    param([Parameter(Mandatory)][object]$Action)
    try{$current=Get-WinCareWorkspaceLayoutStore -Strict;if((Get-WinCareWorkspaceLayoutStoreHash $current) -ne [string]$Action.Parameters.ExpectedCurrentHash){return New-WinCareResult -Success $false -Status Blocked -Code 'LayoutStoreChanged' -Message 'Workspace layout catalog changed after preview.' -ExitCode 74};$after=Set-WinCareWorkspaceLayoutStoreInternal $Action.Parameters.Store;New-WinCareResult -Success $true -Code 'LayoutStoreReplaced' -Message 'Workspace layout catalog updated and verified.' -Data $after}catch{New-WinCareResult -Success $false -Code 'LayoutStoreUpdateFailed' -Message $_.Exception.Message -ExitCode 1}
}



