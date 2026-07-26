function Format-WinCarePortRow {
    param([object]$Port,[int]$Width)
    $addressWidth=24; $portWidth=8; $pidWidth=9; $rest=$Width-$addressWidth-$portWidth-$pidWidth-3
    "$(Limit-WinCareText $Port.Address $addressWidth) $(Limit-WinCareText ([string]$Port.Port) $portWidth) $(Limit-WinCareText $Port.Process $rest) $(Limit-WinCareText ([string]$Port.PID) $pidWidth)"
}

function Show-WinCareNetworkOverview {
    $items=@(Get-WinCareNetworkSummary)
    $lines=[System.Collections.Generic.List[string]]::new()
    foreach ($item in $items) {
        $lines.Add("$($item.Name) - $($item.Status)")
        $lines.Add("  $($item.Description)")
        $lines.Add("  IPv4: $($item.IPv4)   Link: $($item.LinkSpeed)   MAC: $($item.MacAddress)")
        $lines.Add('')
    }
    if (-not $lines.Count) { $lines.Add('No active network adapters were detected.') }
    Show-WinCareTextPage -Title 'Network overview' -Lines @($lines)
}

function New-WinCareFlushDnsPlan {
    $action=New-WinCareAction -Type 'RunProcess' -Label 'Flush Windows DNS resolver cache' -Risk Low -Parameters @{FilePath='ipconfig.exe';Arguments=@('/flushdns');Timeout=120} -RequiresAdmin $true -Verification 'ipconfig exits successfully.'
    New-WinCarePlan -Title 'Flush DNS cache' -Actions @($action)
}

function Show-WinCareNetworkScreen {
    while ($true) {
        $menu=@(
            [pscustomobject]@{ Label='Adapter overview'; Description='Active adapters, IPv4 addresses, and link speeds'; Action='Adapters' },
            [pscustomobject]@{ Label='Test internet connectivity'; Description='TCP connection test to Microsoft over port 443'; Action='Test' },
            [pscustomobject]@{ Label='Listening TCP ports'; Description='Read-only local port and owning process inventory'; Action='Ports' },
            [pscustomobject]@{ Label='Flush DNS cache'; Description='Clear the Windows DNS resolver cache'; Action='Flush' },
            [pscustomobject]@{ Label='Open Network settings'; Description='Open the native Windows network page'; Action='Settings' },
            [pscustomobject]@{ Label='Back'; Description='Return to main menu'; Action='Back' }
        )
        $choice=Show-WinCareMenu -Title 'Network' -Subtitle 'Diagnostics first; no hidden network tuning' -Items $menu
        if (-not $choice -or $choice.Action -eq 'Back') { return }
        switch ($choice.Action) {
            'Adapters' { Show-WinCareNetworkOverview }
            'Test' {
                Clear-WinCareScreen; Write-WinCareHeader -Title 'Connectivity test'; Write-WinCareText ' Testing TCP connectivity...' Accent
                $result=Test-WinCareInternetConnection
                Show-WinCareMessage -Title 'Connectivity test' -Lines @("Success: $($result.Success)","Remote address: $($result.RemoteAddress)","Source address: $($result.SourceAddress)","Interface: $($result.Interface)") -Kind $(if ($result.Success) {'Success'} else {'Warning'})
            }
            'Ports' {
                $ports=@(Get-WinCareListeningPort)
                $null=Show-WinCareListSelector -Title 'Listening TCP ports' -Subtitle "$($ports.Count) result(s)" -Items $ports -Formatter ${function:Format-WinCarePortRow} -SearchProperties @('Address','Port','Process','PID')
            }
            'Flush' { Invoke-WinCarePlanFromUi (New-WinCareFlushDnsPlan) }
            'Settings' { $null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('ms-settings:network-status') }
        }
    }
}

