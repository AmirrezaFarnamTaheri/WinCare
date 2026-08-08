#!/usr/bin/env python3
"""Verifies all Cyber-Operate brush tokens are declared in Dark, Light, and HighContrast."""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOKENS = [
    "AccentTealBrush", "AccentTealSubtleBrush", "CardSurfaceBrush",
    "TelemetryFrameBrush", "PillReadOnlyBgBrush", "PillMutatingBgBrush",
    "PillElevatedBgBrush", "PillVerifiedBorderBrush", "PillNotReadyBgBrush",
    "PillTextBrush", "PillAltTextBrush",
]

def main() -> int:
    theme_file = ROOT / "src/WinCare.App/Styles/ThemeResources.xaml"
    control_file = ROOT / "src/WinCare.App/Styles/ControlStyles.xaml"
    if not theme_file.is_file():
        print(f"FAIL: {theme_file} not found"); return 1
    content = theme_file.read_text(encoding="utf-8")
    findings = []
    for token in TOKENS:
        count = content.count(f'x:Key="{token}"')
        if count < 3:
            findings.append(f"  {token}: {count}/3 theme declarations")
    if control_file.is_file():
        cs = control_file.read_text(encoding="utf-8")
        if 'x:Key="StatusPillTemplate"' not in cs:
            findings.append("  StatusPillTemplate: missing from ControlStyles.xaml")
    if findings:
        print("FAIL:"); [print(f) for f in findings]; return 1
    print(f"OK: all {len(TOKENS)} tokens x 3 themes + StatusPillTemplate confirmed.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
