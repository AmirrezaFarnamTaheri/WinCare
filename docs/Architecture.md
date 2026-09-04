# WinCare Architecture Specification

## 1. System Overview & Boundaries

WinCare is structured as a layered desktop application utilizing **WinUI 3 (Windows App SDK)** for its presentation layer, **.NET 8** for application logic, and **Rust 2024** for high-performance, memory-safe native primitives and background health monitoring.

The native source distribution contains **zero PowerShell files**. Historical PowerShell scripts are isolated in a separately hashed legacy oracle archive for parity verification and are never loaded, embedded, or invoked by the native runtime.

---

## 2. Subsystem Ownership & Layering

| Subsystem | Responsibilities & Boundaries |
|---|---|
| **`WinCare.App`** | WinUI 3 shell, page navigation, XAML views, theme resource dictionaries, accessibility automation metadata, lifecycle handling, AI Doctor UI, and Plugin Store UI |
| **`WinCare.Application`** | Core use cases, navigation services, fail-closed `CommandDispatcher`, dynamic plugin host, AI Doctor diagnostic orchestrator, and activity journaling |
| **`WinCare.Domain`** | Strongly typed requests, results, command contracts, activity entries, sync models, and evidence records |
| **`WinCare.Infrastructure`** | Windows API integration, process runner (`BoundedProcessRunner`), registry/WMI/Defender integration, Rust FFI bindings, encrypted cloud sync (AES-256-GCM), and persistent state stores |
| **`WinCare.CommandCatalog`** | Embedded typed definitions and frozen data for all 259 command IDs + built-in plugin manifests |
| **`native/wincare-core`** | High-performance bounded native primitives (`sys_info`, `dir_size`, `sha256`) exported via a versioned C ABI |
| **`native/wincare-guard`** | Standalone background health daemon monitoring RAM pressure, disk quota, and thermal state, exposing a named-pipe health endpoint (`WinCareGuardIPC`); on critical thresholds it stages Windows Toast XML to a per-user queue directory (`%LOCALAPPDATA%`) for the app to display (no SCM service registration or native toast popup is wired yet) |
| **`tools/wincare-plugin-cli`**| Node.js developer CLI for creating, validating, linting, and packaging community plugins |

```
+----------------------------------------------------------------------------------------------------+
|                                         WinCare.App                                                |
|                   (WinUI 3 Shell, Mica Backdrop, XAML Views, ThemeResources)                       |
+------------------------------------+----------------------------------+----------------------------+
                                     │
                                     ▼
+----------------------------------------------------------------------------------------------------+
|                                    WinCare.Application                                             |
|        (Fail-Closed CommandDispatcher, Plugin Host, AI Doctor Engine, Activity Journal)           |
+------------------------------------+----------------------------------+----------------------------+
                  │                                                     │
                  ▼                                                     ▼
+------------------------------------+               +-----------------------------------------------+
|         WinCare.Domain             |               |           WinCare.Infrastructure              |
| (Typed Requests, Results, Contracts|               | (Windows APIs, Process Runner, Rust Interop)  |
+------------------------------------+               +-----------------------------------------------+
                  ▲                                                     │
                  │                                                     ▼
+------------------------------------+               +-----------------------------------------------+
|      WinCare.CommandCatalog        |               |       native/wincare-core (Rust C ABI)        |
|  (259 Typed Command Definitions)   |               |     native/wincare-guard (Daemon Monitor)     |
+------------------------------------+               +-----------------------------------------------+
```

---

## 3. Command & Plugin Lifecycle

Every tool and command execution follows an explicit, fail-closed lifecycle:

```text
UI Request (Tool Execution / AI Action Plan / Plugin Command)
   │
   ▼
1. Command Catalog / Dynamic Plugin Lookup
   │
   ▼
2. Current-State Observation (Read-Only)
   │
   ▼
3. Plan Construction & Parameter Preflight (Validation)
   │
   ▼
4. Policy & Elevation Boundary Check (Fail-closed if unauthorized)
   │
   ▼
5. Preview & Two-Phase Approval (Gated by request.Apply + options.ReviewApproved)
   │
   ▼
6. Admitted Provider Execution (Native executor / PluginScriptCommandHandler)
   │
   ▼
7. Evidence & Postcondition Collection
   │
   ▼
8. Journal Entry & Receipt Generation (PII-sanitized)
   │
   ▼
9. Compensation / Rollback (When explicitly supported)
```

The dispatcher fails closed for unknown, unavailable, disabled, or unmigrated commands. All tools may display the full catalog, but catalog presence never grants execution authority. Dynamic plugin commands are isolated, bounded by `BoundedProcessRunner`, and cannot overwrite reserved core namespaces (`wincare.core.*`, `system.*`).

---

## 4. Plugin Subsystem Trust Model & Package Admission

1. **Publisher Signatures**: Remote catalog entries may provide a publisher public key and signature used to verify manifest bytes. Package-local keys and author names never establish trust. The current catalog is not independently signed or pinned, so this proves catalog/package consistency rather than publisher identity; an independently anchored catalog or publisher-key policy remains required.
2. **Catalog Policy & Revocation Lists**: The remote catalog supplies `revokedPublishers` and `revokedPackages` lists. Revocation is enforced both in the catalog UI and independently at install time (`EnforceRevocationPolicy`), so a revoked package ID or publisher is rejected even if a caller bypasses UI filtering.
3. **Declared Capabilities & In-Process Disclosure**: Plugins execute in-process with user privileges. `PluginDetailDialog` presents explicit full-trust warnings, publisher trust badges, and capability declarations before installation, and the installer enforces per-capability consent (`EnforceCapabilityConsent`) so every declared capability must be covered by the user's explicit consent.
4. **Package ID Equality**: Package archive manifests must strictly match the target plugin ID before extraction and filesystem promotion.
5. **SHA-256 Digest**: HTTPS catalog downloads require and verify a SHA-256 digest. Local file and direct-stream installation paths accept an optional digest for development and test scenarios.
6. **Staging & Backup Isolation**: Updates create rollback snapshots in `.staging/backups/`, completely isolated from active discovery routines.
7. **Dynamic Lifecycle Management**: Discovered plugins are managed via `IPluginRegistry.RegistryChanged`, updating `ToolCatalogService` dynamically.

The validated current-state trust flow, including unresolved boundaries, is available in the standalone [plugin admission architecture diagram](diagrams/plugin-admission.html). Its renderer input is checked in beside it as `plugin-admission.architecture.json`.

---

## 5. AI Doctor & Diagnostic Telemetry Flow

The AI Doctor subsystem strictly separates machine learning intent interpretation from evidence-grounded system diagnosis:

```text
User Symptom Description
         │
         ▼
Rule-Based Intent Classification (Intent & Symptom Extraction)
         │
         ▼
Read-Only Hardware & OS Diagnostics (Disk capacity, RAM usage, System logs)
         │
         ▼
DiagnosticEvidence Records (Source, Metric, Value, Timestamp)
         │
         ▼
Evidence-Grounded Recommendation (DoctorActionPlan with RootCause & Telemetry)
         │
         ▼
Explicit User Step-by-Step Approval
         │
         ▼
Safe Preview & Mutation Execution (Gated by CommandExecutionOptions)
```

No mutative action may be executed by the AI Doctor without prior read-only evidence collection and explicit user confirmation.

---

## 6. Rust Native Engine & Health Guard Daemon

- **`wincare_core`**: Versioned C ABI (`sys_info`, `dir_size`, `sha256`) wrapped in `std::panic::catch_unwind` for bounded native execution.
- **`wincare-guard`**: Autonomous background daemon monitoring RAM pressure, disk usage, and thermal state, exposing a named-pipe health endpoint (`WinCareGuardIPC`) that answers `ping`/`health`, and returning structured JSON telemetry. On critical thresholds it stages Windows Toast XML to a per-user queue directory (`%LOCALAPPDATA%`) and echoes it to stderr for the app to display; SCM service registration and a native toast popup are not yet wired.

---

## 7. Startup Telemetry & Performance Markers

The first WinUI frame performs no network access, external process launch, WMI/CIM query, Defender query, optional-runtime probe, or command execution.

Startup markers:
1. `AppConstructed`: Application instance initialized.
2. `WindowCreated`: MainWindow constructed with Mica backdrop.
3. `FirstContentRendered`: Shell navigation view populated.
4. `ShellInteractive`: Background services and catalog ready for user interaction.

---

## 8. Packaging & Distribution Model

- **MSIX Package**: Primary modern Windows installer distribution.
- **Portable Folder & ZIP**: Secondary standalone distributions for offline recovery.
- **Architecture Matrix**: Architecture-matched `wincare_core.dll` and `wincare_guard.exe` binaries for `x64` and `ARM64`.
- **Source Finalization**: Deterministic separation of pure native source and historical migration oracle archives.
