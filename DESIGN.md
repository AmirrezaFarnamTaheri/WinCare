# WinCare — Precision Workspace

The user selected a hybrid of Instrument Bench (A), System Atlas (C), Focus (D), and Care Board (E) in September 2026. This replaces the former Cyber-Operate visual specification. The app remains native WinUI 3.

## Design intent

Give people a clear starting point, an understandable view of their PC, and a reliable place to maintain it. Character comes from the custom exploded-system illustration, precise diagnostic channels, generous action hierarchy, and the sequence **Understand → Maintain → Review**.

Studies in `design/redesign-2026-09/precision-character/` are concept art, not runtime evidence. The implementation combines their strongest ideas rather than duplicating one screenshot.

## Canonical owners

- `src/WinCare.App/Styles/ThemeResources.xaml` owns semantic brushes in Light, Dark and HighContrast.
- `src/WinCare.App/Styles/ControlStyles.xaml` owns typography, radii and shared controls.
- `ThemeResourceBrushConverter` resolves view-model keys against the window theme and updates stable brushes for cached pages on appearance changes.
- `UX-CONTRACT.md` records navigation, feedback and action behavior.

## Palette

| Role / resource | Light | Dark |
|---|---|---|
| PageBackgroundBrush | #EDF2F5 | #101B24 |
| SurfaceBrush / CardSurfaceBrush | #FFFFFF | #182A35 |
| SurfaceSecondaryBrush | #F2F6F8 | #203542 |
| SurfaceHoverBrush | #E0EBF0 | #2B4655 |
| HeroBackgroundBrush | #E5EFF4 | #14232D |
| NavigationRailBrush | #E4ECF1 | #14232D |
| CardBorderBrush / BorderSubtleBrush | #CBD8E0 | #36505E |
| TextPrimaryBrush | #172D3B | #EEF5F7 |
| TextSecondaryBrush | #435C6B | #B0C4CE |
| AccentTealBrush / AccentBrush | #006B80 | #70D6DF |
| AccentTealSubtleBrush | #DCEFF2 | #203F4A |
| TextOnAccentBrush | #FFFFFF | #06151C |

Status colors retain semantic meaning. Brand color is not a health result. High contrast uses Windows system colors; native caption controls follow appearance too.

## Typography and geometry

- Segoe UI Variable Display for headings, Segoe UI Variable Text for controls and prose. Native Windows legibility is intentional.
- Cascadia Code / Cascadia Mono / Consolas only for measurements, identifiers and technical evidence.
- Page title 34 DIP; section 20; row title 15; body 14 with 21-DIP line height; secondary prose 13 with 18-DIP line height. No decorative labels above page headings.
- Four-DIP rhythm, 32-DIP desktop inset, 20-DIP compact inset. Related controls use 8–12 DIP gaps; sections use 24–28.
- Outer panels 12-DIP corners, inner panels and controls 8, status labels 4. Use one enclosure per functional group.
- Primary actions use cyan. Secondary actions use QuietButtonStyle. Diagnostic channels share a native button style with visible focus.
- Preserve native hover, pressed, disabled and busy states. No perpetual animation or delayed feedback for effect.

## Composition

Home begins with its checkup action. The atlas and live category buttons share a diagnostic workspace. Maintenance follows with catalog-derived risk badges, busy feedback and expandable evidence. Activity and review guidance close the page. The atlas is an illustration, never a detected hardware inventory.

Checkup uses a rectangular findings readout rather than a health gauge. All Tools puts search and filters first, retaining typed parameters, its inspector and execution feedback. Settings and documentation favor flowing sections.

Wide navigation uses an open left instrument rail. Below 920 DIP it becomes compact; below 680 DIP it becomes an overlay menu. All routes remain available, grouped as overview, care areas, and workspace tools.

Home preserves its atlas-and-evidence split down to 780 DIP because its channels use short labels. Data-heavy pages continue to use the shared 920 DIP compact boundary.

All Tools uses its own measured thresholds: the data table compacts below 840 DIP, while the 390-DIP inspector overlays below 1320 DIP so selecting a command never crushes the table.

System care, Security and Repair share `CareToolList`: descriptions sit under tool names, catalog status and requirements form supporting columns, and records stack below 760 DIP of available list width. Selecting a record opens its exact command in the inspector; these lists do not claim to be live machine assessments.

`LayoutVisibility.CompactBreakpointDip = 920.0` remains the page-level boundary. Home stacks the illustration/evidence and maintenance sections; below 600 DIP channels form a single column. Data pages preserve stacked records and All Tools retains its overlay inspector. Avoid fixed-height prose.

## Product truth and accessibility

Activity uses a theme-aware document illustration and a separate empty composition; an empty message never overlays column headings. The tool inspector retains invalid raw input and focuses it for correction. Cancellation uses a pending “Stopping…” state until execution settles.

- No generic Undo claim: show Undo only with an executable compensator.
- No unverifiable health score: collection coverage is not machine health. Findings retain their actual runtime interpretation.
- No blanket approval claim: risk determines direct execution, confirmation or destructive preview/receipt requirements. The dispatcher remains authoritative.
- No false publisher verification, invented plugin availability, decorative settings or telemetry.
- Meaningful automation names, keyboard access and visible focus; target 44-DIP controls.
- Validate text, accent and status contrast in both themes. High contrast uses system brushes.
- The decorative atlas is excluded from the accessibility tree; adjacent text explains its role.

## Verification

Build/source checks do not establish runtime accessibility. Record captures and exercised interactions in `docs/design/hybrid-validation.md`. Historical captures remain labeled by build. Generated art never counts as execution evidence.
