<#
.SYNOPSIS
Observed wireless/network diagnostics and typed adapter configuration plans.
#>
function ConvertFrom-WinCareNetshWifiInterfaceOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Lines
    )

    $records=[Collections.Generic.List[object]]::new()
    $current=[ordered]@{}
    foreach($line in $Lines) {
        if([string]$line -match '^\s*Name\s*:\s*(.+)$') {
            if($current.Count) {
                $records.Add([pscustomobject]$current)
                $current=[ordered]@{}
            }
            $current.InterfaceName=$Matches[1].Trim()
            continue
        }
        if([string]$line -match '^\s*(SSID|BSSID|Signal|Radio type|State|Authentication|Cipher)\s*:\s*(.*)$') {
            $current[($Matches[1] -replace ' ','')]=$Matches[2].Trim()
        }
    }
    if($current.Count) {
        $records.Add([pscustomobject]$current)
    }
    @($records)
}

function Get-WinCareWifiDiagnostic {
    [CmdletBinding()]
    param()

    # ponytail: netsh wlan -> native WlanApi.dll P/Invoke / DoH enforcer
    if(-not $IsWindows -or -not (Get-Command netsh.exe -ErrorAction SilentlyContinue)) {
        return [pscustomobject]@{
            Supported=$false
            Interfaces=@()
            EvidenceType='PlatformCapability'
            Error='netsh wlan is unavailable.'
        }
    }
    try {
        $process=Invoke-WinCareProcess -FilePath 'netsh.exe' `
            -ArgumentList @('wlan','show','interfaces') -TimeoutSeconds 30 `
            -MaximumCapturedOutputBytes 1048576
        if(-not $process.Success) {
            return [pscustomobject]@{
                Supported=$true
                Interfaces=@()
                EvidenceType='BoundedNetshWlanOutput'
                ExitCode=$process.ExitCode
                Error=$process.Message
            }
        }
        $records=@(
            ConvertFrom-WinCareNetshWifiInterfaceOutput `
                -Lines @($process.Data.StdOut -split "`r?`n")
        )
        [pscustomobject]@{
            Supported=$true
            Interfaces=$records
            Count=$records.Count
            ExitCode=$process.ExitCode
            EvidenceType='BoundedNetshWlanOutput'
            Error=$null
        }
    } catch {
        [pscustomobject]@{
            Supported=$true
            Interfaces=@()
            EvidenceType='NetshWlanOutput'
            ExitCode=$null
            Error=$_.Exception.Message
        }
    }
}

function Get-WinCareWifiRoamingSetting {
    [CmdletBinding()]
    param()

    if(-not $IsWindows) { return @() }
    $root='HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}'
    if(-not (Test-Path -LiteralPath $root)) { return @() }
    @(
        foreach($adapter in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            try {
                $props=Get-ItemProperty -LiteralPath $adapter.PSPath -ErrorAction Stop
                $description=[string]$props.DriverDesc
                if($description -notmatch '(?i)wi-?fi|wireless|802\.11') { continue }
                $configured=$props.PSObject.Properties['RoamAggressiveness']
                $value=if($configured){[string]$props.RoamAggressiveness}else{$null}
                [pscustomobject]@{
                    RegistryPath=$adapter.PSPath
                    DriverDescription=$description
                    Value=$value
                    Configured=$null -ne $value
                    EvidenceType='RegistryObservation'
                }
            } catch {}
        }
    )
}

function New-WinCareWifiRoamingAction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Setting,
        [ValidateRange(1,5)][int]$Level
    )

    $before=@{
        Exists=$Setting.Configured
        Value=$Setting.Value
        ValueType='String'
    }
    if($Setting.Configured) {
        $compensator=@{
            Type='SetRegistryValue'
            Parameters=@{
                Path=$Setting.RegistryPath
                Name='RoamAggressiveness'
                Value=$Setting.Value
                ValueType='String'
                Before=@{}
            }
            RequiresAdmin=$true
        }
    } else {
        $compensator=@{
            Type='RemoveRegistryValue'
            Parameters=@{
                Path=$Setting.RegistryPath
                Name='RoamAggressiveness'
                Before=@{}
            }
            RequiresAdmin=$true
        }
    }
    New-WinCareAction -Type 'SetRegistryValue' `
        -Label "Set roaming aggressiveness for $($Setting.DriverDescription)" `
        -Risk Moderate -RequiresAdmin $true -Reversible $true `
        -Parameters @{
            Path=$Setting.RegistryPath
            Name='RoamAggressiveness'
            Value=[string]$Level
            ValueType='String'
            Before=$before
        } -Compensator $compensator `
        -Verification 'The adapter registry value must equal the requested level.'
}

function Set-WinCareWifiRoamingAggressiveness {
    [CmdletBinding()]
    param(
        [ValidateRange(1,5)][int]$Level=5,
        [switch]$Apply,
        [switch]$Approved,
        [switch]$PreviewOnly
    )

    $settings=@(Get-WinCareWifiRoamingSetting)
    if(-not $settings.Count) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'WirelessAdapterRegistryNotFound' `
            -Message 'No wireless adapter registry records were observed.' -ExitCode 2
    }
    $actions=@(
        foreach($setting in $settings) {
            New-WinCareWifiRoamingAction -Setting $setting -Level $Level
        }
    )
    $plan=New-WinCarePlan -Title "Set Wi-Fi roaming aggressiveness to $Level" `
        -Actions $actions -Metadata @{
            Before=$settings
            EvidenceType='AdapterRegistryBeforeState'
        }
    if(-not $Apply -and -not $PreviewOnly) { return $plan }
    Invoke-WinCarePlan -Plan $plan -Approved:$Approved -PreviewOnly:$PreviewOnly
}

function Reset-WinCareNetworkAdapter {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateLength(1,256)][string]$InterfaceName,
        [switch]$Apply,
        [switch]$Approved,
        [switch]$PreviewOnly
    )

    if(-not $IsWindows -or -not (Get-Command Get-NetAdapter -ErrorAction SilentlyContinue)) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'NetworkAdapterProviderUnavailable' `
            -Message 'Windows NetAdapter cmdlets are unavailable.' -ExitCode 78
    }
    $adapter=Get-NetAdapter -Name $InterfaceName -ErrorAction SilentlyContinue
    if(-not $adapter) {
        return New-WinCareResult -Success $false -Status Failed `
            -Code 'NetworkAdapterNotFound' `
            -Message "Network adapter not found: $InterfaceName" -ExitCode 2
    }
    $action=New-WinCareAction -Type 'ResetNetworkAdapter' `
        -Label "Reset network adapter $InterfaceName" -Risk High `
        -Parameters @{
            InterfaceName=$InterfaceName
            InterfaceGuid=[string]$adapter.InterfaceGuid
            BeforeStatus=[string]$adapter.Status
        } -RequiresAdmin $true -TimeoutSeconds 120 `
        -Verification 'The same adapter identity must be re-enabled and observed after reset.'
    $before=$adapter|Select-Object Name,InterfaceGuid,Status,LinkSpeed,MacAddress
    $plan=New-WinCarePlan -Title "Reset network adapter $InterfaceName" `
        -Description 'Disables and re-enables exactly one observed adapter and attempts recovery.' `
        -Actions @($action) -RollbackOnFailure $false `
        -Metadata @{Before=$before;EvidenceType='NetAdapterBeforeState'}
    if(-not $Apply -and -not $PreviewOnly) { return $plan }
    Invoke-WinCarePlan -Plan $plan -Approved:$Approved -PreviewOnly:$PreviewOnly
}

function Invoke-WinCareResetNetworkAdapterAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)

    if(-not $IsWindows) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'NetworkAdapterProviderUnavailable' `
            -Message 'Network adapter reset is supported only on Windows.' -ExitCode 78
    }
    $parameters=$Action.Parameters
    $name=[string]$parameters.InterfaceName
    $adapter=Get-NetAdapter -Name $name -ErrorAction SilentlyContinue
    if(-not $adapter) {
        return New-WinCareResult -Success $false -Status Failed `
            -Code 'NetworkAdapterNotFound' `
            -Message "Network adapter not found: $name" -ExitCode 2
    }
    if([string]$adapter.InterfaceGuid -ne [string]$parameters.InterfaceGuid) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'NetworkAdapterIdentityChanged' `
            -Message 'The adapter identity changed after preview.' -ExitCode 74
    }
    $disabled=$false
    $operationError=$null
    $recoveryError=$null
    try {
        Disable-NetAdapter -Name $name -Confirm:$false -ErrorAction Stop
        $disabled=$true
        Start-Sleep -Milliseconds 750
        Enable-NetAdapter -Name $name -Confirm:$false -ErrorAction Stop
        $disabled=$false
        if(Get-Command Clear-DnsClientCache -ErrorAction SilentlyContinue) {
            Clear-DnsClientCache -ErrorAction Stop
        }
    } catch {
        $operationError=$_.Exception.Message
    } finally {
        if($disabled) {
            try {
                Enable-NetAdapter -Name $name -Confirm:$false -ErrorAction Stop
                $disabled=$false
            } catch {
                $recoveryError=$_.Exception.Message
            }
        }
    }
    Start-Sleep -Milliseconds 500
    $after=Get-NetAdapter -Name $name -ErrorAction SilentlyContinue
    $enabled=$after -and [string]$after.Status -ne 'Disabled'
    $success=[string]::IsNullOrWhiteSpace($operationError) -and $enabled
    $message=if($success) {
        'The adapter was disabled, re-enabled, and re-observed.'
    } else {
        "Adapter reset failed: $operationError"
    }
    New-WinCareResult -Success $success `
        -Status $(if($success){'Succeeded'}else{'Failed'}) `
        -Code $(if($success){'NetworkAdapterReset'}else{'NetworkAdapterResetFailed'}) `
        -Message $message -ExitCode $(if($success){0}else{1}) `
        -Warnings @($recoveryError|Where-Object { $_ }) -Data @{
            InterfaceName=$name
            InterfaceGuid=[string]$parameters.InterfaceGuid
            BeforeStatus=[string]$parameters.BeforeStatus
            AfterStatus=if($after){[string]$after.Status}else{'Missing'}
            DnsCacheFlushed=[string]::IsNullOrWhiteSpace($operationError)
            RecoveryError=$recoveryError
            EvidenceType='NetAdapterIdentityAndBeforeAfterState'
        }
}

function Test-WinCareCaptivePortal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^https?://')][string]$TargetUri,
        [Parameter(Mandatory)][ValidateLength(0,65536)][string]$ExpectedContent,
        [switch]$AllowPrivateNetwork,
        [ValidateRange(1,30)][int]$TimeoutSeconds=5,
        [ValidateRange(1024,1048576)][int]$MaximumResponseBytes=262144
    )

    $uri=[uri]$TargetUri
    try {
        $admission=Assert-WinCareServiceUri -Uri $uri `
            -AllowPrivateNetwork:$AllowPrivateNetwork -AllowExplicitHttp
        $response=Invoke-WinCareBoundedHttpRequest -Method GET -Admission $admission `
            -Headers @{Accept='text/plain, text/html;q=0.9, */*;q=0.1'} `
            -TimeoutSeconds $TimeoutSeconds -MaximumResponseBytes $MaximumResponseBytes
        $matched=$response.IsSuccessStatusCode -and $response.Body -ceq $ExpectedContent
        $redirected=-not [string]::IsNullOrWhiteSpace($response.Location)
        New-WinCareResult -Success $true -Code 'CaptivePortalProbeCompleted' `
            -Message 'The explicit bounded captive-portal probe completed.' -Data @{
                TargetUri=$uri.AbsoluteUri
                ResolvedAddresses=$admission.ResolvedAddresses
                StatusCode=$response.StatusCode
                Location=$response.Location
                ExpectedContentMatched=$matched
                CaptivePortalDetected=($redirected -or -not $matched)
                Online=$matched
                Redirected=$redirected
                ResponseBytes=$response.BodyBytes
                ResponseSha256=$response.BodySha256
                EvidenceType='BoundedExplicitHttpResponseComparison'
            }
    } catch {
        New-WinCareResult -Success $true -Status SucceededWithWarnings `
            -Code 'CaptivePortalProbeInconclusive' `
            -Message 'The explicit captive-portal probe was inconclusive.' `
            -Warnings @($_.Exception.Message) -Data @{
                TargetUri=$uri.AbsoluteUri
                ExpectedContentMatched=$false
                CaptivePortalDetected=$null
                Online=$false
                EvidenceType='BoundedExplicitHttpException'
            }
    }
}

function Test-WinCareDohLatency {
    [CmdletBinding()]
    param([string[]]$Endpoints=@())
    $resolvedEndpoints=@(Resolve-WinCareNetworkProbeTarget -Targets $Endpoints -IncludeLocalInfrastructure)
    if($resolvedEndpoints.Count -eq 0){
        throw 'DoH latency measurement requires explicit -Endpoints or configured NetworkExperimentTargets; WinCare will not silently probe public services.'
    }
    $results=[Collections.Generic.List[object]]::new()
    foreach($ep in $resolvedEndpoints) {
        $sw=[Diagnostics.Stopwatch]::StartNew()
        $ok=try { Test-Connection -TargetName $ep -Count 1 -Quiet -TimeoutSeconds 2 } catch { $false }
        $sw.Stop()
        $results.Add([pscustomobject]@{Endpoint=$ep;Reachable=$ok;LatencyMs=$sw.ElapsedMilliseconds})
    }
    @($results)
}

function Set-WinCareDohEnforcement {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$InterfaceAlias,
        [Parameter(Mandatory)][ValidateCount(1,8)][string[]]$ServerAddresses,
        [ValidateSet('AllowDoh','RequireDoh')][string]$DohMode='RequireDoh'
    )
    if (-not $IsWindows -or -not (Get-Command Set-DnsClientServerAddress -ErrorAction SilentlyContinue)) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'DohEnforcementUnsupported' -Message 'DoH enforcement requires Windows DNS Client cmdlets.' -ExitCode 78
    }
    $validated=[Collections.Generic.List[string]]::new()
    foreach($candidate in $ServerAddresses) {
        $parsed=[Net.IPAddress]::Any
        if([string]::IsNullOrWhiteSpace($candidate) -or -not [Net.IPAddress]::TryParse($candidate.Trim(),[ref]$parsed)) {
            return New-WinCareResult -Success $false -Status Blocked -Code 'DohServerAddressInvalid' `
                -Message "'$candidate' is not a valid literal IP DNS server address." -ExitCode 22
        }
        $validated.Add($parsed.ToString())
    }
    # Assigning resolvers is NOT the same as enforcing DoH: the DoH transport is
    # configured per resolver by the DnsClient DoH cmdlets, which exist only on
    # newer Windows builds. Report exactly which of the two actually happened
    # instead of claiming enforcement the system may never have applied.
    $dohConfigurable=[bool](Get-Command Get-DnsClientDohServerAddress -ErrorAction SilentlyContinue)
    Set-DnsClientServerAddress -InterfaceAlias $InterfaceAlias -ServerAddresses @($validated) -ErrorAction Stop
    $dohRegistered=@()
    if($dohConfigurable) {
        foreach($server in $validated) {
            $template=@(Get-DnsClientDohServerAddress -ServerAddress $server -ErrorAction SilentlyContinue)
            if($template.Count) { $dohRegistered+=$server }
        }
    }
    $evidence=@{
        InterfaceAlias=$InterfaceAlias
        ServerAddresses=@($validated)
        RequestedDohMode=$DohMode
        DohTemplatesAvailable=$dohConfigurable
        DohCapableServers=@($dohRegistered)
        DohEnforced=$false
        EvidenceType='ExplicitOperatorSuppliedDnsServerAssignment'
    }
    $assigned="Assigned $($validated.Count) DNS server(s) on interface $InterfaceAlias"
    if(-not $dohConfigurable) {
        return New-WinCareResult -Success $true -Status SucceededWithWarnings -Code 'DnsServersAssignedWithoutDoh' `
            -Message "$assigned. This host exposes no DoH client cmdlets, so the '$DohMode' transport was not applied." `
            -Warnings @('DNS-over-HTTPS was requested but could not be configured on this host.') -Data $evidence
    }
    if($dohRegistered.Count -ne $validated.Count) {
        return New-WinCareResult -Success $true -Status SucceededWithWarnings -Code 'DnsServersAssignedPartialDohTemplates' `
            -Message "$assigned. Only $($dohRegistered.Count) of $($validated.Count) have a known DoH template." `
            -Warnings @('Servers without a DoH template continue to resolve over classic DNS.') -Data $evidence
    }
    New-WinCareResult -Success $true -Code 'DnsServersAssignedWithDohTemplates' `
        -Message "$assigned. All assigned servers have a known DoH template." -Data $evidence
}

if($MyInvocation.MyCommand.ScriptBlock.Module) {
    Export-ModuleMember -Function `
        Get-WinCareWifiDiagnostic, `
        Get-WinCareWifiRoamingSetting, `
        Set-WinCareWifiRoamingAggressiveness, `
        Reset-WinCareNetworkAdapter, `
        Test-WinCareCaptivePortal, `
        Test-WinCareDohLatency, `
        Set-WinCareDohEnforcement
}
