#!/usr/bin/env python3
"""WCAG 2.1 AA contrast checker — reads deployed values from ThemeResources.xaml."""
import sys
try:
    import defusedxml.ElementTree as ET
except ImportError:
    import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
THEME_FILE = ROOT / "src/WinCare.App/Styles/ThemeResources.xaml"

XAML_NS = "http://schemas.microsoft.com/winfx/2006/xaml/presentation"
X_NS = "http://schemas.microsoft.com/winfx/2006/xaml"

# (label, fg_key, bg_key, theme)
PAIRS_SPEC = [
    ("ReadOnly dark",       "PillTextBrush",    "PillReadOnlyBgBrush",  "Dark"),
    ("Mutating dark",       "PillTextBrush",    "PillMutatingBgBrush",  "Dark"),
    ("Elevated dark",       "PillAltTextBrush", "PillElevatedBgBrush",  "Dark"),
    ("NotReady dark",       "PillTextBrush",    "PillNotReadyBgBrush",  "Dark"),
    ("ReadOnly light",      "PillTextBrush",    "PillReadOnlyBgBrush",  "Light"),
    ("Mutating light",      "PillTextBrush",    "PillMutatingBgBrush",  "Light"),
    ("Elevated light",      "PillAltTextBrush", "PillElevatedBgBrush",  "Light"),
    ("NotReady light",      "PillAltTextBrush", "PillNotReadyBgBrush",  "Light"),
]

MINIMUM = 4.5


def luminance(hex6: str) -> float:
    h = hex6.lstrip("#")
    r, g, b = int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)

    def c(v: int) -> float:
        v /= 255.0
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4

    return 0.2126 * c(r) + 0.7152 * c(g) + 0.0722 * c(b)


def ratio(fg: str, bg: str) -> float:
    l1, l2 = luminance(fg), luminance(bg)
    hi, lo = max(l1, l2), min(l1, l2)
    return (hi + 0.05) / (lo + 0.05)


def parse_theme_colors(path: Path) -> dict[tuple[str, str], str]:
    """Return {(theme_key, brush_key): #RRGGBB}."""
    tree = ET.parse(path)
    root = tree.getroot()
    colors: dict[tuple[str, str], str] = {}
    for rd in root.iter(f"{{{XAML_NS}}}ResourceDictionary"):
        theme = rd.get(f"{{{X_NS}}}Key")
        if theme not in ("Light", "Dark", "HighContrast"):
            continue
        for child in rd:
            key = child.get(f"{{{X_NS}}}Key")
            color = child.get("Color", "")
            if key and color.startswith("#") and len(color) in (7, 9):
                # Strip alpha channel if present (#AARRGGBB → #RRGGBB)
                hex6 = "#" + color.lstrip("#")[-6:]
                colors[(theme, key)] = hex6
    return colors


def main() -> int:
    if not THEME_FILE.is_file():
        print(f"FAIL: {THEME_FILE} not found")
        return 1

    try:
        colors = parse_theme_colors(THEME_FILE)
    except ET.ParseError as exc:
        print(f"FAIL: XML parse error in {THEME_FILE}: {exc}")
        return 1

    fails = []
    for label, fg_key, bg_key, theme in PAIRS_SPEC:
        fg_hex = colors.get((theme, fg_key))
        bg_hex = colors.get((theme, bg_key))
        if fg_hex is None or bg_hex is None:
            msg = f"  MISS {label}: {fg_key}={fg_hex!r} {bg_key}={bg_hex!r} [{theme}]"
            print(msg)
            fails.append(label)
            continue
        r = ratio(fg_hex, bg_hex)
        ok = r >= MINIMUM
        print(f"  {'OK  ' if ok else 'FAIL'} {label} [{theme}]: {r:.2f}:1"
              f" (fg={fg_hex} bg={bg_hex})")
        if not ok:
            fails.append(f"{label} ({r:.2f}:1)")

    if fails:
        print(f"\nFAIL: {len(fails)} pair(s) below WCAG AA {MINIMUM}:1")
        return 1
    print(f"\nOK: all {len(PAIRS_SPEC)} pairs pass WCAG 2.1 AA {MINIMUM}:1.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
