# WinCare Native Design System & Visual Specification

**Status:** Approved native Operate-mode design system  
**Visual direction:** Tactile Telemetry / Cyber-Operate

WinCare is a native Windows diagnostics, maintenance, and recovery workspace. Its visual language combines WinUI 3 Fluent materials, restrained tonal surfaces, monospaced telemetry, and a teal diagnostic accent. Product truth outranks decoration: words such as **verified**, **reviewed**, **safe**, **undo**, and **healthy** may appear only when the underlying runtime can prove them.

## Visual signature

```text
Shell surface     Windows Mica / Acrylic fallback
Dark background   #09131D
Light background  #F3F3F3
Dark card          #172531
Light card         #FFFFFF
Dark accent        #27D6CE
Light accent       #006F87
Typography         Segoe UI Variable + Cascadia Code telemetry
Corner scale       4 / 8 / 12 DIP
```

The theme resources in `src/WinCare.App/Styles/ThemeResources.xaml` are the authoritative palette. This document describes those resources; it must not duplicate stale color values.

## Color and semantic roles

| Resource | Light | Dark | Purpose |
|---|---:|---:|---|
| `PageBackgroundBrush` | `#F3F3F3` | `#09131D` | Main app surface |
| `CardSurfaceBrush` | `#FFFFFF` | `#172531` | Cards and table records |
| `TextPrimaryBrush` | `#1A1A1A` | `#F5F8FB` | Primary text |
| `TextSecondaryBrush` | `#5D5D5D` | `#B7C2CC` | Supporting text |
| `TextOnAccentBrush` | `#FFFFFF` | `#06151C` | Text on the primary accent |
| `AccentTealBrush` | `#006F87` | `#27D6CE` | Focus, active state, progress, diagnostic emphasis |
| `SuccessBrush` | `#0F7B0F` | `#75D36B` | Confirmed success only |
| `WarningBrush` | `#8A5700` | `#FFCB45` | Caution / degraded state |
| `DangerBrush` | `#C42B1C` | `#FF99A4` | Failure / destructive risk |

High Contrast uses Windows system colors rather than brand colors. `tools/verify_pill_contrast.py` remains the automated contrast gate for status-pill pairs. Accent text must also retain at least 4.5:1 contrast when used as small text.

## Typography

- `DisplayFontFamily`: Segoe UI Variable Display / Segoe UI
  - page titles: 32 DIP, SemiBold
  - hero telemetry: approximately 28–54 DIP depending on compact state
- `BodyFontFamily`: Segoe UI Variable Text / Segoe UI
  - descriptions: 14 DIP
  - row titles: 14 DIP SemiBold
  - secondary text: 12 DIP
- `TelemetryFontFamily`: Cascadia Code / Cascadia Mono / Consolas
  - command IDs, hashes, evidence, status, advanced JSON, and other machine-readable values

Text containers should wrap and grow. Avoid fixed text-control heights; Windows text scaling up to 225% is a release-gate scenario.

## Corner radius scale

- `RadiusPill` = 4 DIP — badges and micro-status
- `RadiusControl` = 8 DIP — buttons, inputs, inner panels, dialogs
- `RadiusCard` = 12 DIP — cards and major surface containers

## Interaction and task hierarchy

WinCare is an **Operate** interface. Each screen should make one primary task obvious, keep evidence close to decisions, and progressively disclose lower-level detail.

### Command execution

```text
Choose tool
  → complete typed parameters
  → preview
  → inspect evidence / affected resources
  → dispatcher issues a short-lived single-use review receipt
  → explicit approval
  → apply
  → record outcome / certainty in Activity
  → offer compensation only when a concrete executable compensator exists
```

All Tools renders declared parameter schemas as native controls. Raw JSON exists only as an explicit **Advanced parameter editing** escape hatch for power users; it is not the default authoring experience.

### Plugin trust

- An unsigned or unverified remote catalog is browse-only.
- Remote installation is enabled only when the exact catalog bytes verify against the WinCare-pinned catalog key and the selected package has publisher-signed manifest metadata.
- Capability declarations are informed-consent metadata, not an in-process sandbox.
- Destructive plugin removal requires confirmation and restores prior enabled state if package removal fails.

## Responsive architecture

`LayoutVisibility.CompactBreakpointDip = 920.0` is the app-level compact-mode boundary.

At widths below 920 DIP:
- desktop tables collapse into stacked records;
- Checkup collapses both hero and evidence layouts;
- All Tools stacks its search/filter controls and uses an overlay detail pane;
- Home stacks its hero/status/safety surfaces and primary actions;
- System Care, Security, Repair & Recovery, and Activity use compact row presentations.

A component may have a narrower local breakpoint only for its own internal header/content fit (for example a search/header arrangement). Such a breakpoint must not redefine app-level `IsCompactLayout` and must be documented in the component source.

## Accessibility floor

- Native WinUI controls are preferred over custom interaction primitives.
- Interactive controls require meaningful `AutomationProperties.Name` and stable automation IDs where they are part of an important flow.
- Keyboard navigation, visible focus, High Contrast, Narrator, 100/150/200/225% text scaling, and narrow-window behavior are release checks.
- Minimum primary-action target height is 44 DIP; layouts should allow larger controls when text grows.
- Never rely on color alone to communicate read-only, mutating, elevated, failed, or verified state.

## Strict exclusions

- **No hero gradients.** Hero and accent resources are tonal solid brushes.
- **No decorative glass orbs.** Use native Mica/Acrylic and restrained tonal surfaces.
- **No caller-minted approval.** Mutation requires a dispatcher-issued preview receipt bound to command, parameters, correlation ID, lifetime, and single use.
- **No generic Undo claim.** Show Undo only when an executable compensator exists.
- **No unverifiable health score.** Checkup reports evidence-collection coverage unless an actual health model exists.
- **No false publisher verification.** Catalog/package consistency is not publisher identity unless the catalog trust root verified.
- **No decorative settings.** A setting exists only when it changes real persisted behavior.
- **No inconsistent icon family.** Use Windows Fluent iconography.
- **No raw exception disclosure in user-facing copy.** Detailed failures belong in protected diagnostics.

## WinUI implementation standards

- Prefer compiled `{x:Bind}` for stable view bindings; use runtime bindings only where the interaction requires them.
- Keep presentation state in ViewModels; platform-only layout mechanics may remain in code-behind when WinUI visual states are not practical.
- XAML colors come from `ThemeResources.xaml`; controls use semantic theme resources instead of literal colors.
- Long collections use virtualizing WinUI list controls.
- Async operations expose loading, success, failure, cancellation, and degraded states instead of failing silently.
- Unknown fatal UI faults are logged and allowed to terminate rather than being swallowed and continued in an unknown process state.

## Native / safety invariants that affect UX

- Rust C ABI entry points remain panic-contained with caller-owned buffers and explicit safety comments.
- Mutating handler failures after execution begins are presented as **final system state unknown** unless the handler can prove a terminal state.
- Activity and preference persistence must not block UI state locks; durability failures are visible to the user.
- Checkup runs measurement-sensitive probes sequentially so one probe's load does not contaminate another probe's evidence.
- WinCare Guard is an experimental daemon boundary until SCM installation/lifecycle and app notification consumption are production-wired; the UI and docs must not imply otherwise.

## Visual evidence policy

Checked-in runtime screenshots are historical evidence for the exact build they name. They are never a perpetual source of truth after UI code changes. `docs/Screenshots.md` records capture provenance and whether images are current or need recapture. Current XAML/theme resources are the source of truth between verified capture runs.
