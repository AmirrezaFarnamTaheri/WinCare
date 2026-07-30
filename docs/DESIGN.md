# WinCare design system

WinCare uses a utilitarian, evidence-first design language for Windows administration and diagnostics. The graphical, terminal, and headless surfaces must present the same operation authority and result semantics even when their visual density differs.

This document defines interaction and presentation contracts. It does not grant capabilities or mutation authority.

## Design objectives

WinCare prioritizes:

1. operational clarity over decoration;
2. real system evidence over simulated metrics;
3. risk and authority visibility before action;
4. dense but readable information presentation;
5. keyboard, screen-reader, high-contrast, monochrome, and reduced-motion usability;
6. consistent status semantics across GUI, TUI, logs, and JSON;
7. stable performance on machines that are already under diagnosis.

## Cross-surface hierarchy

Every surface should answer these questions in order:

1. **Where am I?** — capability/workspace and target context.
2. **What is the current state?** — observations and confidence/evidence.
3. **What can be done?** — available actions constrained by capability/policy.
4. **What will change?** — preview, affected resources, risk, and reversibility.
5. **What authority is required?** — apply, approval, elevation, network, or persistence.
6. **What happened?** — result, postconditions, journal/receipt, and residual state.

Presentation layers must not hide blocked, unsupported, unknown, partial, or failed states merely to simplify the interface.

## Information architecture

The default workstation model is:

- **Navigation** — capability areas and workspaces.
- **Context header** — current machine/target, mode, read-only state, and capability status.
- **Primary content** — observations, plans, evidence, tables, or forms.
- **Action area** — preview, apply, cancel, export, or navigation actions.
- **Evidence/status area** — result state, timestamps, hashes/identities, warnings, postconditions, and recovery information.

The GUI may use panes, tabs, or cards; the TUI may use sections and tables. Both must preserve the same conceptual order.

## Semantic status model

Use consistent labels and iconography:

| Status | Meaning | Visual treatment |
| --- | --- | --- |
| `Available` | Capability and prerequisite are present | Neutral-positive, not celebratory |
| `ReadOnly` | Observation permitted; persistent mutation unavailable or disabled | Informational |
| `Preview` | Plan exists but has not been applied | Accent/informational |
| `AwaitingApproval` | Explicit approval/elevation is required | Warning |
| `Running` | Bounded operation is active | Progress without implying success |
| `Succeeded` | Required postconditions/evidence passed | Positive |
| `Partial` | Some work/evidence completed; residual state exists | Warning |
| `Blocked` | Prerequisite, policy, or environment prevents operation | Warning with reason |
| `Unsupported` | Capability is not meaningful/implemented in this environment | Neutral-muted with explanation |
| `Unknown` | Evidence is insufficient to assert state | Neutral-warning; never display as healthy |
| `Denied` | Policy, approval, identity, or scope rejected the request | Alert |
| `Failed` | Attempted operation did not satisfy its contract | Alert |

Color must reinforce text and iconography, never carry status alone.

## Semantic color roles

Implementation-specific colors live in WPF resources and terminal theme mappings. All themes must provide these roles:

| Role | Purpose |
| --- | --- |
| Canvas | Main application background |
| Surface | Panels, tables, forms, and grouped evidence |
| Raised surface | Active controls, selected rows, dialogs, and temporary layers |
| Border | Structural separation without heavy shadows |
| Primary text | Main labels and values |
| Secondary text | Metadata, descriptions, and supporting context |
| Muted text | Disabled or low-priority information that remains legible |
| Accent | Selected navigation and primary non-destructive actions |
| Information | Read-only, preview, and explanatory states |
| Warning | approval, blocked, partial, caution, and non-recoverable disclosure |
| Alert | denied, failed, destructive, or integrity-critical states |
| Success | verified completion/postcondition success |
| Focus | keyboard focus and active accessibility target |

### Theme requirements

- `Normal` may use a dark or system-aligned high-density palette.
- `HighContrast` must prioritize OS/user contrast semantics over brand colors.
- `Monochrome` must remain fully usable without hue distinctions.
- Terminal themes must degrade cleanly when ANSI color is unavailable.
- Read-only and mutation authority must remain visible in every theme.

## Typography

Use Windows/system-installed typefaces; WinCare must not require downloaded fonts.

Recommended roles:

- UI and body: `Segoe UI` with system fallback;
- terminal/code/path/hash: `Cascadia Mono`, `Consolas`, or system monospace fallback;
- headings: the same UI family with weight/size hierarchy rather than a separate decorative family.

Text should remain crisp at Windows scaling settings. Avoid all-caps paragraphs, ultra-light weights, and font sizes that sacrifice dense readability.

## Spacing and density

Use a 4-pixel conceptual spacing grid:

| Token | Typical value | Use |
| --- | ---: | --- |
| `xs` | 4 px | icon/text gap, compact metadata |
| `sm` | 8 px | dense cell/control padding |
| `md` | 12 px | grouped controls and panel interiors |
| `lg` | 16 px | section separation |
| `xl` | 24 px | major workspace separation |

Density rules:

- prefer scan-friendly tables for comparable evidence;
- keep labels close to their values;
- avoid oversized empty cards and decorative whitespace;
- allow user-resizable columns/panes where data length varies;
- never truncate the only copy of a critical path, identity, error, or risk disclosure without a way to reveal/copy it.

## Controls and actions

### Primary action

Only one primary action should dominate a local context. For mutating workflows, the primary action is normally **Preview** before **Apply** becomes available.

### Destructive action

Destructive actions must:

- use explicit verbs and target names;
- disclose scope, reversibility, and evidence expectations;
- avoid being the default focused button when a safer action exists;
- require the same policy/approval path as non-UI callers;
- remain distinguishable in high-contrast and monochrome modes.

### Disabled versus unavailable

Do not silently disable a control without explanation. Prefer a visible `Blocked`/`Unsupported` state with the prerequisite or policy reason.

### Confirmation

Confirmation is not a replacement for target validation. Use confirmation for human intent while the backend independently enforces identity, scope, policy, and postconditions.

## Plan and preview presentation

A plan view should expose, as applicable:

- operation name and target;
- current observed state;
- proposed changes;
- affected resources;
- prerequisites and capability status;
- required authority/elevation;
- risk and non-recoverable steps;
- compensation/rollback behavior;
- expected postconditions;
- evidence that will be recorded.

The apply control must remain bound to the reviewed plan identity; a materially changed target or plan should require a new preview.

## Evidence presentation

Evidence views should favor structured fields over prose-only summaries. Make it possible to inspect and copy:

- timestamps and duration;
- target/provider identity;
- selected arguments;
- before/after state;
- hashes, manifests, signatures, or exit codes;
- verification/postcondition outcome;
- blocked/unsupported reason;
- journal, receipt, or artifact location;
- residual or recovery state.

Do not display fabricated percentages, random sparklines, generic health scores, or decorative telemetry.

## Tables and logs

- freeze or preserve important identity columns when practical;
- provide sorting/filtering without changing underlying evidence;
- distinguish empty, unavailable, and error states;
- use monospaced text for paths, command lines, IDs, hashes, and raw events;
- bound rendered rows and offer explicit paging/export for large datasets;
- avoid auto-scrolling that prevents inspection;
- preserve exact raw values behind formatted summaries.

## Accessibility

All operator surfaces must support the applicable platform capabilities:

- complete keyboard navigation;
- visible focus indicator;
- logical focus order;
- accessible names/descriptions for icon-only controls;
- status announcements that do not rely on color;
- screen-reader-compatible tables and form relationships;
- Windows high-contrast behavior;
- monochrome and ASCII terminal rendering;
- reduced or absent nonessential motion;
- text scaling without clipping critical controls;
- copyable error and evidence details.

Automation IDs and accessibility labels should be stable enough for UI validation where feasible.

## Motion and performance

WinCare is a diagnostic tool and must not compete with the system being diagnosed.

- avoid continuous background animation;
- use motion only to communicate state transition or progress;
- honor reduced-motion preferences;
- stop progress animation when the operation stops;
- virtualize/bound large collections;
- keep expensive evidence collection outside the UI thread;
- never create decorative GPU workloads or WebGL-like backgrounds.

## Icons and product identity

Product and capability icons must be simple, high-contrast, and recognizable at small Windows sizes.

The WinCare product mark should have:

- one vector source of truth;
- square icon-safe geometry;
- transparent-background exports;
- tested sizes for 16, 20, 24, 32, 48, 64, 128, and 256 pixels;
- a multi-resolution Windows `.ico` for executable and shortcut use;
- monochrome/high-contrast fallback treatment;
- no embedded small text that becomes unreadable in Explorer or the taskbar.

Every standalone executable and installed shortcut should resolve to the approved product icon through the build/install pipeline. Asset generation and executable embedding must be deterministic and validated as part of release assurance.

## Anti-slop rules

Do not add:

- decorative gradients or blurred blobs without operational meaning;
- fake metrics, placeholder counters, or random charts;
- heavy shadows as a substitute for hierarchy;
- gratuitous glass/transparency that reduces readability;
- oversized marketing-style hero areas inside operational workspaces;
- animated backgrounds;
- generic icons that conceal distinct risk levels;
- success-colored states before postconditions pass;
- unexplained disabled controls;
- duplicate panels that present the same evidence differently.

## Review checklist

A UI change is ready when:

- it uses the existing backend contract rather than independent logic;
- read-only/preview/apply/elevation states are explicit;
- blocked, unsupported, unknown, partial, and failed states are represented;
- critical information survives high-contrast, monochrome, and narrow layouts;
- keyboard and screen-reader paths are considered;
- large data is bounded or virtualized;
- no fake or decorative operational data was introduced;
- UI catalog/XAML/resource tests and relevant Windows launch checks pass;
- command/capability/security documentation is updated when behavior changed.

## Related documents

- [Architecture](ARCHITECTURE.md)
- [Commands](COMMANDS.md)
- [Security](../SECURITY.md)
- [Testing](Testing.md)
