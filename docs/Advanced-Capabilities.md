# Advanced capabilities

This document describes WinCare capabilities that rely on optional runtimes, specialized Windows providers, sensitive evidence, network peers, or high-impact plans. It records executable boundaries and explicit non-claims; it is not a roadmap or a promise that every capability is available on every machine.

Use the runtime registry to inspect local availability:

```powershell
Import-Module .\src\WinCare\WinCare.psd1 -Force
Get-WinCareCapabilityRegistry
```

## Admission model

An advanced capability is usable only when:

1. the WinCare implementation is present;
2. the required local runtime/provider is discovered;
3. its identity, version, digest, configuration, or endpoint satisfies the capability contract;
4. target scope, policy, approval, and elevation requirements pass;
5. the operation can remain bounded and produce required evidence.

WinCare does not silently install optional runtimes, models, programs, drivers, credentials, or remote infrastructure.

## Capability summary

| Capability | Current implementation | Required dependency | Authority | Explicit boundary |
|---|---|---|---|---|
| ONNX diagnostics | Bounded source-built host plus deterministic fallback | Approved model and native host | Read-only | Fallback is labeled non-neural; model execution is claimed only after successful bounded host output |
| ML-KEM | Bounded digest-pinned OpenSSL adapter and authenticated file envelope | Compatible OpenSSL | Cryptographic mutation | No bundled keys/runtime; file and metadata integrity remain explicit |
| WASI | Bounded digest-pinned runtime and admitted module execution | Wasmtime or Wasmer | Module-defined within contract | No ambient authority, arbitrary preopens, or marketplace auto-install |
| eBPF | Inventory, runtime evidence, and digest-pinned prebuilt program admission | eBPF for Windows | Admission plus separately guarded direct operation | No driver installation, hidden interception, or admission-as-attachment claim |
| Fleet topology | Authenticated bounded peer health and policy drift | Explicit configured peers | Read-only network | No Raft compliance or general remote administration claim |
| Cloud telemetry | Admitted HTTPS endpoints and protected payloads | Explicit targets and credentials | Network send | No background/implicit egress or provider inference from empty configuration |
| Forensics | Bounded event timeline and process/module anomaly correlation | Windows evidence providers | Primarily read-only | No target execution, injection, or proof that all compromise is absent |
| Windows Sandbox | Deterministic `.wsb` configuration plan | Pre-enabled Windows Sandbox | Generates plan/file | No implicit feature enablement, driver installation, or automatic launch |
| VBS/DMA assurance | Device Guard, DMA policy, BitLocker, page-file evidence and catalog-backed plans | Supported Windows edition/hardware | Read-only plus plans | No enclave or compromise-absence guarantee inferred from configuration |
| Display | Installed-driver inventory and physical DDC/CI calibration plans | Windows display APIs and compatible monitor | Read-only plus plans | No virtual-display driver build or installation |
| Telemetry lake | Local protected, redacted, hash-verified, bounded daily shards | WinCare state store | Local state mutation | No remote warehouse, hidden collection, or implicit upload |
| Binary intelligence | Compiled bounded hash, entropy, PE section, metadata, and signature inspection | Source-built `WinCare.Native.dll` | Read-only | No dynamic instrumentation, process injection, or target execution |
| WireGuard | Adapter inventory and optional digest-pinned CLI observation | Optional WireGuard CLI | Read-only | Key material is not collected |
| QUIC | Local .NET runtime capability probe | .NET QUIC support | Read-only | No traffic mutation, evasion, or synthetic throughput claim |

## ONNX diagnostics

WinCare may invoke an approved ONNX model through a bounded source-built host. The capability contract should identify:

- model identity and admitted path;
- host identity and source-built release evidence;
- input/output size limits;
- timeout and process exit behavior;
- output schema validation;
- deterministic non-neural fallback labeling.

A heuristic or deterministic fallback must never be presented as neural-model inference.

## ML-KEM and authenticated envelopes

The ML-KEM adapter depends on a compatible, admitted OpenSSL implementation. WinCare does not bundle long-lived keys or infer key-management policy.

The contract should preserve:

- OpenSSL executable identity/digest;
- algorithm availability;
- bounded process execution;
- authenticated envelope metadata;
- source/destination file identity and hashes;
- explicit key/secret lifecycle and cleanup;
- failure without plaintext-success ambiguity.

Cryptographic execution does not automatically make the surrounding key storage or operational process secure.

## WASI modules

WASI execution is a constrained plugin mechanism, not arbitrary local code execution.

A module admission includes, as applicable:

- module hash and local identity;
- runtime hash/version;
- allowed preopened directories;
- allowed arguments/environment;
- timeout and output ceilings;
- exit code and result evidence;
- explicit network/host capability restrictions.

WinCare does not automatically download marketplace modules or grant ambient filesystem, credential, process, or network access.

## eBPF governance

WinCare can observe a compatible eBPF for Windows runtime and create digest-bound admission records for reviewed prebuilt programs.

Important distinctions:

- inventory is observation;
- admission records an approved identity;
- admission does not attach or execute the program;
- runtime attach/detach remains a separate high-impact operation guarded by policy, `ShouldProcess`, target validation, and evidence;
- WinCare does not install the driver or hide interception.

Example admission route:

```powershell
.\WinCare.ps1 -Command ebpf-admit -ArgumentsJson '<reviewed arguments>' -Json
```

Use the returned plan/record and current source contract rather than guessing argument fields.

## Fleet and cloud adapters

Fleet and cloud capabilities are endpoint-specific bounded adapters, not a hosted WinCare control plane.

### Fleet

Fleet observations require explicit peers and per-peer authentication. Evidence includes peer admission, protocol/endpoint, reachability, response identity/hash where applicable, and policy drift.

WinCare does not claim:

- standards-compliant Raft consensus;
- leader election;
- autonomous deployment authority;
- arbitrary shell access;
- vendor-managed infrastructure.

### Cloud telemetry

Cloud sends require explicit HTTPS targets, credentials, and payload configuration. Empty configuration does not imply a default provider. There is no hidden collector or background upload path.

## Forensics and binary intelligence

Forensic routes collect bounded Windows evidence. Static binary intelligence inspects files without executing them.

Evidence can include:

- selected event sources and time/entry limits;
- process/module identity;
- file SHA-256 and size;
- PE headers/sections and metadata;
- entropy and signature observations;
- exact collection failures or inaccessible providers.

These observations can identify anomalies but do not prove a machine or file is safe.

Example routes:

```powershell
.\WinCare.ps1 -Command forensics-timeline -Json
.\WinCare.ps1 `
  -Command binary-intelligence `
  -ArgumentsJson '{"LiteralPath":"C:\\Windows\\System32\\notepad.exe"}' `
  -Json
```

## VBS, DMA, firmware, and isolation assurance

WinCare reports directly observed configuration/provider evidence for VBS, HVCI, Credential Guard, DMA policy, BitLocker, page-file, Secure Boot, TPM, and related controls where supported.

It does not infer an enclave guarantee, bootkit absence, or complete compromise absence from settings alone.

Observation:

```powershell
.\WinCare.ps1 -Command vbs-assurance -Json
```

Plan preview:

```powershell
.\WinCare.ps1 `
  -Command vbs-harden `
  -ArgumentsJson '{"IncludeCredentialGuard":true,"IncludeHvci":true}' `
  -Json
```

Apply only after reviewing edition/hardware prerequisites, recovery implications, and non-recoverable disclosures:

```powershell
.\WinCare.ps1 `
  -Command vbs-harden `
  -ArgumentsJson '{"IncludeCredentialGuard":true,"IncludeHvci":true}' `
  -Apply `
  -Json
```

## Windows Sandbox configuration

WinCare can generate a deterministic, bounded Windows Sandbox configuration plan when the feature is already available.

```powershell
.\WinCare.ps1 `
  -Command sandbox-config `
  -ArgumentsJson '{"Id":"analysis-box","MappedFolder":"C:\\Cases"}' `
  -Json
```

The plan does not automatically:

- enable the Windows optional feature;
- create a security boundary for arbitrary host-side validation;
- mount unrestricted host directories;
- launch the sandbox;
- install software or drivers inside it.

## Local telemetry lake

The telemetry lake is local product state. Its contract uses protected, redacted, hash-verified, bounded shards and transaction-backed retention.

```powershell
.\WinCare.ps1 `
  -Command telemetry-retention `
  -ArgumentsJson '{"RetentionDays":30}' `
  -Json
```

There is no remote warehouse or implicit cloud synchronization. Cloud transmission, when explicitly configured, is a separate adapter and authority.

## Display and calibration

WinCare observes installed display/driver/pipeline evidence and can produce calibration plans for compatible physical displays.

```powershell
.\WinCare.ps1 -Command display-pipeline -Json

.\WinCare.ps1 `
  -Command display-calibrate `
  -ArgumentsJson '{"DeviceName":"DISPLAY1","PhysicalIndex":0,"Brightness":45}' `
  -Json
```

The capability does not build or install a virtual-display driver. Calibration availability depends on compatible Windows/DDC/CI providers and the target display.

## Deliberately excluded

WinCare does not implement or claim:

- packet desynchronization or censorship bypass;
- covert traffic redirection or hidden interception;
- kernel patching;
- automatic unsigned-driver installation;
- opaque remote command execution;
- autonomous production patch deployment;
- hidden background telemetry;
- bundled third-party runtimes, models, programs, credentials, or infrastructure;
- unsupported performance or security guarantees;
- a security boundary based only on launching a process.

## Headless route patterns

Observation routes do not acquire mutation authority merely because they are callable by automation:

```powershell
.\WinCare.ps1 -Command capabilities -Json
.\WinCare.ps1 -Command fleet-inventory -Json
.\WinCare.ps1 -Command forensics-timeline -Json
.\WinCare.ps1 -Command vbs-assurance -Json
.\WinCare.ps1 -Command display-pipeline -Json
```

Plan-producing routes preview by default. `-Apply` authorizes execution only after the same contract still passes policy, target, dependency, and elevation checks.

See [Command Reference](COMMANDS.md) for complete invocation semantics.

## Maintenance requirements

When an advanced capability changes, update:

- this boundary description;
- [Capability Matrix](CAPABILITY-MATRIX.md);
- [Compatibility](Compatibility.md);
- [Security](../SECURITY.md) when trust/authority changes;
- [Commands](COMMANDS.md) for public routes/examples;
- tests for missing, incompatible, malicious, timeout, excess-output, partial, and postcondition failure paths;
- release evidence when a runtime/native/package closure changes.
