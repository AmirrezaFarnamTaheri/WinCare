# WinCare

WinCare is a safety-first Windows operations and diagnostics workstation. PowerShell owns orchestration and Windows administration; source-built .NET 8 components own narrow compiled primitives; Python owns deterministic packaging and independent archive verification.

The product exposes a WPF interface, an interactive terminal interface, and machine-readable headless commands over one typed transaction engine.

## Execution model

A state-changing request follows one path:

1. construct a typed plan;
2. validate action contracts, policy, compatibility, and scope;
3. preview risk and affected resources;
4. require explicit approval and elevation when necessary;
5. execute through bounded providers;
6. verify observable evidence or postconditions;
7. journal the result and compensate completed reversible actions after downstream failure.

Missing platform dependencies produce explicit `Blocked` or `Unsupported` results. WinCare does not record synthetic success.

## Capability groups

- application, AppX, winget, repair, and uninstall workflows;
- bounded cleanup, storage, duplicate, relocation, and disk-pressure analysis;
- startup, service, task, feature, capability, and maintenance operations;
- Defender, firewall, Sysmon, WDAC, policy, VBS, HVCI, Credential Guard, DMA, and firmware evidence;
- Windows Update Agent discovery, history, download, install, hide, and removal plans;
- network inventory, endpoint measurement, DoH, reversible TCP experiments, process microsegmentation, WireGuard inventory, QUIC capability, and eBPF program governance;
- ETW capture, process/module inspection, forensic timelines, injection-surface quarantine, and static binary intelligence;
- page-file, health, recovery, automation, reporting, offline servicing, workspace, display, and Windows Sandbox configuration;
- dependency-backed ONNX, ML-KEM, WASI, eBPF, fleet, and cloud adapters with explicit prerequisite and evidence boundaries.

See [Advanced Capabilities](docs/Advanced-Capabilities.md) and [Capability Matrix](docs/CAPABILITY-MATRIX.md).

## Requirements

- supported 64-bit Windows 10 or Windows 11;
- PowerShell 7.2 or later;
- .NET 8 SDK only for native builds;
- administrator approval only for contracts that require elevation.

Optional dependencies are reported by `Get-WinCareCapabilityRegistry`.

## Run

```powershell
# Terminal interface
.\WinCare.ps1

# WPF interface
.\WinCare.ps1 -Gui

# Strict non-persistent inspection
.\WinCare.ps1 -ReadOnly

# Machine-readable query
.\WinCare.ps1 -Command system -Json

# Capability discovery
Import-Module .\src\WinCare\WinCare.psd1 -Force
Get-WinCareCapabilityRegistry

# Advanced read-only headless routes
.\WinCare.ps1 -Command capabilities -Json
.\WinCare.ps1 -Command forensics-timeline -Json
.\WinCare.ps1 -Command vbs-assurance -Json
.\WinCare.ps1 -Command display-pipeline -Json
```

Mutation commands default to preview. Add `-Apply` only after reviewing the generated plan and risk.

## Install and validate

```powershell
.\Install-WinCare.ps1
.\tools\Invoke-WindowsValidation.ps1 -OutputDirectory .\artifacts\windows-validation
.\tools\Build-Release.ps1 -OutputDirectory .\artifacts
```

Production promotion requires the complete Windows validation gate, source-built native binaries, deterministic package verification, clean installation, and uninstall verification against the same immutable artifact bytes.
