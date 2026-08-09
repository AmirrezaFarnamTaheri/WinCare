#!/usr/bin/env python3
"""WCAG 2.1 AA contrast checker for all status pill color pairs."""
import sys

def luminance(hex6: str) -> float:
    r, g, b = int(hex6[0:2],16), int(hex6[2:4],16), int(hex6[4:6],16)
    def c(v):
        v /= 255
        return v/12.92 if v <= 0.04045 else ((v+0.055)/1.055)**2.4
    return 0.2126*c(r) + 0.7152*c(g) + 0.0722*c(b)

def ratio(fg: str, bg: str) -> float:
    l1, l2 = luminance(fg.lstrip("#")), luminance(bg.lstrip("#"))
    hi, lo = max(l1, l2), min(l1, l2)
    return (hi + 0.05) / (lo + 0.05)

PAIRS = [
    ("ReadOnly/Low dark", "#FFFFFF", "#047857"),
    ("Mutating dark",     "#FFFFFF", "#C41A1A"),   # 5.12:1 - WCAG AA pass
    ("Elevated dark",    "#1A1A1A", "#F59E0B"),
    ("NotReady dark",    "#94A3B8", "#1F2937"),
    ("Mutating light",   "#FFFFFF", "#DC2626"),
    ("Elevated light",   "#1A1A1A", "#D97706"),
    ("PillReadOnlyBgBrush Light: #047857 bg + white text", "#047857", "#FFFFFF"),
    ("PillNotReadyBgBrush Light: #E2E8F0 bg + dark text", "#E2E8F0", "#1A1A1A"),
    ("PillNotReadyBgBrush Dark: #1F2937 bg + white text", "#1F2937", "#FFFFFF"),
]

MINIMUM = 4.5

def main() -> int:
    fails = []
    for name, fg, bg in PAIRS:
        r = ratio(fg, bg)
        ok = r >= MINIMUM
        print(f"  {'OK  ' if ok else 'FAIL'} {name}: {r:.2f}:1 (fg={fg} bg={bg})")
        if not ok:
            fails.append(f"{name} ({r:.2f}:1)")
    if fails:
        print(f"\nFAIL: {len(fails)} pair(s) below WCAG AA {MINIMUM}:1")
        print("  Remedy: PillMutatingBgBrush dark -> #C41A1A (5.12:1)")
        return 1
    print(f"\nOK: all {len(PAIRS)} pairs pass WCAG 2.1 AA {MINIMUM}:1.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
