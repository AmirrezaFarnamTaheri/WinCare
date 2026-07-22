# WinCare 1.0 Architecture

Architecture

WinCare 1.0 is a policy-governed Windows workstation platform with one authoritative mutation path. It cumulatively accounts for 166 submitted donor IDs representing 153 unique archive baselines and converges selected semantics into target-native capabilities while keeping UI, automation, extensions, maintenance scheduling, offline servicing, and elevated execution non-authoritative.

## Layers

- **Host:** launcher, state initialization, capability detection, lifecycle and authenticated elevated helper.
- **Operator UI:** console primitives, adaptive menus/lists, command palette, plan previews, accessibility and domain screens.
- **Providers:** applications, cleanup, storage, startup, profiles/policy, desktop controls, health, security, WUA, WinGet, network, internals, WDAC, local Group Policy, Sysmon, provisioning, mounted-image servicing, boot, shell/context-menu governance, customization-host inventory, widgets, Bluetooth telemetry, maintenance windows, playbooks, automation, recovery, extensions and reports.
- **Catalog/knowledge:** strict non-executable JSON rules, presets, AppX profiles, context-menu rules, playbooks, widgets and troubleshooting topics.
- **Policy:** effective user/session/organization/machine controls, read-only lock, risk/action/signature limits and maintenance windows.
- **Transaction engine:** action contracts, plan validation, authorization, locks, execution, verification, journals, receipts, cancellation and compensation.
- **Infrastructure:** safe paths, protected records, exact process execution, logging, formatting, packaging and evidence validation.

## Lifecycle

```text
Discover -> Normalize -> Plan -> Contract validation -> Policy -> Preview
         -> Authorize -> Lock -> Execute -> Verify -> Journal -> Reconcile/Undo
```

Providers do not mutate through screen-specific code. Every modifying workflow constructs a versioned typed action and reaches the same dispatcher. Automation, provisioning, extensions, headless mode and UAC do not bypass it.


## Productivity and support responsibility planes

- **Power-session plane:** a dedicated native host owns `SetThreadExecutionState`; protected revisioned records bind mode, display/away flags, expiry, heartbeat, host process identity, module-tree integrity, and exact stop compensation.
- **Window-workspace plane:** read-only HWND/monitor discovery feeds normalized work-area zones and exact identity-bound bounds, topmost, activation, and emergency local-input plans.
- **Color/visual plane:** one-shot screen-pixel reads, normalized color records, protected revisioned palettes, and file/dimension/frame-bounded metadata inspection; no screenshot retention or renderer runtime.
- **Local-note plane:** separate Markdown bodies and strict metadata, atomic snapshots, private-body redaction, explicit local search, compare-and-swap, and executable restoration.
- **Remote-support governance plane:** application/process/service/listener/config-key discovery, bounded expiring consent state, value redaction, and exact allowlisted process recovery; no session host, relay, credential injection, or unattended-access runtime.
- **Browser-workspace plane:** local process/window/profile/extension evidence and permission-risk classification; tab counts remain explicitly unavailable without existing browser-owned authority.
- **Command-intelligence plane:** workspace filtering, bounded deterministic fuzzy ranking, bounded usage history, and schema/hash/route-validated data-only aliases. Ranking and visibility never grant authority.

## Cumulative responsibility planes

- **Maintenance control:** protected scheduled/notice/active/completed/cancelled records, exact transition guards, local metrics and optional playbook linkage.
- **Security baseline:** declarative catalog settings, signed LGPO backup import with provider-internal rollback, Sysmon configuration and existing WDAC/Defender/firewall controls.
- **Offline servicing:** mounted-image driver, package and feature operations gated by explicit policy and healthy read-write mount state.
- **Desktop customization:** bounded context-menu registry trees, imported modern-menu definitions, Rainmeter/Windhawk/ExplorerPatcher inventory and no process injection or binary patch authority.
- **Product intelligence:** bounded read-only widgets, Bluetooth device/event telemetry and accessible capability-aware navigation.
- **Declarative orchestration:** playbooks may compose only existing typed WinCare plans; embedded PowerShell, batch, macro and arbitrary command execution are rejected.

## Privilege boundary

The TUI remains unelevated. An administrative action is serialized into an authenticated short-lived envelope whose operation/action identity, nonce, expiry, state root, result path, policy hash, helper hash and entire module-tree hash are checked again by `Host/ElevatedActionHost.ps1`. A replay record is created before execution. Results are independently authenticated and must match the request.

## Persistence and recovery

Mutable state lives under `%LOCALAPPDATA%\WinCare`. Operation journals use canonical serialization, a hash chain and schema-v4 lifecycle records. Terminal records include authenticated receipts binding the complete plan, results, warnings, state and event-chain head. Idempotency receipts prevent duplicate successful actions. A reversible action is accepted only when it carries a contract-valid static compensator or an explicitly provider-issued dynamic compensator; undo reconstructs a new normal plan only after journal integrity and current-state checks pass.

## Extension boundary

Declarative extensions are data archives only. Strict manifests enumerate every file, byte count and SHA-256. Traversal, absolute paths, duplicates, case collisions, links, executable/script content, unknown schemas, incompatible versions and untrusted signers are rejected before atomic installation.

## Release boundary

The deterministic builder normalizes path order, timestamps, compression and modes; generates an internal manifest, SBOM and build receipt; and verifies the exact ZIP independently. The external release receipt binds source, evidence and archive hashes.

See `docs/convergence/architecture.md` for peer composition and cross-plane contracts.
## Governed expert experimentation plane

The expert plane absorbs formerly blanket or folklore operations without bypassing WinCare authority:

- page-file state and crash-dump evidence become typed system-managed/custom/disabled plans with exact rollback;
- Defender, SmartScreen, UAC, and Windows Update reductions become expiring maintenance records with authenticated automatic restoration;
- network tuning becomes a one-variable experiment with bounded DNS/ICMP/TCP baselines, acceptance thresholds, cancellation, history, and automatic rollback;
- process instrumentation uses WPR/ETW, module/signature inventory, Sysmon evidence, and defensive Registry-surface quarantine rather than code injection.

The plane has no generic command runner, no hidden permanent disable switch, no multi-tweak network preset, and no API for allocating, writing, or starting code in another process.

## Operations convergence planes

### Verified download plane

`100-Downloads.ps1` owns a protected schema-versioned queue. Queue mutation is compare-and-swap. BITS owns transfer persistence; WinCare owns normalized state, mirror rotation, schedule admission, policy/config intersections, checksums, and finalization. Completed data is copied to a same-directory staging file, verified, atomically renamed into place, and protected by a reversible destination tombstone until final verification succeeds.

### Telemetry plane

`101-Telemetry.ps1` samples local structured Windows providers into a bounded public schema. Raw cumulative network counters remain transient; public history contains only derived rates and normalized process summaries. No network listener or remote collector exists.

### Launcher plane

`102-Launcher.ps1` discovers exact local targets from bounded Start-menu traversal, App Paths, Control Panel, and an allowlisted Settings-URI catalog. Target hashes are re-resolved at apply time. The calculator is a bounded token parser and never invokes PowerShell evaluation or a shell.

### Game-state plane

`103-GameState.ps1` owns Steam library/user/cache discovery, exact manifests, backup archives, conflict admission, rollback backups, and post-restore snapshot verification. Defensive game-integrity findings are read-only. Cheat execution and injection remain denied by policy and absent from the dispatcher.

### Offline-reduction plane

`104-OfflineReduction.ps1` exposes immutable strict profiles. It composes existing mounted-image feature actions with exact provisioned-AppX removal and supported component cleanup. Irreversible actions disable plan-wide automatic rollback and state their mounted-image recovery boundary.

### Layout plane

`105-WorkspaceLayouts.ps1` owns revisioned layout records and ratio slots. Layout application resolves current windows and monitors, applies timeout-bounded title matching, and emits exact `SetWindowBounds` actions with compensators.


### Workspace and Device Studio plane

`107-WorkspaceStudio.ps1` owns bounded Studio state and plans. It reuses the existing transaction engine rather than introducing a donor-specific executor. File workspaces, brightness schedules, folder appearance, package metadata, monitoring exports, Kanata/Syncthing/WezTerm integration, ADB inventory, and layout presets remain target-native. External-tool mutation is confined to `RunVerifiedPeerTool`, which requires exact hash admission, strict arguments, no shell, bounded runtime, expected exits, and policy. Xbox full-screen enablement composes this boundary with Critical Legacy Unsafe authorization and exact acknowledgement.

### Cleanup and AppX convergence plane

`108-CleanupMods.ps1` owns reversible Explorer quick settings, bounded cleanup profiles, AppX list admission/assessment, exact package-plan construction, coordinated Windows Update cache cleanup, and supported component cleanup. It does not introduce a donor-script runner. `106-PeerUtilities.ps1` composes the three new destructive profiles through the same typed-plan and policy path as all prior Legacy Unsafe actions.

## Experience Studio plane

The Experience Studio is a non-authoritative operator plane over existing typed owners. It exposes bounded visual metadata, location policy/service separation, exact RDP/OpenSSH plans, condition-aware power recommendations, BITS strategy inspection, privacy-profile composition, and searchable capability cards. Mutations remain plans until the standard policy, preview, apply, UAC, journal, verification, and recovery lifecycle authorizes them.

## Cleaner and File Preview Studio

`Providers/111-CleanerPreviewStudio.ps1` owns four additive responsibilities: pressure-based selection of existing cleanup targets, Winapp2 data admission and exact manifests, known-profile data relocation through three typed actions, and non-executing local preview. It reuses existing automation, archive, PE, image, managed-file, plan, policy, transaction, receipt, and journal owners. The TUI, V5 GUI, headless catalog, and broker remain non-authoritative projections over the same contracts.
