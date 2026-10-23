# Changelog & Release Notes

## Unreleased — native Downloads hardening

- Protect private transfer URLs with Windows user-scoped DPAPI; migrate legacy queues, omit private URLs from queue responses, and remove them on completion/cancellation.
- Serialize destination reservations and hold crash-released handles across transfer and filesystem recovery; reject competing processes and unsafe path aliases.
- Save verified publication evidence before rename and require Internet-zone origin metadata before publication or completed-file recovery.
- See [behavior, recovery limits and validation status](docs/releases/download-hardening.md). Development signing and unperformed device-level acceptance remain separate from production certification.

All notable changes to WinCare are documented in this file in accordance with [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) principles.

This history restarts at 3.0.0 with the Fluent redesign re-release. The safety model is unchanged: risk-tiered admission (Safe/Moderate/Destructive/Critical), dispatcher-issued single-use approval capabilities, fail-closed command dispatch, the frozen 269-command catalog, and plugin trust/signing all behave exactly as before; 3.0.0 marks the design-system and code-hygiene reset, not a change in what the app is willing to touch.

---

## [Unreleased]

### Safety

- Extension packs and extension manifests now share one host-owned admission floor (`HostRiskPolicy`): a mutating command declared by third-party code can no longer derive the Safe tier, so it is always admitted through the Moderate tier and requires an explicit user confirmation. Read-only commands stay Safe, unreadable risk tiers are preserved for explicit dispatcher rejection, and contradictory metadata (a read-only risk tier declared with `readOnly=false`) cannot buy a bypass.
- Preset execution digests are now recomputed with the dispatcher's own elevation probe, so the admission gate and the digest can no longer disagree about whether a preset is executable.
- The plugin trust store now reads the same shared elevation answer as the dispatcher, preset digest, and native executor. A second private copy of that probe could answer differently for one process, which would let a trust anchor be decided by a different answer than the command gate that depends on it.
- Documented the host-owned admission floor in `docs/Architecture.md`.
- The shared parameter validator no longer faults a dispatch on a malformed schema. A declared numeric bound that cannot be parsed, and a schema that declares one parameter name twice under different casing, are now reported as blocked requests instead of throwing out of the validator before the dispatcher's own error handling could run.
- Numeric bounds are now compared exactly. A 64-bit value above 2^53 does not survive a round trip through a double, so a value and its declared limit that differed by one collapsed onto the same number and an out-of-range value was admitted. Integral bounds are compared with arbitrary-precision integers.
- The required-parameter check now matches the case-insensitive lookup used for every other parameter. A payload supplying `packagenames` for a declared `PackageNames` was accepted by the value check and then rejected as missing the parameter.

### User experience

- The destructive-change approval surface no longer appears before there is anything to review. The permanent-change warning and the "I reviewed this change" switch now open only after a successful preview has resolved the real targets, instead of stacking a warning, a switch, and a procedure paragraph on top of a "Preview impact" button that had nothing to approve yet.
- A failed preview resolves no targets and therefore opens no approval surface: nothing can be approved on the strength of a preview that did not succeed.
- The tool inspector's flow heading now reads "Approve to apply" at the approval stage instead of continuing to say "Preview before applying" when the preview was already done.
- The review-approval explanation now states plainly that the approval applies once to the current inputs.


## [4.0.0-rc1] - 2026-10-03

### Kinetic Mission Control Overhaul

- **Eight Strategic Kinetic Pillars (Zero-Stub Guarantee)**:
  - Developer Workspaces (`cleaner-developer-uv`, `cleaner-ide-cursor-snapshots`, `cleaner-ai-agent-ledgers`, native SQLite database vacuuming via `winsqlite3.dll`).
  - Servicing & OS Footprint (`cleaner-servicing-remnants` with elevated token privileges, DISM component store resetbase, Windows Update cache purging, CompactOS compression).
  - Virtualization & Containers (WSL2 guest `fstrim -v /` across registered distros with host `ext4.vhdx` diskpart compact, Docker volume/builder pruning).
  - Storage Deduplication (exact hash deduplication and safe storage cleanup).
  - Hardware & Performance Optimization (Multi-Plane Overlay flags, multimedia timer resolution tuning to 0.5ms precision, Ultimate Performance power plan).
  - Desktop Ergonomics & Shell Visuals (desktop drop shadows, snap assist, window corner rounding customization).
  - Background Guard Service (IPC named pipe listener with token-based access control and native Rust ETW monitor integration).
  - Application & MSI Remediation (application remnants and orphaned MSI installer cache cleanups).
- **Subsystem Command Executor Architecture**:
  - Implemented modular `ISubsystemCommandExecutor` and `SubsystemCommandRegistry` across Dism, Driver, Remediation, and Storage domains (`src/WinCare.Application/Commands/Subsystems/`).
  - Enforced two-phase execution: mutating requests run validation and generate a concrete `MutationPreview` detailing affected resources before changes apply.
  - Standardized file system traversal on `SafeRecursiveEnumeration` (`RecurseSubdirectories = true`, `IgnoreInaccessible = true`, `AttributesToSkip = FileAttributes.ReparsePoint`).
- **Interactive Topology & Web Showcase**:
  - Replaced legacy showcase with compiled semantic SVG topology diagram and interactive simulator at `docs/index.html`.
  - Offline-first architecture with zero external CDN dependencies.
- **Safety, Ergonomics & Memory Safety**:
  - 1-click execution for Safe and Moderate operations; confirmation switches strictly bounded to Destructive actions.
  - Complete memory safety audit (`docs/Memory-Safety-Audit.md`) covering native Rust boundary invariants and unsafe block containment.
  - Rust ETW monitor (`native/wincare-guard/src/monitors/etw.rs`) for proactive system health tracking.

## [3.0.0]

### Design system

- Re-based the theme system on a Fluent-aligned palette for both Light and Dark, with surface, text, border, status, telemetry, and hero colors chosen against measured WCAG AA contrast (all 8 pill and 14 text foreground/background pairings measure ≥ 4.5:1 across Light and Dark, enforced by the `verify_pill_contrast` and `verify_palette_contrast` source gates).
- Brand teal left the chrome: accent-colored surfaces now track the Windows system accent (`AccentFillColorDefaultBrush` / `TextOnAccentFillColorPrimaryBrush`), so WinCare's buttons and selection follow the user's Windows setting like a first-party app.
- Status pills keep literal, gate-measured colors and gained a corrected composition (text/background pairs pinned to the runtime consumer mapping); high-contrast mode no longer collapses distinct statuses onto fill hue; state is carried by text and borders with system ink colors.
- Added spacing tokens (`SpacingXS`–`XXL`, `GapXS`–`XL`) for use by future passes.
- Landed the interface design pass across runtime chrome: Checkup consolidated to a single results section whose rows update in place, and Home, Help, AI Doctor, and All Tools layouts simplified without losing a live control.

### Accessibility

- Fixed the high-contrast status-color collapse described above; HC users now distinguish pill states without relying on hue.

### Architecture

- Extracted global-search ranking from `MainWindow` into `WinCare.Application.Navigation.GlobalSearchService`, retaining identical ranking behavior, now unit-tested (11 tests) and free of window code; route keys resolve through `NavigationCatalog` at construction so a catalog rename fails at startup instead of emitting dead suggestions.

### Naming

- Renamed the checkup summary chrome `HealthScore*` → `CheckupStatus*`; it reports qualitative states ("Action needed", "Looks good", …) and never a numeric score.

### Release engineering

- Navigation route contract (catalog ⇄ PageService ⇄ ShellPage tags) is now enforced by `verify_native_foundation.py` in both directions, with the hidden `about` route as the only exception.
- Nav/page chrome labels are pinned identical across `NavigationCatalog`, XAML attributes, and `Resources.resw` by a new source gate.
- Pill contrast verification corrected to measure the actual runtime foreground/background composition.
- Added a read-only `--capture-screens` mode that renders each documented route to PNG from a built portable executable. Its provenance manifest (`docs/images/runtime-captures.json`) records route, architecture, version, commit, and appearance, and `docs/Screenshots.md` is generated from that manifest so the document cannot drift from its images; the CI smoke job re-runs the capture against the signed artifact.
- Care-area taxonomy is now pinned in both directions by a source gate: each care view model claims exactly the catalog's sections (no orphans, no phantoms), and each page's tab titles equal its `NavigationCatalog` section list. The Checkup route's stale `Quick check`/`Results` sections were aligned to the single section the page shows.
