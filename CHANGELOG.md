# Changelog & Release Notes

All notable changes to WinCare are documented in this file in accordance with [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) principles.

This history restarts at 3.0.0 with the Fluent redesign re-release. The safety model is unchanged: risk-tiered admission (Safe/Moderate/Destructive/Critical), dispatcher-issued single-use approval capabilities, fail-closed command dispatch, the frozen 269-command catalog, and plugin trust/signing all behave exactly as before; 3.0.0 marks the design-system and code-hygiene reset, not a change in what the app is willing to touch.

---

## [Unreleased]

## [3.0.0]

### Design system

- Re-based the theme system on a Fluent-aligned palette for both Light and Dark, with surface, text, border, status, telemetry, and hero colors chosen against measured WCAG AA contrast (all 8 pill and 14 text foreground/background pairings measure ≥ 4.5:1 across Light and Dark, enforced by the `verify_pill_contrast` and `verify_palette_contrast` source gates).
- Brand teal left the chrome: accent-colored surfaces now track the Windows system accent (`AccentFillColorDefaultBrush` / `TextOnAccentFillColorPrimaryBrush`), so WinCare's buttons and selection follow the user's Windows setting like a first-party app.
- Status pills keep literal, gate-measured colors and gained a corrected composition (text/background pairs pinned to the runtime consumer mapping); high-contrast mode no longer collapses distinct statuses onto fill hue — state is carried by text and borders with system ink colors.
- Added spacing tokens (`SpacingXS`–`XXL`, `GapXS`–`XL`) for use by future passes.
- Landed the interface design pass across runtime chrome: Checkup consolidated to a single results section whose rows update in place, and Home, Help, AI Doctor, and All Tools layouts simplified without losing a live control.

### Accessibility

- Fixed the high-contrast status-color collapse described above; HC users now distinguish pill states without relying on hue.

### Architecture

- Extracted global-search ranking from `MainWindow` into `WinCare.Application.Navigation.GlobalSearchService` — same ranking behavior, now unit-tested (11 tests) and free of window code; route keys resolve through `NavigationCatalog` at construction so a catalog rename fails at startup instead of emitting dead suggestions.

### Naming

- Renamed the checkup summary chrome `HealthScore*` → `CheckupStatus*`; it reports qualitative states ("Action needed", "Looks good", …) and never a numeric score.

### Release engineering

- Navigation route contract (catalog ⇄ PageService ⇄ ShellPage tags) is now enforced by `verify_native_foundation.py` in both directions, with the hidden `about` route as the only exception.
- Nav/page chrome labels are pinned identical across `NavigationCatalog`, XAML attributes, and `Resources.resw` by a new source gate.
- Pill contrast verification corrected to measure the actual runtime foreground/background composition.
- Added a read-only `--capture-screens` mode that renders each documented route to PNG from a built portable executable. Its provenance manifest (`docs/images/runtime-captures.json`) records route, architecture, version, commit, and appearance, and `docs/Screenshots.md` is generated from that manifest so the document cannot drift from its images; the CI smoke job re-runs the capture against the signed artifact.
- Care-area taxonomy is now pinned in both directions by a source gate: each care view model claims exactly the catalog's sections (no orphans, no phantoms), and each page's tab titles equal its `NavigationCatalog` section list. The Checkup route's stale `Quick check`/`Results` sections were aligned to the single section the page shows.
