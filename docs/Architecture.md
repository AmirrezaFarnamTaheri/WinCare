# Architecture

## Release boundary

The 2.4.0-rc1 line is finalized as a native source release candidate. The distributable native source archive contains no PowerShell files. Historical PowerShell source is isolated in a separately hashed legacy oracle archive and is not invoked, embedded, or required by the native runtime.

Production promotion remains blocked until every one of the 259 command contracts has a native implementation and Windows behavioral evidence.

## Ownership

| Project | Responsibility |
|---|---|
| `WinCare.App` | WinUI 3 shell, pages, accessibility, lifecycle, presentation state |
| `WinCare.Application` | use cases, navigation, tool discovery, command dispatch |
| `WinCare.Domain` | requests, results, activity, recovery and evidence models |
| `WinCare.Infrastructure` | Windows integration, persistence, elevation boundary, telemetry, Rust interop |
| `WinCare.CommandCatalog` | typed definitions and frozen data for all 259 command IDs |
| `native/wincare-core` | bounded native primitives through a versioned C ABI |

C# owns product decisions and safety policy. Rust returns structured evidence or performs narrowly admitted primitives. Business rules are not duplicated across languages.

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
```

Domain and application projects contain no XAML types. ViewModels contain no `Microsoft.UI.Xaml.*` references.

## Command lifecycle

```text
UI request
  -> command catalog contract
  -> current-state observation
  -> plan construction
  -> policy and compatibility checks
  -> preview and approval
  -> admitted provider operation
  -> evidence and postcondition
  -> journal and receipt
  -> compensation when supported
```

The dispatcher fails closed for unknown, unavailable, disabled, or unmigrated commands. All tools may display the full catalog, but catalog presence never grants execution authority.

## Rust boundary

The initial ABI exposes:

- ABI version
- library version
- bounded SHA-256 file hashing

The interface uses explicit pointer lengths, output lengths, maximum byte counts, and integer status codes. Unsafe blocks stay minimal and documented. New Rust functionality requires profiling evidence or a concrete safety benefit.

## Startup

The first WinUI frame performs no network access, external process launch, WMI/CIM query, Defender query, optional-runtime probe, or command execution. The 259-item catalog is loaded when All tools is opened, not on the critical startup path.

Startup markers:

- `AppConstructed`
- `WindowCreated`
- `FirstContentRendered`
- `ShellInteractive`

Performance targets require Windows measurements and are not claimed from source inspection.

## Packaging

- Signed MSIX is the primary production distribution.
- A self-contained portable folder and deterministic ZIP are secondary distributions.
- x64 and ARM64 use architecture-matched `wincare_core.dll` files.
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
- 2 implemented
- 0 behavior-verified
- 257 native implementation blockers
- 259 production behavior-verification blockers
