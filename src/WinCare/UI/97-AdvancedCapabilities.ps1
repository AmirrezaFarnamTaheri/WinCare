function Get-WinCareAdvancedHeadlessCommandName {
    @(
        'capabilities',
        'fleet-inventory',
        'fleet-policy-drift',
        'hypervisor-isolation',
        'forensics-timeline',
        'memory-anomalies',
        'vbs-assurance',
        'vbs-harden',
        'sandbox-config',
        'telemetry-lake-records',
        'telemetry-lake',
        'telemetry-ingest',
        'telemetry-retention',
        'binary-intelligence',
        'binary-intelligence-capability',
        'ebpf-capability',
        'ebpf-programs',
        'ebpf-admit',
        'display-pipeline',
        'virtual-display-capability',
        'display-calibrate',
        'wireguard-tunnels',
        'quic-capability'
    )
}

function Get-WinCareAdvancedBrokerReadOnlyCommandName {
    @(
        'capabilities',
        'fleet-inventory',
        'fleet-policy-drift',
        'hypervisor-isolation',
        'forensics-timeline',
        'memory-anomalies',
        'vbs-assurance',
        'telemetry-lake-records',
        'telemetry-lake',
        'binary-intelligence',
        'binary-intelligence-capability',
        'ebpf-capability',
        'ebpf-programs',
        'display-pipeline',
        'virtual-display-capability',
        'wireguard-tunnels',
        'quic-capability'
    )
}

function Get-WinCareAdvancedPeerParameters {
    [CmdletBinding()]
    param([hashtable]$Arguments=@{})

    $tokens=Get-WinCarePropertyValue $Arguments 'PeerBearerToken' @{}
    if($null -eq $tokens) { $tokens=@{} }
    if($tokens -isnot [hashtable]) {
        throw 'PeerBearerToken must be a hashtable keyed by peer URI.'
    }
    @{
        PeerUri=@(Get-WinCarePropertyValue $Arguments 'PeerUri' @())
        PeerBearerToken=$tokens
        StatePath=[string](Get-WinCarePropertyValue $Arguments 'StatePath' '')
        AllowPrivateNetworkPeers=[bool](Get-WinCarePropertyValue $Arguments 'AllowPrivateNetworkPeers' $false)
        TimeoutSeconds=[int](Get-WinCarePropertyValue $Arguments 'TimeoutSeconds' 5)
    }
}

function Invoke-WinCareAdvancedHeadlessCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Command,
        [hashtable]$Arguments=@{},
        [switch]$Apply
    )

    if($Command -notin (Get-WinCareAdvancedHeadlessCommandName)) {
        throw "Unknown advanced WinCare command: $Command"
    }

    switch($Command) {
        'capabilities' {
            Get-WinCareCapabilityRegistry
        }
        'fleet-inventory' {
            $peer=Get-WinCareAdvancedPeerParameters -Arguments $Arguments
            Get-WinCareFleetNodeInventory @peer
        }
        'fleet-policy-drift' {
            $peer=Get-WinCareAdvancedPeerParameters -Arguments $Arguments
            Compare-WinCareFleetPolicyState -PolicyPath ([string]$Arguments.PolicyPath) @peer
        }
        'hypervisor-isolation' {
            Get-WinCareHypervisorIsolationState
        }
        'forensics-timeline' {
            Get-WinCareForensicTimeline `
                -Since ([datetime](Get-WinCarePropertyValue $Arguments 'Since' ([datetime]::Now.AddHours(-24)))) `
                -Until ([datetime](Get-WinCarePropertyValue $Arguments 'Until' ([datetime]::Now))) `
                -MaximumEvents ([int](Get-WinCarePropertyValue $Arguments 'MaximumEvents' 5000)) `
                -MaximumMessageCharacters ([int](Get-WinCarePropertyValue $Arguments 'MaximumMessageCharacters' 512)) `
                -Source @((Get-WinCarePropertyValue $Arguments 'Source' @('System','Security','PowerShell','Defender','Sysmon')))
        }
        'memory-anomalies' {
            Get-WinCareMemoryAnomalySummary `
                -Since ([datetime](Get-WinCarePropertyValue $Arguments 'Since' ([datetime]::Now.AddHours(-24)))) `
                -MaximumModules ([int](Get-WinCarePropertyValue $Arguments 'MaximumModules' 1000)) `
                -MaximumRemoteThreadEvents ([int](Get-WinCarePropertyValue $Arguments 'MaximumRemoteThreadEvents' 500))
        }
        'vbs-assurance' {
            Get-WinCareVbsEnclaveState
        }
        'vbs-harden' {
            $plan=New-WinCareVbsEnclaveHardeningPlan `
                -IncludeCredentialGuard:([bool](Get-WinCarePropertyValue $Arguments 'IncludeCredentialGuard' $false)) `
                -IncludeHvci:([bool](Get-WinCarePropertyValue $Arguments 'IncludeHvci' $false))
            Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply
        }
        'sandbox-config' {
            $plan=New-WinCareMicroVmSandboxPlan `
                -Id ([string]$Arguments.Id) `
                -MappedFolder ([string]$Arguments.MappedFolder) `
                -EnableNetworking:([bool](Get-WinCarePropertyValue $Arguments 'EnableNetworking' $false)) `
                -EnableClipboard:([bool](Get-WinCarePropertyValue $Arguments 'EnableClipboard' $false)) `
                -MemoryInMB ([int](Get-WinCarePropertyValue $Arguments 'MemoryInMB' 4096))
            Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply
        }
        'telemetry-lake-records' {
            Get-WinCareTelemetryLakeRecord `
                -Since ([datetime](Get-WinCarePropertyValue $Arguments 'Since' ([datetime]::UtcNow.AddDays(-1)))) `
                -Until ([datetime](Get-WinCarePropertyValue $Arguments 'Until' ([datetime]::UtcNow))) `
                -Name ([string](Get-WinCarePropertyValue $Arguments 'Name' '')) `
                -MaximumRecords ([int](Get-WinCarePropertyValue $Arguments 'MaximumRecords' 10000)) `
                -MaximumShardBytes ([long](Get-WinCarePropertyValue $Arguments 'MaximumShardBytes' 33554432))
        }
        'telemetry-lake' {
            Get-WinCareTelemetryLakeAggregate `
                -Since ([datetime](Get-WinCarePropertyValue $Arguments 'Since' ([datetime]::UtcNow.AddDays(-1)))) `
                -Until ([datetime](Get-WinCarePropertyValue $Arguments 'Until' ([datetime]::UtcNow))) `
                -MaximumRecords ([int](Get-WinCarePropertyValue $Arguments 'MaximumRecords' 10000))
        }
        'telemetry-ingest' {
            $record=Get-WinCarePropertyValue $Arguments 'Record' $null
            if($null -eq $record) { throw 'Record is required.' }
            $plan=New-WinCareTelemetryLakeIngestPlan `
                -Name ([string]$Arguments.Name) `
                -Record $record `
                -Timestamp ([datetime](Get-WinCarePropertyValue $Arguments 'Timestamp' ([datetime]::UtcNow))) `
                -MaximumShardBytes ([long](Get-WinCarePropertyValue $Arguments 'MaximumShardBytes' 33554432))
            Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply
        }
        'telemetry-retention' {
            $plan=New-WinCareTelemetryLakeRetentionPlan `
                -RetentionDays ([int](Get-WinCarePropertyValue $Arguments 'RetentionDays' 30))
            Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply
        }
        'binary-intelligence' {
            Get-WinCareBinaryBehaviorProfile `
                -LiteralPath ([string]$Arguments.LiteralPath) `
                -MaximumFileBytes ([long](Get-WinCarePropertyValue $Arguments 'MaximumFileBytes' 268435456)) `
                -MaximumSections ([int](Get-WinCarePropertyValue $Arguments 'MaximumSections' 96))
        }
        'binary-intelligence-capability' {
            Get-WinCareBinaryIntelligenceCapability
        }
        'ebpf-capability' {
            Get-WinCareEbpfSocketRedirectCapability
        }
        'ebpf-programs' {
            $parameters=@{}
            $toolPath=[string](Get-WinCarePropertyValue $Arguments 'ToolPath' '')
            if($toolPath) {
                $parameters.ToolPath=$toolPath
                $parameters.ExpectedToolSha256=[string]$Arguments.ExpectedToolSha256
            }
            Get-WinCareEbpfProgramInventory @parameters
        }
        'ebpf-admit' {
            $parameters=@{
                ProgramPath=[string]$Arguments.ProgramPath
                ExpectedProgramSha256=[string]$Arguments.ExpectedProgramSha256
                Hook=[string](Get-WinCarePropertyValue $Arguments 'Hook' 'SockOps')
            }
            $interface=[string](Get-WinCarePropertyValue $Arguments 'Interface' '')
            if($interface) { $parameters.Interface=$interface }
            $toolPath=[string](Get-WinCarePropertyValue $Arguments 'ToolPath' '')
            if($toolPath) {
                $parameters.ToolPath=$toolPath
                $parameters.ExpectedToolSha256=[string]$Arguments.ExpectedToolSha256
            }
            New-WinCareEbpfSocketProgramAdmission @parameters
        }
        'display-pipeline' {
            Get-WinCareDisplayPipelineState `
                -MaximumDisplays ([int](Get-WinCarePropertyValue $Arguments 'MaximumDisplays' 16))
        }
        'virtual-display-capability' {
            Get-WinCareVirtualDisplayCapability
        }
        'display-calibrate' {
            $parameters=@{
                DeviceName=[string]$Arguments.DeviceName
                PhysicalIndex=[int]$Arguments.PhysicalIndex
            }
            if($Arguments.ContainsKey('Brightness')) { $parameters.Brightness=[int]$Arguments.Brightness }
            if($Arguments.ContainsKey('Contrast')) { $parameters.Contrast=[int]$Arguments.Contrast }
            $plan=New-WinCareDisplayCalibrationPlan @parameters
            Invoke-WinCareHeadlessPlan -Plan $plan -Apply:$Apply
        }
        'wireguard-tunnels' {
            $parameters=@{
                MaximumTunnels=[int](Get-WinCarePropertyValue $Arguments 'MaximumTunnels' 64)
            }
            $toolPath=[string](Get-WinCarePropertyValue $Arguments 'ToolPath' '')
            if($toolPath) {
                $parameters.ToolPath=$toolPath
                $parameters.ExpectedToolSha256=[string]$Arguments.ExpectedToolSha256
            }
            Get-WinCareWireGuardTunnelState @parameters
        }
        'quic-capability' {
            Get-WinCareQuicRuntimeCapability
        }
    }
}
