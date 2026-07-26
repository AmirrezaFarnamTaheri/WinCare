Describe 'Advanced capability contracts' {
    BeforeAll {
        $Root=Split-Path -Parent $PSScriptRoot
        Import-Module (Join-Path $Root 'src/WinCare/WinCare.psd1') -Force
    }

    It 'exports every current advanced capability surface' {
        $required=@(
            'Get-WinCareFleetNodeInventory',
            'Compare-WinCareFleetPolicyState',
            'Get-WinCareHypervisorIsolationState',
            'New-WinCareVbsHardeningPlan',
            'New-WinCareMicroVmSandboxPlan',
            'New-WinCareTelemetryLakeIngestPlan',
            'Get-WinCareTelemetryLakeRecord',
            'Get-WinCareTelemetryLakeAggregate',
            'New-WinCareTelemetryLakeRetentionPlan',
            'Get-WinCareBinaryBehaviorProfile',
            'Get-WinCareBinaryIntelligenceCapability',
            'Get-WinCareVbsEnclaveState',
            'New-WinCareVbsEnclaveHardeningPlan',
            'Get-WinCareEbpfSocketRedirectCapability',
            'Get-WinCareEbpfProgramInventory',
            'New-WinCareEbpfSocketProgramAdmission',
            'Get-WinCareForensicTimeline',
            'Get-WinCareMemoryAnomalySummary',
            'New-WinCareWindowsSandboxConfigurationPlan',
            'Get-WinCareDisplayPipelineState',
            'Get-WinCareVirtualDisplayCapability',
            'New-WinCareDisplayCalibrationPlan',
            'Get-WinCareWireGuardTunnelState',
            'Get-WinCareQuicRuntimeCapability'
        )
        foreach($name in $required) {
            Get-Command $name -Module WinCare -ErrorAction Stop|Should -Not -BeNullOrEmpty
        }
    }

    It 'reports capability state without synthetic success claims' {
        $registry=Get-WinCareCapabilityRegistry
        $ids=@($registry.Capabilities.Id)
        foreach($id in @(
            'fleet.topology','forensics.timeline','display.pipeline','isolation.microvm',
            'telemetry.lake','binary.intelligence','security.vbs-dma',
            'network.socket-governance','network.quic','network.wireguard'
        )) {
            $ids|Should -Contain $id
        }
        $registry.EvidenceType|Should -Be 'DependencyAndRuntimeCapabilityProbe'
    }

    It 'keeps binary intelligence static and compiled' {
        $source=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/Providers/129-BinaryIntelligence.ps1') -Raw
        $native=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/Native/WinCare.FileIntelligence.cs') -Raw
        $source|Should -Match 'AnalyzePortableExecutable'
        $source|Should -Match 'ExecutionPerformed=\$false'
        $source|Should -Match 'DynamicInstrumentationPerformed=\$false'
        $source|Should -Not -Match 'Add-Type'
        $native|Should -Match 'PEReader'
        $native|Should -Match 'maximumFileBytes'
    }

    It 'keeps isolation changes transaction-backed and non-automatic' {
        $hyperv=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/Providers/124-HypervMicroVmSandbox.ps1') -Raw
        $forensics=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/Providers/134-ForensicsStudio.ps1') -Raw
        $hyperv|Should -Match 'New-WinCareCatalogPlan'
        $hyperv|Should -Match 'will not install or enable it implicitly'
        $forensics|Should -Match "Type 'WriteManagedFile'"
        $forensics|Should -Match 'LaunchPerformed=\$false'
    }

    It 'keeps eBPF, WireGuard, and QUIC free of covert interception behavior' {
        $ebpf=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/Providers/131-EbpfSocketGovernance.ps1') -Raw
        $network=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/Providers/136-NetworkAcceleration.ps1') -Raw
        $ebpf|Should -Match 'AutomaticPacketRedirectionSupported=\$false'
        $ebpf|Should -Match 'TransparentInterceptionSupported=\$false'
        $ebpf|Should -Match 'ExpectedProgramSha256'
        $network|Should -Match 'KeyMaterialCollected=\$false'
        $network|Should -Match 'TrafficEvasionSupported=\$false'
        $network|Should -Match 'ExpectedToolSha256'
    }

    It 'protects local telemetry with bounds, hashes, and typed writes' {
        $source=Get-Content -LiteralPath (Join-Path $Root 'src/WinCare/Providers/125-TelemetryDataLake.ps1') -Raw
        $source|Should -Match 'Protect-WinCareJsonRecord'
        $source|Should -Match 'RecordHash'
        $source|Should -Match 'MaximumShardBytes'
        $source|Should -Match "Type 'WriteManagedFile'"
        $source|Should -Match "Type 'RemoveManagedFile'"
    }

    It 'publishes advanced capabilities through the headless operator contract' {
        $commands=@(Get-WinCareHeadlessCommandName)
        foreach($name in @(
            'capabilities','fleet-inventory','fleet-policy-drift','hypervisor-isolation',
            'forensics-timeline','memory-anomalies','vbs-assurance','vbs-harden',
            'sandbox-config','telemetry-lake-records','telemetry-lake','telemetry-ingest',
            'telemetry-retention','binary-intelligence','binary-intelligence-capability',
            'ebpf-capability','ebpf-programs','ebpf-admit','display-pipeline',
            'virtual-display-capability','display-calibrate','wireguard-tunnels','quic-capability'
        )) {
            $commands|Should -Contain $name
        }
        $result=Invoke-WinCareHeadlessCommand -Command capabilities -JsonObject
        $result.EvidenceType|Should -Be 'DependencyAndRuntimeCapabilityProbe'
    }

    It 'keeps observation routes broker-readable and mutation routes approval-gated' {
        $readOnly=& (Get-Module WinCare) { @(Get-WinCareBrokerReadOnlyCommandName) }
        foreach($name in @(
            'capabilities','fleet-inventory','fleet-policy-drift','hypervisor-isolation',
            'forensics-timeline','memory-anomalies','vbs-assurance','telemetry-lake-records',
            'telemetry-lake','binary-intelligence','binary-intelligence-capability',
            'ebpf-capability','ebpf-programs','ebpf-admit','display-pipeline','virtual-display-capability',
            'wireguard-tunnels','quic-capability'
        )) {
            $readOnly|Should -Contain $name
        }
        foreach($name in @(
            'vbs-harden','sandbox-config','telemetry-ingest','telemetry-retention',
            'ebpf-admit','display-calibrate'
        )) {
            $readOnly|Should -Not -Contain $name
        }
    }

}
