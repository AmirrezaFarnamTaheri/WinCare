# WinCare native design system

**Status:** Approved native replacement design

## Design read

WinCare is an Operate-mode Windows utility. Users must scan system state, understand risk, review changes, and recover from failures. Native expectations, clarity, accessibility, and responsiveness take precedence over decorative expression.

## Product principles

1. Plain language first. Command IDs and implementation details stay under Advanced details.
2. Checking and changing are visibly separate.
3. Every mutation shows the affected item, expected effect, risk, elevation need, restart need, and undo availability before apply.
4. The interface is never policy or durable truth.
5. Long work is cancellable, bounded, and visibly progressing.
6. Empty, loading, partial, blocked, failed, and completed states are designed states.
7. All 259 commands remain reachable without placing all 259 in the startup path.

## Navigation

The application uses a left WinUI `NavigationView`:

- Home
- Checkup
- System care
- Security
- Repair & recovery
- All tools
- Activity

Footer destinations:

- Settings
- Help and documentation
- About WinCare

Each main destination uses a `SelectorBar` for peer sections. Tabs do not create nested sidebars.

## Page tabs

| Page | Tabs |
|---|---|
| Home | Overview, Recommendations, Favorites |
| Checkup | Quick check, Full check, Custom check, Results |
| System care | Clean up, Performance, Apps & startup, Network & updates, Routines |
| Security | Status, Protection, Privacy, Hardening |
| Repair & recovery | Repair, Restore, Undo, Backup, Reset & media |
| All tools | Commands, Categories, Favorites, Recent, Presets |
| Activity | Running, Needs attention, Completed, Reports |
| Settings | General, Appearance, Safety, Notifications, Data, Advanced |

## Tabular surfaces

WinUI has no built-in first-party data grid suitable for this contract. Tabular views use:

- a shared header `Grid`
- a virtualized `ListView`
- a matching `Grid` in each typed item template
- a detail pane for explanations and actions
- compact labeled records below the responsive threshold

Example columns:

| Surface | Columns |
|---|---|
| All tools | Command, Category, Risk, Administrator access, Restart |
| Checkup results | Finding, Affected area, Importance, Recommended action |
| Activity | Operation, State, Started, Duration, Result |
| Undo | Change, Affected item, Created, Undo availability |
| Security findings | Protection area, Current state, Expected state, Action |

## Visual language

- Foundation: Fluent and native WinUI controls
- Typography: Segoe UI Variable, with Cascadia Mono only for technical details
- Theme: system preference with complete Light, Dark, and HighContrast dictionaries
- Accent: Windows system accent for selection and primary action
- Status color: only for actual information, success, warning, or danger states, always paired with text
- Spacing: 4, 8, 12, 16, 24, and 32 DIPs
- Radius: 8 DIPs for controls, 12 DIPs for major surfaces
- Interactive height: 40 DIPs minimum, 44 DIPs preferred for primary rows
- Material: Mica for the shell when supported; solid readable content surfaces
- Elevation: borders and tonal separation first; shadows only for true floating layers

## Explicit exclusions

- no hero gradient
- no giant marketing headline
- no card-on-card dashboard composition
- no neon, glow, or decorative glass panels
- no default dark-only theme
- no tiny low-contrast metadata as the main information layer
- no raw JSON as the default parameter experience
- no automatic execution from search
- no custom control when a native control satisfies the requirement

## Interaction model

Mutating work follows:

1. Choose task
2. Check current state
3. Review changes
4. Apply
5. View result or undo

Checkup remains read-only. High-impact work requires explicit confirmation with the affected identity in the dialog. Elevation occurs only after review and only for the selected operation.

## Search

Global search is available in the title bar and with `Ctrl+K`. It routes to All tools and searches plain names, command IDs, summaries, categories, and keywords. Submitting a search never runs a command.

## Responsive rules

- Wide: complete columns and inline detail pane
- Medium: secondary columns hidden and detail promoted contextually
- Narrow: compact labeled records and overlay detail pane
- Required actions remain available through responsive promotion or overflow
- `ListView` and `GridView` own their scrolling and are not wrapped in another vertical `ScrollViewer`

## Accessibility

- stable `AutomationId` values for navigation and tested actions
- `AutomationProperties.Name` on interactive controls
- typed `DataTemplate` bindings with `x:DataType`
- compiled `x:Bind`
- visible keyboard focus
- no color-only status communication
- Windows Contrast theme validation
- keyboard-only navigation and screen-reader checks
- clear labels above inputs, never placeholder-only labels

## Loading and failure states

Content uses shape-matched placeholders for longer hydration. Short control-level work may use `ProgressRing`. Inline status uses `InfoBar`. Failures identify what stopped, what remains safe, and the next action. Partial results remain visible.

## Motion

Motion communicates state changes or hierarchy only. It uses native transitions, opacity, and transforms, respects reduced-motion settings, and never delays access to controls.

## Startup contract

The first frame does not perform network access, launch external processes, probe optional runtimes, query the complete system, or instantiate 259 tool views. Startup markers record:

- AppConstructed
- WindowCreated
- FirstContentRendered
- ShellInteractive

Targets require Windows evidence:

- warm shell interactive under 1 second
- cold shell interactive under 2 seconds
- navigation response under 100 ms
- no UI-thread task longer than 50 ms
