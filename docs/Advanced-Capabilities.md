# Advanced Capabilities

This document describes current executable contracts, dependency boundaries, and explicit non-claims.

| Capability | Current implementation | Required dependency | Explicit boundary |
|---|---|---|---|
| ONNX diagnostics | bounded source-built host plus deterministic fallback | approved model and native host | fallback is labeled non-neural |
| ML-KEM | bounded digest-pinned OpenSSL adapter; authenticated file envelope | compatible OpenSSL | no bundled keys or runtime |
| WASI | bounded digest-pinned runtime and module execution | Wasmtime or Wasmer | no ambient authority or marketplace auto-install |
| eBPF | inventory and digest-pinned prebuilt program admission | eBPF for Windows | no driver installation or hidden interception |
| Fleet topology | authenticated bounded peer health and policy drift | configured peers | no Raft compliance claim |
| Cloud telemetry | admitted HTTPS endpoints and protected payloads | explicit configuration and credentials | no background or implicit egress |
| Forensics | bounded event timeline and memory/module anomaly correlation | Windows evidence providers | read-only; no target execution |
| Micro-VM isolation | deterministic Windows Sandbox configuration plan | pre-enabled Windows Sandbox | no implicit feature enablement or auto-launch |
| VBS/DMA assurance | Device Guard, DMA policy, BitLocker, page-file evidence; catalog-backed plans | supported Windows edition/hardware | no enclave guarantee inferred from configuration |
| Virtual display | installed-driver inventory and physical DDC/CI calibration plans | Windows display APIs | no driver build or installation |
| Telemetry lake | local protected, redacted, hash-verified, bounded daily shards | WinCare state store | no remote warehouse or hidden collection |
| Binary intelligence | compiled bounded hash, entropy, PE section, metadata, and signature inspection | source-built WinCare.Native.dll | no dynamic instrumentation or process injection |
| WireGuard | adapter inventory and optional digest-pinned CLI observation | optional WireGuard CLI | key material is not collected |
| QUIC | local .NET runtime capability probe | .NET QUIC support | no traffic mutation or evasion |

## Deliberately excluded

WinCare does not implement packet desynchronization, censorship bypass, covert redirection, kernel patching, automatic unsigned-driver installation, opaque remote command execution, autonomous production patch deployment, or unsupported performance claims.


## Headless operator routes

Observation routes are broker-readable and never acquire mutation authority:

```powershell
.\WinCare.ps1 -Command capabilities -Json
.\WinCare.ps1 -Command fleet-inventory -Json
.\WinCare.ps1 -Command forensics-timeline -Json
.\WinCare.ps1 -Command vbs-assurance -Json
.\WinCare.ps1 -Command binary-intelligence -ArgumentsJson '{"LiteralPath":"C:\\Windows\\System32\\notepad.exe"}' -Json
.\WinCare.ps1 -Command display-pipeline -Json
```

Plan-producing routes preview by default and require `-Apply` for execution:

```powershell
.\WinCare.ps1 -Command vbs-harden -ArgumentsJson '{"IncludeCredentialGuard":true,"IncludeHvci":true}' -Json
.\WinCare.ps1 -Command sandbox-config -ArgumentsJson '{"Id":"analysis-box","MappedFolder":"C:\\Cases"}' -Json
.\WinCare.ps1 -Command telemetry-retention -ArgumentsJson '{"RetentionDays":30}' -Json
.\WinCare.ps1 -Command display-calibrate -ArgumentsJson '{"DeviceName":"DISPLAY1","PhysicalIndex":0,"Brightness":45}' -Json
```

`ebpf-admit` creates a digest-bound admission record only. The exported high-impact runtime operation remains separately guarded by PowerShell `ShouldProcess`; admission itself does not attach a program.
