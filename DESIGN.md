# WinCare Native Design System & Visual Specification

**Status:** Approved native Operate-mode design system  
**Visual Direction:** Tactile Telemetry / Cyber-Operate Cockpit  
**Target Platform:** Windows 10 (19041+) / Windows 11 (WinUI 3 / Windows App SDK 2.3.1)  
**Authoritative Standard:** `frontend-design-deslop` / `impeccable` / `high-end-visual-design` (Operate Mode)

---

## 1. Global Vision & Aesthetic Philosophy

WinCare is a native Windows maintenance, diagnostic, and optimization cockpit. Its visual language combines WinUI 3 Fluent materials, restrained tonal surfaces, monospaced tabular telemetry, and a vivid diagnostic teal accent.

### Core Product Principles
1. **Product Truth Outranks Decoration:** Words such as *verified*, *reviewed*, *safe*, *undo*, and *healthy* appear ONLY when proven by runtime facts. No artificial 100% scores based on mere probe completion. Undo is exposed only when a tool produces an invertible receipt snapshot.
2. **Elevate Daily Users to Power Users (Progressive Disclosure):**
   - **Surface Layer:** 1-click everyday maintenance cards designed for speed and clarity.
   - **Deep Layer:** Non-intrusive "System Impact & Telemetry" inspector revealing exact microsecond probe timings, active Win32/NT kernel calls, dry-run diffs, and rollback snapshot IDs.
3. **Anti-Slop Strictness:**
   - ❌ NO gratuitous purple-to-blue AI gradient glows or floating decorative orbs.
   - ❌ NO fake "glassmorphism" that compromises light mode contrast.
   - ❌ NO layout shifts on hover or state transitions; use `scale(0.98)` on active press.
   - ❌ NO confirmation fatigue for safe, idempotent tasks.
   - ❌ NO non-tabular numerals for telemetry or latency metrics.

---

## 2. Design Tokens

The theme resources in `src/WinCare.App/Styles/ThemeResources.xaml` are the authoritative palette.

### 2.1 Color Palette & Semantic Roles (60-30-10 Distribution)

- **Canvas / Backdrop (60%):** Restrained neutral canvas (`PageBackgroundBrush`).
- **Elevated Surfaces (30%):** Structured functional panels (`CardSurfaceBrush`) separated by clean 1px borders (`CardBorderBrush`).
- **Semantic Accents (10%):** Single diagnostic focus accent paired with strict status indicators.

| Token Name | Light Value | Dark Value | Semantic Purpose |
|---|---|---|---|
| `PageBackgroundBrush` | `#F3F3F3` | `#09131D` | Canvas and shell surface (60% field) |
| `CardSurfaceBrush` | `#FFFFFF` | `#172531` | Cards, grouped containers, and list rows (30% surface) |
| `CardBorderBrush` | `#E2E8F0` | `#223544` | Subtle container outline (1px border) |
| `TextPrimaryBrush` | `#0F172A` | `#F5F8FB` | High-contrast headers and primary text |
| `TextSecondaryBrush` | `#475569` | `#94A3B8` | Subtitles, labels, and secondary metadata |
| `AccentTealBrush` | `#006F87` | `#27D6CE` | Diagnostic focus, active tabs, progress (10% accent) |
| `TextOnAccentBrush` | `#FFFFFF` | `#06151C` | Text overlaid on primary accent controls |
| `SuccessBrush` | `#0F7B0F` | `#75D36B` | Confirmed healthy states / completed actions |
| `WarningBrush` | `#8A5700` | `#FFCB45` | Needs attention / non-critical warnings |
| `DangerBrush` | `#C42B1C` | `#FF99A4` | Destructive risk / critical alerts |

### 2.2 Typography Hierarchy
- **Display Font:** `Segoe UI Variable Display` / `Segoe UI`
  - Page Titles: `32 DIP`, SemiBold (`FontWeight.SemiBold`)
  - Section Headers: `20 DIP`, SemiBold
- **Body Font:** `Segoe UI Variable Text` / `Segoe UI`
  - Card Titles: `16 DIP`, SemiBold
  - Body Descriptions: `14 DIP`, Normal (`LineHeight = 20 DIP`)
  - Micro-Labels & Captions: `12 DIP`, Medium
- **Telemetry & Metric Font:** `Cascadia Code` / `Cascadia Mono` (Strictly Tabular Figures)
  - Latencies (`142 µs`), hashes, byte counts (`2.4 GB`), registry keys, paths, and status codes.

### 2.3 Spacing Rhythm & Scale (4px Base)
All padding, margins, and gaps must strictly adhere to the 4-pixel base scale:
- `Space-1` = `4 DIP` (micro-gaps inside badges/pills)
- `Space-2` = `8 DIP` (spacing between icon and label, tight form fields)
- `Space-3` = `12 DIP` (standard button padding, inner card margins)
- `Space-4` = `16 DIP` (card content padding, stack panel gaps)
- `Space-6` = `24 DIP` (section vertical separation)
- `Space-8` = `32 DIP` (major page margins and grid gutters)

### 2.4 Mathematical Concentric Radii Scale
- `RadiusOuterCard` = `14 DIP` — Outer shell container
- `RadiusInnerCard` = `10 DIP` — Inner content core (`calc(14 - 4)`)
- `RadiusControl` = `8 DIP` — Standard buttons, inputs, dropdowns
- `RadiusPill` = `4 DIP` — Status pills, badges, micro-tags

---

## 3. Component State Matrix & Interactive Patterns

Every interactive component must implement its full state lifecycle:

| Component | Resting (Idle) | Hover | Active (Pressed) | In-Flight (Running) | Success | Error |
|---|---|---|---|---|---|---|
| **1-Click Card** | Clean 1px border, teal action button | Subtle border tint (`CardBorderBrushHover`) | `scale(0.98)` physical push | Action button replaced by indeterminate ProgressRing | Green check badge + reclaimed summary | Amber/Red inline InfoBar with retry |
| **Deep Inspector** | Collapsed expander with subtle chevron | Text underline on header | Immediate expand/collapse animation | Live streaming data rows | Verified checkmark next to metric | Red warning tag on failed probe |
| **Safety Button** | Ranked by risk tier (Safe = Teal, Destructive = Red) | Elevated brightness +10% | `-translate-y(1px)` | Disabled with spinner | Toast receipt | Shake animation + error message |

---

## 4. Anti-Slop Quality Gate & Self-Audit

Before delivering any UI surface, the implementation must pass this audit:
- [x] **No AI Slop Colorways:** 0 purple/indigo gradient buttons; 0 raw uncalibrated pure-black `#000000` fills.
- [x] **Tabular Numerals:** All latency ($\mu$s, ms), memory, and size metrics use `Cascadia Code` with tabular digits.
- [x] **WCAG 2.2 AA Contrast:** All button labels, status pills, and text elements satisfy $\ge 4.5:1$ contrast ratio against their immediate background.
- [x] **Zero Layout Jumps:** Expanders use bounded transitions; buttons do not resize on hover or active states.
- [x] **Deterministic Copy:** No AI copywriting filler ("Elevate your experience", "Seamless optimization"). Use direct action verbs ("Reclaim 2.4 GB", "Flush DNS", "Optimize Startup").
- [x] **Complete State Coverage:** Every card and button handles Idle, Hover, Pressed, In-Flight, and Error fallback states.

---

## 5. High-End Haptic Craft & Visual Architecture (`high-end-visual-design` / `impeccable`)

### 5.1 The "Double-Bezel" (Doppelrand) Architecture
To escape flat, generic card boundaries, major action containers employ a machined double enclosure:
- **Outer Shell:** Subtle outer hairline boundary (`CardBorderBrush`, `CornerRadius="14"`) with `3 DIP` padding.
- **Inner Core:** Distinct elevated surface (`CardSurfaceBrush`, `CornerRadius="10"`) featuring a subtle top edge highlight simulating physical hardware machining.

### 5.2 Button-in-Button Trailing Icon Architecture
Primary action buttons on 1-click cards pair action text with an embedded circular icon container:
- **Action Label:** Left-aligned text (`"Clean Now"`, `"Tune Latency"`).
- **Embedded Icon Circle:** Right-aligned `24x24 DIP` circular pill (`CornerRadius="12"`, background tinted with `AccentTealSubtleBrush`) housing the action glyph (`FontIcon`). On active press, the inner icon translates subtly to provide tactile kinetic feedback.

### 5.3 Deep Telemetry Inspector Bay
When the power-user expander is toggled:
- **Telemetry Surface:** Renders inside an indented telemetry bay with a darker contrast background (`#0D1924` in dark mode, `#F1F5F9` in light mode).
- **Tabular Grid:** Left-aligned metric name, right-aligned monospace value in `Cascadia Code`, and a middle status indicator pill. Zero visual clutter; maximum scanability.

---

## 6. Responsive Architecture & Strict Policy Exclusions

### 6.1 Responsive Breakpoint
`LayoutVisibility.CompactBreakpointDip = 920.0` is the app-level compact-mode boundary.

At widths below 920 DIP:
- Desktop tables collapse into stacked records;
- Checkup collapses both hero and evidence layouts;
- All Tools stacks its search/filter controls and uses an overlay detail pane;
- Home stacks its hero/status/safety surfaces and primary actions;
- System Care, Security, Repair & Recovery, and Activity use compact row presentations.

### 6.2 Strict Exclusions & Product Truth
- **Accessibility floor is mandatory.** Every interactive surface supports keyboard navigation in logical order, preserves a visible focus indicator, exposes meaningful automation names for assistive technology, provides a minimum `44 x 44 DIP` interactive target, and remains usable in Windows High Contrast mode.
- **No hero gradients.** Hero and accent resources are tonal solid brushes.
- **No decorative glass orbs.** Use native Mica/Acrylic and restrained tonal surfaces.
- **No caller-minted approval.** Mutation requires a dispatcher-issued preview receipt bound to command, parameters, correlation ID, lifetime, and single use.
- **No generic Undo claim.** Show Undo only when an executable compensator exists.
- **No unverifiable health score.** Checkup reports evidence-collection coverage unless an actual health model exists.
- **No false publisher verification.** Catalog/package consistency is not publisher identity unless the catalog trust root verified.
- **No decorative settings.** A setting exists only when it changes real persisted behavior.
- **No inconsistent icon family.** Use Windows Fluent iconography.
- **No raw exception disclosure in user-facing copy.** Detailed failures belong in protected diagnostics.

