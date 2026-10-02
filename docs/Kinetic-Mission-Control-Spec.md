# WinCare 4.0: System Architecture & Modernization Specification

WinCare 4.0 structures the application as a task-first native Windows workspace. This design specifies the subsystem boundary model, the WinUI 3 presentation layer, and a fail-closed execution model with progressive disclosure.

```
┌─────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                 PRESENTATION LAYER (WinUI 3 / XAML)                                     │
│     Global Search (Ctrl+K)      │  Hardware-Accelerated Chrome │  High-Contrast AA Tokens               │
├─────────────────────────────────────────────────────────────────────────────────────────────────────────┤
│                                 APPLICATION ORCHESTRATION & DISPATCH                                    │
│     Command Dispatcher      │  Compensators       │  Tool Catalog Index   │  Diagnostic Intents         │
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

## 1. Domain Decoupling & Handlers

### Domain Boundary Separation
`WindowsCommandExecutor` and its helper classes separate core operating system health from optional utility capabilities:
- **Window Management & Tiling**: `WindowManagerGeometry`, `WorkspaceTilingEngine` (master-stack, fair-grid, floating placement), and Win32 hotkey dispatch loops.
- **Download & Streaming Engines**: `SegmentedDownloadEngine`, `TokenBucketThrottlerCalculator`, `PieceMapBitset`, `ParseHlsPlaylist`, and `ParseEd2kUri`.
- **Mobile Diagnostics**: `AdbDiagnosticsHelper` (ADB device enumerator, Android mount-point parsers, battery telemetry).
- **Machine Learning / Local Model Tools**: `EstimateModelMemoryFit` (VRAM/RAM tier classification for 3B/8B/70B models), vector cosine similarity, and prompt context bounds.
- **Gaming Runtime Cleanup**: Steam VDF app manifest decoders and shader cache auditors (`DetectSteamGameInstall`, `AuditSteamDebris`).
- **Credential & Secret Maskers**: Luhn number validation, Bearer token sanitizers, and clipboard memory scrapers (`ClipboardPrivacyGuard`, `SensitiveCredentialMasker`).

These utilities run outside the core binary in four isolated extensions:
- **wincare-ext-workspace**: Tiling window manager, layout geometry, and global hotkeys.
- **wincare-ext-downloader**: Multi-segment HTTP engine, HLS/ED2K scrapers, and token-bucket throttlers.
- **wincare-ext-devbridge**: Android ADB inspectors, Redis snapshots, and local model VRAM sizing tools.
- **wincare-ext-gamerig**: Steam shader cache reclamation, driver cache aggregators, and game runtime profilers.

### Subsystem Handlers
The monolithic `WindowsCommandExecutor` partial classes are replaced by single-responsibility handlers registered into dependency injection via `ISubsystemCommandExecutor`:

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
Persistence uses an embedded SQLite engine running in WAL (Write-Ahead Logging) mode via `Microsoft.Data.Sqlite`:
- **ActivityJournal**: Stores structured operation receipts, execution durations, user integrity levels, and affected resource snapshots.
- **ApprovedPlans**: Tracks cryptographic receipts with SHA-256 parameter digests, time-to-live expirations, and single-use consumption states.
- **CompensatorLedger**: Records reversible mutation journals with forward and reverse deltas (Registry DWORD/String values, service start types, and filesystem movements).

---

## 2. Native Systems Engineering & Sandboxing

### Rust Core (`wincare-core`) Memory Safety
- **Audited Unsafe Blocks**: All `unsafe` blocks in `native/wincare-core/src/lib.rs` carry formal `// SAFETY:` invariants, wrapping Win32 API interactions with RAII handles via `windows-rs`.
- **Versioned ABI Surface**: Typed, versioned `repr(C)` ABI structs wrapped in panic-safe boundary harnesses:

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
`wincare-guard` runs as a managed Windows Service registered with the Service Control Manager (SCM):
- **SCM Lifecycle Integration**: Implements native service event handlers via the Rust `windows-service` crate, handling system shutdown and power-state transitions cleanly.
- **DACL-Hardened IPC**: Restricts pipe permissions with an explicit SDDL descriptor limiting communication to callers holding the interactive logon SID.
- **Real-time Kernel ETW Telemetry**: Uses an Event Tracing for Windows (ETW) consumer subscribed to `Microsoft-Windows-Kernel-Process`, `Microsoft-Windows-Kernel-Disk`, and `Microsoft-Windows-WindowsUpdateClient`.

### Out-of-Process Plugin Isolation & PKI
- **Isolated Host Worker (`wincare-plugin-host.exe`)**: Extension discovery, script execution (`.cmd`, `.ps1`), and third-party assembly reflection execute inside an isolated low-privilege process.
- **Restricted Job Object Sandboxing**: The host worker process is constrained within a Windows Job Object with a 256 MiB memory ceiling and a restricted token that denies privilege escalation.
- **Dual-Key Catalog PKI**: Remote extension installation uses an anchored Ed25519 root trust key. Manifests must provide developer-signed packages counter-signed by the WinCare Official Catalog Root.
- **Native CLI Tooling**: The CLI tool (`dotnet-wincare`) shares data contracts directly with `WinCare.CommandCatalog`.

---

## 3. High-Agency UX: Removing Cognitive & Defensive Burdens

```
REVIEW FLOW (Multi-Step Approvals for Irreversible Changes):
[Tool Selected] ──► [Generate Preview] ──► [Inspect Receipt & Hashes] ──► [Confirm Approval] ──► [Execute]

DIRECT FLOW (Reversible Safe Operations):
[Direct Execution] ──► (State Snapshot Recorded) ──► [10s Undo Option Available]
```

### Direct Execution with Reversible Snapshots
- **Reversible Operations**: For Safe and Moderate risk operations (such as DNS flushing, temporary cache cleaning, and explorer preference adjustments), actions run on confirmation while writing a delta snapshot to SQLite.
- **Undo Option**: Displays a non-intrusive action status: `System cache optimized (-2.4 GB). [ Undo (Ctrl+Z) ] (10s)`.
- **Two-Phase Approval for Irreversible Operations**: Two-phase cryptographic approval (preview receipt validation, parameter digests, and signature checks) is reserved strictly for Destructive and Critical operations (such as BCD boot reconfiguration, disk zeroing, and unrecoverable driver removals).
- **Session-Wide Elevation**: Requests administrator elevation once per session through a single User Account Control prompt rather than surfacing repeated runtime privilege errors during execution.

### Progressive Disclosure: Standard View vs. Technical Details Drawer
Low-level architecture metadata is organized behind an expandable details surface:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Clean Storage Pressure                                                      │
│ Purges obsolete Windows caches and temporary setup files.                   │
│                                                                             │
│ [  Reclaim ~4.2 GB  ]                                 [ Advanced Details ]  │
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

- **The Standard Surface**: Uses clear titles (for example, "Uninstall Bing Weather" instead of `appx-registered-remove --name Microsoft.BingWeather`), concrete reclaimed space metrics, and clean status indicators.
- **The Technical Details Drawer**: Low-level metadata, such as `planDigest` hashes, JSON parameter contracts, and execution durations, remains accessible inside a collapsible tray or via the `~` / `Ctrl+I` shortcut.

### Diagnostic Guidance & In-Place Remediation
- **Direct Guidance**: Delivers clear assessments: what occurred, why it matters, and how to resolve it.
- **In-Place Remediation**: Diagnostic discoveries on the Checkup or Troubleshoot pages include a direct resolution action right on the finding card.
- **Correlated Remediation Plans**: Groups related findings into a single fix plan with one execution button (such as stopping orphaned telemetry services and purging their log buffers together).

---

## 4. Visual Architecture: Layered Presentation System

WinCare uses Windows 11 Mica Alt, compositional lighting, and defined border contrast.

```
[ Z-3: Floating Layer ]         Global Command Palette (Ctrl+K), Notification Toasts, Modals
[ Z-2: Active Workspace ]        Interactive DAG Playbook, Mutation Timeline, Virtual Grids
[ Z-1: Base Layout Chrome ]      Mica Alt Surface, Navigation Rail, Status Bar
[ Z-0: Diagnostics Canvas ]      Hardware-accelerated Direct2D / Win2D Topology Canvas
```

```
┌────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ [=] WinCare Workspace          [ Search actions, inspect registry, run playbooks (Ctrl+K) ]    _ [] X  │
├───────┬────────────────────────────────────────────────────────────────────────────────────────────────┤
│ Home  │  SYSTEM TOPOLOGY                                                           [ Profile: Pro-Rig ]│
│ Check │  ┌───────────────────────┐  ┌───────────────────────┐  ┌─────────────────────────────────────┐ │
│ Care  │  │ STORAGE INTEGRITY     │  │ KERNEL ATTACK SURFACE │  │ COMPENSATOR JOURNAL                 │ │
│ Sec   │  │ 78.4% Clean (21.4 GB) │  │ VBS: Active | HVCI: On│  │ 14 Reversible Snapshots             │ │
│ Rep   │  │ [===...............] │  │ 0 Vulnerable Drivers  │  │ Last: explorer.show-extensions      │ │
│       │  └───────────────────────┘  └───────────────────────┘  └─────────────────────────────────────┘ │
│ ---   │ ────────────────────────────────────────────────────────────────────────────────────────────── │
│ Tools │  ACTIVITY & MUTATION TIMELINE                                                                  │
│ Ext   │  o------------o------------------------o-----------------------o (NOW)                         │
│       │  10:14 AM     11:30 AM                 02:15 PM                04:05 PM                        │
│ Help  │  DeepClean    WinGet Upgrade           Disable Telemetry       AppX Provision Clean            │
│       │  [-1.2 GB]    [4 Packages Updated]     [2 Rules Applied]       [3 Apps Removed]                │
│       │               [View Diff]              [Rollback State]        [Inspect Receipt]               │
└───────┴────────────────────────────────────────────────────────────────────────────────────────────────┘
```

### Spacing Scale & Token System
Spacing literals are standardized on an 8-point geometric scale:

| Token | Light Value | Dark Value | Purpose |
|---|---|---|---|
| CanvasBackdrop | Mica Alt (#F3F3F3) | Mica Alt (#0D1117) | Ambient desktop-composited window base |
| SurfaceGlass | rgba(255, 255, 255, 0.70) | rgba(22, 27, 34, 0.65) | Acrylic backdrop surface |
| SurfaceElevated | #FFFFFF | #161B22 | Focus cards, active inspectors, dialog shells |
| BorderSpecular | rgba(0, 0, 0, 0.08) | rgba(255, 255, 255, 0.12) | 1 DIP edge highlight |
| AccentPrimary | System Accent / #0066FF | System Accent / #388BFD | Execution triggers, active nodes, focus outlines |
| GlowSafe | #10B981 (Emerald) | #059669 | Bounded, verified read-only states |
| GlowMutating | #F59E0B (Amber) | #D97706 | Confirmation-gated mutation states |
| GlowHazard | #EF4444 (Crimson) | #DC2626 | Destructive preview and elevated boundaries |

- **Spacing Tokens**: SpacingXS (4px), SpacingS (8px), SpacingM (12px), SpacingL (16px), SpacingXL (24px), SpacingXXL (32px).
- **Corner Radii**: Controls and input elements use 8 DIP; cards, flyouts, and layout containers use 12 DIP.

### Unified Page Chrome (`UnifiedPageHeader`)
Consolidates header layouts across views into a shared control:

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

### High-Performance Rendering & Fluid Transitions
- **Recycled ItemsRepeater**: Uses virtualized `ItemsRepeater` controls with fixed layout recycling in `AllToolsPage` and `ActivityPage` for responsive scrolling.
- **Natural Spring Transitions**: Hardware-accelerated animations via `Microsoft.UI.Composition`:

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

## 5. Primary Workspaces & Interfaces

### Command Palette (`Ctrl+K`)
Provides global command search supporting structured queries, inline execution, and diff inspections:

```
┌────────────────────────────────────────────────────────────────────────┐
│  > clean temp olderThan:7days --dry-run                                │
├────────────────────────────────────────────────────────────────────────┤
│  SUGGESTED ACTIONS                                                     │
│  cleaner-disk-pressure          Purge temporary cache > 7 days  [Enter]│
│  disk-storage-inventory         Analyze large footprint blocks  [Tab]  │
│  audit-driver-store             Inspect obsolete OEM INF files  [Alt+1]│
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
- **Symptom Mapping**: Queries such as "pc feels slow" map to startup optimization, memory cache trimming, and thermal profile checks.
- **Inline Execution**: Safe read-only diagnostics render inline summaries directly inside the palette without navigating away from the active screen.

### Subsystem Topology
Provides an interactive subsystem node representation powered by Win2D:

```
        [ Security & VBS ]
             ( 98% )
                │
                ▼
[ Storage ] ─── * ─── [ Servicing & DISM ]
  ( 72% )      CORE     ( Update Pending )
                ▲
                │
          [ Kernel Drivers ]
             ( 100% )
```

- **Status Indicators**: Solid emerald rings indicate verified health assertions; amber signals reversible anomalies (such as orphan component packages); crimson indicates security configuration drift (such as HVCI disabled).
- **Transitions**: Selecting a subsystem node displays its diagnostic telemetry using a composition-backed `Vector3KeyFrameAnimation`.

### Activity & Rollback Timeline
Visual history view backed by SQLite WAL:

```
(Timeline Scrub Bar)
<---[ 2026-09-20 14:00 ]------[ 2026-09-21 09:30 ]------[ 2026-09-22 18:00 (HEAD) ]--->

  o Commit: #m-882193b - Applied Remediation Preset "Hardened"
  | Author: Elevation Admin | Duration: 420ms | Plan Digest: a9f8...12c0
  |
  +-- [REGISTRY DIFF]
  |   HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard
  |   - "EnableVirtualizationBasedSecurity" = 0x00000000 (DWORD)
  |   + "EnableVirtualizationBasedSecurity" = 0x00000001 (DWORD)
  |
  +-- [SERVICES MODIFIED]
  |   - DiagTrack (Connected User Experiences): Running -> Stopped & Disabled
  |
  +-- [COMPENSATOR HOOK: ACTIVE]
      [ Rollback All Changes in This Snapshot ]   [ Export Audit JSON ]
```

- **Property Diffs**: Displays side-by-side diffs for registry modifications (Green `+`, Red `-`), service configuration adjustments, and removed package manifests.
- **Rollback Execution**: Clicking "Rollback" triggers the snapshot's companion compensator directly from SQLite, validating the reverse mutation before writing the updated outcome to the journal.

### Node-Based Playbook Pipelines
Visual DAG builder for auditable maintenance pipelines:

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

- **Pre-Flight Dry-Run**: The pipeline runs read-only previews sequentially, presenting node outputs before requesting batch approval.
- **JSON Templates**: Playbooks export to deterministic JSON files for deployment across machines.

---

## 6. Functional Workspaces

The 296 atomic commands are organized into five primary workspaces:

```
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│ STORAGE          │ │ STARTUP &        │ │ TELEMETRY &      │ │ DEVELOPER        │ │ SYSTEM REPAIR &  │
│ RECLAMATION      │ │ SERVICES         │ │ PRIVACY          │ │ ENVIRONMENTS     │ │ SERVICING        │
│ Reclaim storage  │ │ Startup impact   │ │ Diagnostic data  │ │ Clean toolchain  │ │ Repair DISM,     │
│ & purge caches   │ │ & background apps│ │ & app permissions│ │ caches & builds  │ │ BCD, SFC files   │
└──────────────────┘ └──────────────────┘ └──────────────────┘ └──────────────────┘ └──────────────────┘
```

- **Storage Reclamation**: User and system cache purging, Delivery Optimization cleanup, and Component Store (WinSxS) compression.
- **Startup & Services**: Startup impact analysis, delayed service initialization, and background task schedules.
- **Telemetry & Privacy**: Diagnostic tracking levels, telemetry domains via firewall/hosts, and camera/microphone privacy policies.
- **Developer Environments**: Stale build artifacts, dependency directories (`node_modules`), package manager caches, and container storage.
- **System Repair & Servicing**: SFC integrity checks, online DISM component health remediation, and Windows Update cache repairs.

---

## 7. Preserved Architectural Invariants

- **Two-Phase Mutation Authority**: Mutation planning and execution remain strictly separated. Single-use `ApprovedMutationPlan` tokens fail closed if modified, replayed, or expired.
- **Verifiable Recovery Guarantees**: Rollback controls are exposed only when an executable compensator and a valid state snapshot exist.
- **Fail-Closed Verification Gates**: Structural repository tests, WCAG AA contrast gates, and package signing checks remain enforced across CI pipelines.
