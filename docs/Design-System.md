# WinCare V5 design system

## Principles

1. Operational clarity before decoration.
2. Risk and recovery are visible at the decision point.
3. Dense information is grouped, not hidden.
4. Progressive disclosure preserves expert speed and novice safety.
5. Presentation cannot imply authority the engine has not granted.
6. Accessibility is structural, not a theme afterthought.

## Tokens

### Color

| Token | Value | Use |
|---|---:|---|
| `background` | `#0B1020` | application canvas |
| `sidebar` | `#0E1526` | primary navigation |
| `surface` | `#121A2B` | cards and panels |
| `surface-raised` | `#1B2740` | controls and secondary actions |
| `surface-hover` | `#22314F` | hover/focus support |
| `border` | `#2B3A58` | boundaries and dividers |
| `accent` | `#7C5CFC` | primary action/selection |
| `information` | `#36C5F0` | read-only and informational state |
| `success` | `#2DD4A8` | healthy/completed state |
| `warning` | `#F6C85F` | caution/moderate risk |
| `critical` | `#F97066` | destructive/critical risk |
| `text` | `#F4F7FB` | primary text |
| `text-muted` | `#9AA9C3` | secondary text |

Validated contrast ratios include 17.62:1 for primary text/background, 7.31:1 for muted text/surface, and at least 4.72:1 for status text on its status surface.

### Typography

- Family: Segoe UI Variable Text, fallback Segoe UI.
- Page title: 26 px semibold.
- Hero: 30 px semibold.
- Section title: 20 px semibold.
- Body: 13–14 px.
- Caption/metadata: 11–12 px.
- Command/output: Cascadia Mono/Consolas fallback where appropriate.

### Shape and spacing

- Main cards: 14–16 px radius.
- Controls: 8–10 px radius.
- Core spacing sequence: 4, 8, 12, 16, 18, 24, 32.
- Minimum interactive target: approximately 36–40 px desktop height.
- One-pixel semantic borders; shadows are restrained and non-essential.

## Components

- Sidebar navigation item with icon, title, selected surface, focus state.
- Risk badge with text and semantic foreground/background.
- Status card with label, value, detail, optional progress.
- Action card/list row with category, risk, command, description.
- Primary, neutral, and danger buttons.
- JSON argument editor and structured result viewer.
- Busy overlay, toast, and critical confirmation dialog.
- Evidence grid with source/record/path linkage.

## Interaction

- Hover supports recognition but is never required.
- Focus and selection are distinct.
- Apply remains unavailable for read-only actions.
- Critical confirmation requires text entry, not a single accidental click.
- Search changes discovery only; it never runs an action.
- Long tasks show a non-blocking busy overlay while the durable operation runs outside the UI process.

## Assets

- `design/WinCare-V5-Logo.svg` and `.png`
- `design/WinCare-V5-GUI-Preview.svg` and `.png`
- `src/WinCare/Data/Gui/WinCare.ico`

The design assets are target-native and do not copy donor branding.
