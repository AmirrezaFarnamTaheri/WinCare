# WinCare Native Design System & Visual Specification

**Status:** Approved Native Design System (Tactile Telemetry & Cyber-Operate Engine)  
**DFII Score:** 18/15 (Aesthetic Impact: 4, Fit: 5, Feasibility: 5, Performance: 5, Consistency Risk: 1 -> **Excellent: Execute Fully**)

---

## 🎨 Visual Signature & Character Identity

WinCare is an **Operate-Mode Industrial Diagnostic & Recovery Engine**. It avoids generic SaaS layouts and cliché AI gradient clutter. Instead, it adopts a distinct **Tactile Telemetry & Cyber-Operate** visual character—combining native Windows Fluent Mica surfaces with crisp monospaced telemetry readouts, high-contrast status pill badges, and electric cyan/teal accent highlights.

```text
Visual System Architecture (Tactile Telemetry & Cyber-Operate)
├── Shell Surface    : Windows Mica backdrop with Obsidian Slate depth (#202020 Dark / #F3F3F3 Light)
├── Accent Identity  : Electric Cyber-Teal (#00D2B4 Dark / #007A99 Light) for active states & primary focus
├── Typography       : Segoe UI Variable (Display H1) + Cascadia Code (Monospaced telemetry readouts)
├── Status Badges    : Monospaced status pills ([ READ-ONLY ], [ MUTATING ], [ ELEVATED ], [ NOT READY ])
└── Corner Ergonomics: Subtle 1px corner frame accents on diagnostic KPI surfaces (4px / 8px / 12px radius)
```

---

## 📐 Color Tokens & Palette Matrix

| Role | Dark Token | Light Token | Purpose | Contrast Ratio (WCAG) |
|---|---|---|---|:---:|
| `bg-shell` | `#202020` (Mica Slate) | `#F3F3F3` (Mica Light) | App backdrop surface | — |
| `bg-card` | `#161C26` (Tonal Card) | `#FFFFFF` (Tonal Card) | Command & checkup container surfaces | — |
| `fg-primary` | `#F8FAFC` (Pure Slate) | `#0F172A` (Deep Slate) | Primary titles & headers | > 12:1 |
| `fg-secondary` | `#94A3B8` (Muted Slate) | `#475569` (Subtle Slate) | Descriptive summaries & subtext | > 4.5:1 |
| `AccentTealBrush` | `#00D2B4` (Electric Cyber Teal) | `#007A99` (Deep Cyber Teal) | Active selections, progress rings, focus rings | 5.1:1 |
| `PillReadOnlyBgBrush` | `#047857` (Safe Emerald) | `#047857` (Safe Emerald) | Safe diagnostic findings & read-only badges | **5.48:1** (White text) |
| `PillElevatedBgBrush` | `#F59E0B` (Alert Amber) | `#D97706` (Alert Amber) | Moderate risk & elevation warning badges | **8.10:1** (#1A1A1A text) |
| `PillMutatingBgBrush` | `#C41A1A` (Hazard Crimson) | `#DC2626` (Hazard Crimson) | High risk, mutating operations, critical alerts | **5.98:1** (White text) |
| `PillNotReadyBgBrush` | `#1F2937` (Muted Slate) | `#E2E8F0` (Muted Light) | Unimplemented / blocked candidate status | **14.68:1** |

> All 8 pill color pairs pass **WCAG 2.1 AA** (≥ 4.5:1), verified continuously by `tools/verify_pill_contrast.py`.

---

## 🔤 Typography Hierarchy & Centralized Tokens

- **Display Font Token (`DisplayFontFamily`)**: `Segoe UI Variable Display, Segoe UI, sans-serif`
  - **Page Titles (H1)**: `32px`, SemiBold (`PageTitleTextStyle`)
  - **Hero Readouts**: `42px - 54px`, SemiBold
  - **App Title**: `18px`, SemiBold (`AppTitleTextStyle`)
- **Body Font Token (`BodyFontFamily`)**: `Segoe UI Variable Text, Segoe UI, sans-serif`
  - **Section Descriptions**: `14px`, Regular (`PageDescriptionTextStyle`)
  - **Row Titles**: `14px`, SemiBold (`RowTitleTextStyle`)
  - **Secondary Subtext**: `12px`, Regular (`RowSecondaryTextStyle`)
  - **Column Headers**: `12px`, SemiBold (`ColumnHeaderTextStyle`)
- **Telemetry Font Token (`TelemetryFontFamily`)**: `Cascadia Code, Cascadia Mono, Consolas, Courier New`
  - Monospaced telemetry readouts, command IDs, parameter JSON, capability tags, file hashes, byte counts, and status pills (`TelemetryTextStyle`, `StatusPillTemplate`).

---

## 🔘 Standardized Corner Radius Scale

All interactive controls and structural surfaces adhere strictly to a locked 3-tier corner radius scale:
- **`RadiusPill` (`4px`)**: Status pill tags, badge containers, micro-indicators.
- **`RadiusControl` (`8px`)**: Buttons, text inputs, combo boxes, dialog panels, card inner tiles.
- **`RadiusCard` (`12px`)**: Main dashboard cards, surface containers, split-view panels (`SurfaceBorderStyle`, `DashboardCardStyle`).

---

## 📦 Tabular Surface Architecture

Tabular views across `AllToolsPage`, `CheckupPage`, `PluginStorePage`, and `ActivityPage` implement a **Compound Grid-List Pattern**:

```text
+-------------------------------------------------------------------------------------------------------+
| Search Palette (Ctrl+K): [ Search catalog tools... ]  [ Area: All v ]  [ Risk: All v ]  [x] ReadOnly |
+-------------------------------------------------------------------------------------------------------+
| Command (2*)              | Category (1*)   | Risk Badge (1*)  | Admin Access (1*) | Status Pill (80) |
+-------------------------------------------------------------------------------------------------------+
| sysinfo                   | System Care     | [ READ-ONLY ]    | No                | [ VERIFIED ]     |
| disk_cleanup              | Maintenance     | [ MODERATE  ]    | Required          | [ MUTATING ]     |
| wdac_audit                | Security        | [ HIGH RISK ]    | Required          | [ MUTATING ]     |
+-------------------------------------------------------------------------------------------------------+
| Detail Drawer (Right/Overlay): Diagnostic Review -> Affected Paths -> Apply (ReviewApproved) -> Undo  |
+-------------------------------------------------------------------------------------------------------+
```

### Micro-Ergonomic Details
1. **Status Micro-Pills**: Monospaced status pill badges (`[ READ-ONLY ]` in Emerald, `[ MUTATING ]` in Crimson) for instant visual scanning.
2. **Telemetry Corner Accents**: High-priority KPI cards feature subtle 1px corner accents (`border-subtle`) to establish an industrial diagnostic tone.
3. **Responsive Cards (< 920 DIP)**: On narrow windows under 920 DIP, tabular rows collapse into compact stacked records with monospaced telemetry headers (`LayoutVisibility.CompactBreakpointDip = 920.0`).

---

## 🚫 Strict Exclusions (Anti-AI Slop Rules)

- **No Hero Gradients**: Surfaces remain clean, tonal, and readable without cliché purple/indigo gradients.
- **No Decorative Glass Orbs**: Backdrop materials rely strictly on Windows native Mica and Acrylic.
- **No Unverifiable State**: Every mutating action requires explicit review approval (`ReviewApproved = true`).
- **No Inconsistent Icons**: All visual indicators use official Windows Fluent icons (`Segoe Fluent Icons` / SVG).
- **No Deceptive Color Signals**: Mutating operations never dress in green Read-Only badges.

---

## 🦀 Native Core & FFI Safety Invariants

All underlying Rust native engine components (`native/wincare-core`) strictly enforce Rust 2024 edition guidelines:
- **`#[unsafe(no_mangle)]`**: Required attribute syntax for exported C-ABI symbols such as `wincare_core_dir_size` and `wincare_core_sys_info`.
- **Unwind Safety (`catch_unwind`)**: Every FFI entry point wraps internal Rust logic in `std::panic::catch_unwind` to prevent unwinding across C ABI boundaries (`err-result-over-panic`).
- **Caller-Owned Pointer/Length ABI**: Status codes use `repr(i32)`. Byte buffers cross the C ABI as caller-owned pointer-plus-length pairs (`uint8_t*` / `byte*` with `size_t` / `nuint`). The caller keeps each buffer valid for the duration of the call; Rust neither takes ownership nor retains the pointer after returning.
- **Explicit Safety Comments (`unsafe-safety-comment`)**: Every `unsafe` block includes an explicit `// SAFETY:` invariant comment and `# Safety` doc comment section.

---

## 🔷 .NET 8 & WinUI 3 Architecture Standards

All C# ViewModel and XAML View components strictly adhere to .NET 8 and WinUI 3 desktop engineering guidelines:
- **Compiled XAML Bindings (`{x:Bind}`)**: All UI controls use compiled `{x:Bind}` with explicit `Mode=OneWay` or `Mode=TwoWay` to eliminate runtime reflection overhead and achieve zero-allocation data binding.
- **Clean MVVM Separation (CommunityToolkit.Mvvm)**: ViewModels inherit `ObservableObject` and encapsulate dynamic presentation logic using C# 12 pattern-matching switch expressions (`ToolRowViewModel.cs`).
- **Dynamic Theme Resource System (`{ThemeResource}`)**: Zero hardcoded colors in XAML. All brushes bind dynamically to `{ThemeResource}` keys defined in `ThemeResources.xaml`.
- **Keyboard Ergonomics & Accessibility**: `Ctrl+F` global shortcut for catalog search focus, 3px high-contrast `FocusVisualPrimaryBrush` focus rings, and explicit `AutomationProperties.AutomationId` / `AutomationProperties.Name` on every interactive control.
