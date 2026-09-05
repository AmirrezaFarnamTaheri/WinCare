# WinCare Architecture Specification

## 1. System overview and boundaries

WinCare is a layered Windows desktop application using **WinUI 3 / Windows App SDK** for presentation, **.NET 8** for the managed application and Windows integration layers, and **Rust 2024** for bounded native primitives plus the experimental Guard daemon.

The native source distribution contains **zero PowerShell files**. Historical PowerShell scripts are isolated in a separately hashed legacy-oracle archive for parity verification and are never loaded, embedded, or invoked by the native runtime.

| Subsystem | Responsibilities and boundaries |
|---|---|
| `WinCare.App` | WinUI shell, navigation, native controls, theme/accessibility resources, tool parameter UI, Doctor UI, Plugin Store UI |
| `WinCare.Application` | Fail-closed command dispatcher, dispatcher-issued review receipts, plugin host/registry, rule-based Doctor orchestration, activity journal |
| `WinCare.Domain` | Typed requests/results, policy/evidence models, activity records |
| `WinCare.Infrastructure` | Windows APIs, bounded process execution, persistent state, encrypted profiles, plugin catalog/package verification, Rust FFI |
| `WinCare.CommandCatalog` | Frozen 259-command catalog plus typed UI parameter schemas |
| `native/wincare-core` | Bounded native primitives exposed through a versioned C ABI |
| `native/wincare-guard` | **Experimental** local health daemon and local named-pipe endpoint; production SCM lifecycle and app notification consumption are not complete |
| `tools/wincare-plugin-cli` | Plugin development/validation/packaging CLI |

## 2. Command lifecycle

A command's catalog presence is never execution authority.

```text
UI / automation request
  → command and handler lookup
  → typed parameter preflight
  → read-only preview
  → evidence / affected-resource presentation
  → dispatcher issues short-lived, parameter-bound, single-use review receipt
  → explicit user approval
  → admitted execution
  → postcondition / outcome collection
  → Activity receipt with outcome certainty
  → compensation only when an executable compensator exists
```

For mutating commands, callers cannot mint a valid approval. `CommandDispatcher` issues the receipt only after a successful preview and consumes it atomically during Apply. Parameter edits, correlation changes, expiry, or replay all fail closed.

If a mutating handler faults after execution starts and the final state cannot be proven, WinCare reports **final system state unknown** and directs the user to verify the affected resource before retrying.

All Tools renders typed parameters from `CommandParameterCatalog`. Raw JSON remains an explicit Advanced escape hatch. The executor remains the final validation authority.

## 3. Plugin trust and lifecycle

Plugin trust has two separate cases: local/development packages and remote catalog packages.

### Remote catalog boundary

`RemoteCatalogService` can verify the **exact catalog bytes** against a configured WinCare-pinned catalog public key and detached signature. Only a catalog with that runtime verification state may authorize remote installation; otherwise it is browse-only.

The current production composition root constructs `RemoteCatalogService` without an approved catalog public key because no production catalog root is shipped in this repository. Therefore the current build intentionally keeps remote installation **disabled** instead of inventing a trust root. A future release may enable remote installation only by shipping an explicitly reviewed pinned catalog key and a live catalog/signature endpoint.

The configured default catalog URL is not itself a trust anchor. Network location, TLS, author strings, package-local keys, and a signature/key pair supplied by an unverified catalog are insufficient to establish publisher identity.

### Package admission

When a trusted remote catalog is configured, installation requires:

1. a freshly fetched catalog for the security-sensitive install step; no stale-cache fallback;
2. re-resolution of the selected plugin from that fresh catalog;
3. exact package ID equality;
4. package SHA-256 verification;
5. publisher manifest signature verification using metadata from the trusted catalog;
6. trusted PublisherId/package revocation checks;
7. explicit consent covering all declared capabilities;
8. external admission metadata outside the plugin directory and signature re-verification during discovery.

A delisted plugin, publisher/key/hash/URL/permission change, stale advisory, revoked publisher/package, changed installed manifest, or replayed stale UI card fails closed.

### Runtime capabilities

Plugins execute full-trust **in-process** with the user's privileges. Declared capabilities are informed-consent metadata; they are not an AppContainer or process sandbox. The UI states this explicitly. Dynamic commands cannot replace reserved core namespaces.

Plugin initialization is transactional. Commands registered during a failed assembly-plugin initialization are rolled back by ownership, and shutdown/disposal/load-context cleanup runs before the plugin is left in an error state. Uninstall is confirmation-gated and restores the prior enabled state when package removal fails.

## 4. Rule-based System Doctor

The shipped Doctor is a **rule-based diagnostic assistant**, not a general AI agent.

```text
User symptom text
  → rule-based intent / symptom classification
  → read-only diagnostic commands
  → evidence-backed recommendation
  → actual dispatcher preview of proposed mutation
  → user reviews the preview
  → dispatcher-issued approval receipt
  → optional Apply
```

The Doctor cannot synthesize its own mutation approval. It also does not expose raw exception text in the conversation surface.

## 5. Activity, reports, persistence, and recovery

Activity is the durable operation ledger. It exposes Running, Needs attention, Completed, and aggregated daily Reports views.

Journal updates are event-driven rather than UI-polled. In-memory state is updated under synchronization, but serialization/disk work is queued outside state locks. Preference writes follow the same non-blocking pattern. Persistence degradation is visible in the UI rather than silently masquerading as durable success.

`UndoAvailable` is false unless a concrete executable compensator is implemented. WinCare does not advertise a generic Undo action for operations that cannot safely reverse themselves.

## 6. Native core and Guard

### `wincare_core`

The Rust core exposes versioned bounded primitives such as system information, directory sizing, and SHA-256. FFI entry points contain panics, use caller-owned pointer/length buffers, and do not retain caller memory after return.

### `wincare-guard`

Guard currently provides an **experimental daemon boundary**:

- RAM/disk/thermal monitoring;
- local `WinCareGuardIPC` named-pipe `ping` / `health` responses;
- an explicit Windows DACL rather than the default named-pipe ACL;
- staged per-user notification payloads.

The current release does **not** claim a production SCM-installed service lifecycle or complete native/app toast delivery. Those remain promotion requirements. Until they are wired and exercised, Guard must be described as experimental in user-facing surfaces and release documentation.

## 7. Startup and performance boundaries

The first WinUI frame performs no network access, external process launch, WMI/CIM query, Defender query, optional-runtime probe, or command execution.

Startup markers are:

1. `AppConstructed`
2. `WindowCreated`
3. `FirstContentRendered`
4. `ShellInteractive`

Long lists use WinUI virtualization, search is debounced, and stable XAML data surfaces prefer compiled `x:Bind`.

Checkup intentionally runs measurement-sensitive read-only probes sequentially; overlapping disk/CPU/update/security probes could contaminate the evidence they are trying to collect.

## 8. UI, adaptivity, and accessibility

`LayoutVisibility.CompactBreakpointDip = 920` is the app-level compact boundary. Home, Checkup, All Tools, System Care, Security, Repair & Recovery, and Activity consume the shared compact state. Local component breakpoints may exist for a specific header/content fit but do not redefine app compact state.

High Contrast uses system colors. The light diagnostic accent was darkened for small-text contrast. Interactive controls carry automation metadata, and important layouts wrap rather than depending on fixed text heights. Narrator, keyboard traversal, 225% text scaling, and live native resizing remain required release validation; source inspection alone does not prove those runtime behaviors.

## 9. Packaging and promotion

- MSIX is the primary installer format.
- x64 and ARM64 portable artifacts remain secondary technician/recovery distributions.
- Architecture-matched `wincare_core.dll` and `wincare_guard.exe` are staged per target.
- CI verifies catalog parity, source contracts, structural/native tests, managed tests, Rust formatting/clippy/tests where executable, package creation, and signature checks.

Production promotion additionally requires live Windows behavioral verification, accessibility/text-scale testing, production certificate installation/upgrade validation, current runtime screenshots, and completion or explicit deferral of experimental Guard service/notification integration.
