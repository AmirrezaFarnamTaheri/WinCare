# WinCare visual direction review

Status: archived concept round. The user selected a hybrid of Precision Console studies A, C, D and E. See the root DESIGN.md and docs/design/hybrid-validation.md. These images remain concept references, not runtime screenshots.

## Grounding

Reviewed README.md, DESIGN.md, current HomePage.xaml and ShellPage.xaml, and the historical v2.5.0-rc5 runtime-dashboard.png. Current source takes precedence over the old capture. WinCare remains a native WinUI 3 app.

## Review

- Too many equally weighted containers compete with the primary next step. Make Run checkup easy to find in the first viewport.
- Reduce nested borders and repeated status framing. Use typography and alignment to establish grouping.
- Replace prominent unmeasured health-like graphics with clearly named evidence states.
- Group navigation by task while preserving access to every existing page.
- Keep quick actions close to their risk and result feedback; maintain the existing dispatcher behavior.

## Five directions

### 01 — Fluent Calm

Light pearl #F4F7FA, white surfaces, deep ink #182C40, blue #175CD3. Calm, legible, deliberate. A compact 210px left navigation, large open workspace with a single broad next-step panel 'Start with a checkup' and blue 'Run checkup' button. Below: a flat two-column six-category evidence list and recent activity sidebar. Spacious native Windows settings-like composition, subtle separators, restrained 8px corners. No oversized health ring.

### 02 — Precision Console

Dark graphite #141B24, slate #202C38, icy white #EFF6FC, cyan #46C9DA. Precise, focused, technical. Compact left navigation, structured horizontal command header 'Home', primary 'Run checkup' at top right, wide evidence table occupying main left 70%, right 30% context inspector titled 'Check details'. Bottom three compact quick actions. Horizontal status strip 'No checkup yet'. Table columns Category / Evidence / Next step. Tabular Cascadia Mono values, crisp dense rows, 6px corners, no charts without data. Premium technician workstation.

### 03 — Care Studio

Light mist #F0F5F2, white #FFFFFF, forest #24614D, dark green-black #182C26. Reassuring, spacious, practical. Narrow grouped sidebar. Main headline 'A clear view of your PC.' Beneath it an asymmetric split: tall spacious guided checkup panel on left with step text 'Collect evidence', 'Review findings', 'Choose an action' and primary 'Run checkup'; right six simple horizontally aligned category rows. Bottom wide quick-action list. Soft 14px rounding, airy Segoe typography, subtle green selected nav. No illustrations, ornamental nature, gradients, or decorative health scores.

### 04 — Cobalt Workbench

Silver #EEF1F5 background, dark cobalt #183A72 sidebar, white panels, ink #1A2638. Organized, capable, direct. Native desktop app with grouped left nav; main workspace deliberately uses a tool workbench composition. Compact Home header and run checkup button. Wide pinned-tools shelf with Quick Clean, Network Tune, Startup Review. Below main split: tabbed evidence master-list on left, selected-category detail panel on right. Small recent activity ledger at bottom. Rectangular 6px corners, clean strong typography. Distinct blue vertical sidebar is signature; primary controls dark cobalt. No faux live results.

### 05 — Quiet Slate

Dark desaturated plum-slate #211F29, layered grey-violet #2C2935, soft white #F3F0F7, restrained lavender #C1B3F1. Quiet, ordered, refined. Full labeled 210px navigation; large calm title Home with thin divider and discrete global search. Main content editorial asymmetry: expansive unboxed 'Your next step' area with 'Run your first checkup' and one lavender primary button, narrow right evidence summary list. Below, a single continuous full-width maintenance list of Quick Clean / Network Tune / Startup Review, with inline risk labels and right actions; understated activity footer. 8px corners, precise spacing, no neon, glow, gradients, black gaming UI or giant metrics. Native productivity design.

## Whole-app implementation after selection

1. Record the selected reference, palette, type scale, spacing, surfaces and component states in the durable design specification.
2. Update shared ThemeResources.xaml and ControlStyles.xaml, then titlebar and navigation.
3. Apply the direction to Home, Checkup, System care, Security, Repair & recovery, All tools, Activity, Plugin store, AI Doctor, Settings, Help, About, plugin details and shared dialogs/widgets.
4. Preserve bindings, commands, risk classification, review receipts, persisted settings, truthful evidence, and accessibility names. No new backend features are implied by a concept.
5. Verify native build, existing relevant checks, keyboard navigation, high contrast, both appearance modes, compact layout below 920 DIP, and representative pending/empty/error/success states. Capture a newly built app for comparison; never claim generated art is a tested implementation.

Image generation: built-in image tool. The 12ui CLI installation failed with spawn EINVAL; no hosted paid job was dispatched. Exact generation prompts are saved alongside this document.
