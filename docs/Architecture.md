# Architecture

## Release boundary

The 2.5.0-rc1 line is finalized as a native source release candidate with modular plugin platform support, local AI diagnostics, and background health guard capabilities. The distributable native source archive contains zero PowerShell files. Historical PowerShell source is isolated in a separately hashed legacy oracle archive and is not invoked, embedded, or required by the native runtime.

Production promotion remains blocked until every one of the 259 command contracts has a native implementation and Windows behavioral evidence.

## Ownership

| Project | Responsibility |
|---|---|
| `WinCare.App` | WinUI 3 shell, pages, accessibility, lifecycle, presentation state, AI Doctor UI, Plugin Store UI |
| `WinCare.Application` | use cases, navigation, dynamic command dispatch, plugin registry, local AI diagnostics |
| `WinCare.Domain` | requests, results, activity, sync payloads, recovery and evidence models |
| `WinCare.Infrastructure` | Windows integration, persistence, elevation boundary, telemetry, Rust interop, cloud sync |
| `WinCare.CommandCatalog` | typed definitions and frozen data for all 259 command IDs + built-in plugin manifests |
| `WinCare.SourceGenerators` | compile-time Roslyn source generators for zero-overhead command routing |
| `native/wincare-core` | bounded native primitives through a versioned C ABI (`sys_info`, `dir_size`, `sha256`) |
| `native/wincare-guard` | background health daemon (RAM/disk/thermal monitoring + Windows Toast XML notifications) |
| `tools/wincare-plugin-cli`| Node.js developer CLI for creating, validating, linting, and packaging plugins |

C# owns product decisions and safety policy. Rust returns structured evidence, executes bounded native primitives, or runs low-overhead background monitoring. Business rules are not duplicated across languages.

## Dependency direction

```text
WinCare.App
  -> WinCare.Application
  -> WinCare.Domain
  -> WinCare.CommandCatalog

WinCare.App
  -> WinCare.Infrastructure
  -> WinCare.Domain
  -> wincare_core.dll
  -> wincare_guard.exe (IPC Named Pipe)
```

Domain and application projects contain no XAML types. ViewModels contain no `Microsoft.UI.Xaml.*` references.

## Command & Plugin Lifecycle

```text
UI request
  -> command catalog / dynamic plugin registration
  -> current-state observation
  -> plan construction
  -> policy and compatibility checks
  -> preview and approval (gated by request.Apply + options.ReviewApproved)
  -> admitted provider operation (Native executor / PluginScriptCommandHandler)
  -> evidence and postcondition
  -> journal and receipt
  -> compensation when supported
```

The dispatcher fails closed for unknown, unavailable, disabled, or unmigrated commands. All tools may display the full catalog, but catalog presence never grants execution authority. Dynamic plugin commands are isolated, bounded by `BoundedProcessRunner`, and cannot overwrite reserved core namespaces (`wincare.core.*`, `system.*`).

## Plugin Subsystem Trust Model & Package Admission

1. **Declared Capabilities & In-Process Disclosure**: Plugins execute with full user privileges. `PluginDetailDialog` presents explicit trust warnings and capability declarations before installation.
2. **Package ID Equality**: Package archive manifests must strictly match the target plugin ID before extraction and filesystem promotion.
3. **Mandatory SHA-256 Digest**: Every stream and URL package undergoes full byte-level SHA-256 hashing.
4. **Staging & Backup Isolation**: Updates create rollback snapshots in `.staging/backups/`, completely isolated from active discovery routines.
5. **Dynamic Lifecycle Management**: Discovered plugins are managed via `IPluginRegistry.RegistryChanged`, updating `ToolCatalogService` dynamically.

## Rust Boundaries

- **`wincare_core`**: Exposes ABI version, library version, bounded iterative `dir_size`, `sys_info` JSON metrics, and SHA-256 file hashing. Every export is wrapped in `std::panic::catch_unwind`.
- **`wincare-guard`**: Background daemon exposing asynchronous Named Pipe IPC (`\\.\pipe\WinCareGuardPipe`) for RAM pressure, disk quota, and thermal alerts without UI blocking.

## Startup

The first WinUI frame performs no network access, external process launch, WMI/CIM query, Defender query, optional-runtime probe, or command execution.

Startup markers:
- `AppConstructed`
- `WindowCreated`
- `FirstContentRendered`
- `ShellInteractive`

## Packaging

- Signed MSIX is the primary production distribution.
- A self-contained portable folder and deterministic ZIP are secondary distributions.
- x64 and ARM64 use architecture-matched `wincare_core.dll` and `wincare_guard.exe` binaries.
- Native source and legacy oracle are separate deterministic source artifacts.
- Production mode fails unless all 259 commands are `BehaviorVerified`.

## Migration rule

A command may advance only through evidence-backed states:

1. `Cataloged`
2. `ContractVerified`
3. `Implemented`
4. `BehaviorVerified`

Current state:
- 259 cataloged
- 259 implemented native routes
- 0 behavior-verified
- 0 native implementation blockers
- 259 production behavior-verification blockers
