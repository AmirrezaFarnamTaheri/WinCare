# WinCare Enterprise Design System (Utilitarian Specification)

## Overview

The WinCare Enterprise Design System is a high-density, utilitarian visual and interaction architecture designed for system-level diagnosis, optimization, and administration. It prioritizes operational clarity, deterministic state presentation, and instant responsiveness over visual decoration.

---

## 1. OKLCH Color System

WinCare uses modern OKLCH color spaces for perceptual uniformity, high-contrast readability, and accurate dark mode rendering.

### Core Dark Mode Palette

| Token Key | OKLCH Value | CSS Variable | Fallback (sRGB Hex) | Usage / Intent |
| :--- | :--- | :--- | :--- | :--- |
| **Base Canvas** | `oklch(0.12 0.02 240)` | `--color-bg-base` | `#0b101d` | Application background & outer shell |
| **Card Background** | `oklch(0.16 0.025 240)` | `--color-bg-card` | `#121828` | Container panels, data cards, inspect surfaces |
| **Primary Accent** | `oklch(0.68 0.18 210)` | `--color-accent-primary` | `#00a2f3` | Key action buttons, selected navigation, interactive highlights |
| **Warning Accent** | `oklch(0.75 0.16 75)` | `--color-accent-warning` | `#e59a00` | Cautionary states, non-critical telemetry alerts, pending tasks |
| **Alert Accent** | `oklch(0.65 0.22 25)` | `--color-accent-alert` | `#ff4d4d` | Destructive actions, system errors, critical risk badges |

### Extended Surface & Text Tokens

| Token Key | OKLCH Value | CSS Variable | Usage |
| :--- | :--- | :--- | :--- |
| **Surface Raised** | `oklch(0.20 0.03 240)` | `--color-bg-raised` | Interactive controls, inputs, hovering surfaces |
| **Border Subdued** | `oklch(0.28 0.03 240)` | `--color-border-subdued` | Container boundaries, grid table dividers (1px sharp) |
| **Border Active** | `oklch(0.45 0.08 210)` | `--color-border-active` | Focus rings, active tab indicators, selected cards |
| **Text Primary** | `oklch(0.96 0.01 240)` | `--color-text-primary` | High-emphasis headers, telemetry values, active code |
| **Text Secondary** | `oklch(0.72 0.02 240)` | `--color-text-secondary` | Labels, table headers, metadata descriptions |
| **Text Muted** | `oklch(0.52 0.02 240)` | `--color-text-muted` | Disabled elements, timestamp footers, hints |

### CSS Variables Implementation

```css
:root {
  /* OKLCH Base Palette */
  --color-bg-base: oklch(0.12 0.02 240);
  --color-bg-card: oklch(0.16 0.025 240);
  --color-bg-raised: oklch(0.20 0.03 240);
  
  --color-accent-primary: oklch(0.68 0.18 210);
  --color-accent-warning: oklch(0.75 0.16 75);
  --color-accent-alert: oklch(0.65 0.22 25);

  --color-border-subdued: oklch(0.28 0.03 240);
  --color-border-active: oklch(0.45 0.08 210);

  --color-text-primary: oklch(0.96 0.01 240);
  --color-text-secondary: oklch(0.72 0.02 240);
  --color-text-muted: oklch(0.52 0.02 240);
}
```

---

## 2. Typography Scale

The typography system strictly uses system-native monospace and clean sans-serif typefaces to guarantee zero webfont load delay and crisp subpixel rendering.

### Font Stacks

- **Primary Sans**: `Inter, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif`
- **System Monospace**: `"JetBrains Mono", "Cascadia Code", Consolas, "Courier New", monospace`

### Scale Matrix

| Role | Font Size | Line Height | Weight | Font Stack | Target Usage |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Display / Title** | 22px (`1.375rem`) | 28px (`1.27`) | 600 (SemiBold) | Sans | Top bar titles, main view headers |
| **Section Header** | 16px (`1.000rem`) | 22px (`1.37`) | 600 (SemiBold) | Sans | Card section titles, group headers |
| **Body / Label** | 13px (`0.8125rem`)| 18px (`1.38`) | 400 (Regular)  | Sans | Primary form labels, descriptions |
| **Dense Data Table**| 12px (`0.750rem`) | 16px (`1.33`) | 400 / 500      | Sans | Enterprise telemetry tables, logs |
| **Code / Terminal** | 12px (`0.750rem`) | 16px (`1.33`) | 400 (Regular)  | Monospace | Command outputs, paths, JSON args |
| **Caption / Meta**  | 11px (`0.6875rem`)| 14px (`1.27`) | 500 (Medium)   | Monospace | Timestamps, status pills, IDs |

---

## 3. Grid & Spacing Scale (Compact Enterprise Density)

WinCare enforces a compact 4px baseline grid tailored for high-density data display.

### Spacing Tokens

| Scale | Token | Value | Recommended Usage |
| :--- | :--- | :--- | :--- |
| **XS** | `--space-xs` | **4px** | Internal padding between icon and text, micro gap in pills |
| **SM** | `--space-sm` | **8px** | Dense table cell padding, input internal padding, button gaps |
| **MD** | `--space-md` | **12px**| Card internal padding, control stack spacing |
| **LG** | `--space-lg` | **16px**| Grid row gaps, container padding |
| **XL** | `--space-xl` | **24px**| Main section margins, layout column gaps |

```css
:root {
  --space-xs: 4px;
  --space-sm: 8px;
  --space-md: 12px;
  --space-lg: 16px;
  --space-xl: 24px;

  --radius-sm: 4px;
  --radius-md: 6px;
  --radius-lg: 8px;
}
```

---

## 4. WebGL & Animation Budgets

Interactive WebGL backgrounds and micro-animations must adhere to strict performance constraints to avoid consuming host CPU/GPU resources intended for system management tasks.

### Performance Constraints

| Metric / Library | Hard Upper Limit | Enforcement Mechanism |
| :--- | :--- | :--- |
| **Target Frame Rate** | **60 fps max** | Clamp rendering with `requestAnimationFrame` delta lock |
| **Startup Latency** | **< 50 ms budget** | Lazy-initialize WebGL canvas post-DOM Mount |
| **Vanta.js Limit** | **Max 500 vertices / particles** | Override default mesh complexity; disable on low-power mode |
| **Cobe.js Limit** | **Max 100 markers** | Limit geo-point render count; disable smooth auto-rotate if CPU > 15% |
| **Matter.js Limit**| **Max 50 active bodies** | Set aggressive sleeping (`enableSleeping: true`); freeze on idle |

### Battery & Reduced Motion Policy

```javascript
// Hardware / Preference Adaptive Guardrail Example
const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
const isLowPower = navigator.hardwareConcurrency && navigator.hardwareConcurrency <= 4;

if (prefersReducedMotion || isLowPower) {
  // Disable WebGL animations completely; fallback to static OKLCH canvas background
  disableWebGLCanvas();
}
```

---

## 5. Anti-Slop Guidelines (Strict Zero AI Slop Rule)

To prevent visual noise and unmaintainable bloat, all UI components must comply with the WinCare Anti-Slop Standard:

1. **No Useless Gradients or Blurred Background Blobs**:
   - *Prohibited*: Decorative purple/pink ambient blurred background circles (`filter: blur(100px)`) that convey zero operational state.
   - *Allowed*: Solid OKLCH dark canvas with crisp 1px semantic borders.

2. **No Fake Metrics or Decorative Sparklines**:
   - *Prohibited*: Decorative random charts, fake progress bars, or placeholder counters that are not connected to real backend telemetry.
   - *Allowed*: Real-time system metrics directly backed by OS API calls.

3. **No Generic Drop Shadows Without Data Purpose**:
   - *Prohibited*: Heavy, fuzzy card drop-shadows (`box-shadow: 0 20px 25px rgba(0,0,0,0.5)`).
   - *Allowed*: 1px solid semantic borders (`border: 1px solid var(--color-border-subdued)`). Elevation must be communicated via OKLCH surface lightness steps, not blur shadows.

4. **Information Density Over Whitespace Waste**:
   - Avoid oversized padding that requires vertical scrolling to view system state. Every pixel must serve data discovery or execution.
