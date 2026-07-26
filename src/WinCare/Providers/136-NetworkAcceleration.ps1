#requires -Version 7.2

function Get-WinCareWireGuardTunnelState {
    [CmdletBinding()]
    param(
        [string]$ToolPath,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedToolSha256,
        [ValidateRange(1,256)][int]$MaximumTunnels=64
    )

    if(-not $IsWindows) {
        return [pscustomobject]@{
            Supported=$false
            Tunnels=@()
            Reason='Windows is required.'
            EvidenceType='PlatformRequirement'
        }
    }

    $adapters=@(Get-NetAdapter -ErrorAction SilentlyContinue|Where-Object {
        $_.InterfaceDescription -match '(?i)wireguard' -or $_.Name -match '(?i)wireguard'
    }|Select-Object -First $MaximumTunnels|ForEach-Object {
        [pscustomobject]@{
            Name=[string]$_.Name
            InterfaceDescription=[string]$_.InterfaceDescription
            Status=[string]$_.Status
            LinkSpeed=[string]$_.LinkSpeed
            MacAddressHash=if($_.MacAddress){Get-WinCareSha256Text -Text ([string]$_.MacAddress)}else{$null}
            EvidenceType='NetAdapterObservation'
        }
    })

    $runtime=$null
    if($ToolPath) {
        if(-not $ExpectedToolSha256) {
            throw 'ExpectedToolSha256 is required for a custom WireGuard tool path.'
        }
        $runtime=Invoke-WinCareProcess -FilePath $ToolPath `
            -ArgumentList @('show','all','dump') `
            -ExpectedSha256 $ExpectedToolSha256 `
            -TimeoutSeconds 15 `
            -MaximumCapturedOutputBytes 1048576
    }

    [pscustomobject]@{
        Supported=$true
        CapturedAt=[datetime]::UtcNow.ToString('o')
        Tunnels=$adapters
        RuntimeProbe=if($runtime){
            [pscustomobject]@{
                Success=[bool]$runtime.Success
                ExitCode=$runtime.ExitCode
                OutputHash=Get-WinCareSha256Text -Text ([string]$runtime.Data.StdOut)
                StandardError=[string]$runtime.Data.StdErr
            }
        }else{$null}
        ConfigurationMutationSupported=$false
        KeyMaterialCollected=$false
        EvidenceType='WireGuardAdapterAndOptionalDigestPinnedRuntimeObservation'
    }
}

function Get-WinCareQuicRuntimeCapability {
    [CmdletBinding()]
    param()

    $type='System.Net.Quic.QuicConnection' -as [type]
    $supported=$false
    if($type) {
        try { $supported=[bool]$type::IsSupported }
        catch { $supported=$false }
    }
    [pscustomobject]@{
        Available=$supported
        RuntimeType=if($type){$type.FullName}else{$null}
        ActiveProbePerformed=$false
        DatagramMutationSupported=$false
        TrafficEvasionSupported=$false
        Reason=if($supported){
            'The installed .NET runtime reports QUIC support.'
        }else{
            'The installed .NET runtime does not expose supported QUIC transport.'
        }
        EvidenceType='DotNetQuicCapabilityProbe'
    }
}
