# WinCare WinUI 3, C#, and Rust Redesign Specification

**Status:** Approved on 2026-08-05

## Product goal

Replace the current PowerShell/WPF product surface with a fast, native Windows application that preserves every existing capability and all 259 command IDs while making routine work understandable to non-specialists.

## Fixed decisions

- The primary UI is WinUI 3 on Windows App SDK.
- Product behavior, workflow orchestration, safety policy, approval, journaling, recovery, packaging, accessibility, and localization are written in C#.
- Rust is limited to measured, bounded native workloads such as filesystem scanning, hashing, binary parsing, compression, and selected collectors.
- No PowerShell dependency remains in the completed runtime or repository.
- The migration may retain the current PowerShell source only as a temporary behavior oracle until each command reaches parity.
- Every existing command ID remains stable. The native catalog must contain exactly 259 unique IDs.
- MSIX is the recommended package. A signed self-contained portable bundle is also produced from the same tested binaries.
- The product uses one authoritative mutation path. UI state is never policy or durable truth.

## Information architecture

The left `NavigationView` contains:

1. Home
2. Checkup
3. System care
4. Security
5. Repair & recovery
6. All tools
7. Activity

The footer contains Settings, Help and documentation, and About WinCare.

Each main area uses a horizontal `SelectorBar` for peer sections:

- Home: Overview, Recommendations, Favorites
- Checkup: Quick check, Full check, Custom check, Results
- System care: Clean up, Performance, Apps & startup, Network & updates, Routines
- Security: Status, Protection, Privacy, Hardening
- Repair & recovery: Repair, Restore, Undo, Backup, Reset & media
- All tools: Commands, Categories, Favorites, Recent, Presets
- Activity: Running, Needs attention, Completed, Reports
- Settings: General, Appearance, Safety, Notifications, Data, Advanced

## Tabular content

Data-heavy surfaces use a virtualized `ListView` with a shared header `Grid` and a matching `Grid` item template. WinCare does not add a web-style data-grid dependency.

- Checkup results: Finding, affected area, importance, recommended action
- All tools: Command, category, risk, administrator access, restart
- Activity: Operation, state, started, duration, result
- Undo: Change, affected item, created, undo availability
- Security findings: Protection area, current state, expected state, action
- Installed apps: Application, publisher, version, size, action

Wide layouts show the complete column set and a side detail pane. Medium layouts hide secondary columns. Narrow layouts render compact labeled records. Required commands always remain reachable.

## UX rules

- Default copy is plain language. Command IDs, raw payloads, source records, and implementation details appear under Advanced details.
- Every mutation follows: Choose task, Check current state, Review changes, Apply, View result or undo.
- Checkup is read-only. It cannot silently apply changes.
- Destructive and high-impact actions identify the affected item and require explicit confirmation.
- Elevation is requested only for the selected operation and only after review.
- Search is global, available from the title bar and `Ctrl+K`, and covers tasks, settings, commands, activity, and help.

## Visual system

- Mode: Operate
- Foundation: native Fluent controls and Windows interaction conventions
- Typography: Segoe UI Variable and Segoe Fluent Icons
- Theme: system light/dark preference with an explicit High Contrast design
- Accent: Windows system accent, with semantic status colors used only for real state
- Spacing: 4, 8, 12, 16, 24, 32 DIPs
- Radius: 8 DIPs for controls, 12 DIPs for major surfaces
- Minimum interaction height: 40 DIPs, with 44 DIPs preferred for primary rows
- Mica may be used for the shell. Content surfaces remain solid and readable.
- No decorative gradients, card-on-card composition, neon, giant hero copy, fake glass panels, or excessive shadows.
- Motion is limited to purposeful state transitions and respects reduced-motion settings.

## Runtime architecture

```text
WinCare.App                 WinUI 3 shell, views, accessibility, lifecycle
WinCare.Application         use cases, command dispatch, search, activity
WinCare.Domain              policy-neutral models, plans, results, recovery contracts
WinCare.Infrastructure      Windows APIs, persistence, elevation, packaging services
WinCare.CommandCatalog      stable definitions for all 259 commands
native/wincare-core         bounded Rust primitives exposed through a versioned C ABI
```

C# owns all decisions. Rust returns structured evidence or performs a narrowly admitted operation. Business rules are never duplicated across languages.

## Safety path

```text
UI request
  -> typed command contract
  -> current-state observation
  -> plan construction
  -> policy and compatibility checks
  -> preview and approval
  -> admitted provider operation
  -> evidence and postcondition
  -> journal and receipt
  -> compensation when supported
```

No page, command palette entry, plugin, native function, or elevated host may bypass this path.

## Startup contract

The shell renders before command discovery, system probing, networking, external processes, or advanced capability checks.

- No network access on the startup path.
- No external process launch on the startup path.
- The embedded command manifest is loaded without probing the machine.
- Cached state is clearly labeled with its last-checked time.
- System checks hydrate independently after the first frame.
- All tools is virtualized and loads details lazily.
- Every long operation supports cancellation, bounded output, and a deadline.

Measurement targets, to be proven on Windows hardware:

- Warm shell interactive: under 1 second
- Cold shell interactive: under 2 seconds
- Navigation response: under 100 ms
- No UI-thread task longer than 50 ms

## Packaging

- Windows App SDK stable channel
- x64 and ARM64
- Signed MSIX primary distribution
- Signed self-contained portable bundle secondary distribution
- Both packages contain the same managed and native binaries
- Production signing uses timestamping

## Verification gates

- Exactly 259 unique command IDs in the native catalog
- No command ID renamed or omitted
- No `Microsoft.PowerShell.SDK`, WPF, `PresentationCore`, or `System.Windows.*` reference in native projects
- No `.ps1`, `.psm1`, or `.psd1` file in the completed repository
- `x:Bind` and `x:DataType` on WinUI data templates
- Automation names and stable automation IDs on interactive controls
- Light, dark, and High Contrast screenshots
- Keyboard-only and screen-reader paths
- MSIX install, upgrade, uninstall, and portable launch tests
- Rust formatting, Clippy, unit tests, and Miri for crates containing unsafe code
- Startup spans and regression budgets in Windows CI

## Migration rule

The old implementation is removed only after every command is represented in the native catalog, assigned to an owner, backed by parity fixtures, implemented, and verified. Unsupported automatic translation is prohibited because it can silently weaken safety behavior.
