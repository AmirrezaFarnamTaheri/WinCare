# WinCare Native Design System & Visual Character Specification

**Status:** Approved Native Design System (Tactile Telemetry & Cyber-Operate Engine)

## 🎨 Visual Signature & Character Identity

WinCare is an **Operate-Mode Industrial Diagnostic & Recovery Engine**. It avoids bland generic SaaS layouts and cliché purple AI gradients. Instead, it adopts a distinct **Tactile Telemetry & Cyber-Operate Engine** visual character—combining native Windows Fluent Mica surfaces with crisp monospaced telemetry readouts, status pill badges, and electric cyan accent highlights.

```
Visual System Architecture (Tactile Telemetry & Cyber-Operate)
├── Shell Surface   : Windows Mica backdrop with Obsidian Slate depth (#0B0F17 Dark / #F4F6F9 Light)
├── Accent Identity : Electric Cyber-Teal (#00D2FF / #0078D4) for active states & primary focus
├── Typography      : Segoe UI Variable (Display H1) + Cascadia Code (Monospaced telemetry readouts)
├── Status Badges   : Monospaced status pills ([ READ-ONLY ], [ MUTATING ], [ ELEVATED ])
└── Corner Ergonomics: Subtle 1px corner frame accents on diagnostic KPI surfaces (8px / 12px radius)
```

---

## 📐 Color Tokens & Palette Matrix

| Role | Dark Token | Light Token | Purpose |
|---|---|---|---|
| `bg-shell` | `#0B0F17` (Mica Slate) | `#F4F6F9` (Mica Light) | App backdrop surface |
| `bg-card` | `#161C26` (Tonal Card) | `#FFFFFF` (Tonal Card) | Command & checkup container surfaces |
| `fg-primary` | `#F8FAFC` (Pure Slate) | `#0F172A` (Deep Slate) | Primary titles & headers |
| `fg-secondary` | `#94A3B8` (Muted Slate) | `#475569` (Subtle Slate) | Descriptive summaries & subtext |
| `accent-telemetry`| `#00D2FF` (Electric Teal) | `#0078D4` (Windows Blue) | Active selections, progress rings, focus rings |
| `status-emerald`  | `#10B981` (Safe Emerald) | `#059669` (Safe Emerald) | Safe diagnostic findings & read-only badges |
| `status-amber`    | `#F59E0B` (Alert Amber) | `#D97706` (Alert Amber) | Moderate risk & elevation warning badges |
| `status-crimson`  | `#EF4444` (Hazard Crimson) | `#DC2626` (Hazard Crimson) | High risk, mutating operations, critical alerts |

---

## 🔤 Typography Hierarchy

- **Page Titles (H1)**: Segoe UI Variable Display (`28px`, SemiBold, `-0.5px` tracking).
- **Section Headers (H2)**: Segoe UI Variable Display (`20px`, Medium).
- **Body & Controls**: Segoe UI Variable Text (`14px`, Regular).
- **Telemetry Data & IDs**: **Cascadia Code** / **Fira Code** (`12px`, Monospaced) for command IDs, file hashes, byte counts, and execution status pills.

---

## 📦 Tabular Surface Architecture

Tabular views across `AllToolsPage`, `CheckupPage`, and `ActivityPage` implement a **Compound Grid-List Pattern**:

```text
+-------------------------------------------------------------------------------------------------------+
| Search Palette (Ctrl+K): [ 🔍 Search catalog tools... ]  [ Area: All v ]  [ Risk: All v ]  [x] ReadOnly |
+-------------------------------------------------------------------------------------------------------+
| Command (2*)              | Category (1*)   | Risk Badge (1*)  | Admin Access (1*) | Status Pill (80) |
+-------------------------------------------------------------------------------------------------------+
| 🔍 sysinfo                | System Care     | [ READ-ONLY ]    | No                | [ VERIFIED ]     |
| 🧹 disk_cleanup           | Maintenance     | [ MODERATE  ]    | Required          | [ MUTATING ]     |
| 🔒 wdac_audit             | Security        | [ HIGH RISK ]    | Required          | [ MUTATING ]     |
+-------------------------------------------------------------------------------------------------------+
| Detail Drawer (Right/Overlay): Diagnostic Review -> Affected Paths -> Apply (ReviewApproved) -> Undo  |
+-------------------------------------------------------------------------------------------------------+
```

### Micro-Ergonomic Details
1. **Status Micro-Pills**: Monospaced status pill badges (`[ READ-ONLY ]` in Emerald, `[ MUTATING ]` in Crimson) for instant visual scanning.
2. **Telemetry Corner Accents**: High-priority KPI cards feature subtle 1px corner accents (`border-subtle`) to establish an industrial diagnostic tone.
3. **Responsive Cards (< 640 DIP)**: On narrow windows under 640 DIP, tabular rows collapse into compact stacked records with monospaced telemetry headers.

---

## 🚫 Strict Exclusions (Anti-AI Slop)

- **No Hero Gradients**: Surfaces remain clean, tonal, and readable.
- **No Decorative Glass Orbs**: Backdrop materials rely strictly on Windows native Mica.
- **No Unverifiable State**: Every mutating action requires explicit review approval (`ReviewApproved = true`).
- **No Emoji Icons**: All visual indicators use official Windows Fluent icons (`Segoe Fluent Icons` / SVG).

---

## 🔒 Accessibility & Keyboard Navigation

- **Global Accelerator**: Pressing `Ctrl+K` focuses the search palette in `AllToolsPage.xaml` from any destination.
- **Focus Rings**: Electric cyan (`#00D2FF`) focus rings highlight active keyboard controls.
- **Automation Properties**: Every control defines explicit `AutomationProperties.Name` and stable `AutomationId` values.
- **High Contrast Support**: Dedicated High Contrast resource dictionaries satisfy WCAG 2.1 AA `4.5:1` contrast ratios.
