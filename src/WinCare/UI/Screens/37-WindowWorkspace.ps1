function Format-WinCareWindowRow {
    param($Item,$Width)
    $flag=if($Item.Topmost){'TOP'}else{''}
    "$(Limit-WinCareText $Item.ProcessName 18) $(Limit-WinCareText $Item.Title ([math]::Max(24,$Width-52))) $($Item.Width)x$($Item.Height) $flag"
}
function Format-WinCareMonitorRow {
    param($Item,$Width)
    $flag=if($Item.Primary){'PRIMARY'}else{''}
    "$(Limit-WinCareText $Item.DeviceName 24) $($Item.WorkWidth)x$($Item.WorkHeight) @ $($Item.WorkLeft),$($Item.WorkTop) $flag"
}
function Format-WinCareZoneRow {param($Item,$Width);"$(Limit-WinCareText $Item.LayoutTitle 22) $(Limit-WinCareText $Item.ZoneTitle ([math]::Max(20,$Width-34))) [$($Item.X),$($Item.Y),$($Item.Width),$($Item.Height)]"}

function Show-WinCareWindowWorkspaceScreen {
    while($true){
        $windows=@(Get-WinCareWindowInventory);$monitors=@(Get-WinCareMonitorInventory)
        $menu=@(
            [pscustomobject]@{Label='Windows';Description="$($windows.Count) eligible top-level window(s)";Action='List'},
            [pscustomobject]@{Label='Monitors';Description="$($monitors.Count) monitor work area(s)";Action='Monitors'},
            [pscustomobject]@{Label='Place window in zone';Description='DPI-independent normalized layouts with window/process CAS';Action='Zone';Disabled=$windows.Count -eq 0 -or $monitors.Count -eq 0},
            [pscustomobject]@{Label='Toggle always on top';Description='Exact HWND/process identity and reversible state';Action='Topmost';Disabled=$windows.Count -eq 0},
            [pscustomobject]@{Label='Activate window';Description='Foreground activation through supported Win32 APIs';Action='Activate';Disabled=$windows.Count -eq 0},
            [pscustomobject]@{Label='Emergency local input release';Description='Release cursor clipping and common stuck modifier states';Action='Release'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Window workspace' -Subtitle 'Ratio zones, monitor work areas, topmost governance, activation, and exact identity checks' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'List' {
                    $selected=Show-WinCareListSelector -Title 'Windows' -Items $windows -Formatter ${function:Format-WinCareWindowRow} -SearchProperties @('ProcessName','Title','ProcessId')
                    if($selected){
                        $lines=@("Handle: $($selected.Handle)","Process: $($selected.ProcessName) ($($selected.ProcessId))","Process start: $($selected.ProcessStartTime)","Bounds: $($selected.Left),$($selected.Top) $($selected.Width)x$($selected.Height)","Topmost: $($selected.Topmost)","Minimized: $($selected.Minimized)","Cloaked: $($selected.Cloaked)","Title hash: $($selected.TitleHash)")
                        Show-WinCareTextPage -Title $selected.Title -Lines $lines
                    }
                }
                'Monitors'{$selected=Show-WinCareListSelector -Title 'Monitors' -Items $monitors -Formatter ${function:Format-WinCareMonitorRow} -SearchProperties @('DeviceName');if($selected){Show-WinCareTextPage -Title $selected.DeviceName -Lines @("Primary: $($selected.Primary)","Bounds: $($selected.Left),$($selected.Top) $($selected.Width)x$($selected.Height)","Work area: $($selected.WorkLeft),$($selected.WorkTop) $($selected.WorkWidth)x$($selected.WorkHeight)")}}
                'Zone'{$window=Show-WinCareListSelector -Title 'Select window' -Items $windows -Formatter ${function:Format-WinCareWindowRow} -SearchProperties @('ProcessName','Title');if(-not $window){continue};$monitor=Show-WinCareListSelector -Title 'Select monitor' -Items $monitors -Formatter ${function:Format-WinCareMonitorRow} -SearchProperties @('DeviceName');if(-not $monitor){continue};$zone=Show-WinCareListSelector -Title 'Select zone' -Items @(Get-WinCareWindowZoneDefinition) -Formatter ${function:Format-WinCareZoneRow} -SearchProperties @('LayoutTitle','ZoneTitle','LayoutId','ZoneId');if($zone){Invoke-WinCarePlanFromUi (New-WinCareWindowZonePlan -WindowHandle ([long]$window.Handle) -LayoutId $zone.LayoutId -ZoneId $zone.ZoneId -MonitorDevice $monitor.DeviceName)}}
                'Topmost'{$window=Show-WinCareListSelector -Title 'Select window' -Items $windows -Formatter ${function:Format-WinCareWindowRow} -SearchProperties @('ProcessName','Title');if($window){Invoke-WinCarePlanFromUi (New-WinCareWindowTopmostPlan -WindowHandle ([long]$window.Handle) -Topmost (-not [bool]$window.Topmost))}}
                'Activate'{$window=Show-WinCareListSelector -Title 'Activate window' -Items $windows -Formatter ${function:Format-WinCareWindowRow} -SearchProperties @('ProcessName','Title');if($window){Invoke-WinCarePlanFromUi (New-WinCareWindowActivatePlan -WindowHandle ([long]$window.Handle))}}
                'Release'{Invoke-WinCarePlanFromUi (New-WinCareEmergencyInputReleasePlan)}
            }
        }catch{Show-WinCareMessage -Title 'Window operation failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

