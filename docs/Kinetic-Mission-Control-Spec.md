# WinCare 4.0: The Kinetic Mission Control Architecture

WinCare 4.0 transforms the application from a 1990s-style diagnostic utility into a high-performance Windows workspace. This design merges a micro-kernel architectural model, a hardware-accelerated WinUI 3 presentation layer, and a high-agency user experience that replaces defensive friction with optimistic execution and progressive disclosure.

```
┌─────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                 PRESENTATION LAYER (WinUI 3 / XAML)                                     │
│     Omni-Command Deck (Ctrl+K)  │  Kinetic Glass Mesh (Win2D)  │  Unified Adaptive Chrome              │
├─────────────────────────────────────────────────────────────────────────────────────────────────────────┤
│                                 APPLICATION ORCHESTRATION & SAGA LAYER                                 │
│     Optimistic Exec Engine  │  Saga Compensators  │  Playbook DAG Engine  │  Intent Routers             │
├────────────────────────────┬────────────────────────────────────────────┬────────────────────────────────┤
│       CORE EXECUTORS       │          ISOLATED PLUGIN HOSTS             │   TRANSACTIONAL PERSISTENCE    │
│        (In-Process)        │        (Out-of-Process AppContainer)       │          (SQLite WAL)          │
│   Storage / Servicing /    │     Workspace Tiling / ADB Bridge /        │     Activity / Snapshots /     │
│   Drivers / Remediation    │     Segmented DL / Dev Ecosystems          │     Cryptographic Receipts     │
├────────────────────────────┴────────────────────────────────────────────┴────────────────────────────────┤
│                                       NATIVE SYSTEM CORE (Rust)                                         │
│      Safe C-ABI Primitives  │  ETW Real-Time Stream  │  SCM Daemon Service  │  Restricted Job Tokens     │
└─────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

## 1. Domain Decoupling & Micro-Kernel Deconstruction

### Alien Domain Pruning & Extension Extraction
`WindowsCommandExecutor` and its helper classes currently bundle capabilities unrelated to core operating system health:
- **Window Management & Tiling**: `WindowManagerGeometry`, `WorkspaceTilingEngine` (master-stack, fair-grid, floating placement), and Win32 hotkey dispatch loops.
- **Download & Streaming Engines**: `SegmentedDownloadEngine`, `TokenBucketThrottlerCalculator`, `PieceMapBitset`, `ParseHlsPlaylist`, and `ParseEd2kUri`.
- **Mobile Diagnostics**: `AdbDiagnosticsHelper` (ADB device enumerator, Android mount-point parsers, battery telemetry).
- **Machine Learning / Local LLM Tools**: `EstimateModelMemoryFit` (VRAM/RAM tier classification for 3B/8B/70B models), vector cosine similarity, and prompt context truncators.
- **Gaming Runtime Cleanup**: Steam VDF app manifest decoders and shader cache auditors (`DetectSteamGameInstall`, `AuditSteamDebris`).
- **Credential & Secret Maskers**: Luhn credit card validation, Bearer token sanitizers, and clipboard memory scrapers (`ClipboardPrivacyGuard`, `SensitiveCredentialMasker`).

These utilities are extracted from the core binary into four independently versioned, out-of-process extensions:
- **wincare-ext-workspace**: Tiling window manager, layout geometry, and global hotkeys.
- **wincare-ext-downloader**: Multi-segment HTTP engine, HLS/ED2K scrapers, and token-bucket throttlers.
- **wincare-ext-devbridge**: Android ADB inspectors, Redis snapshots, and local LLM VRAM sizing tools.
- **wincare-ext-gamerig**: Steam shader cache reclamation, driver cache aggregators, and game runtime profilers.

### Deconstructing the Monolithic Executor
The monolithic `WindowsCommandExecutor` partial classes (`.System.cs`, `.Security.cs`, `.Desktop.cs`, `.Remediation.cs`, `.Experience.cs`, `.Productivity.cs`, `.State.cs`) are decommissioned. They are replaced by autonomous, single-responsibility handlers registered into the dependency injection container via `ISubsystemCommandExecutor`:

```csharp
public interface ISubsystemCommandExecutor
{
    string Subsystem { get; }
    bool CanHandle(string commandId);
    Task<CommandHandlerOutcome> ExecuteAsync(
        CommandDefinition definition, 
        CommandRequest request, 
        CancellationToken ct);
    CommandPreview PlanPreview(
        CommandDefinition definition, 
        CommandParameters parameters);
}
```

- **StorageReclamationHandler**: Handles boundary canonicalization, reparse-point rejection, and user/system cache purge routines.
- **DismServicingHandler**: Manages DISM online provisioning, AppX inventory/removal, and component-store cleanups.
- **DriverSecurityHandler**: Audits Code Integrity (CI) policies, HVCI/VBS posture, and orphaned INF packages.
- **RemediationPolicyHandler**: Evaluates baseline deviations, applies registry policies, and executes compensating rollback transactions.

### Transactional SQLite WAL Persistence
The file-based JSON persistence in `CommandStateStore` (relying on OS mutexes and full-file rewrites) is replaced with an embedded SQLite engine running in WAL (Write-Ahead Logging) mode via `Microsoft.Data.Sqlite`:
- **ActivityJournal**: Stores structured operation receipts, execution durations, user integrity levels, and affected resource snapshots.
- **ApprovedPlans**: Tracks cryptographic receipts with SHA-256 parameter digests, time-to-live expirations, and single-use consumption states.
- **CompensatorLedger**: Records reversible mutation journals with forward and reverse deltas (Registry DWORD/String values, service start types, and filesystem movements).

---

## 2. Native Systems Engineering & Sandboxing

### Rust Core (`wincare-core`) Memory Safety
- **Audit Unsafe Blocks**: Audit and annotate the 107 unannotated unsafe blocks in `native/wincare-core/src/lib.rs` with formal `// SAFETY:` invariants, wrapping Win32 API interactions with RAII handles via `windows-rs`.
- **Versioned ABI Surface**: Replace raw byte pointer passing (`*const u8` / `*mut u8`) with typed, versioned `repr(C)` ABI structs wrapped in panic-safe boundary harnesses:

```rust
#[repr(C)]
pub struct NativeBufferView {
    pub data: *const u8,
    pub length: usize,
}

#[repr(C)]
pub struct EntropyResult {
    pub shannon_entropy: f64,
    pub status_code: i32,
}

#[no_mangle]
pub extern "C" fn wincare_core_calculate_entropy(
    view: NativeBufferView,
    out: *mut EntropyResult,
) -> i32 {
    std::panic::catch_unwind(|| {
        if view.data.is_null() || out.is_null() {
            return -1;
        }
        let slice = unsafe { std::slice::from_raw_parts(view.data, view.length) };
        let entropy = compute_shannon_entropy(slice);
        unsafe {
            (*out).shannon_entropy = entropy;
            (*out).status_code = 0;
        }
        0
    }).unwrap_or(-1)
}
```

### Production SCM Service (`wincare-guard`)
`wincare-guard` moves from an experimental local daemon using named pipes (`WinCareGuardIPC`) to a managed Windows Service registered with the Service Control Manager (SCM):
- **SCM Lifecycle Integration**: Implements native service event handlers via the Rust `windows-service` crate, handling system shutdown and power-state transitions cleanly.
- **DACL-Hardened IPC**: Replaces default pipe permissions with an explicit SDDL descriptor restricting communication to callers holding the interactive logon SID.
- **Real-time Kernel ETW Telemetry**: Replaces background polling with an Event Tracing for Windows (ETW) consumer subscribed to `Microsoft-Windows-Kernel-Process`, `Microsoft-Windows-Kernel-Disk`, and `Microsoft-Windows-WindowsUpdateClient`.

### Out-of-Process Plugin Isolation & PKI
- **Isolated Host Worker (`wincare-plugin-host.exe`)**: Extension discovery, script execution (`.cmd`, `.ps1`), and third-party assembly reflection execute inside an isolated low-privilege process.
- **Restricted Job Object Sandboxing**: The host worker process is constrained within a Windows Job Object with a 256 MiB memory ceiling and a restricted token that denies privilege escalation.
- **Dual-Key Catalog PKI**: Remote extension installation uses an anchored Ed25519 root trust key. Manifests must provide developer-signed packages counter-signed by the WinCare Official Catalog Root.
- **Native CLI Tooling**: Decommission the Node.js CLI under `tools/wincare-plugin-cli`. Replace it with a compiled .NET tool (`dotnet-wincare`) that shares models directly with `WinCare.CommandCatalog`.

---

## 3. High-Agency UX: Removing Cognitive & Defensive Burdens

```
CURRENT DEFENSIVE FLOW (4-5 Actions, High Fatigue):
[Tool Selected] ──► [Generate Preview] ──► [Inspect Receipt & Hashes] ──► [Confirm Approval] ──► [Execute]

OPTIMISTIC HIGH-AGENCY FLOW (1 Action, Zero Fatigue):
[One-Touch Execute] ──► (Instant Execution + Silent State Snapshot) ──► [Floating 10s Undo Toast]
```

### Optimistic One-Touch Execution with Transient Undo
- **Bypass Previews for Reversible Operations**: For Safe and Moderate risk operations (e.g., DNS flushing, temporary cache cleaning, explorer tweaks), eliminate upfront preview receipts. Actions run immediately on click while writing a delta snapshot to SQLite.
- **Floating Undo Toasts**: Display a non-intrusive action toast: `System cache optimized (-2.4 GB). [ Undo (Ctrl+Z) ] • 10s`.
- **Isolate Dual-Phase Approvals to Irreversible Boundaries**: Two-phase cryptographic approval (preview receipt validation, parameter digests, and signature checks) is reserved strictly for Destructive and Critical operations (e.g., BCD boot reconfiguration, disk zeroing, unrecoverable driver removals).
- **Session-Wide Elevation**: Request administrator elevation once per session through a single User Account Control prompt rather than surfacing runtime privilege errors during execution.

### Progressive Disclosure: "Civilian View vs. Engineer Drawer"
Low-level architecture metadata is removed from standard operational views:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ ⚡ Clean Storage Pressure                                                   │
│ Purges obsolete Windows caches and temporary setup files.                   │
│                                                                             │
│ [  Reclaim ~4.2 GB  ]                                 [ ⚙ Advanced Details ]│
└──────┬──────────────────────────────────────────────────────────────────────┘
       │ (Toggled via ~ or Ctrl+I)
       ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ TECHNICAL SPECIFICATIONS & AUDIT TRACE                                      │
│ Command ID: cleaner-disk-pressure      Risk: Moderate                       │
│ Target Path: %LOCALAPPDATA%\Temp\*     Method: WinCareCoreUnlink            │
│ Parameter Digest: 9f8a...12c0          Compensator: Active                  │
└─────────────────────────────────────────────────────────────────────────────┘
```

- **The Default Surface (Human-First)**: Uses descriptive titles (e.g., "Uninstall Bing Weather" instead of `appx-registered-remove --name Microsoft.BingWeather`), concrete reclaimed space metrics, and clean status indicators.
- **The Engineer Drawer**: Low-level metadata—such as `planDigest` hashes, JSON parameter contracts, and execution durations—remains accessible inside a collapsible tray or via the `~` / `Ctrl+I` shortcut.

### Decisive Autonomous Co-Pilot
- **Eliminate Advisory Hedging**: Remove noncommittal boilerplate warnings. Deliver clear assessments: what occurred, why it matters, and how to resolve it.
- **Direct In-Place Remediation**: Eliminate page switching. Diagnostic discoveries on the Checkup or Troubleshoot pages include a direct "Resolve Instantly" action right on the finding card.
- **Correlated Remediation Bundles**: Group related findings into a single fix plan with one execution button (e.g., stopping three orphaned telemetry services and purging their log buffers together).

---

## 4. Visual Philosophy: The "Kinetic Glass" System

WinCare replaces flat solid cards with a layered visual system utilizing Windows 11 Mica Alt, compositional lighting, and specular borders.

```
[ Z-3: Floating HUD Layer ]      Global Command Palette (Ctrl+K), Notification Toasts, Modals
[ Z-2: Active Workspace ]        Interactive DAG Playbook, Time-Machine Slider, Virtual Grids
[ Z-1: Base Layout Chrome ]      Mica Alt Glass Surface, Floating Navigation Pill, Status Bar
[ Z-0: Ambient Mesh Engine ]     Hardware-accelerated Direct2D / Win2D Topology Canvas
```

```
┌────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ [≡] ❖ WinCare OS Workspace     [ ⌘ Search actions, inspect registry, run playbooks (Ctrl+K) ]  _ □ ✕ │
├───────┬────────────────────────────────────────────────────────────────────────────────────────────────┤
│ ⌂     │  ACTIVE TELEMETRY TOPOLOGY                                                 [ Profile: Pro-Rig ]│
│ ⛨     │  ┌───────────────────────┐  ┌───────────────────────┐  ┌─────────────────────────────────────┐ │
│ 🛠    │  │ STORAGE INTEGRITY     │  │ KERNEL ATTACK SURFACE │  │ COMPENSATOR JOURNAL                 │ │
│ 🛡    │  │ 78.4% Clean (21.4 GB) │  │ VBS: Active | HVCI: On│  │ 14 Reversible Snapshots             │ │
│ ⟲     │  │ [===••••••••••••••••] │  │ 0 Vulnerable Drivers  │  │ Last: explorer.show-extensions      │ │
│       │  └───────────────────────┘  └───────────────────────┘  └─────────────────────────────────────┘ │
│ ───   │ ────────────────────────────────────────────────────────────────────────────────────────────── │
│ ⚙     │  INTERACTIVE STATE-TIME MACHINE (SYSTEM RESTORE & MUTATION TIMELINE)                           │
│ ≡     │  ●────────────●────────────────────────●───────────────────────● (NOW)                         │
│       │  10:14 AM     11:30 AM                 02:15 PM                04:05 PM                        │
│ ?     │  DeepClean    WinGet Upgrade           Disable Telemetry       AppX Provision Clean            │
│       │  [-1.2 GB]    [4 Packages Updated]     [2 Rules Applied]       [3 Apps Removed]                │
│       │               [View Diff]              [Rollback State ↺]      [Inspect Receipt]               │
└───────┴────────────────────────────────────────────────────────────────────────────────────────────────┘
```

### Spacing Scale & Token System
The 202 hardcoded spacing literals (inconsistent DIP values: 6, 10, 14, 18, 20, 28) are removed and standardized on an 8-point geometric scale:

| Token | Light Value | Dark Value | Purpose |
|---|---|---|---|
| CanvasBackdrop | Mica Alt (#F3F3F3) | Mica Alt (#0D1117) | Ambient desktop-composited window base |
| SurfaceGlass | rgba(255, 255, 255, 0.70) | rgba(22, 27, 34, 0.65) | Acrylic glassmorphism with 30px backdrop blur |
| SurfaceElevated | #FFFFFF | #161B22 | Focus cards, active inspectors, dialog shells |
| BorderSpecular | rgba(0, 0, 0, 0.08) | rgba(255, 255, 255, 0.12) | 1 DIP high-index edge highlight |
| AccentPrimary | System Accent / #0066FF | System Accent / #388BFD | Execution triggers, active nodes, focus outlines |
| GlowSafe | #10B981 (Emerald) | #059669 | Bounded, verified read-only states |
| GlowMutating | #F59E0B (Amber) | #D97706 | Confirmation-gated mutation states |
| GlowHazard | #EF4444 (Crimson) | #DC2626 | Destructive preview & elevated boundaries |

- **Spacing Tokens**: SpacingXS (4px), SpacingS (8px), SpacingM (12px), SpacingL (16px), SpacingXL (24px), SpacingXXL (32px).
- **Corner Radii**: Controls and input elements use 8 DIP; cards, flyouts, and layout containers use 12 DIP.

### Unified Page Chrome (`UnifiedPageHeader`)
Consolidate the duplicated header layouts across all 12 XAML views into a single shared control:

```xml
<UserControl x:Class="WinCare.App.Controls.UnifiedPageHeader">
    <Grid Margin="0,0,0,24">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <StackPanel Spacing="4">
            <TextBlock Text="{x:Bind Title}" Style="{StaticResource TitleLargeTextBlockStyle}" />
            <TextBlock Text="{x:Bind Subtitle}" Style="{StaticResource BodySubtleTextBlockStyle}" />
        </StackPanel>
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
            <ContentPresenter Content="{x:Bind PrimaryAction}" />
            <ContentPresenter Content="{x:Bind SecondaryActions}" />
        </StackPanel>
    </Grid>
</UserControl>
```

### High-Performance Rendering & Natural Motion
- **Recycled ItemsRepeater**: Replace `ListView` in `AllToolsPage` and `ActivityPage` with virtualized `ItemsRepeater` controls using fixed layout recycling to ensure steady 60 FPS scrolling during search queries.
- **Natural Spring Physics**: Replace linear UI transitions with hardware-accelerated spring animations via `Microsoft.UI.Composition`:

```csharp
public static void ApplyNaturalSpring(UIElement element, Vector3 targetScale)
{
    var visual = ElementCompositionPreview.GetElementVisual(element);
    var compositor = visual.Compositor;

    var springAnimation = compositor.CreateSpringVector3Animation();
    springAnimation.FinalValue = targetScale;
    springAnimation.DampingRatio = 0.75f;
    springAnimation.Period = TimeSpan.FromMilliseconds(50);

    visual.StartAnimation("Scale", springAnimation);
}
```

---

## 5. The Four Pillar Experiences

### Pillar A: Omni-Command Deck (`Ctrl+K`)
Replaces the standard search box with an overlay command palette supporting natural language intents, inline execution, and diff inspections:

```
┌────────────────────────────────────────────────────────────────────────┐
│  > clean temp olderThan:7days --dry-run                                │
├────────────────────────────────────────────────────────────────────────┤
│  SUGGESTED ACTIONS                                                     │
│  ⚡ cleaner-disk-pressure       Purge temporary cache > 7 days  [Enter] │
│  🔍 disk-storage-inventory      Analyze large footprint blocks  [Tab]   │
│  🛡 audit-driver-store          Inspect obsolete OEM INF files  [Alt+1] │
├────────────────────────────────────────────────────────────────────────┤
│  INLINE PARAMETER INSPECTOR & ADMISSION PREVIEW                        │
│  Command: cleaner-disk-pressure [ID: disk.clean.pressure]              │
│  Privilege: Standard User (No Elevation Required)                      │
│  Risk Rating: [ Moderate (Confirmation Required) ]                     │
│                                                                        │
│  Affected Targets:                                                     │
│  • %TEMP%/* (> 7 days)                         ~1.42 GB (12,410 files) │
│  • %LOCALAPPDATA%/WinCare/cache/*              ~128 MB (42 files)      │
│                                                                        │
│  [ Execute Preview (F5) ]       [ Approve Mutation Plan & Apply (F9) ] │
└────────────────────────────────────────────────────────────────────────┘
```

- **Syntax & Intent Tokenizer**: `>` switches to direct command execution; `@` scopes queries to system targets (`@services`, `@registry`, `@drivers`); `:` filters by risk level (`:readonly`, `:mutating`, `:destructive`).
- **Natural Language Mapping**: Queries such as "my PC feels sluggish" automatically resolve to startup optimization, memory cache trimming, and thermal profile checks.
- **In-Deck Execution**: Safe read-only diagnostics render inline summaries directly inside the palette without navigating away from the active screen.

### Pillar B: Kinetic Subsystem Canvas
Replaces static rows on the Checkup page with an interactive subsystem node constellation powered by Win2D:

```
        [ Security & VBS ]
             ( 98% )
               │
               ▼
[ Storage ] ─── ❖ ─── [ Servicing & DISM ]
  ( 72% )      CORE     ( Update Pending )
               ▲
               │
         [ Kernel Drivers ]
             ( 100% )
```

- **Status Indicators**: Solid emerald rings indicate verified health assertions; pulsating amber signals reversible anomalies (e.g., orphan component packages); strobing crimson indicates critical security drift (e.g., HVCI disabled).
- **Fluid Transitions**: Selecting a subsystem node zooms into its diagnostic telemetry using a composition-backed `Vector3KeyFrameAnimation`.

### Pillar C: State-Time Machine (Rollback Timeline)
Replaces the tabular Activity view with a Git-like visual history tree:

```
(Time-Machine Scrub Bar)
◄──[ 2026-09-20 14:00 ]──────[ 2026-09-21 09:30 ]──────[ 2026-09-22 18:00 (HEAD) ]──►

  ● Commit: #m-882193b - Applied Remediation Preset "Hardened"
  │ Author: Elevation Admin • Duration: 420ms • Plan Digest: a9f8...12c0
  │
  ├─ [REGISTRY DIFF]
  │  HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard
  │  - "EnableVirtualizationBasedSecurity" = 0x00000000 (DWORD)
  │  + "EnableVirtualizationBasedSecurity" = 0x00000001 (DWORD)
  │
  ├─ [SERVICES MODIFIED]
  │  - DiagTrack (Connected User Experiences): Running -> Stopped & Disabled
  │
  └─ [COMPENSATOR HOOK: ACTIVE]
     [ ↺ Rollback All Changes in This Snapshot ]   [ 📋 Export Audit JSON ]
```

- **Property Diffs**: Displays side-by-side colorized diffs for registry modifications (Green `+`, Red `-`), service configuration adjustments, and removed package manifests.
- **One-Click Compensator Execution**: Clicking "Rollback" triggers the snapshot's companion compensator directly from SQLite, validating the reverse mutation before writing the updated outcome to the journal.

### Pillar D: Node-Based Playbook Orchestrator
Replaces the flat tool directory with a visual DAG builder, allowing power users to construct auditable maintenance pipelines:

```
┌─────────────────┐       ┌─────────────────┐       ┌──────────────────┐
│ Dism-Inventory  │ ────► │ Filter-Obsolete │ ────► │ Dism-Remove-Appx │
│ (Read-Only)     │       │ (Rule Engine)   │       │ (Apply Mutate)   │
└─────────────────┘       └─────────────────┘       └──────────────────┘
                                                              │
                                                              ▼
                                                    ┌──────────────────┐
                                                    │ Verify-Integrity │
                                                    │ (Postcondition)  │
                                                    └──────────────────┘
```

- **Pre-Flight Dry-Run**: The canvas runs read-only previews across the pipeline sequentially, highlighting node outputs before requesting batch approval.
- **Shareable JSON Templates**: Completed playbooks export to deterministic JSON files for deployment across multi-machine environments.

---

## 6. Five Intent-Led Workspaces

The 269 atomic commands are organized into five primary workspaces to eliminate decision paralysis:

```
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│   DISK DIET     │ │   BOOT BOOST    │ │ PRIVACY SHIELD  │ │   DEV RIG RUN   │ │  STABILITY FIX  │
│ Reclaim storage │ │ Shave startup   │ │ Block telemetry │ │ Clean toolchain │ │ Repair DISM,    │
│ & purge caches  │ │ delay & apps    │ │ & app tracking  │ │ caches & Docker │ │ BCD, SFC files  │
└─────────────────┘ └─────────────────┘ └─────────────────┘ └─────────────────┘ └─────────────────┘
```

- **Disk Diet (Storage Reclamation)**: Focuses on user and system cache purging, Delivery Optimization cleanup, and Component Store (WinSxS) compression. Features a single "Reclaim Safe Space" action.
- **Boot Boost (Startup Optimization)**: Manages startup impact, delayed service initialization, and background task schedules without requiring registry exploration.
- **Privacy Shield (Telemetry Hardening)**: Controls diagnostic tracking levels, telemetry domains via firewall/hosts, and camera/microphone privacy policies.
- **Dev Rig Run (Developer Workstation Cleanup)**: Cleans stale `node_modules`, builds artifacts, Docker caches, and toolchain junk directories with safety safeguards.
- **Stability Fix (System Repair & Servicing)**: Automates SFC integrity checks, online DISM component health remediation, and Windows Update cache repairs.

---

## 7. Phased Implementation Roadmap

```
PHASE 1: Domain Decoupling        PHASE 2: Core Refactor           PHASE 3: Native & Sandbox         PHASE 4: UX & Polish
[ Month 1 ]                       [ Month 2 ]                      [ Month 3 ]                       [ Month 4 ]
• Prune Alien Domains             • Deconstruct WindowsCommand-    • Annotate 107 Rust unsafe blocks • Migrate 202 spacing literals
• Extract 4 Extension Packs       Executor into Handlers           • Promote wincare-guard to SCM    • Build UnifiedPageHeader
• Port Plugin CLI to .NET Tool    • SQLite WAL Persistence Layer   • Out-of-process Plugin Host      • Ctrl+K Command Palette
• Re-align Catalog to 5 Domains   • Formalize Saga Compensators    • Ed25519 PKI Trust Root          • Rollback Audit Timeline
```

### Phase 1: Domain Decoupling & Toolchain Unification
- Extract window management, downloaders, ADB helpers, and LLM sizing utilities into standalone extension projects.
- Reorganize the core catalog around the five intent domains.
- Decommission `tools/wincare-plugin-cli` and deploy the `dotnet-wincare` CLI tool.

### Phase 2: Core Refactoring & Persistence
- Deconstruct `WindowsCommandExecutor` into autonomous subsystem handlers.
- Replace file-based JSON persistence in `CommandStateStore` with SQLite in WAL mode.
- Implement the Saga compensator engine tied directly to journal snapshots.

### Phase 3: Native Hardening & Process Sandboxing
- Audit and annotate the 107 undocumented unsafe blocks in `wincare-core`.
- Convert `wincare-guard` into an SCM-managed Windows Service with SDDL IPC controls.
- Deploy `wincare-plugin-host.exe` running within an AppContainer boundary.
- Establish the Ed25519 root PKI for extension verification.

### Phase 4: WinUI 3 Modernization & UI Polish
- Replace the 202 hardcoded spacing literals with the standardized design tokens.
- Replace the 12 view headers with `UnifiedPageHeader`.
- Implement `ItemsRepeater` virtualization on data-heavy surfaces.
- Deploy the Omni-Command Deck (`Ctrl+K`), the Kinetic Subsystem Canvas, and the State-Time Machine rollback timeline.

---

## 8. Preserved Architectural Invariants

- **Two-Phase Mutation Authority**: Mutation planning and execution remain strictly separated. Single-use `ApprovedMutationPlan` tokens continue to fail closed if modified, replayed, or expired.
- **Verifiable Recovery Guarantees**: Rollback controls are exposed only when an executable compensator and a valid state snapshot exist.
- **Fail-Closed Verification Gates**: Structural repository tests, WCAG AA contrast gates, and package signing checks remain enforced across CI pipelines.
