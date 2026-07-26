#requires -Version 7.2

function Test-WinCareEbpfRuntimeCapability {
    [CmdletBinding()]
    param()

    if(-not $IsWindows) {
        return [pscustomobject]@{
            Available=$false
            Reason='Windows required.'
            Evidence=$null
        }
    }
    $netsh=Get-Command netsh.exe -CommandType Application -ErrorAction SilentlyContinue
    if(-not $netsh) {
        return [pscustomobject]@{
            Available=$false
            Reason='netsh.exe was not found.'
            Evidence=$null
        }
    }
    try {
        $probe=Invoke-WinCareProcess `
            -FilePath $netsh.Source `
            -ArgumentList @('ebpf','show','programs') `
            -TimeoutSeconds 15 `
            -SuccessExitCodes @(0)
        [pscustomobject]@{
            Available=[bool]$probe.Success
            Reason=if($probe.Success){''}else{'The eBPF runtime probe failed.'}
            Evidence=[pscustomobject]@{
                ExitCode=$probe.ExitCode
                OutputHash=Get-WinCareSha256Text -Text ([string]$probe.Data.StdOut)
                StandardError=[string]$probe.Data.StdErr
            }
        }
    } catch {
        [pscustomobject]@{
            Available=$false
            Reason=$_.Exception.Message
            Evidence=$null
        }
    }
}

function New-WinCareCapabilityRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][bool]$Available,
        [Parameter(Mandatory)][string]$Mode,
        [Parameter(Mandatory)][string]$Dependency,
        [Parameter(Mandatory)][string]$Reason,
        [object]$Evidence
    )
    [pscustomobject]@{
        Id=$Id
        Title=$Title
        Available=$Available
        Mode=$Mode
        Dependency=$Dependency
        Reason=$Reason
        Evidence=$Evidence
    }
}

function Get-WinCareCapabilityRegistry {
    [CmdletBinding()]
    param()

    $openssl=Get-WinCareOpenSslPqcCapability
    $wasi=Get-WinCareWasiRuntime
    $ebpf=Test-WinCareEbpfRuntimeCapability
    $binary=Get-WinCareBinaryIntelligenceCapability
    $socketRedirect=Get-WinCareEbpfSocketRedirectCapability
    $quic=Get-WinCareQuicRuntimeCapability
    $hypervisor=Get-WinCareHypervisorIsolationState
    $virtualDisplay=Get-WinCareVirtualDisplayCapability
    $cloudConfig=Join-Path $PSScriptRoot '..\..\..\config\multicloud-targets.json'
    $onnxHost=Join-Path $PSScriptRoot '..\Native\WinCare.OnnxHost.exe'
    $windows=[bool]$IsWindows

    $capabilities=@(
        New-WinCareCapabilityRecord -Id 'threat.sysmon' -Title 'Sysmon threat mapping' `
            -Available ([bool]($windows -and (Get-Command Get-WinEvent -ErrorAction SilentlyContinue))) `
            -Mode 'Native' -Dependency 'Windows Event Log / Sysmon' `
            -Reason $(if($windows){'Sysmon presence is validated per call.'}else{'Windows required.'})
        New-WinCareCapabilityRecord -Id 'network.doh' -Title 'DNS-over-HTTPS query' `
            -Available $true -Mode 'HTTPS adapter' -Dependency 'Configured DoH endpoint' `
            -Reason 'Endpoint admission and response content are validated per call.'
        New-WinCareCapabilityRecord -Id 'storage.reliability' -Title 'Storage reliability counters' `
            -Available ([bool]($windows -and (Get-Command Get-PhysicalDisk -ErrorAction SilentlyContinue))) `
            -Mode 'Native' -Dependency 'Storage module' `
            -Reason $(if($windows){'Physical disk support is validated per call.'}else{'Windows required.'})
        New-WinCareCapabilityRecord -Id 'diagnostics.onnx' -Title 'ONNX diagnostic model' `
            -Available (Test-Path -LiteralPath $onnxHost -PathType Leaf) `
            -Mode 'External verified host' -Dependency 'Approved model and WinCare.OnnxHost.exe' `
            -Reason $(if(Test-Path -LiteralPath $onnxHost -PathType Leaf){
                'Model and host hashes are validated per call.'
            }else{'The native host is absent; only the labeled deterministic diagnostic path is available.'})
        New-WinCareCapabilityRecord -Id 'network.ebpf' -Title 'eBPF for Windows adapter' `
            -Available ([bool]$ebpf.Available) -Mode 'Dependency adapter' `
            -Dependency 'eBPF for Windows netsh helper' -Reason $ebpf.Reason -Evidence $ebpf.Evidence
        New-WinCareCapabilityRecord -Id 'firmware.uefi' -Title 'UEFI Secure Boot evidence' `
            -Available ([bool]($windows -and (Get-Command Get-SecureBootUEFI -ErrorAction SilentlyContinue))) `
            -Mode 'Native evidence' -Dependency 'UEFI Secure Boot cmdlets' `
            -Reason $(if($windows){'Firmware access and permissions are validated per call.'}else{'Windows required.'})
        New-WinCareCapabilityRecord -Id 'crypto.mlkem' -Title 'ML-KEM cryptography' `
            -Available ([bool]$openssl.Available) -Mode 'OpenSSL adapter' `
            -Dependency 'OpenSSL with ML-KEM KEM support' -Reason $openssl.Reason
        New-WinCareCapabilityRecord -Id 'plugins.wasi' -Title 'WASI plugin execution' `
            -Available ($null -ne $wasi) -Mode 'Runtime adapter' -Dependency 'Wasmtime or Wasmer' `
            -Reason $(if($wasi){'Runtime and module hashes are validated per call.'}else{'No WASI runtime found.'})
        New-WinCareCapabilityRecord -Id 'fleet.local' -Title 'Loopback fleet coordination' `
            -Available ([bool](
                $null -ne (Get-Command Start-ThreadJob -ErrorAction SilentlyContinue) -and
                $null -ne ('System.Net.HttpListener' -as [type])
            )) `
            -Mode 'Loopback quorum coordination' -Dependency 'ThreadJob and HttpListener' `
            -Reason 'Raft compliance is not claimed.'
        New-WinCareCapabilityRecord -Id 'fleet.cloud' -Title 'Configured cloud telemetry' `
            -Available (Test-Path -LiteralPath $cloudConfig -PathType Leaf) `
            -Mode 'HTTPS adapter' -Dependency 'Explicit target configuration and credentials' `
            -Reason $(if(Test-Path -LiteralPath $cloudConfig -PathType Leaf){
                'Targets and credentials are validated per call.'
            }else{'No cloud target configuration exists.'})
        New-WinCareCapabilityRecord -Id 'fleet.topology' -Title 'Fleet topology and drift' `
            -Available ([bool]($windows -and (Get-Command Start-ThreadJob -ErrorAction SilentlyContinue))) `
            -Mode 'Authenticated bounded coordinator' -Dependency 'Configured admitted peers' `
            -Reason 'Peer health and policy hashes are collected without claiming Raft consensus.'
        New-WinCareCapabilityRecord -Id 'forensics.timeline' -Title 'DFIR timeline and anomaly correlation' `
            -Available ([bool]($windows -and (Get-Command Get-WinEvent -ErrorAction SilentlyContinue))) `
            -Mode 'Read-only Windows evidence' -Dependency 'Windows event and process providers' `
            -Reason $(if($windows){'Evidence sources are validated per call.'}else{'Windows required.'})
        New-WinCareCapabilityRecord -Id 'display.pipeline' -Title 'Display pipeline governance' `
            -Available ([bool]$virtualDisplay.Available) -Mode 'Driver audit and DDC/CI plans' `
            -Dependency 'Windows display APIs' -Reason $virtualDisplay.Reason
        New-WinCareCapabilityRecord -Id 'isolation.microvm' -Title 'Windows Sandbox isolation' `
            -Available ([bool]$hypervisor.WindowsSandboxAvailable) -Mode 'Configuration-only micro-VM plan' `
            -Dependency 'Pre-enabled Windows Sandbox' `
            -Reason $(if($hypervisor.WindowsSandboxAvailable){
                'Configuration creation is available; feature installation and auto-launch are excluded.'
            }else{'Windows Sandbox is not enabled.'})
        New-WinCareCapabilityRecord -Id 'telemetry.lake' -Title 'Local telemetry lake' `
            -Available $true -Mode 'Bounded protected local NDJSON' `
            -Dependency 'WinCare protected-record and transaction stores' `
            -Reason 'Records are redacted, hash-verified, bounded, and retained locally.'
        New-WinCareCapabilityRecord -Id 'binary.intelligence' -Title 'Static binary intelligence' `
            -Available ([bool]$binary.Available) -Mode $binary.Mode `
            -Dependency $binary.Dependency -Reason $binary.Reason
        New-WinCareCapabilityRecord -Id 'security.vbs-dma' -Title 'VBS and DMA assurance' `
            -Available $windows -Mode 'Native evidence and catalog-backed plans' `
            -Dependency 'Device Guard, DMA policy, and BitLocker providers' `
            -Reason $(if($windows){'Evidence is collected without inferring enclave guarantees.'}else{'Windows required.'})
        New-WinCareCapabilityRecord -Id 'network.socket-governance' `
            -Title 'eBPF socket-policy governance' -Available ([bool]$socketRedirect.Available) `
            -Mode 'Digest-pinned prebuilt program admission' -Dependency 'eBPF for Windows' `
            -Reason $socketRedirect.Reason
        New-WinCareCapabilityRecord -Id 'network.quic' -Title 'QUIC runtime diagnostics' `
            -Available ([bool]$quic.Available) -Mode 'Local runtime capability probe' `
            -Dependency '.NET QUIC support' -Reason $quic.Reason
        New-WinCareCapabilityRecord -Id 'network.wireguard' -Title 'WireGuard tunnel inventory' `
            -Available $windows -Mode 'Adapter audit with optional digest-pinned runtime probe' `
            -Dependency 'Windows networking and optional WireGuard CLI' `
            -Reason $(if($windows){'Tunnel state can be inventoried without collecting key material.'}else{'Windows required.'})
    )

    [pscustomobject]@{
        CapturedAt=[datetime]::UtcNow
        Capabilities=$capabilities
        AvailableCount=@($capabilities|Where-Object Available).Count
        UnavailableCount=@($capabilities|Where-Object { -not $_.Available }).Count
        EvidenceType='DependencyAndRuntimeCapabilityProbe'
    }
}
