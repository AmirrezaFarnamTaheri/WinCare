# WinCare: Task-first Fluent Workspace

WinCare is a task-first native WinUI 3 workspace played straight against Fluent: calm surfaces, system-tracked accent, semantics carried by text and structure rather than hue. The 3.0.0 re-release re-based the palette onto this system; the task-first product model itself predates it.

## Design intent

Give people a clear starting point, make the next useful action obvious, and keep advanced capability available without forcing everyone through a 296-command catalog. The product sequence is **Check → Understand → Act → Review**.

The 2026-09 redesign concept studies were retired with the 3.0.0 re-release; this file is the visual system of record. Runtime truth lives in the code and in `docs/Screenshots.md` captures, never in concept art.

## Canonical owners

- `src/WinCare.App/Styles/ThemeResources.xaml` owns semantic brushes in Light, Dark and HighContrast.
- `src/WinCare.App/Styles/ControlStyles.xaml` owns typography, radii and shared controls.
- `ThemeResourceBrushConverter` resolves view-model keys against the window theme and updates stable brushes for cached pages on appearance changes.
- `UX-CONTRACT.md` records navigation, feedback and action behavior.

## Palette

| Role / resource | Light | Dark |
|---|---|---|
| PageBackgroundBrush | #F7F7F2 | #161F1B |
| SurfaceBrush | #FFFFFF | #202B26 |
| CardSurfaceBrush | #FFFFFF | #202B26 |
| SurfaceSecondaryBrush | #EDF1EB | #293A32 |
| SurfaceHoverBrush | #E1E7DF | #374A3E |
| HeroBackgroundBrush | #E5EFE6 | #263F32 |
| NavigationRailBrush | #EFF1EC | #1B2A23 |
| TitleBarBackgroundBrush | #ECEFE8 | #18241F |
| CardBorderBrush | #D3DBD0 | #455B4F |
| BorderSubtleBrush | #CCD5C9 | #455B4F |
| TextPrimaryBrush | #1D2925 | #F2F5EC |
| TextSecondaryBrush | #425950 | #C0CEBE |
| AccentChromeBrush | #0E6254 | #7CDFB5 |
| AccentChromeSubtleBrush | #DCEEE7 | #253F35 |
| TextOnAccentBrush | #FFFFFF | #0C2319 |
| SuccessBrush | #176342 | #8BE0A6 |
| WarningBrush | #78500F | #FFD08C |
| DangerBrush | #A92B35 | #FFB0A7 |

Status colors retain semantic meaning. Brand color is not a health result. Primary-action styles use WinUI's native `AccentFillColorDefaultBrush`/`TextOnAccentFillColorPrimaryBrush`, so buttons and links follow the user's system accent; the static `AccentBrush`/`AccentChromeBrush` values above carry the Windows default accent for view-model-bound brushes. Status-pill pairs (`PillReadOnlyBg`/`PillMutatingBg`/`PillElevatedBg`/`PillNotReadyBg` with `PillText`/`PillAltText`) stay literal hex so the contrast gates can measure them; runtime composition is pinned in `verify_pill_contrast.py`. High contrast uses Windows system colors: the not-ready pill drops to the window ground and HC pills carry meaning through text and borders, never fill hue alone. Native caption controls follow appearance too.

## Typography and geometry

- Segoe UI Variable Display for headings, Segoe UI Variable Text for controls and prose. Native Windows legibility is intentional.
- Cascadia Code / Cascadia Mono / Consolas only for measurements, identifiers and technical evidence.
- Page title 31 DIP; section 19; row title 15; body 14 with 21-DIP line height; secondary prose 13 with 18-DIP line height. No decorative labels above page headings.
- Status masthead 36 DIP with 44-DIP line height (`HeroTitleTextStyle`) carries the Home verdict and the Checkup action heading, which is the only type permitted to outrank a page title, and the sole full-strength display moment per page. Coverage and last-status render as an inline stat (metric, caption, 1-DIP `BorderSubtleBrush` hairline divider) inside the hero enclosure, never a nested card; compact stacks the stat and drops the hairline.
- Entry-point icons sit in 40-DIP `IconTileStyle` tiles (`AccentChromeSubtleBrush` ground, `AccentChromeBrush` glyph), the system's recurring structural device, built only from the existing accent pair.
- Spacing tokens `SpacingXS` to `XXL` (4/8/12/16/24/32) and `GapXS` to `XL` exist in `ControlStyles.xaml` for new work; existing page markup still uses a 6 to 28 DIP mix that does not sit on that scale. Migrating page literals is a deferred, render-verified pass; replacing values blind would be a redesign, not a cleanup.
- Desktop inset 32 DIP; compact pages use 16–20. Outer panels have 14-DIP corners, inner panels and controls 8, status labels 6. One enclosure per functional group.
- Primary actions use the Windows system accent via native WinUI accent brushes. Secondary actions use QuietButtonStyle. Diagnostic channels share a native button style with visible focus.
- Preserve native hover, pressed, disabled and busy states. No perpetual animation or delayed feedback for effect.

## Composition

Home is recommendation-led: one checkup action, three common care entry points, a compact evidence summary, and recent activity. Power tools, Extensions, and Troubleshoot live on the navigation rail; Home does not repeat them. Decorative hardware atlases and telemetry HUDs are not part of the runtime Home hierarchy.

Checkup is explicitly read-only. Its four areas are listed up front and their rows update in place when a check runs, and findings deep-link to the relevant care surface instead of performing maintenance in place. The summary is a checked-area status, not a synthetic health score.

System care, Security, and Repair & recovery share `CareToolList` and project commands through exact catalog Area/Section values. Descriptions sit under task names; impact and requirements are supporting metadata. Portable playbooks remain a dedicated Repair & recovery section.

Power tools is the advanced catalog surface. Search, Area, Section, impact and read-only filters lead; Categories browses the real taxonomy; Favorites and Recent support repetition; Care plans remains separate. Normal rows omit command IDs and migration state. Those details stay behind **Advanced details** in the inspector.

The wide shell uses a native left navigation rail grouped around Home, Checkup, Care, Power tools, and Activity. Extensions, Troubleshoot, Settings, and Help are secondary/footer routes. About is reachable from Help and global search without permanent rail space. Below 920 DIP the rail compacts, then becomes minimal below 680 DIP.

Global search is product-wide: pages, native tools, extensions, and help topics participate. Unknown free text falls back to Power tools search. Data-heavy pages preserve stacked compact records; the Power tools inspector overlays below 1320 DIP. Avoid fixed-height prose and nested decorative enclosures.

`LayoutVisibility.CompactBreakpointDip = 920.0` remains the shared page-level compact boundary unless a component has a narrower measured breakpoint. Home's hero/evidence composition stacks below 820 DIP so the summary never competes with the primary action for horizontal space.

## Product truth and accessibility

Activity uses a theme-aware document illustration and a separate empty composition; an empty message never overlays column headings. The tool inspector retains invalid raw input and focuses it for correction. Cancellation uses a pending "Stopping..." state until execution settles.

- No generic Undo claim: show Undo only with an executable compensator.
- No unverifiable health score: collection coverage is not machine health. Findings retain their actual runtime interpretation.
- No blanket approval claim: risk determines direct execution, confirmation or destructive preview/receipt requirements. The dispatcher remains authoritative.
- No false publisher verification, invented plugin availability, decorative settings or telemetry.
- Meaningful automation names, keyboard access and visible focus; target 44-DIP controls.
- Use body typography for ordinary state, time, labels and prose; reserve monospace for identifiers, paths, raw parameters and technical evidence.
- Validate text, accent and status contrast in both themes. High contrast uses system brushes.

## Verification

The palette above is not decoration: `verify_visual_tokens.py` pins key presence per theme dictionary, and `verify_pill_contrast.py` plus `verify_palette_contrast.py`/`tests/native/test_visual_contrast.py` enforce WCAG 2.1 AA (≥4.5:1) on the measured literal pairs. Route and chrome-label parity across catalog/XAML/resources is gated by `verify_native_foundation.py` and `tests/native/test_product_ui_parity.py`. Build/source checks do not establish runtime accessibility: follow `docs/Windows-Validation.md` for the maintained Windows interaction, theme, scaling, keyboard, Narrator, packaging, and release-validation procedure. Historical captures remain labeled by build, and generated art never counts as execution evidence.

## 4.0 interface refinement

- Home starts with a read-only checkup and real last-check states. Technical engine labels are not shown as health or readiness claims.
- Power tools opens with available actions. Users can include unavailable catalog entries when inspecting a specific command, category or search deep link; a one-click reset restores the default filters.
- Troubleshoot is task-first, not an artificial chatbot. Common problems run the existing local diagnostic translator; results open the canonical Power tools inspector. No action is applied by the Troubleshoot page.
- The guided tour is optional in Help. First launch never blocks the Home checkup behind a modal.
- Controls use native Windows focus and semantic accent. Real theme, DPI, screen reader and keyboard validation still requires running the WinUI application.

### Troubleshoot results and memory widget safety

Troubleshoot shows a single result and suggested next actions, not a pretend assistant chat or a transcript of the user's input. Leaving the page clears the description and results, and a cancelled/stale check cannot repopulate it. Progress and cancellation are explicit. The memory usage widget is read-only; it opens the ordinary Power tools review flow for standby-memory cleanup instead of invoking native mutation directly. Memory metrics are sampled when the Performance section is opened, not labeled as live telemetry.

- Shared page headers rely on typography and spacing rather than a decorative accent stripe; optional badges are reserved for meaningful status.

## Downloads workspace (native WinUI)

Downloads is a top-level application destination and a Home shortcut, not a plugin
or a second HTTP client. The layout follows an editorial hierarchy: practical
entry field first, factual queue counts second, compact filter tabs and a native
virtualized list third. Use the existing semantic theme brushes, 4-DIP spacing
scale, clear focus rings, and system type. Never show URLs with query credentials,
session-bearing paths, or artificial transfer speeds. Every control is keyboard
reachable and has a useful automation name.

The process-wide DownloadCenterService is constructed by AppRuntime from the same
ICommandDispatcher used in Power tools and Activity. The DownloadsPageViewModel
projects native persisted job state, and the view never owns a transfer task.
A page can navigate away without interrupting a file; closing the application
requests a pause and retains partial files. The transport is limited to HTTP(S),
uses source and destination checks, and does not silently overwrite existing files.

## Mineral / evergreen visual system

The current brand palette uses soft mineral neutrals for surfaces and deliberate
evergreen accents only for navigation, selection and primary emphasis. It avoids
artificial neon indicators, dashboard wallpaper, gratuitous gradients, technobabble
and decorative badge repetition. Text uses Segoe UI Variable; size and contrast do
the hierarchical work, not uppercase telemetry labels. Corners and spacing stay
consistent with Fluent controls and the native Windows accessibility system.

All 26 documented foreground/surface combinations, eight status-pill contrast
pairs, and the full primary/secondary text surface matrix were tested against a
minimum 4.5:1 ratio on Light and Dark; high-contrast keeps Windows system colors.
Accent semantics remain text-and-shape distinguishable, not color-only.
