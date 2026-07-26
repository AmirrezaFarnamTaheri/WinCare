#requires -Version 7.2

function Get-WinCareDisplayPipelineState {
    [CmdletBinding()]
    param([ValidateRange(1,64)][int]$MaximumDisplays=16)

    if(-not $IsWindows) {
        return [pscustomobject]@{
            Supported=$false
            Displays=@()
            GraphicsAdapters=@()
            Errors=@('Windows display APIs are required.')
            EvidenceType='PlatformRequirement'
        }
    }

    $errors=[Collections.Generic.List[string]]::new()
    $monitors=@()
    $vcp=@()
    $graphics=@()
    try { $monitors=@(Get-WinCareMonitorInventory|Select-Object -First $MaximumDisplays) }
    catch { $errors.Add("Monitor inventory: $($_.Exception.Message)") }
    try { $vcp=@(Get-WinCareMonitorControlState|Select-Object -First ($MaximumDisplays*4)) }
    catch { $errors.Add("DDC/CI inventory: $($_.Exception.Message)") }
    try {
        $graphics=@(Get-CimInstance Win32_VideoController -ErrorAction Stop|Select-Object -First 16|ForEach-Object {
            [pscustomobject]@{
                Name=[string]$_.Name
                DriverVersion=[string]$_.DriverVersion
                AdapterRam=[long]$_.AdapterRAM
                CurrentHorizontalResolution=$_.CurrentHorizontalResolution
                CurrentVerticalResolution=$_.CurrentVerticalResolution
                CurrentRefreshRate=$_.CurrentRefreshRate
                VideoProcessor=[string]$_.VideoProcessor
                PnpDeviceIdHash=if($_.PNPDeviceID){Get-WinCareSha256Text -Text ([string]$_.PNPDeviceID)}else{$null}
                EvidenceType='CimVideoControllerObservation'
            }
        })
    } catch { $errors.Add("Video controller inventory: $($_.Exception.Message)") }

    $displayRows=foreach($monitor in $monitors) {
        $controls=@($vcp|Where-Object DeviceName -eq $monitor.DeviceName)
        [pscustomobject]@{
            Handle=$monitor.Handle
            DeviceName=$monitor.DeviceName
            Bounds=$monitor.Bounds
            WorkArea=$monitor.WorkArea
            Primary=$monitor.Primary
            DpiX=$monitor.DpiX
            DpiY=$monitor.DpiY
            DdcCiControls=$controls
            EvidenceType='MonitorAndDdcCiCorrelation'
        }
    }

    [pscustomobject]@{
        Supported=$true
        CapturedAt=[datetime]::UtcNow.ToString('o')
        Displays=@($displayRows)
        GraphicsAdapters=$graphics
        Errors=@($errors)
        MutationPerformed=$false
        EvidenceType='DisplayPipelineInventory'
    }
}

function Get-WinCareVirtualDisplayCapability {
    [CmdletBinding()]
    param()

    if(-not $IsWindows) {
        return [pscustomobject]@{
            Available=$false
            InstalledDrivers=@()
            Reason='Windows is required.'
            DriverInstallationSupported=$false
            EvidenceType='PlatformRequirement'
        }
    }

    $drivers=@()
    $errors=[Collections.Generic.List[string]]::new()
    try {
        $drivers=@(Get-CimInstance Win32_PnPSignedDriver -Filter "DeviceClass='DISPLAY'" -ErrorAction Stop|ForEach-Object {
            $isVirtual=([string]$_.DeviceName) -match '(?i)virtual|indirect display|idd|remote display'
            [pscustomobject]@{
                DeviceName=[string]$_.DeviceName
                Manufacturer=[string]$_.Manufacturer
                DriverProviderName=[string]$_.DriverProviderName
                DriverVersion=[string]$_.DriverVersion
                DriverDate=$_.DriverDate
                IsSigned=[bool]$_.IsSigned
                Signer=[string]$_.Signer
                IsVirtualDisplayCandidate=$isVirtual
                DeviceIdHash=if($_.DeviceID){Get-WinCareSha256Text -Text ([string]$_.DeviceID)}else{$null}
                EvidenceType='PnpSignedDisplayDriverObservation'
            }
        })
    } catch { $errors.Add($_.Exception.Message) }
    $virtual=@($drivers|Where-Object IsVirtualDisplayCandidate)
    [pscustomobject]@{
        Available=$virtual.Count -gt 0
        InstalledDrivers=$virtual
        AllDisplayDrivers=$drivers
        Reason=if($virtual.Count){'A signed installed virtual or indirect display driver was observed.'}else{'No installed virtual or indirect display driver was observed.'}
        DriverInstallationSupported=$false
        DriverBuildSupported=$false
        Errors=@($errors)
        EvidenceType='InstalledDriverCapabilityProbe'
    }
}

function New-WinCareDisplayCalibrationPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$DeviceName,
        [Parameter(Mandatory)][ValidateRange(0,63)][int]$PhysicalIndex,
        [ValidateRange(0,65535)][int]$Brightness,
        [ValidateRange(0,65535)][int]$Contrast
    )

    if(-not $PSBoundParameters.ContainsKey('Brightness') -and
        -not $PSBoundParameters.ContainsKey('Contrast')) {
        throw 'Brightness or Contrast must be specified.'
    }

    $actions=[Collections.Generic.List[object]]::new()
    if($PSBoundParameters.ContainsKey('Brightness')) {
        $plan=New-WinCareMonitorControlPlan `
            -DeviceName $DeviceName `
            -PhysicalIndex $PhysicalIndex `
            -Feature Brightness `
            -Value $Brightness
        foreach($action in @($plan.Actions)) { $actions.Add($action) }
    }
    if($PSBoundParameters.ContainsKey('Contrast')) {
        $plan=New-WinCareMonitorControlPlan `
            -DeviceName $DeviceName `
            -PhysicalIndex $PhysicalIndex `
            -Feature Contrast `
            -Value $Contrast
        foreach($action in @($plan.Actions)) { $actions.Add($action) }
    }

    New-WinCareBridgePlan `
        -Title 'Apply physical-monitor calibration controls' `
        -Description 'Applies only supported DDC/CI VCP brightness and contrast values with exact monitor identity and before-state checks.' `
        -Actions @($actions) `
        -Metadata @{
            DeviceName=$DeviceName
            PhysicalIndex=$PhysicalIndex
            VirtualDriverMutationPerformed=$false
            EvidenceType='DdcCiCalibrationPlan'
        }
}
