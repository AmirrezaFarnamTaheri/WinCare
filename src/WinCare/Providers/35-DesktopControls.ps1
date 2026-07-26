function Initialize-WinCareAudioInterop {
    if('WinCare.Native.AudioEndpoint' -as [type]){return $true}
    $path=Join-Path $script:WinCareModuleRoot 'Native\WinCare.Audio.cs'
    try{Add-Type -Path $path -ErrorAction Stop;return $true}catch{Write-WinCareLog -Level Warning -Message 'Core Audio interop could not be loaded.' -Data @{error=$_.Exception.Message};return $false}
}

function Get-WinCareDesktopControlSummary {
    [CmdletBinding()]param()
    $volume=$null;$muted=$null
    if(Initialize-WinCareAudioInterop){try{$volume=[math]::Round([WinCare.Native.AudioEndpoint]::GetVolume()*100);$muted=[WinCare.Native.AudioEndpoint]::GetMute()}catch { Write-Verbose 'A best-effort operation was unavailable.' }}
    $brightness=@()
    try{$brightness=@(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop|ForEach-Object{[pscustomobject]@{InstanceName=$_.InstanceName;Brightness=[int]$_.CurrentBrightness;Levels=@($_.Level)}})}catch { Write-Verbose 'A best-effort operation was unavailable.' }
    $wifi=$null
    if(Get-Command netsh.exe -ErrorAction SilentlyContinue){$r=Invoke-WinCareProcess -FilePath netsh.exe -ArgumentList @('wlan','show','interfaces') -TimeoutSeconds 20;$wifi=if($r.Success){$r.Data.StdOut}else{$null}}
    [pscustomobject]@{VolumePercent=$volume;Muted=$muted;Brightness=$brightness;WifiSummary=$wifi;ClipboardHistoryCollected=$false;SampledAt=[datetime]::UtcNow}
}

function New-WinCareMasterVolumePlan {
    [CmdletBinding()]param([ValidateRange(0,100)][int]$Percent,[Nullable[bool]]$Mute)
    if(-not (Initialize-WinCareAudioInterop)){throw 'Core Audio is unavailable.'}
    $beforePercent=[math]::Round([WinCare.Native.AudioEndpoint]::GetVolume()*100);$beforeMute=[WinCare.Native.AudioEndpoint]::GetMute()
    $parameters=@{Percent=$Percent};if($null -ne $Mute){$parameters.Mute=[bool]$Mute}
    $action=New-WinCareAction -Type SetMasterVolume -Label "Set master volume to $Percent%" -Risk Low -Parameters $parameters -Reversible $true -Compensator @{Type='SetMasterVolume';Parameters=@{Percent=$beforePercent;Mute=$beforeMute}} -Tags @('DesktopControl','Idempotent') -SourceRecords @('SRC:bf0bb87122')
    New-WinCarePlan -Title 'Change master audio' -Actions @($action) -SourceRecords @('SRC:bf0bb87122')
}

function Invoke-WinCareMasterVolumeAction {
    param([object]$Action)
    if(-not (Initialize-WinCareAudioInterop)){return New-WinCareResult -Success $false -Message 'Core Audio is unavailable.' -ExitCode 2}
    $percent=[int]$Action.Parameters.Percent;if($percent -lt 0 -or $percent -gt 100){return New-WinCareResult -Success $false -Message 'Volume must be between 0 and 100.' -ExitCode 22}
    [WinCare.Native.AudioEndpoint]::SetVolume($percent/100.0)
    if($Action.Parameters.ContainsKey('Mute')){[WinCare.Native.AudioEndpoint]::SetMute([bool]$Action.Parameters.Mute)}
    $actual=[math]::Round([WinCare.Native.AudioEndpoint]::GetVolume()*100)
    New-WinCareResult -Success ([math]::Abs($actual-$percent)-le 1) -Message 'Master audio updated.' -Data @{VolumePercent=$actual;Muted=[WinCare.Native.AudioEndpoint]::GetMute()}
}

function New-WinCareBrightnessPlan {
    [CmdletBinding()]param([ValidateRange(0,100)][int]$Percent)
    $current=Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop|Select-Object -First 1
    $action=New-WinCareAction -Type SetBrightness -Label "Set display brightness to $Percent%" -Risk Low -Parameters @{Percent=$Percent;InstanceName=$current.InstanceName} -Reversible $true -Compensator @{Type='SetBrightness';Parameters=@{Percent=[int]$current.CurrentBrightness;InstanceName=$current.InstanceName}} -Tags @('DesktopControl','Idempotent') -SourceRecords @('SRC:bf0bb87122')
    New-WinCarePlan -Title 'Change display brightness' -Actions @($action) -SourceRecords @('SRC:bf0bb87122')
}

function Invoke-WinCareBrightnessAction {
    param([object]$Action)
    $percent=[int]$Action.Parameters.Percent;$methods=Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -ErrorAction Stop
    foreach($method in $methods){$null=Invoke-CimMethod -InputObject $method -MethodName WmiSetBrightness -Arguments @{Timeout=1;Brightness=[byte]$percent}}
    Start-Sleep -Milliseconds 200;$after=Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop
    $ok=-not (@($after|Where-Object CurrentBrightness -ne $percent).Count)
    New-WinCareResult -Success $ok -Message 'Brightness updated.' -Data @{Brightness=@($after.CurrentBrightness)}
}

function New-WinCareClearClipboardPlan {
    $action=New-WinCareAction -Type ClearClipboard -Label 'Clear the current clipboard contents' -Risk Moderate -Parameters @{} -Reversible $false -Tags @('Privacy','Clipboard') -SourceRecords @('SRC:bf0bb87122') -RecoveryDescription 'Clipboard contents cannot be restored by WinCare.'
    New-WinCarePlan -Title 'Clear clipboard' -Actions @($action) -RollbackOnFailure $false -SourceRecords @('SRC:bf0bb87122')
}

function Invoke-WinCareClearClipboardAction {
    param([object]$Action)
    if(-not (Get-Command Set-Clipboard -ErrorAction SilentlyContinue)){return New-WinCareResult -Success $false -Message 'Set-Clipboard is unavailable.' -ExitCode 2}
    Set-Clipboard -Value ''
    $actual=if(Get-Command Get-Clipboard -ErrorAction SilentlyContinue){Get-Clipboard -Raw -ErrorAction SilentlyContinue}else{''}
    New-WinCareResult -Success ([string]::IsNullOrEmpty([string]$actual)) -Message 'Current clipboard contents cleared.'
}

function Get-WinCareWifiProfile {
    [CmdletBinding()]param()
    if(-not (Get-Command netsh.exe -ErrorAction SilentlyContinue)){return @()}
    $result=Invoke-WinCareProcess -FilePath netsh.exe -ArgumentList @('wlan','show','profiles') -TimeoutSeconds 20
    if(-not $result.Success){return @()}
    @($result.Data.StdOut -split "`r?`n" | Where-Object {$_ -match ':\s*(.+)$'} | ForEach-Object {[pscustomobject]@{Name=$Matches[1].Trim();SecretsRead=$false}})
}

function Open-WinCareDisplaySettings { Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('ms-settings:display') }
function Open-WinCareSoundSettings { Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('ms-settings:sound') }


