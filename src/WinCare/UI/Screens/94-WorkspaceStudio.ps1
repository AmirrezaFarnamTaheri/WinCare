function Format-WinCareWorkspaceStudioProfileRow {
    param($Item,$Width)
    "$(Limit-WinCareText $Item.Id 20)  $(Limit-WinCareText $Item.Name ([math]::Max(20,$Width-24)))"
}

function Show-WinCareWorkspaceStudioScreen {
    while($true){
        $menu=@(
            [pscustomobject]@{Label='Unified system snapshot';Description='Telemetry, top processes, service states, and TCP state counts';Action='Snapshot'},
            [pscustomobject]@{Label='Monitoring templates';Description='Generate local-only Telegraf inputs and Grafana dashboard JSON';Action='Monitoring'},
            [pscustomobject]@{Label='File workspaces';Description='Persist bounded tab roots, bookmarks, and search patterns';Action='Files'},
            [pscustomobject]@{Label='Workspace layout presets';Description='Columns, rows, main-stack, and docked-side layouts';Action='Layouts'},
            [pscustomobject]@{Label='Package archive metadata';Description='Inspect APK, IPA, AppX, MSIX, or ZIP without extraction or execution';Action='Package'},
            [pscustomobject]@{Label='Brightness schedules';Description='Store and apply time-based native brightness steps';Action='Brightness'},
            [pscustomobject]@{Label='Folder appearance';Description='Apply desktop.ini icon metadata with exact recovery';Action='Folder'},
            [pscustomobject]@{Label='Kanata configuration admission';Description='Validate layers and confined includes without starting input hooks';Action='Kanata'},
            [pscustomobject]@{Label='Syncthing state';Description='Read local folders, devices, GUI address, and process state with API key redacted';Action='Syncthing'},
            [pscustomobject]@{Label='WezTerm status module';Description='Generate an offline workspace, host, cwd, and clock status module';Action='WezTerm'},
            [pscustomobject]@{Label='Verified ADB inventory';Description='Run exact-hash adb devices -l through the typed tool boundary';Action='Adb'},
            [pscustomobject]@{Label='Xbox Full Screen Experience';Description='Critical hidden-feature and DeviceForm one-click mutation';Action='Xbox'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Workspace and device studio' -Subtitle 'Monitoring, file, layout, display, device, and workflow operations' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'Snapshot'{
                    $s=Get-WinCareUnifiedSystemSnapshot
                    $lines=@("Captured: $($s.CapturedAt)",'',"Top processes: $(@($s.Processes).Count)")+@($s.Processes|ForEach-Object{"$($_.Name) pid=$($_.Id) cpu=$($_.CpuSeconds)s memory=$(Format-WinCareBytes $_.WorkingSetBytes) threads=$($_.Threads)"})+@('','Services:')+@($s.Services|ForEach-Object{"$($_.State): $($_.Count)"})+@('','TCP connections:')+@($s.TcpConnections|ForEach-Object{"$($_.State): $($_.Count)"})
                    Show-WinCareTextPage -Title 'Unified system snapshot' -Lines $lines
                }
                'Monitoring'{Invoke-WinCarePlanFromUi (New-WinCareMonitoringExportPlan)}
                'Files'{
                    $records=@(Get-WinCareFileWorkspace);$lines=if($records.Count){@($records|ForEach-Object{"$($_.Name) [$($_.Id)] roots=$(@($_.Paths).Count) bookmarks=$(@($_.Bookmarks).Count)"})}else{@('No file workspaces exist.')};Show-WinCareTextPage -Title 'File workspaces' -Lines $lines
                    if((Read-WinCareInput -Prompt 'Create or update a workspace? (y/N)' -Default 'N') -match '^(?i)y(es)?$'){$name=Read-WinCareInput -Prompt 'Workspace name';$paths=(Read-WinCareInput -Prompt 'Folder paths separated by semicolons') -split ';'|Where-Object{$_};$bookmarks=(Read-WinCareInput -Prompt 'Optional bookmarks separated by semicolons' -Default '') -split ';'|Where-Object{$_};$pattern=Read-WinCareInput -Prompt 'Optional search pattern' -Default '';Invoke-WinCarePlanFromUi (New-WinCareFileWorkspacePlan -Name $name -Path $paths -Bookmark $bookmarks -SearchPattern $pattern)}
                }
                'Layouts'{
                    $profile=Show-WinCareListSelector -Title 'Workspace Studio layouts' -Subtitle 'Choose a governed workspace layout' -Items @(Get-WinCareWorkspaceStudioProfile) -Formatter ${function:Format-WinCareWorkspaceStudioProfileRow} -SearchProperties @('Id','Name')
                    if($profile){$processes=(Read-WinCareInput -Prompt "Enter $(@($profile.Ratios).Count) process names separated by commas") -split ','|ForEach-Object{$_.Trim()}|Where-Object{$_};Invoke-WinCarePlanFromUi (New-WinCareWorkspaceStudioLayoutPlan -ProfileId $profile.Id -ProcessName $processes)}
                }
                'Package'{
                    $m=Get-WinCarePackageArchiveMetadata -LiteralPath (Read-WinCareInput -Prompt 'Package archive path')
                    Show-WinCareTextPage -Title 'Package archive metadata' -Lines @("Kind: $($m.Kind)","Path: $($m.Path)","SHA-256: $($m.Sha256)","Archive bytes: $(Format-WinCareBytes $m.ArchiveBytes)","Expanded bytes: $(Format-WinCareBytes $m.ExpandedBytes)","Entries: $($m.EntryCount)","Compression ratio: $($m.CompressionRatio)","Executed: $($m.Executed)","Extracted: $($m.Extracted)",'',"Manifest entries: $(@($m.Manifests.Keys)-join ', ')")
                }
                'Brightness'{
                    $records=@(Get-WinCareBrightnessSchedule);$lines=if($records.Count){@($records|ForEach-Object{"$($_.Name) [$($_.Id)] steps=$(@($_.Steps).Count)"})}else{@('No brightness schedules exist.')};Show-WinCareTextPage -Title 'Brightness schedules' -Lines $lines
                    $mode=Read-WinCareInput -Prompt 'Enter CREATE, APPLY, or blank' -Default ''
                    if($mode -match '^(?i)create$'){$name=Read-WinCareInput -Prompt 'Schedule name';$raw=(Read-WinCareInput -Prompt 'Steps like 08:00=75,18:30=45') -split ',';$steps=@($raw|ForEach-Object{$pair=$_ -split '=',2;[ordered]@{Time=$pair[0].Trim();Percent=[int]$pair[1]}});Invoke-WinCarePlanFromUi (New-WinCareBrightnessSchedulePlan -Name $name -Step $steps)}
                    elseif($mode -match '^(?i)apply$'){Invoke-WinCarePlanFromUi (New-WinCareBrightnessScheduleApplyPlan -Id (Read-WinCareInput -Prompt 'Schedule ID'))}
                }
                'Folder'{Invoke-WinCarePlanFromUi (New-WinCareFolderAppearancePlan -LiteralPath (Read-WinCareInput -Prompt 'Folder path') -IconResource (Read-WinCareInput -Prompt 'Icon resource path and optional index'))}
                'Kanata'{$r=Test-WinCareKanataConfiguration -LiteralPath (Read-WinCareInput -Prompt 'Kanata configuration path');Show-WinCareTextPage -Title 'Kanata configuration admission' -Lines @("Path: $($r.Path)","SHA-256: $($r.Sha256)","Admitted: $($r.Admitted)","Has defcfg: $($r.HasDefcfg)","Has defsrc: $($r.HasDefsrc)","Layers: $($r.LayerCount)","Includes: $(@($r.Includes)-join ', ')","Unsafe includes: $(@($r.UnsafeIncludes)-join ', ')",'Executed: false')}
                'Syncthing'{$r=Get-WinCareSyncthingState;Show-WinCareTextPage -Title 'Syncthing state' -Lines @("Config: $($r.ConfigPath)","Present: $($r.ConfigPresent)","GUI: $($r.GuiAddress)","Folders: $(@($r.Folders).Count)","Devices: $(@($r.Devices).Count)","Processes: $(@($r.Processes).Count)",'API key: redacted')}
                'WezTerm'{Invoke-WinCarePlanFromUi (New-WinCareWezTermStatusBarPlan)}
                'Adb'{Invoke-WinCarePlanFromUi (New-WinCareAdbInventoryPlan -AdbPath (Read-WinCareInput -Prompt 'adb.exe path') -Sha256 (Read-WinCareInput -Prompt 'Expected SHA-256'))}
                'Xbox'{
                    $enable=(Read-WinCareInput -Prompt 'Enable Xbox Full Screen Experience? (Y/n)' -Default 'Y') -notmatch '^(?i)n(o)?$'
                    $basic=(Read-WinCareInput -Prompt 'Use basic two-feature set? (y/N)' -Default 'N') -match '^(?i)y(es)?$'
                    Invoke-WinCarePlanFromUi (New-WinCareXboxFullscreenExperiencePlan -ViVeToolPath (Read-WinCareInput -Prompt 'ViVeTool executable path') -Sha256 (Read-WinCareInput -Prompt 'Expected SHA-256') -Enabled $enable -BasicFeatureSet:$basic)
                }
            }
        }catch{Show-WinCareMessage -Title 'Workspace Studio failed' -Lines @($_.Exception.Message) -Kind Danger}
    }
}

