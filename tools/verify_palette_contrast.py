#!/usr/bin/env python3
"""Full-palette WCAG 2.1 AA contrast check for the WinCare semantic brushes.

`verify_pill_contrast.py` covers the admission status pills. This covers the rest of the
product surface: primary/secondary text and accents over every background they are
composed against in Light and Dark, per DESIGN.md. HighContrast follows Windows system
colors and is intentionally excluded here (it is verified by the Windows validation pass).

Reuses the XML parsing and luminance math from verify_pill_contrast.py.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_pill_contrast import MINIMUM, parse_theme_colors, ratio  # noqa: E402

THEME_FILE = Path(__file__).resolve().parents[1] / "src/WinCare.App/Styles/ThemeResources.xaml"

# (label, foreground brush, background brush) — composed as the UI actually composes them.
# Body prose is 13–14px, so every pair must clear the 4.5:1 normal-text bar, not 3:1.
TEXT_PAIRS = [
    ("Primary text on page background",   "TextPrimaryBrush",   "PageBackgroundBrush"),
    ("Primary text on card surface",      "TextPrimaryBrush",   "CardSurfaceBrush"),
    ("Primary text on navigation rail",   "TextPrimaryBrush",   "NavigationRailBrush"),
    ("Secondary text on page background", "TextSecondaryBrush", "PageBackgroundBrush"),
    ("Secondary text on card surface",    "TextSecondaryBrush", "CardSurfaceBrush"),
    ("Secondary text on secondary surface", "TextSecondaryBrush", "SurfaceSecondaryBrush"),
    ("Secondary text on hero background", "TextSecondaryBrush", "HeroBackgroundBrush"),
    ("Secondary text on accent-subtle",   "TextSecondaryBrush", "AccentTealSubtleBrush"),
    ("Accent on page background",         "AccentBrush",        "PageBackgroundBrush"),
    ("Accent on card surface",            "AccentBrush",        "CardSurfaceBrush"),
    ("Text on accent (button)",           "TextOnAccentBrush",  "AccentBrush"),
    ("Success on card surface",           "SuccessBrush",       "CardSurfaceBrush"),
    ("Warning on card surface",           "WarningBrush",       "CardSurfaceBrush"),
    ("Danger on card surface",            "DangerBrush",        "CardSurfaceBrush"),
]

THEMES = ("Light", "Dark")


def main() -> int:
    if not THEME_FILE.is_file():
        print(f"FAIL: {THEME_FILE} not found")
        return 1

    try:
        colors = parse_theme_colors(THEME_FILE)
    except Exception as exc:  # noqa: BLE001 — match the sibling tool's error surface
        print(f"FAIL: could not parse {THEME_FILE}: {exc}")
        return 1

    fails = []
    checked = 0
    for theme in THEMES:
        print(f"--- {theme} ---")
        for label, fg_key, bg_key in TEXT_PAIRS:
            fg_hex = colors.get((theme, fg_key))
            bg_hex = colors.get((theme, bg_key))
            if fg_hex is None or bg_hex is None:
                msg = f"  MISS {label}: {fg_key}={fg_hex!r} {bg_key}={bg_hex!r} [{theme}]"
                print(msg)
                fails.append(f"{label} [{theme}] (missing brush)")
                continue
            r = ratio(fg_hex, bg_hex)
            ok = r >= MINIMUM
            checked += 1
            print(f"  {'OK  ' if ok else 'FAIL'} {label} [{theme}]: {r:.2f}:1"
                  f" (fg={fg_hex} bg={bg_hex})")
            if not ok:
                fails.append(f"{label} [{theme}] ({r:.2f}:1)")

    if fails:
        print(f"\nFAIL: {len(fails)} pair(s) below WCAG AA {MINIMUM}:1 or missing:")
        for f in fails:
            print(f"  - {f}")
        return 1
    print(f"\nOK: all {checked} surface pairs pass WCAG 2.1 AA {MINIMUM}:1.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
