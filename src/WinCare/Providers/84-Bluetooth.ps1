function Get-WinCarePnpPropertyValue {
    param([Parameter(Mandatory)][string]$InstanceId,[Parameter(Mandatory)][string[]]$KeyName)
    if(-not(Get-Command Get-PnpDeviceProperty -ErrorAction SilentlyContinue)){return $null}
    foreach($key in $KeyName){
        try{$property=Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName $key -ErrorAction Stop;if($null -ne $property.Data){return $property.Data}}
        catch{Write-Verbose "PnP property ${key} unavailable for ${InstanceId}: $($_.Exception.Message)"}
    }
    return $null
}

function Get-WinCareBluetoothDeviceKind {
    param([string]$Name,[string]$Manufacturer)
    $text="$Name $Manufacturer"
    if($text -match '(?i)airpods|max|beats'){return 'AppleAudio'}
    if($text -match '(?i)head(phone|set)|buds|audio|speaker'){return 'Audio'}
    if($text -match '(?i)mouse|trackpad'){return 'PointingDevice'}
    if($text -match '(?i)keyboard'){return 'Keyboard'}
    if($text -match '(?i)phone|android|iphone'){return 'Phone'}
    return 'BluetoothDevice'
}

function Get-WinCareBluetoothDevice {
    [CmdletBinding()]param([switch]$IncludeDisconnected)
    if(-not $IsWindows){return @()}
    if(-not(Get-Command Get-PnpDevice -ErrorAction SilentlyContinue)){return @()}
    $deadline=[datetime]::UtcNow.AddSeconds([int](Get-WinCareConfig 'BluetoothScanTimeoutSeconds'))
    $devices=try{@(Get-PnpDevice -Class Bluetooth -PresentOnly:$(-not $IncludeDisconnected) -ErrorAction Stop|Select-Object -First 512)}catch{Write-Verbose "Bluetooth inventory unavailable: $($_.Exception.Message)";@()}
    foreach($device in $devices){
        if([datetime]::UtcNow -gt $deadline){Write-Verbose 'Bluetooth scan reached its configured deadline.';break}
        $instance=[string]$device.InstanceId;$friendly=[string]$device.FriendlyName
        if([string]::IsNullOrWhiteSpace($friendly)){$friendly=[string]$device.Name}
        $manufacturer=Get-WinCarePnpPropertyValue -InstanceId $instance -KeyName @('DEVPKEY_Device_Manufacturer')
        $connectedValue=Get-WinCarePnpPropertyValue -InstanceId $instance -KeyName @('DEVPKEY_Device_IsPresent','DEVPKEY_Device_ProblemStatus')
        $battery=Get-WinCarePnpPropertyValue -InstanceId $instance -KeyName @('DEVPKEY_Device_BatteryLevel','{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2','{2BD67D8B-8B3E-4E83-BA0A-4A77F95E4BDE} 2')
        $batteryPercent=$null
        if($null -ne $battery){try{$candidate=[int]$battery;if($candidate -ge 0 -and $candidate -le 100){$batteryPercent=$candidate}}catch{Write-Verbose "Bluetooth battery value was not numeric for $instance."}}
        $connected=[string]$device.Status -eq 'OK'
        if($connectedValue -is [bool]){$connected=[bool]$connectedValue}
        [pscustomobject][ordered]@{
            Name=$friendly;Kind=Get-WinCareBluetoothDeviceKind -Name $friendly -Manufacturer ([string]$manufacturer);Manufacturer=[string]$manufacturer;Status=[string]$device.Status;Connected=$connected;BatteryPercent=$batteryPercent;Class=[string]$device.Class;InstanceId=$instance;Problem=[string]$device.Problem;CapturedAt=[datetime]::UtcNow.ToString('o');SourceRecords=@('SRC:7ed4a49b96')
        }
    }
}

function Get-WinCareBluetoothEvent {
    [CmdletBinding()]param([ValidateRange(1,5000)][int]$MaxEvents=100)
    if(-not $IsWindows -or -not(Get-Command Get-WinEvent -ErrorAction SilentlyContinue)){return @()}
    $logs=@('Microsoft-Windows-Bluetooth-BthLEPrepairing/Operational','Microsoft-Windows-Bluetooth-MTPEnum/Operational','System')
    $result=[Collections.Generic.List[object]]::new()
    foreach($log in $logs){
        try{
            $filter=if($log -eq 'System'){@{LogName='System';ProviderName=@('BTHUSB','BTHMINI');StartTime=[datetime]::Now.AddDays(-14)}}else{@{LogName=$log;StartTime=[datetime]::Now.AddDays(-14)}}
            foreach($event in @(Get-WinEvent -FilterHashtable $filter -MaxEvents $MaxEvents -ErrorAction Stop)){
                $message=([string]$event.Message -replace '\s+',' ').Trim()
                if($message.Length -gt 4096){$message=$message.Substring(0,4096)}
                $result.Add([pscustomobject]@{TimeCreated=$event.TimeCreated;Id=$event.Id;Level=$event.LevelDisplayName;Provider=$event.ProviderName;Log=$event.LogName;Message=$message;SourceRecords=@('SRC:7ed4a49b96')})
                if($result.Count -ge $MaxEvents){break}
            }
        }catch{Write-Verbose "Bluetooth event log unavailable ($log): $($_.Exception.Message)"}
        if($result.Count -ge $MaxEvents){break}
    }
    return @($result|Sort-Object TimeCreated -Descending|Select-Object -First $MaxEvents)
}



