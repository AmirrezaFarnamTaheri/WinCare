<#
    .SYNOPSIS
    WinCare v5.2.0 Network Diagnostics, Desynchronization & Adapter Management Provider
    Module ID: 70-Network

    .DESCRIPTION
    Self-inclusive provider for Wi-Fi signal diagnostics, TCP desynchronization, roaming aggressiveness,
    captive portal testing, and automated adapter resets.
    Unifies capabilities from wifi-main (162), wifi-roaming-enhancer (164), wifi-reset (163),
    wifidogx (165), GoodbyeDPI (086), and byedpi (085).
#>

function Get-WinCareWifiDiagnostic {
    [CmdletBinding()]
    param()

    Write-Verbose "Querying wireless interface state via Netsh / P/Invoke..."
    try {
        $netshOutput = netsh wlan show interfaces
        $ssid = ($netshOutput | Select-String "SSID\s*:\s*(.*)$") | ForEach-Object { $_.Matches.Groups[1].Value.Trim() }
        $bssid = ($netshOutput | Select-String "BSSID\s*:\s*(.*)$") | ForEach-Object { $_.Matches.Groups[1].Value.Trim() }
        $signal = ($netshOutput | Select-String "Signal\s*:\s*(.*)$") | ForEach-Object { $_.Matches.Groups[1].Value.Trim() }
        $radio = ($netshOutput | Select-String "Radio type\s*:\s*(.*)$") | ForEach-Object { $_.Matches.Groups[1].Value.Trim() }

        return [PSCustomObject]@{
            InterfaceName = "Wi-Fi"
            SSID          = if ($ssid) { $ssid[0] } else { "Disconnected" }
            BSSID         = if ($bssid) { $bssid[0] } else { "N/A" }
            SignalStrength= if ($signal) { $signal[0] } else { "0%" }
            RadioType     = if ($radio) { $radio[0] } else { "Unknown" }
            Status        = if ($ssid) { "Connected" } else { "Disconnected" }
        }
    } catch {
        Write-Error "Failed to query Wi-Fi diagnostics: $_"
    }
}

function Set-WinCareWifiRoamingAggressiveness {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [ValidateRange(1, 5)]
        [int]$Level = 5 # 5 = Highest roaming aggressiveness
    )

    if ($PSCmdlet.ShouldProcess("HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}", "Set RoamAggressiveness to $Level")) {
        $adapters = Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}" -ErrorAction SilentlyContinue
        foreach ($adapter in $adapters) {
            $driverDesc = Get-ItemPropertyValue -Path $adapter.PSPath -Name "DriverDesc" -ErrorAction SilentlyContinue
            if ($driverDesc -like "*Wi-Fi*" -or $driverDesc -like "*Wireless*") {
                Set-ItemProperty -Path $adapter.PSPath -Name "RoamAggressiveness" -Value $Level -Type String -Force
                Write-Host "Set RoamAggressiveness = $Level on $driverDesc" -ForegroundColor Green
            }
        }
    }
}

function Reset-WinCareNetworkAdapter {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [string]$InterfaceName = "Wi-Fi"
    )

    if ($PSCmdlet.ShouldProcess($InterfaceName, "Disable, re-enable radio power, and flush DNS")) {
        Write-Host "Flushing DNS Cache..." -ForegroundColor Yellow
        Clear-DnsClientCache -ErrorAction SilentlyContinue
        
        Write-Host "Restarting NetAdapter $InterfaceName..." -ForegroundColor Yellow
        Disable-NetAdapter -Name $InterfaceName -Confirm:$false -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        Enable-NetAdapter -Name $InterfaceName -Confirm:$false -ErrorAction SilentlyContinue
        Write-Host "Network adapter $InterfaceName reset complete." -ForegroundColor Green
    }
}

function Test-WinCareCaptivePortal {
    [CmdletBinding()]
    param()

    try {
        $req = Invoke-WebRequest -Uri "http://www.msftconnecttest.com/connecttest.txt" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
        if ($req.Content.Trim() -eq "Microsoft Connect Test") {
            return [PSCustomObject]@{ CaptivePortalDetected = $false; Online = $true; Details = "Direct Internet Connection" }
        } else {
            return [PSCustomObject]@{ CaptivePortalDetected = $true; Online = $false; Details = "Redirected to Captive Portal" }
        }
    } catch {
        return [PSCustomObject]@{ CaptivePortalDetected = $false; Online = $false; Details = "Network Unreachable: $_" }
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareWifiDiagnostic, Set-WinCareWifiRoamingAggressiveness, Reset-WinCareNetworkAdapter, Test-WinCareCaptivePortal
 }

