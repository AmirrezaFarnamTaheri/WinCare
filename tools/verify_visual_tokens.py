#!/usr/bin/env python3
"""Verifies all Cyber-Operate brush tokens are declared in Dark, Light, and HighContrast."""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOKENS = [
    'AccentTealBrush', 'AccentTealSubtleBrush', 'CardSurfaceBrush',
    'TelemetryFrameBrush', 'PillReadOnlyBgBrush', 'PillMutatingBgBrush',
    'PillElevatedBgBrush', 'PillVerifiedBorderBrush', 'PillNotReadyBgBrush',
    'PillTextBrush', 'PillAltTextBrush',
]
REQUIRED_THEMES = ['Light', 'Dark', 'HighContrast']
XAML_NS = 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'
X_NS = 'http://schemas.microsoft.com/winfx/2006/xaml'


def verify_tokens_xml(path):
    tree = ET.parse(path)
    root = tree.getroot()
    # Find theme dictionaries
    theme_dicts = {}
    for rd in root.iter(f'{{{XAML_NS}}}ResourceDictionary'):
        key = rd.get(f'{{{X_NS}}}Key')
        if key in REQUIRED_THEMES:
            theme_keys = set()
            for child in rd:
                k = child.get(f'{{{X_NS}}}Key')
                if k:
                    theme_keys.add(k)
            theme_dicts[key] = theme_keys
    all_pass = True
    for theme in REQUIRED_THEMES:
        if theme not in theme_dicts:
            print(f'FAIL: Theme "{theme}" not found')
            all_pass = False
            continue
        for token in TOKENS:
            if token not in theme_dicts[theme]:
                print(f'FAIL [{theme}]: Missing token "{token}"')
                all_pass = False
            else:
                print(f'OK [{theme}]: {token}')
    return all_pass


def main() -> int:
    theme_file = ROOT / "src/WinCare.App/Styles/ThemeResources.xaml"
    if not theme_file.is_file():
        print(f"FAIL: {theme_file} not found")
        return 1
    if not verify_tokens_xml(theme_file):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
