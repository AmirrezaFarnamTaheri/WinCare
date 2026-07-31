# WinCare brand system

![WinCare logo](../src/WinCare/Data/Gui/WinCare-Logo.svg)

## Identity

WinCare uses a protective shield/squircle, a continuous **W**, and a central care pulse. The identity communicates the product’s three responsibilities: **system health, security, and control**.

The mark is deliberately small-size-first. It must remain recognizable in the Windows taskbar, Start menu, file explorer, installer shortcuts, and executable resources before it is used as a large promotional lockup.

## Canonical palette

| Token | Value | Purpose |
| --- | --- | --- |
| Ink | `#07131F` | Primary dark field |
| Surface | `#10283B` | Elevated surface and shield field |
| Cyan | `#20D5E8` | Health/diagnostic accent |
| Mint | `#50F2A4` | Recovery/verified accent |
| White | `#F5FCFF` | High-contrast signal and type |

## Canonical assets

All product assets live in `src/WinCare/Data/Gui`:

- `WinCare-Mark.svg` — source app mark
- `WinCare-Logo.svg` — source horizontal lockup
- `WinCare-Logo.png` — 1600×520 lockup
- `WinCare-Logo-256.png` — 256×256 app image
- `WinCare.ico` — 16, 24, 32, 48, 64, 128, and 256 pixel frames
- `WinCare.Brand.json` — closed hash, dimension, palette, and frame manifest

`tools/Generate-WinCareBrand.ps1` is the only supported generator. Run it on Windows to regenerate the complete asset family:

```powershell
pwsh -NoProfile -File .\tools\Generate-WinCareBrand.ps1 -Root .
```

Use `-Check` in validation to regenerate into an isolated directory and prove that committed assets are reproducible:

```powershell
pwsh -NoProfile -File .\tools\Generate-WinCareBrand.ps1 -Root . -Check
pwsh -NoProfile -File .\tools\Test-WinCareBrand.ps1 -Root .
```

## Product ownership

- `Directory.Build.props` embeds `WinCare.ico` into all three standalone executables.
- `Build-Exe.ps1` verifies associated-icon pixel identity and records icon hashes in `WinCare.Standalone.build.json`.
- The installer writes and verifies explicit `IconLocation` values for GUI and console shortcuts.
- Release output carries the complete brand packet and its manifest.
- Windows validation rejects missing, non-reproducible, hash-divergent, incorrectly framed, or non-embedded logo assets.

Do not replace individual PNG/ICO files manually. Change the generator’s shared geometry and regenerate the full closed asset set.
