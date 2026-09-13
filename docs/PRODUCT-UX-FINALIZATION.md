# Product / UX finalization

This document records the task-first product restructuring and the deeper second product/UI/UX/frontend-parity pass applied to WinCare. The work preserves the dispatcher, command contracts, Windows integration, and risk/admission semantics while removing duplicated product logic and presentation debt.

## Product architecture

- Primary navigation is organized around user jobs: Home, Checkup, System care, Security, Repair & recovery, Power tools, and Activity.
- Extensions, Troubleshoot, Settings, and Help are secondary routes. About remains reachable without occupying permanent navigation.
- Home is a presentation-only evidence and routing surface. It no longer owns command dispatch, native probing, cleanup, startup, or network mutation workflows.
- Troubleshoot owns local symptom-to-evidence conversation state only. Suggested commands open in the canonical Power tools inspector; Troubleshoot no longer maintains a second preview/apply pipeline.
- Power tools remains the single advanced review/execution surface for typed command parameters, risk-tier flow, result evidence, and advanced technical details.
- Care-page deep links use stable section names rather than positional tab numbers. Integer section parameters remain only as a compatibility path for older callers.
- A regression contract keeps `NavigationCatalog`, `PageService`, and the visible shell route set aligned.

## Capability discoverability

- Ctrl+K searches routes, all 269 native tools, discovered extensions, and help topics.
- Power tools has exact Area and Section filters, plus a product-facing safety-tier filter.
- The safety-tier filter exposes **Safe / Moderate / Destructive**, matching the domain admission model. Raw catalog values such as Low/High/Critical remain implementation/advanced-detail data rather than primary UX taxonomy.
- Categories is a real Area/Section browser rather than a different sort of the same table.
- Favorites, Recent, and Care plans are distinct modes.
- Normal tool rows show user-relevant task, category, admission tier, administrator requirement, and restart expectation. Command IDs, raw catalog risk, and migration metadata are confined to Advanced details.
- Extensions can be reached from the shell, Home, Help, and global search.

## Correct care and evidence taxonomy

System care, Security, and Repair & recovery project the command catalog through exact `Area` + `Section` values rather than fuzzy text matches. This prevents unrelated commands from leaking into a tab because their title or summary happens to share a word.

System care intentionally combines the catalog's `Routines` and `Maintenance` sections under **Routines & maintenance**. Repair & recovery keeps **Portable playbooks** as its own workflow and does not present an empty generic change-records tab.

Home now mirrors the four read-only Checkup evidence sources one-to-one: Windows & hardware, Storage, Security, and Windows Update. It no longer invents a separate “Performance” status from the generic system-information probe.

## Checkup boundary

Checkup is read-only end to end. System/storage/security probes run concurrently with bounded concurrency; Windows Update readiness continues in the background so it cannot block the first evidence summary. Findings hand off to named care sections instead of applying maintenance inside Checkup.

The Results projection preserves the same follow-up actions as the quick-check rows, including background Windows Update findings and security findings. The UI reports checked-area status rather than a synthetic whole-machine health score.

## Extension trust parity

The Extensions view now exposes the catalog trust/availability state already modeled by the backend. The detail dialog explains that installation is available only when current catalog and package trust checks pass; otherwise the package remains browse-only. User-facing copy consistently says “extension,” while internal plugin type names remain implementation details.

## Safety presentation

The dispatcher and risk/admission protections are unchanged. Product copy is aligned with the actual domain policy:

- **Safe** — direct read-only or bounded low-risk execution.
- **Moderate** — reviewed/confirmed mutation flow.
- **Destructive** — preview plus explicit approval of the exact plan.

The UI no longer claims that every mutation necessarily uses the destructive two-phase flow. Technical catalog risk remains visible in Advanced details for engineering/diagnostic use.

## Visual hierarchy and de-slop pass

- Home has one primary Checkup call to action, not competing duplicate CTAs.
- Home compact layout uses real grid rows instead of moving controls into undefined rows.
- Home/Help use flatter task-oriented sections with fewer nested enclosures.
- Checkup uses a simple read-only hero, tabs before content, and direct findings.
- Power tools uses named controls and declarative parameter placement rather than visual-tree/order discovery.
- Extension cards are less vertically bloated and make trust/availability visible before action.
- The abandoned instrument-panel resource family (DoubleBezel/HUD/Island/Glow styles and brushes) has been removed from the active design system.
- Typography, semantic status colors, native focus behavior, high-contrast resources, and shared Fluent/WinUI controls remain intact.

## Documentation and screenshot truth

The checked-in `runtime-dashboard.png` and `runtime-checkup.png` are explicitly versioned as historical v2.5.0-rc5 runtime evidence. They are not presented as current-source screenshots after this redesign. Current source/XAML is authoritative until a new installed candidate is captured and visually reviewed.

## Validation contract

The PR is gated by the repository's native verification job plus x64/ARM64 Windows build/package jobs. The second pass adds source-level regression checks for:

- canonical Troubleshoot → Power tools execution handoff;
- no Home command ownership;
- stable named care-section routing;
- Power tools accessibility IDs and removal of visual-tree-order discovery;
- visible extension trust state;
- product-facing safety-tier filtering;
- shell/PageService/navigation-catalog route parity;
- Checkup follow-up action parity;
- removal of legacy instrument-panel resources;
- documentation/screenshot truthfulness.

Visual-token and status-pill contrast verification remain part of repository validation, and the command catalog remains 269 unique native commands.

## Remaining live-Windows evidence

Automated Windows builds can establish compile/package correctness, but they do not replace a rendered human/accessibility pass. A final installed-candidate review should still cover Narrator, keyboard traversal, High Contrast, narrow-window behavior, 100–225% text/display scaling, dialogs, and fresh runtime screenshots. `docs/Windows-Validation.md` remains the authoritative procedure.
