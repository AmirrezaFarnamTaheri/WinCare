# WinCare Architecture Specification

## 1. System overview and boundaries

WinCare is a layered Windows desktop application using **WinUI 3 / Windows App SDK** for presentation, **.NET 8** for the managed application and Windows integration layers, and **Rust 2024** for bounded native primitives plus the experimental Guard daemon.

The native source distribution contains **zero PowerShell files**. Historical PowerShell scripts are isolated in a separately hashed legacy-oracle archive for parity verification and are never loaded, embedded, or invoked by the native runtime.

| Subsystem | Responsibilities and boundaries |
|---|---|
| `WinCare.App` | WinUI shell, task-first navigation, Home/Checkup/care presentation, Power tools parameter/review UI, Troubleshoot UI, Extensions UI, theme/accessibility resources |
| `WinCare.Application` | Fail-closed command dispatcher, dispatcher-issued review receipts, exact catalog projection, extension host/registry, rule-based Troubleshoot orchestration, activity journal |
| `WinCare.Domain` | Typed requests/results, risk/admission policy, evidence models, activity records |
| `WinCare.Infrastructure` | Windows APIs, bounded process execution, persistent state, encrypted profiles, extension catalog/package verification, Rust FFI |
| `WinCare.CommandCatalog` | 269-command catalog, preserving the 259 frozen legacy IDs, plus typed UI parameter schemas |
| `native/wincare-core` | Bounded native primitives exposed through a versioned C ABI |
| `native/wincare-guard` | **Experimental** local health daemon and local named-pipe endpoint; production SCM lifecycle and app notification consumption are not complete |
| `tools/wincare-plugin-cli` | Extension/plugin development, validation, and packaging CLI for developers |

The presentation layer is deliberately task-first. **Home** summarizes shared evidence and routes to workflows; it does not own command execution. **Checkup** owns read-only evidence collection. **System care**, **Security**, and **Repair & recovery** project the exact command catalog by Area/Section. **Power tools** is the canonical advanced command inspector and execution surface. **Troubleshoot** interprets symptoms and evidence, then hands suggested commands to Power tools rather than creating a second execution path.

## 2. Command lifecycle and risk tiering

Commands are categorized into three product-facing operational risk tiers:

```text
Request
  → Lookup command definition and handler
  → Validate typed parameters
  → Risk tier check:
      ├── Safe: direct admitted execution
      ├── Moderate: explicit reviewed confirmation
      └── Destructive: read-only preview → single-use parameter-bound plan → explicit approval
  → Execute through the admitted Windows boundary
  → Record execution outcome in Activity journal
```

- **Safe:** Read-only work and bounded low-risk actions can execute directly after validation and admission.
- **Moderate:** Non-destructive configuration changes require explicit user confirmation of the reviewed operation.
- **Destructive:** High-impact operations require a successful read-only preview and an issued, parameter-bound review plan before execution.
- If a mutating handler faults during execution, WinCare records the outcome accurately and indicates when host state may have been partially modified.

Power tools renders typed UI inputs generated from `CommandParameterCatalog`. Raw JSON remains available as an Advanced mode; it does not bypass admission, validation, or risk policy. Raw catalog risk values remain implementation/technical-detail data while the normal UI uses the domain's Safe / Moderate / Destructive tiers.

## 3. Presentation ownership and navigation

Product navigation is defined centrally by `NavigationCatalog` and resolved by `PageService`. The visible shell route set is expected to remain in parity with those definitions; hidden routes such as About may be opened through Help/search without falsely selecting a different visible navigation item.

Cross-page handoffs are explicit:

- Home routes to Checkup, named care sections, Activity, Power tools, Extensions, and Troubleshoot.
- Checkup findings route to named care sections rather than positional tab numbers.
- Care rows open the canonical Power tools inspector directly and preserve configured parameters.
- Troubleshoot suggestions open the same Power tools inspector.
- Legacy integer section parameters remain supported only as a compatibility path; new product navigation uses stable section names.

Cached pages that subscribe to shared services must subscribe/unsubscribe with navigation or control lifetime so stale pages do not continue reacting to journal/catalog changes off-screen.

## 4. Extension trust and lifecycle

Extension trust has two separate cases: local/development packages and remote catalog packages. Internal code retains `Plugin*` type names for compatibility, while the user-facing product term is **Extensions**.

### Remote catalog boundary

`RemoteCatalogService` can verify the **exact catalog bytes** against a configured WinCare-pinned catalog public key and detached signature. Only a catalog with that runtime verification state may authorize remote installation; otherwise it is browse-only.

The current production composition root constructs `RemoteCatalogService` without an approved catalog public key because no production catalog root is shipped in this repository. Therefore the current build intentionally keeps remote installation **disabled** instead of inventing a trust root. A future release may enable remote installation only by shipping an explicitly reviewed pinned catalog key and a live catalog/signature endpoint.

The configured default catalog URL is not itself a trust anchor. Network location, TLS, author strings, package-local keys, and a signature/key pair supplied by an unverified catalog are insufficient to establish publisher identity.

The Extensions page surfaces the current catalog trust/availability state so browse-only or offline behavior is visible rather than inferred from disabled actions.

### Package admission

When a trusted remote catalog is configured, installation requires:

1. a freshly fetched catalog for the security-sensitive install step; no stale-cache fallback;
2. re-resolution of the selected extension from that fresh catalog;
3. exact package ID equality;
4. package SHA-256 verification;
5. publisher manifest signature verification using metadata from the trusted catalog;
6. trusted PublisherId/package revocation checks;
7. explicit consent covering all declared capabilities;
8. external admission metadata outside the extension directory and signature re-verification during discovery.

A delisted extension, publisher/key/hash/URL/permission change, stale advisory, revoked publisher/package, changed installed manifest, or replayed stale UI card fails closed.

### Runtime capabilities

Extensions execute full-trust **in-process** with the user's privileges. Declared capabilities are informed-consent metadata; they are not an AppContainer or process sandbox. Dynamic commands cannot replace reserved core namespaces.

Extension initialization is transactional. Commands registered during a failed assembly-extension initialization are rolled back by ownership, and shutdown/disposal/load-context cleanup runs before the extension is left in an error state. Uninstall is confirmation-gated and restores the prior enabled state when package removal fails.

## 5. Rule-based Troubleshoot

The shipped Troubleshoot experience is a **local rule-based diagnostic assistant**, not a general AI agent and not a cloud model.

```text
User symptom text
  → rule-based intent / symptom classification
  → read-only diagnostic commands
  → measured evidence + findings
  → supported suggested command
  → open command in canonical Power tools inspector
  → normal Safe / Moderate / Destructive admission flow
  → outcome recorded in Activity
```

Troubleshoot does **not** preview or apply a mutation itself and cannot synthesize its own mutation approval. It also does not expose raw exception text in the conversation surface. This prevents a split-brain safety model between diagnostic recommendations and ordinary command execution.

## 6. Checkup evidence flow

Checkup is read-only end to end. Its system, storage, and security probes execute concurrently with bounded concurrency through `ParallelCommandProbeRunner`. Windows Update readiness runs in the background because update search can be materially slower than the other probes.

```text
Run Checkup
  ├── system preview ─┐
  ├── storage preview ├─ bounded concurrent fast probes
  └── security preview┘
  └── Windows Update search continues independently
       ↓
  evidence summary + findings
       ↓
  named handoff to the relevant care section when follow-up is useful
```

Home consumes the same four evidence sources through the shared Activity journal—Windows & hardware, Storage, Security, and Windows Update—rather than inventing a second performance/health model.

## 7. Activity, reports, persistence, and recovery

Activity is the durable operation ledger. It exposes Running, Needs attention, Completed, and aggregated daily Reports views. **Needs attention** represents operations whose recorded outcome requires review or follow-up; it is not a queue of pending confirmation dialogs.

Journal updates are event-driven rather than UI-polled. In-memory state is updated under synchronization, but serialization/disk work is queued outside state locks. Preference writes follow the same non-blocking pattern. Persistence degradation is visible in the UI rather than silently masquerading as durable success.

`UndoAvailable` is false unless a concrete executable compensator is implemented. WinCare does not advertise a generic Undo action for operations that cannot safely reverse themselves.

## 8. Native core and Guard

### `wincare_core`

The Rust core exposes versioned bounded primitives such as system information, directory sizing, and SHA-256. FFI entry points contain panics, use caller-owned pointer/length buffers, and do not retain caller memory after return.

### `wincare-guard`

Guard currently provides an **experimental daemon boundary**:

- RAM/disk/thermal monitoring;
- local `WinCareGuardIPC` named-pipe `ping` / `health` responses;
- an explicit Windows DACL rather than the default named-pipe ACL;
- staged per-user notification payloads.

The current release does **not** claim a production SCM-installed service lifecycle or complete native/app toast delivery. Those remain promotion requirements. Until they are wired and exercised, Guard must be described as experimental in user-facing surfaces and release documentation.

## 9. Startup and performance boundaries

The first WinUI frame performs no network access, external process launch, WMI/CIM query, Defender query, optional-runtime probe, or command execution.

Startup markers are:

1. `AppConstructed`
2. `WindowCreated`
3. `FirstContentRendered`
4. `ShellInteractive`

Long lists use WinUI virtualization, Power tools search is debounced, and stable XAML data surfaces prefer compiled `x:Bind`. Checkup uses bounded concurrency only for independent read-only probes; Windows Update work is isolated so a slow search does not block the first evidence summary.

## 10. UI, adaptivity, and accessibility

`LayoutVisibility.CompactBreakpointDip = 920` is the app-level compact boundary used by core task pages. Local component breakpoints may exist for a specific header/content fit—such as the Power tools table/inspector and Extensions header—but they do not redefine the product-wide compact state.

High Contrast uses system colors. The light diagnostic accent is chosen for small-text contrast. Interactive controls carry automation metadata, important layouts wrap rather than depending on fixed text heights, and Power tools avoids visual-tree/order discovery for named interactive controls.

Narrator, full keyboard traversal, 100–225% text/display scaling, High Contrast rendering, dialog focus, and live native resizing remain required release validation; source inspection and successful compilation alone do not prove those runtime behaviors.

## 11. Packaging and promotion

- MSIX is the primary installer format.
- x64 and ARM64 portable artifacts remain secondary technician/recovery distributions.
- Architecture-matched `wincare_core.dll` and `wincare_guard.exe` are staged per target.
- CI verifies catalog parity, source contracts, structural/native tests, managed tests, Rust formatting/clippy/tests where executable, package creation, and signature checks.

Production promotion additionally requires live Windows behavioral verification, accessibility/text-scale testing, production certificate installation/upgrade validation, fresh runtime screenshots for the exact installed candidate, and completion or explicit deferral of experimental Guard service/notification integration.
