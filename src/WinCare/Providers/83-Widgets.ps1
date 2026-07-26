function Get-WinCareWidgetCatalogPath { Join-Path $script:WinCareModuleRoot 'Data\Catalog\widgets.json' }

function Test-WinCareWidgetDefinition {
    [CmdletBinding()]param([Parameter(Mandatory)][Collections.IDictionary]$Widget)
    $null=Test-WinCareStrictObjectKeys -InputObject $Widget -AllowedKeys @('id','title','description','provider','minimumRefreshSeconds','sourceRecords') -Context 'widget definition'
    foreach($required in @('id','title','description','provider','minimumRefreshSeconds','sourceRecords')){if(-not $Widget.ContainsKey($required)){throw "Widget definition is missing $required."}}
    if([string]$Widget.id -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$'){throw 'Widget ID is invalid.'}
    if([string]::IsNullOrWhiteSpace([string]$Widget.title) -or ([string]$Widget.title).Length -gt 120){throw "Widget title is invalid: $($Widget.id)"}
    if(([string]$Widget.description).Length -gt 500){throw "Widget description is too long: $($Widget.id)"}
    $providers=@('SystemHealth','StoragePressure','MaintenanceState','UpdateStatus','SecurityPosture','BluetoothDevices','ProcessPressure','NetworkStatus')
    if([string]$Widget.provider -notin $providers){throw "Widget provider is not approved: $($Widget.provider)"}
    if([int]$Widget.minimumRefreshSeconds -lt 1 -or [int]$Widget.minimumRefreshSeconds -gt 3600){throw "Widget refresh interval is invalid: $($Widget.id)"}
    if($Widget.sourceRecords -is [string] -or $Widget.sourceRecords -isnot [Collections.IEnumerable]){throw "Widget sourceRecords must be an array: $($Widget.id)"}
    $records=@($Widget.sourceRecords);if($records.Count -lt 1 -or $records.Count -gt 32){throw "Widget source record list is invalid: $($Widget.id)"}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($record in $records){if([string]$record -notmatch '^[A-Za-z0-9._:-]{2,160}$' -or -not $seen.Add([string]$record)){throw "Widget source record is invalid or duplicated: $record"}}
    return $true
}

function Get-WinCareWidgetDefinition {
    [CmdletBinding()]param([string]$Id)
    $document=Read-WinCareJsonHashtable -LiteralPath (Get-WinCareWidgetCatalogPath)
    $null=Test-WinCareStrictObjectKeys -InputObject $document -AllowedKeys @('schemaVersion','widgets') -Context 'widget catalog'
    if([int]$document.schemaVersion -ne 1){throw 'Unsupported widget catalog schema.'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($widget in @($document.widgets)){$null=Test-WinCareWidgetDefinition -Widget $widget;if(-not $seen.Add([string]$widget.id)){throw "Duplicate widget ID: $($widget.id)"}}
    if($Id){return @($document.widgets|Where-Object id -eq $Id|Select-Object -First 1)}
    return @($document.widgets)
}

function Get-WinCareConfiguredWidget {
    [CmdletBinding()]param()
    $definitions=@(Get-WinCareWidgetDefinition);$byId=@{};foreach($item in $definitions){$byId[[string]$item.id]=$item}
    $result=[Collections.Generic.List[object]]::new();$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($id in @(Get-WinCareConfig 'WidgetLayout')){if($byId.ContainsKey([string]$id) -and $seen.Add([string]$id)){$result.Add($byId[[string]$id])}}
    return @($result)
}

function New-WinCareWidgetResult {
    param([Parameter(Mandatory)][object]$Definition,[Parameter(Mandatory)][string]$Status,[Parameter(Mandatory)][string]$Summary,[object]$Data,[string[]]$Warnings=@())
    if($Status.Length -gt 80){$Status=$Status.Substring(0,80)}
    if($Summary.Length -gt 2000){$Summary=$Summary.Substring(0,2000)}
    $boundedWarnings=@($Warnings|ForEach-Object{$text=[string]$_;if($text.Length -gt 2000){$text.Substring(0,2000)}else{$text}}|Select-Object -First 32)
    [pscustomobject][ordered]@{Id=[string]$Definition.id;Title=[string]$Definition.title;Description=[string]$Definition.description;Provider=[string]$Definition.provider;Status=$Status;Summary=$Summary;CapturedAt=[datetime]::UtcNow.ToString('o');Data=$Data;Warnings=$boundedWarnings;SourceRecords=@($Definition.sourceRecords)}
}

function Get-WinCareWidgetSnapshot {
    [CmdletBinding()]param([string[]]$Id)
    if($Id){
        $definitions=[Collections.Generic.List[object]]::new();$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($requested in @($Id)){if(-not $seen.Add([string]$requested)){continue};$definition=Get-WinCareWidgetDefinition -Id ([string]$requested)|Select-Object -First 1;if(-not $definition){throw "Unknown widget ID: $requested"};$definitions.Add($definition)}
        $definitions=@($definitions)
    }else{$definitions=@(Get-WinCareConfiguredWidget)}
    foreach($definition in $definitions){
        try{
            switch([string]$definition.provider){
                'SystemHealth'{
                    $summary=Get-WinCareSystemSummary;$findings=@(Get-WinCareHealthFinding);$critical=@($findings|Where-Object Severity -in @('Critical','High')).Count
                    $status=if($critical){'Warning'}elseif([bool]$summary.PendingRestart){'Notice'}else{'Healthy'}
                    New-WinCareWidgetResult -Definition $definition -Status $status -Summary "$($findings.Count) finding(s); restart=$($summary.PendingRestart)" -Data @{ComputerName=$summary.ComputerName;Windows=$summary.Windows;Build=$summary.Build;Uptime=$summary.Uptime;MemoryUsed=$summary.MemoryUsed;MemoryTotal=$summary.MemoryTotal;PendingRestart=$summary.PendingRestart;FindingCount=$findings.Count;CriticalFindingCount=$critical}
                }
                'StoragePressure'{
                    $drives=@(Get-WinCareDriveSummary);$highest=@($drives|Sort-Object UsedPercent -Descending|Select-Object -First 1);$status=if($highest.UsedPercent -ge 95){'Critical'}elseif($highest.UsedPercent -ge 85){'Warning'}else{'Healthy'}
                    New-WinCareWidgetResult -Definition $definition -Status $status -Summary $(if($highest){"$($highest.DeviceId) $($highest.UsedPercent)% used"}else{'No fixed drive data'}) -Data @{Drives=$drives}
                }
                'MaintenanceState'{
                    $windows=@(Get-WinCareMaintenanceWindow);$activeAll=@($windows|Where-Object EffectiveState -eq 'Active');$active=@($activeAll|Sort-Object StartAt|Select-Object -First 50);$upcoming=@($windows|Where-Object EffectiveState -in @('Scheduled','Notice')|Sort-Object StartAt|Select-Object -First 3)
                    $status=if($activeAll.Count){'Active'}elseif(@($upcoming).Count){'Scheduled'}else{'Idle'}
                    New-WinCareWidgetResult -Definition $definition -Status $status -Summary "$($activeAll.Count) active; $(@($upcoming).Count) upcoming" -Data @{Active=$active;ActiveCount=$activeAll.Count;Upcoming=$upcoming;Total=$windows.Count;Truncated=$activeAll.Count -gt $active.Count}
                }
                'UpdateStatus'{
                    $updates=@(Get-WinCareWuaUpdate -Criteria 'IsInstalled=0 and IsHidden=0' -Limit 100);$restart=[bool](Get-WinCarePendingRestart);$status=if($restart){'RestartRequired'}elseif($updates.Count){'UpdatesAvailable'}else{'Current'}
                    New-WinCareWidgetResult -Definition $definition -Status $status -Summary "$($updates.Count) update(s); restart=$restart" -Data @{Count=$updates.Count;RestartRequired=$restart;Updates=@($updates|Select-Object -First 20 Title,UpdateId,IsDownloaded,RebootRequired)}
                }
                'SecurityPosture'{
                    $security=Get-WinCareSecuritySummary;$warnings=[Collections.Generic.List[string]]::new()
                    foreach($name in @('Defender','Firewall','SecureBoot','Tpm')){if($security.PSObject.Properties[$name] -and [string]$security.$name -match '(?i)disabled|off|false|unavailable|not ready'){$warnings.Add("${name}: $($security.$name)")}}
                    New-WinCareWidgetResult -Definition $definition -Status $(if($warnings.Count){'Warning'}else{'Healthy'}) -Summary $(if($warnings.Count){$warnings -join '; '}else{'Core Windows security controls report healthy'}) -Data $security -Warnings @($warnings)
                }
                'BluetoothDevices'{
                    $devices=@(Get-WinCareBluetoothDevice);$connected=@($devices|Where-Object Connected);$battery=@($connected|Where-Object{$null -ne $_.BatteryPercent})
                    New-WinCareWidgetResult -Definition $definition -Status $(if($connected.Count){'Connected'}else{'Idle'}) -Summary "$($connected.Count) connected; $($battery.Count) battery reading(s)" -Data @{Devices=$devices;ConnectedCount=$connected.Count}
                }
                'ProcessPressure'{
                    $processes=@(Get-WinCareProcessInternals -Top 8);$top=@($processes|Select-Object -First 1)
                    New-WinCareWidgetResult -Definition $definition -Status 'Observed' -Summary $(if($top){"$($top.Name) PID $($top.Id)"}else{'No process snapshot'}) -Data @{Processes=$processes}
                }
                'NetworkStatus'{
                    $network=@(Get-WinCareNetworkSummary);$up=@($network|Where-Object Status -match '(?i)up|connected');$connections=try{@(Get-NetTCPConnection -State Established -ErrorAction Stop).Count}catch{Write-Verbose "TCP connection inventory unavailable: $($_.Exception.Message)";0}
                    New-WinCareWidgetResult -Definition $definition -Status $(if($up.Count){'Connected'}else{'Offline'}) -Summary "$($up.Count) operational adapter(s); $connections established TCP connection(s)" -Data @{Adapters=$network;EstablishedConnections=$connections}
                }
            }
        }catch{
            New-WinCareWidgetResult -Definition $definition -Status 'Unavailable' -Summary $_.Exception.Message -Data $null -Warnings @($_.Exception.Message)
        }
    }
}

function New-WinCareWidgetSnapshotExportPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Path,[string[]]$WidgetId)
    Ensure-WinCareState
    $root=[IO.Path]::GetFullPath((Join-Path $script:WinCareState.Root 'Widgets'))
    $full=[IO.Path]::GetFullPath($Path)
    if(-not(Test-WinCarePathWithin -Child $full -Parent $root -AllowEqual)){throw "Widget exports must remain under $root"}
    $snapshot=[ordered]@{SchemaVersion=1;GeneratedAt=[datetime]::UtcNow.ToString('o');WinCareVersion=$script:WinCareVersion;Widgets=@(Get-WinCareWidgetSnapshot -Id $WidgetId)}
    $content=[Text.Encoding]::UTF8.GetBytes((ConvertTo-WinCareCanonicalJson -InputObject $snapshot -Depth 40))
    if($content.Length -gt 1048576){throw 'Widget snapshot exceeds the 1 MiB managed export limit.'}
    $plan=New-WinCarePlan -Title 'Export widget snapshot' -Description 'Write a bounded, local dashboard snapshot using exact-content file semantics.' -SourceRecords @('SRC:372822dad9') -Metadata @{Path=$full}
    $null=Add-WinCarePlanAction -Plan $plan -Action (New-WinCareManagedFileWriteAction -Path $full -Content $content -AllowedRoots @($root) -Label 'Write widget snapshot' -SourceRecords @('SRC:372822dad9'))
    return $plan
}


