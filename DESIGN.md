# WinCare — Task-first Fluent Workspace

The September 2026 product finalization moves WinCare from an instrument-panel metaphor to a calmer task-first native WinUI 3 workspace. Earlier concepts remain reference studies only.

## Design intent

Give people a clear starting point, make the next useful action obvious, and keep advanced capability available without forcing everyone through a 269-command catalog. The product sequence is **Check → Understand → Act → Review**.

Studies in `design/redesign-2026-09/precision-character/` are concept art, not runtime evidence. The implementation combines their strongest ideas rather than duplicating one screenshot.

## Canonical owners

- `src/WinCare.App/Styles/ThemeResources.xaml` owns semantic brushes in Light, Dark and HighContrast.
- `src/WinCare.App/Styles/ControlStyles.xaml` owns typography, radii and shared controls.
- `ThemeResourceBrushConverter` resolves view-model keys against the window theme and updates stable brushes for cached pages on appearance changes.
- `UX-CONTRACT.md` records navigation, feedback and action behavior.

## Palette

| Role / resource | Light | Dark |
|---|---|---|
| PageBackgroundBrush | #F2F4F7 | #1C2733 |
| SurfaceBrush / CardSurfaceBrush | #FFFFFF | #26313D |
| SurfaceSecondaryBrush | #EDF1F5 | #2E3B49 |
| SurfaceHoverBrush | #E1E8EE | #374656 |
| HeroBackgroundBrush | #DFEAF2 | #202B37 |
| NavigationRailBrush / TitleBarBackgroundBrush | #EAEEF2 | #202B37 |
| CardBorderBrush / BorderSubtleBrush | #D5DDE4 | #46586A |
| TextPrimaryBrush | #14202B | #EAF0F5 |
| TextSecondaryBrush | #3D5568 | #AFC0CD |
| AccentChromeBrush / AccentBrush | #005FB8 | #60CDFF |
| AccentChromeSubtleBrush | #D6EAF7 | #1E3B4F |
| TextOnAccentBrush | #FFFFFF | #0A1E2C |

Status colors retain semantic meaning. Brand color is not a health result. Primary-action styles use WinUI's native `AccentFillColorDefaultBrush`/`TextOnAccentFillColorPrimaryBrush`, so buttons and links follow the user's system accent; the static `AccentBrush`/`AccentChromeBrush` values above carry the Windows default accent for view-model-bound brushes. High contrast uses Windows system colors; status pills there carry meaning through text and borders, never fill hue alone. Native caption controls follow appearance too.

## Typography and geometry

- Segoe UI Variable Display for headings, Segoe UI Variable Text for controls and prose. Native Windows legibility is intentional.
- Cascadia Code / Cascadia Mono / Consolas only for measurements, identifiers and technical evidence.
- Page title 34 DIP; section 20; row title 15; body 14 with 21-DIP line height; secondary prose 13 with 18-DIP line height. No decorative labels above page headings.
- Four-DIP rhythm, 32-DIP desktop inset, 20-DIP compact inset. Related controls use 8–12 DIP gaps; sections use 24–28.
- Outer panels 12-DIP corners, inner panels and controls 8, status labels 4. Use one enclosure per functional group.
- Primary actions use the Windows system accent via native WinUI accent brushes. Secondary actions use QuietButtonStyle. Diagnostic channels share a native button style with visible focus.
- Preserve native hover, pressed, disabled and busy states. No perpetual animation or delayed feedback for effect.

## Composition

Home is recommendation-led: one checkup action, three common care entry points, a compact evidence summary, recent activity, and secondary links to Power tools, Extensions, and Troubleshoot. Decorative hardware atlases and telemetry HUDs are not part of the runtime Home hierarchy.

Checkup is explicitly read-only. Its selector appears before the findings list, and findings deep-link to the relevant care surface instead of performing maintenance in place. The summary is a checked-area status, not a synthetic health score.

System care, Security, and Repair & recovery share `CareToolList` and project commands through exact catalog Area/Section values. Descriptions sit under task names; impact and requirements are supporting metadata. Portable playbooks remain a dedicated Repair & recovery section.

Power tools is the advanced catalog surface. Search, Area, Section, impact and read-only filters lead; Categories browses the real taxonomy; Favorites and Recent support repetition; Care plans remains separate. Normal rows omit command IDs and migration state. Those details stay behind **Advanced details** in the inspector.

The wide shell uses a native left navigation rail grouped around Home, Checkup, Care, Power tools, and Activity. Extensions, Troubleshoot, Settings, and Help are secondary/footer routes. About is reachable from Help and global search without permanent rail space. Below 920 DIP the rail compacts, then becomes minimal below 680 DIP.

Global search is product-wide: pages, native tools, extensions, and help topics participate. Unknown free text falls back to Power tools search. Data-heavy pages preserve stacked compact records; the Power tools inspector overlays below 1320 DIP. Avoid fixed-height prose and nested decorative enclosures.

`LayoutVisibility.CompactBreakpointDip = 920.0` remains the shared page-level compact boundary unless a component has a narrower measured breakpoint. Home's hero/evidence composition stacks below 820 DIP so the summary never competes with the primary action for horizontal space.

## Product truth and accessibility

Activity uses a theme-aware document illustration and a separate empty composition; an empty message never overlays column headings. The tool inspector retains invalid raw input and focuses it for correction. Cancellation uses a pending “Stopping…” state until execution settles.

- No generic Undo claim: show Undo only with an executable compensator.
- No unverifiable health score: collection coverage is not machine health. Findings retain their actual runtime interpretation.
- No blanket approval claim: risk determines direct execution, confirmation or destructive preview/receipt requirements. The dispatcher remains authoritative.
- No false publisher verification, invented plugin availability, decorative settings or telemetry.
- Meaningful automation names, keyboard access and visible focus; target 44-DIP controls.
- Use body typography for ordinary state, time, labels and prose; reserve monospace for identifiers, paths, raw parameters and technical evidence.
- Validate text, accent and status contrast in both themes. High contrast uses system brushes.

## Verification

Build/source checks do not establish runtime accessibility. Follow `docs/Windows-Validation.md` for the maintained Windows interaction, theme, scaling, keyboard, Narrator, packaging, and release-validation procedure. Historical captures remain labeled by build, and generated art never counts as execution evidence.
