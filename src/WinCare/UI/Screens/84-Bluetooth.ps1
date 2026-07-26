function Format-WinCareBluetoothRow {param($Item,$Width);$battery=if($null -eq $Item.BatteryPercent){'n/a'}else{"$($Item.BatteryPercent)%"};"$(Limit-WinCareText $Item.Name ([math]::Max(24,$Width-40))) $(Limit-WinCareText $Item.Kind 16) $battery $($Item.Status)"}
function Show-WinCareBluetoothScreen {
    while($true){
        $devices=@(Get-WinCareBluetoothDevice -IncludeDisconnected)
        $menu=@(
            [pscustomobject]@{Label='Bluetooth devices';Description="$($devices.Count) observed device node(s)";Action='Devices'},
            [pscustomobject]@{Label='Bluetooth events';Description='Bounded recent pairing, transport, and driver evidence';Action='Events'},
            [pscustomobject]@{Label='Open Windows Bluetooth settings';Description='Use the Windows-owned pairing and removal surface';Action='Settings'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Bluetooth and device telemetry' -Subtitle 'Read-only state and battery observations; WinCare does not replace Windows pairing or install custom drivers' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'Devices'{$item=Show-WinCareListSelector -Title 'Bluetooth devices' -Items $devices -Formatter ${function:Format-WinCareBluetoothRow} -SearchProperties @('Name','Kind','Manufacturer','Status','InstanceId');if($item){Show-WinCareTextPage -Title $item.Name -Lines @("Kind: $($item.Kind)","Manufacturer: $($item.Manufacturer)","Status: $($item.Status)","Connected: $($item.Connected)","Battery: $(if($null -eq $item.BatteryPercent){'Unavailable'}else{"$($item.BatteryPercent)%"})","Instance: $($item.InstanceId)","Problem: $($item.Problem)")}}
            'Events'{$lines=@(Get-WinCareBluetoothEvent -MaxEvents 250|ForEach-Object{"$($_.TimeCreated.ToString('u')) | $($_.Provider) | $($_.Id) | $($_.Level) | $($_.Message)"});Show-WinCareTextPage -Title 'Bluetooth events' -Lines $(if($lines.Count){$lines}else{@('No Bluetooth events were returned.')})}
            'Settings'{$null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('ms-settings:bluetooth')}
        }
    }
}

