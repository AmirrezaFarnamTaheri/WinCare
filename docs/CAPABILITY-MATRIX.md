# WinCare capability matrix

This matrix is authoritative for the capability areas implemented in the current WinCare source tree. It does not assert that every capability is available on every machine.

A capability is usable only when:

1. its implementation is present in the closed source/module manifests;
2. the local Windows mechanism or admitted optional dependency is available;
3. policy, target identity, scope, and required authority permit the request;
4. the capability can produce the evidence required by its contract.

Inspect the current machine state with:

```powershell
Import-Module .\src\WinCare\WinCare.psd1 -Force
Get-WinCareCapabilityRegistry
```

## Reading the matrix

- **Implementation owner** identifies the layer responsible for the capability, not a blanket authority grant.
- **Prerequisite** identifies the important local mechanism; providers may impose additional target-specific validation.
- **Mutation** describes the maximum capability class. Individual routes may be observation-only or plan-only.
- **Assurance behavior** describes the evidence and non-claims that prevent unavailable or incomplete behavior from being reported as success.

Common runtime states include `Available`, `ReadOnly`, `Blocked`, `Unsupported`, and `Unknown`. See [Compatibility](Compatibility.md) for degradation behavior and [Commands](COMMANDS.md) for invocation patterns.

## Matrix

| Area | Capability | Implementation owner | Prerequisite | Mutation | Assurance behavior |
|---|---|---|---|---|---|
| Core | Typed actions, plans, policy, preview, journal, receipt, compensation | PowerShell | PowerShell 7.2+ | Yes | Dispatcher/contract parity is validated; mutators require evidence or postconditions |
| Applications | Registry app, AppX, winget install/upgrade/repair/removal | PowerShell + Windows APIs | Relevant Windows feature/tool | Yes | Exact package/app identity and exit status are recorded |
| Cleanup | Targeted cleanup and admitted cleanup catalogs | PowerShell | Valid bounded target and policy | Yes | Canonical roots, age/pattern/byte limits, reparse rejection, hashes, and post-delete checks |
| Servicing | DISM/SFC, optional features, capabilities, offline images | PowerShell + Windows tools | Windows and elevation | Yes | Exact arguments and exit codes; routine cleanup does not use `/ResetBase` |
| Security | Defender, firewall, hosts, WDAC, Sysmon, security controls | PowerShell + Windows APIs | Windows; some operations require elevation | Yes | State is re-read after changes; unavailable checks remain `Unknown` or `Blocked` |
| Security | VBS, HVCI, Credential Guard, DMA, BitLocker, page-file assurance | PowerShell + CIM/registry/catalog | Supported Windows edition and hardware | Read-only + plans | Configuration is observed directly; no enclave guarantee is inferred from settings |
| Windows Update | Search, history, hide, download, install, uninstall | PowerShell + Microsoft Update COM | Windows Update Agent | Yes | Selected update identities and observed results are returned |
| Network | Adapter/TCP state, DoH, captive portal, endpoint measurements | PowerShell + bounded HTTP/socket brokers | Explicit target/network access | Mostly read-only | Redirects, metadata targets, unsafe credentials, and excess responses are rejected |
| Network | Process microsegmentation | PowerShell + Defender Firewall | Resolvable executable path and elevation | Yes | Exact executable path and resulting firewall rule are verified |
| Network | eBPF inventory and digest-pinned admission | PowerShell + eBPF for Windows CLI | Compatible `netsh ebpf` runtime | Explicit direct operation | Program/runtime hashes, bounded output, and runtime results are recorded; no hidden interception |
| Network | WireGuard inventory and QUIC capability | PowerShell + Windows/.NET | Optional WireGuard CLI; .NET QUIC support | No | No key collection, packet mutation, evasion, or synthetic throughput claim |
| Diagnostics | ETW, event timeline, process/module and injection-surface review | PowerShell + Windows evidence APIs | Windows | Capture/quarantine may mutate | Evidence paths, event limits, counts, and exact registry state are recorded |
| Diagnostics | Static binary intelligence | Source-built C#/.NET 8 | `WinCare.Native.dll` | No | Bounded SHA-256, entropy, PE metadata/sections, signature evidence; target is never executed |
| Storage | Exact and sampled duplicate analysis | PowerShell + compiled file fingerprinting | Readable directory; native runtime for compiled sampling | No | Reports candidates and savings; does not delete duplicates |
| Storage | Reliability counters | PowerShell + Windows Storage module | Physical disk provider support | No | Unknown counters remain unknown; no fabricated health percentage |
| Isolation | Windows Sandbox configuration and hypervisor audit | PowerShell + Windows optional-feature/CIM APIs | Pre-enabled Windows Sandbox for configuration | Generates plan/file | No implicit feature enablement, driver installation, or automatic launch |
| Display | Display pipeline, virtual-display driver audit, DDC/CI calibration | PowerShell + Win32/CIM/native interop | Compatible display and DDC/CI support | Calibration plans only | Installed drivers are observed; no virtual-display driver is built or installed |
| Telemetry | Local protected telemetry lake | PowerShell protected records | WinCare state store | Yes | Redacted, hash-verified, bounded daily shards and transaction-backed retention |
| AI/ML | ONNX diagnostics | PowerShell + source-built host | Approved model and `WinCare.OnnxHost.exe` | No | Model execution is claimed only after successful bounded process output; fallback is labeled non-neural |
| Cryptography | ML-KEM exchange and authenticated file envelope | PowerShell + digest-pinned OpenSSL | OpenSSL exposing compatible ML-KEM operations | Yes | Shared-secret evidence, authenticated metadata, and file hashes are verified |
| Plugins | WASI execution | PowerShell + digest-pinned Wasmtime/Wasmer | Runtime plus admitted local module | Plugin-defined | Module/runtime hashes, exit code, timeout, output bounds, and preopened directories are recorded |
| Firmware | Secure Boot/dbx and TPM evidence | PowerShell + Windows firmware/TPM APIs | Compatible UEFI/TPM platform | No | Reports observed evidence and collection errors; does not claim bootkit absence |
| Remediation | Reviewed catalog materialization and patch validation | PowerShell + pinned external sandbox boundary | Reviewed entry and declared external isolation contract | Generates artifacts / executes supplied script | Refuses invented patches; process-only execution is not called a sandbox |
| Fleet | Authenticated topology and policy drift | PowerShell + bounded HTTP broker | Explicit peers and per-peer bearer tokens | No | Peer admission, protocol, response hash, reachability, and policy drift are explicit; no Raft claim |
| Cloud | Telemetry and endpoint health | PowerShell + bounded HTTPS broker | Explicit JSON targets and credentials | Network send | No implicit egress or provider inference from empty configuration |
| Native | Command-line, process-memory, PE/file, audio/display, security, launcher primitives | Source-built C#/.NET 8 | .NET 8 SDK at build time | Narrow OS operations | Exact source set, source-tree hash, artifacts, and toolchain provenance are release-gated |
| Build | Deterministic packaging and independent archive verification | Python 3 | Python 3 | Generates artifacts | Streamed member verification, clean extraction, SBOM, receipt, checksums, and exact-byte promotion |

## Capability groups for operators

### Broadly available core

The transaction engine, plans, policy, preview, journaling, receipts, and compensation infrastructure form the shared core. Specific Windows actions still depend on their provider and platform requirements.

### Windows-provider capabilities

Applications, Windows Update, servicing, Defender, firewall, storage, ETW, firmware, display, and other system capabilities depend on the corresponding Windows APIs, cmdlets, COM providers, edition, hardware, and session context.

### Source-built compiled capabilities

Static binary intelligence and other narrow primitives depend on release-gated .NET outputs built from the current source closure. A random or stale DLL is not equivalent to the expected native runtime.

### Optional dependency-backed capabilities

ONNX, ML-KEM/OpenSSL, WASI, eBPF, WireGuard, fleet, and cloud adapters require explicitly configured or admitted external mechanisms. WinCare does not silently download or install these dependencies.

## Explicitly not shipped

- Rust FFI libraries or Go daemons without a demonstrated target requirement.
- A hosted cloud service or vendor-managed fleet backend.
- A remote general-purpose administration API.
- An embedded universal diagnosis model or autonomous production patch deployment.
- Bundled third-party eBPF, WASI, OpenSSL, WireGuard, or ONNX runtimes/models.
- Standards-compliant Raft consensus.
- Packet desynchronization, censorship bypass, covert interception, unsigned-driver installation, or kernel patching.
- A guaranteed security boundary for process-only validation.

## Maintenance contract

When adding or changing a capability, update this matrix in the same pull request when any of these change:

- implementation owner;
- prerequisite or compatibility behavior;
- observation/plan/mutation authority;
- evidence or postcondition behavior;
- dependency admission;
- explicit non-claim.

Also update:

- [Advanced Capabilities](Advanced-Capabilities.md) for dependency-gated or high-impact adapters;
- [Compatibility](Compatibility.md) for absence/degradation behavior;
- [Commands](COMMANDS.md) for operator-facing routes;
- [Security](../SECURITY.md) for new trust or authority boundaries;
- [Testing](Testing.md) and [Validation](../VALIDATION.md) for new evidence gates.
