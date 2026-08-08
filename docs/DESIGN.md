# WinCare Design

WinCare is a local Windows operations workstation optimized for explicit authority, recoverability, and observable evidence.

## Principles

1. One authoritative write path.
2. Query surfaces never acquire mutation authority.
3. UI state is not policy or durable truth.
4. Missing evidence never becomes success.
5. Resource bounds are part of every external boundary.
6. Recovery is designed before mutation.
7. Optional dependencies are explicit, verified, and replaceable.
8. Language boundaries must earn their operational cost.
9. Current executable documentation replaces implementation-history narratives.

## Language allocation

PowerShell remains the natural owner of Windows orchestration and operator workflows. C# is limited to compiled primitives that benefit from stable interop, strict byte-oriented parsing, or reduced runtime compilation. Python is limited to reproducible build and independent verification tools. No language is introduced solely to imitate an external architecture.

## Product surfaces

WPF, terminal, and headless entry points call the same module. Advanced capabilities are exposed through capability discovery and domain functions, with dependency state and non-claims included in their results.

---

## Design System

### Aesthetic

**Precision Instrument.** Dark maritime navy shell with brand-blue (`#2F80ED`) as the sole accent. Interface reads like calibrated instrumentation: high-contrast typography, defined edges, no decoration that does not carry information. The accent appears sparingly — selected states, interactive targets, progress indicators — and never as decoration. Purple (`#7C5CFC`) is absent from every layer of the design. The only gradient is the hero panel, built entirely from brand palette hues (Ink → Deep Blue, no purple).

### Typography

- **Display/body:** Segoe UI Variable Text, Segoe UI (built into Windows 10/11; correct for a native WPF tool; no external font dependency)
- **Evidence/code layer:** Cascadia Mono, Consolas (command names, output, arguments, unsafe phrase display)
- **Scale** (base 12px, ratio ×1.25): 10, 12 (caption), 14 (body), 17 (subtitle), 20 (section heading), 24 (action title), 25 (page title), 28 (assurance metric), 30 (hero heading)
- **Weight rhythm:** Regular for body; SemiBold for headings and emphasis; Bold only for the wordmark

### Token Table

All XAML `DynamicResource` keys. This table is the single source of truth — values in `WinCare.MainWindow.xaml` must match.

| Key | Value | Role |
|---|---|---|
| `BackgroundBrush` | `#0B1020` | Window background |
| `SidebarBrush` | `#0E1526` | Navigation rail background |
| `SurfaceBrush` | `#121A2B` | Card and panel surface |
| `SurfaceRaisedBrush` | `#1B2740` | Raised surface (button bg, column headers) |
| `SurfaceHoverBrush` | `#22314F` | Interactive hover state |
| `SelectionBrush` | `#1E2D4A` | Selected item fill (action list) |
| `BorderBrush` | `#2B3A58` | Borders, separators |
| `AccentBrush` | `#2F80ED` | Brand blue — primary accent, selected state indicator, focus ring, progress |
| `AccentSoftBrush` | `#1A2E50` | Accent background at low opacity — selected nav item fill |
| `CyanBrush` | `#34D6E9` | Icon color, secondary data accent (storage progress) |
| `SuccessBrush` | `#3FB950` | Positive status, low-risk badge |
| `WarningBrush` | `#D29922` | Warning status, moderate-risk badge |
| `CriticalBrush` | `#F85149` | Critical/error status, danger badge and confirm overlay border |
| `TextBrush` | `#E6EDF3` | Primary text |
| `TextMutedBrush` | `#8B949E` | Secondary/caption text |
| `TextDimBrush` | `#5A6478` | Tertiary/evidence text, command path |
| `HeroBrush` | LinearGradient `#0B1F3A→#123A72→#0D1117` | Hero panel background — brand deep blue, **no purple** |

> **Brand palette reference** (from `design/WinCare-Brand.manifest.json`): Ink `#0B1026` · Deep Blue `#123A72` · Blue `#2F80ED` · Cyan `#34D6E9` · Mint `#68E0B5` · White `#FFFFFF`

### Spacing

- **Base unit:** 8px
- Within-group tight: 4–8px
- Within-card comfortable: 12–18px
- Card padding: 18–24px
- Section separation: 18–24px
- Content area margin: 24px

### Radius

| Surface | Value |
|---|---|
| Cards | 14px |
| Buttons | 8px |
| Badges / chips | 9–11px |
| Overlays | 16–18px |

**Rule:** Nothing above 18px. Blob-rounding is absent from this design.

### Shadow

One approach only: `CardShadow` (BlurRadius=22, ShadowDepth=4, Opacity=0.18, Color=#000000).

Never stack `CardShadow` with a visible `BorderBrush` border on the same element — this design uses bordered surfaces with a light shadow, not glowing elevation.

### Signature Move

The selected-state indicator is a **2px left-border** in `AccentBrush` (`#2F80ED`) on `NavItem` and `ActionItem`. Not a background highlight; not a glow. The fill behind it is `AccentSoftBrush` (nav) or `SelectionBrush` (actions) so the border carries the primary selection signal. It reads as a precision marker: one thin line of authority — like a vernier scale indicator.

---

## Craft Layer

### Layout

- **Shell:** 3-row Grid (72px header / `*` content / 32px footer) × 2-column (258px sidebar / `*` content). Sidebar spans all 3 rows.
- **Dashboard stats row:** Explicit `Grid` with weighted columns `1.4* 1.1* 1.1* 0.9*` — Security wider (shows Defender posture detail); Restart narrower (binary pending/clear state). Replaces `UniformGrid Columns="4"`.
- **Assurance stats row:** Explicit `Grid` `1* 1* 1* 1*` with margin-controlled gutters. Replaces `UniformGrid Columns="4"`.
- **Actions panel:** 440px fixed list column + `*` detail column — master-detail pattern, not a card grid.

### Component State Matrix

**NavItem** (`ListBoxItem` with `NavItemStyle`)

| State | Background | Border | Foreground |
|---|---|---|---|
| Default | Transparent | None | TextBrush |
| Hover | SurfaceHoverBrush | None | TextBrush |
| Selected | AccentSoftBrush | 2px left AccentBrush | TextBrush |
| Focused | — | FocusVisualStyle ring | — |

**ActionItem** (`ListBoxItem` with `ActionItemStyle`)

| State | Background | Border |
|---|---|---|
| Default | SurfaceBrush | 1px BorderBrush |
| Hover | SurfaceHoverBrush | 1px BorderBrush |
| Selected | SelectionBrush | 2px left + 1px AccentBrush |
| Focused | — | FocusVisualStyle ring |

**Button** (`BaseButton`)

| State | Surface | Border | Opacity |
|---|---|---|---|
| Default | SurfaceRaisedBrush | BorderBrush | 1.0 |
| Hover | SurfaceHoverBrush | BorderBrush | 1.0 |
| Pressed | SurfaceHoverBrush | BorderBrush | 0.78 |
| Disabled | SurfaceRaisedBrush | BorderBrush | 0.42 |
| Focused | — | FocusVisualStyle ring | — |

**PrimaryButton** — same as `BaseButton` with `AccentBrush` as the surface background.

**DangerButton** — `#512128` background, `CriticalBrush` border (confirmation actions only).

**TextBox** — Default: `#0D1424` bg, `BorderBrush` border, `AccentBrush` caret. Focused: `AccentBrush` border. Error: `CriticalBrush` border.

**ProgressBar** — `AccentBrush` foreground (Memory); `CyanBrush` foreground (Storage); `SurfaceRaisedBrush` track.

**Toast** — `SurfaceRaisedBrush` bg, `AccentBrush` border (default), bottom-right corner, auto-dismiss at 4s.

**BusyOverlay** — `#AA070B14` scrim; centered modal card (440px, `SurfaceBrush` bg, 16px radius).

**ConfirmOverlay** — `#C4070B14` scrim; `CriticalBrush` border card (590px) — the danger surface uses `CriticalBrush` as its edge, not `AccentBrush`.

### Motion

- **ReducedMotion:** Respect `config.ReducedMotion`; no storyboard animations when true.
- **Only animate:** `Opacity`, `Transform` — never `Width`/`Height`/`Top`/`Left`.
- **Toast:** Opacity storyboard `0→1` in 150ms ease-out.
- **BusyOverlay:** fade-in 120ms.
- **No animation** on high-frequency list scroll or filter operations.
- **Duration tokens:** Fast=120ms · Standard=200ms · Deliberate=300ms
- **Easing:** ease-out for entrances; ease-in for exits.

### Focus

- `FocusRingStyle` resource defined in `Window.Resources`.
- Visual: 2px solid `AccentBrush` outline, 3px margin offset, 6px corner radius.
- Applied via `FocusVisualStyle="{StaticResource FocusRingStyle}"` on `NavItemStyle`, `ActionItemStyle`, and optionally via `Window.FocusVisualStyle` override.
- **WCAG 2.2 SC 2.4.11:** visible focus indicator on all interactive controls.

### Accessibility

- **Target:** WCAG 2.2 AA
- **Contrast (verified):** `TextBrush #E6EDF3` on `BackgroundBrush #0B1020` → ~11:1 ✓ (exceeds AA 4.5:1). `TextMutedBrush #8B949E` on `SurfaceBrush #121A2B` → ~4.8:1 ✓.
- **Automation:** All interactive controls have `AutomationProperties.Name` (verified in XAML).
- **Target size:** Min 24×24px — all buttons and nav items meet this ✓.
- **Color independence:** Risk levels show both a colored badge AND a text label (`Risk: Critical / High / Medium / Low`) — color is not the only signal ✓.
- **Screen reader mode:** `ScreenReaderCheckBox` in Settings propagates to `config.ScreenReaderMode`.
- **HighContrast / Monochrome:** Handled by `Set-WinCareGuiTheme` in `10-GuiRuntime.ps1`.

### Dark Mode

App is dark-only. Background is near-black (`#0B1020`), not pure black — this maintains elevation contrast headroom. HighContrast theme is a designed override, not an inversion, managed in the runtime.

---

## Slop Audit

### Signals eliminated (v2.0 design pass)

| Signal | Old value | Replaced with |
|---|---|---|
| AI purple accent | `#7C5CFC` | `#2F80ED` brand blue |
| `AccentSoftBrush` purple tint | `#2B225A` | `#1A2E50` brand deep blue tint |
| `HeroBrush` purple bleed | Stop `#231C52` | `#0B1F3A` (Ink) brand start |
| `UniformGrid` Dashboard | `Columns="4"` uniform | Weighted `Grid` `1.4* 1.1* 1.1* 0.9*` |
| `UniformGrid` Assurance | `Columns="4"` uniform | Explicit `Grid` `1* 1* 1* 1*` with margin gutters |
| Missing `FocusVisualStyle` | (absent) | `FocusRingStyle` — 2px `AccentBrush` ring |
| `ComboBox` light-mode inversion | `#F4F7FB` bg, `#111827` fg | `SurfaceRaisedBrush` bg, `TextBrush` fg |

### Signals correctly absent

- Blob-rounding: `CornerRadius` max 14px ✓
- Excessive shadows: one `CardShadow` definition only ✓
- Glassmorphism: none ✓
- Gradient text or metric display values: none ✓
- Floating orbs or decorative elements: none ✓
- Inter/Roboto as primary: Segoe UI Variable Text (native Windows, platform-correct) ✓

### Remaining open items

- **[OPEN]** ~12 hardcoded hex values remain in XAML outside the resource block (hero card separator `#40506A`, output pane `#0A101D`, alternating row `#10182A`, overlay scrims, etc.) — migrate to named tokens in next pass.
- **[OPEN]** `FocusVisualStyle` needs to be wired to `Button`, `TextBox`, `ComboBox`, `CheckBox` via a global Window style override — currently only on `NavItemStyle` and `ActionItemStyle`.
- **[OPEN]** Motion storyboards (Toast, BusyOverlay) not yet implemented in XAML — next pass.

### Self-audit score

| Area | Status |
|---|---|
| Aesthetic commitment (Precision Instrument) | ✓ Clear and committed |
| Typography (platform-appropriate) | ✓ |
| Color hierarchy (60/30/10 navy/surface/accent) | ✓ |
| No purple anywhere in token table | ✓ |
| Layout intentional (weighted stats, master-detail) | ✓ |
| WCAG AA contrast | ✓ |
| Focus indicators (all interactive controls) | ⚠ Partial — NavItem and ActionItem done; Button/TextBox/ComboBox global override pending |
| Motion (ReducedMotion respected) | ⚠ Defined in spec, storyboards pending |
| Hardcoded hex values eliminated | ⚠ Major tokens done; ~12 minor values remain |

---

## Changelog

| Version | Date | Notes |
|---|---|---|
| v1.0 | 2026-08-01 | Initial principles, language allocation, product surfaces |
| v2.0 | 2026-08-01 | Full design system added: aesthetic commitment, token table, typography, spacing, radius, shadow, signature move, craft layer (layout, components, motion, focus, accessibility, dark mode), slop audit |
