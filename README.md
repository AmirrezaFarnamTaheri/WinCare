# WinCare

<p align="center">
  <strong>Safety-first Windows operations, diagnostics, and assurance workstation</strong>
</p>

WinCare is a Windows administration and diagnostics platform designed around one principle: **every operation must be explainable, bounded, observable, and verifiable**.

It combines:

- **PowerShell** for orchestration and Windows administration;
- **source-built .NET 8 components** for narrow compiled primitives;
- **Python tooling** for deterministic packaging and independent artifact verification.

WinCare provides three operator surfaces over one governed transaction model:

- 🖥️ WPF graphical workstation
- ⌨️ interactive terminal interface
- 🤖 machine-readable headless commands

---

## Design principles

WinCare is not a collection of unsafe one-click tweaks. Every state-changing workflow follows a controlled lifecycle:

1. Build a typed action plan.
2. Validate contracts, policy, compatibility, and scope.
3. Preview affected resources and risk.
4. Request approval/elevation when required.
5. Execute through bounded providers.
6. Verify evidence or postconditions.
7. Journal results and compensate reversible actions when possible.

Unavailable dependencies are reported as `Blocked`, `Unsupported`, or `Unknown` rather than being converted into fake success.

---

## What WinCare can do

### System operations

- application, AppX, MSIX, and winget workflows
- repair and uninstall operations
- startup, services, scheduled tasks, optional features, and capabilities
- maintenance planning and execution

### Security and assurance

- Microsoft Defender and firewall visibility
- Sysmon, WDAC, VBS, HVCI, Credential Guard, DMA, TPM, and firmware evidence
- security baseline inspection
- bounded remediation plans

### Diagnostics and forensics

- ETW-based evidence capture
- process/module inspection
- forensic timelines
- static binary intelligence
- injection-surface review

### Storage, networking, and reliability

- cleanup planning
- duplicate analysis
- disk-pressure analysis
- adapter/network inventory
- endpoint measurements
- reversible TCP experiments
- QUIC, WireGuard, and eBPF capability boundaries

### Advanced adapters

Optional integrations include ONNX diagnostics, ML-KEM, WASI, fleet contracts, cloud telemetry adapters, and sandbox workflows. Each capability declares its dependency requirements and limitations.

See:

- [Capability Matrix](docs/CAPABILITY-MATRIX.md)
- [Advanced Capabilities](docs/Advanced-Capabilities.md)
- [Compatibility Model](docs/Compatibility.md)

---

## Requirements

Supported environments:

- Windows 10 22H2 or supported Windows 11 editions
- PowerShell 7.2+
- x64 systems (ARM64 support depends on available Windows providers)

For native development builds:

- .NET 8 SDK
- Python 3 tooling

Optional dependencies are detected dynamically through:

```powershell
Get-WinCareCapabilityRegistry
```

---

## Quick start

### Terminal mode

```powershell
.\WinCare.ps1
```

### GUI mode

```powershell
.\WinCare.ps1 -Gui
```

### Safe read-only inspection

```powershell
.\WinCare.ps1 -ReadOnly
```

### JSON automation mode

```powershell
.\WinCare.ps1 -Command system -Json
```

### Discover capabilities

```powershell
Import-Module .\src\WinCare\WinCare.psd1 -Force
Get-WinCareCapabilityRegistry
```

---

## Mutation safety model

Mutation commands preview by default.

Review the generated plan first, then explicitly apply:

```powershell
.\WinCare.ps1 -Command <operation> -Apply
```

WinCare avoids:

- hidden background changes;
- implicit downloads;
- synthetic health scores;
- unsupported fallbacks;
- unrestricted remote administration.

---

## Standalone executables

Release builds provide self-contained Windows executables:

- `WinCare.exe`
- `WinCare-GUI.exe`
- `WinCare-TUI.exe`

Standalone packages are produced from verified payloads with deterministic metadata, hashes, and release evidence.

---

## Installation

Install:

```powershell
.\Install-WinCare.ps1
```

Validate a Windows environment:

```powershell
.\tools\Invoke-WindowsValidation.ps1 -OutputDirectory .\artifacts\windows-validation
```

Build release artifacts:

```powershell
.\tools\Build-Release.ps1 -OutputDirectory .\artifacts
```

---

## Validation and release assurance

WinCare uses layered validation:

- source topology validation;
- contract and manifest validation;
- security invariant checks;
- bounded I/O checks;
- network/process boundary checks;
- PowerShell tests;
- native .NET builds;
- installation lifecycle validation;
- deterministic release verification.

A development artifact is not considered a production release until the exact published bytes pass the release verification pipeline.

See:

- [Validation](VALIDATION.md)
- [Testing](docs/Testing.md)
- [Release Notes](CHANGELOG.md)

---

## Documentation map

| Document | Purpose |
| --- | --- |
| `docs/CAPABILITY-MATRIX.md` | authoritative feature inventory |
| `docs/Advanced-Capabilities.md` | advanced dependency boundaries and non-claims |
| `docs/Compatibility.md` | supported platforms and degradation behavior |
| `docs/DESIGN.md` | UI and interaction design system |
| `docs/Testing.md` | validation procedures |
| `VALIDATION.md` | evidence and release gates |

---

## Contributing

Before submitting changes:

1. keep behavior evidence-based;
2. preserve existing safety boundaries;
3. add tests for new contracts;
4. update documentation when capabilities change;
5. verify the Windows validation gate.

---

## License

See the repository license file for usage terms.
