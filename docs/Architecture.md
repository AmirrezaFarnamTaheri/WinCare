# Architecture

## Ownership model

WinCare uses three implementation languages for bounded responsibilities:

| Plane | Owner | Responsibility |
|---|---|---|
| Orchestration and Windows integration | PowerShell 7.2+ | typed plans, policy, providers, WPF/TUI/headless surfaces, Windows APIs, recovery |
| Compiled primitives | .NET 8 / C# | command-line parsing, process memory, audio/display interop, security inspection, file and PE intelligence, launchers |
| Build and independent verification | Python 3 | deterministic packaging, streamed ZIP verification, provenance, structural assurance |

A new language boundary is accepted only when it materially improves safety, determinism, performance, or deployment clarity.

## Runtime topology

`WinCare.psm1` loads closed ordered manifests for Core, Providers, and UI. Startup fails when a declared file is missing, duplicated, unexpected, or assigned to the wrong ownership group.

The Core plane owns models, policy, process/HTTP brokers, bounded I/O, path safety, canonical integrity, module identity, transactions, journaling, and action contracts. Providers own domain observation and plan construction. UI layers remain non-authoritative and invoke the same exported contracts.

## Mutation path

`UI or headless request -> exported plan constructor -> action contract -> policy/compatibility -> preview/approval -> provider dispatch -> evidence/postcondition -> journal/receipt -> compensation`

No alternate GUI, automation, extension, or native write path may bypass that chain.

## Trust boundaries

- **Process:** regular executable, trusted path, optional digest, bounded arguments/output/time, cancellation, redacted persistence.
- **Network:** admitted URI, credential/header restrictions, metadata-host denial, redirect denial, DNS-change checks, byte/time ceilings, response hash.
- **Filesystem:** canonical roots, no reparse traversal, ADS rejection, aggregate traversal bounds, race checks, atomic commit and rollback.
- **IPC:** authenticated framed messages, size and deadline limits, replay prevention, peer checks.
- **Extension/runtime:** schema, path, hash, signature, revocation, permission, runtime, and output admission.
- **Elevation:** exact module identity, authenticated request/result envelopes, contract-derived authority, operation-specific approval.

## Advanced capability composition

Advanced features reuse existing owners rather than creating parallel platforms:

- fleet topology uses authenticated health and policy-hash evidence;
- forensics combines event, process, registry, signature, and module evidence;
- Windows Sandbox configuration uses managed-file transactions and does not auto-launch;
- VBS/DMA hardening composes catalog actions and observed Device Guard state;
- telemetry storage uses protected records and managed-file transactions;
- eBPF and WireGuard use dependency-aware, digest-pinned adapters;
- binary intelligence uses the source-built native file-analysis library without executing targets.

## Release topology

The Windows gate builds native components from the audited source, runs static and runtime validation, creates one deterministic candidate, verifies it independently, and publishes the same bytes. Development archives are non-promotable when native or Windows evidence is absent.
