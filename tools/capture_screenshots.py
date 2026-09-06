#!/usr/bin/env python3
"""Automated screenshot capture tool for WinCare documentation, interactive showcase, and TUI previews.

Discovers available headless browsers (Edge, Chrome, Chromium, or Playwright)
and renders high-fidelity screenshots for docs/images/*.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
DOCS_DIR = REPO_ROOT / "docs"
IMAGES_DIR = DOCS_DIR / "images"

CAPTURE_TARGETS = [
    (
        DOCS_DIR / "showcase.html",
        IMAGES_DIR / "showcase-preview.png",
        1440,
        900,
    ),
    (
        DOCS_DIR / "terminal-preview.html",
        IMAGES_DIR / "tui-preview.png",
        1280,
        820,
    ),
    (
        DOCS_DIR / "architecture.html",
        IMAGES_DIR / "architecture-preview.png",
        1440,
        900,
    ),
]

REQUIRED_RUNTIME_IMAGES = [
    "runtime-dashboard.png",
    "runtime-checkup.png",
]


def find_browser_executable() -> str | None:
    if env_browser := os.getenv("BROWSER_PATH"):
        if Path(env_browser).is_file():
            return env_browser

    candidates = []
    if sys.platform == "win32":
        candidates.extend([
            r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            r"C:\Program Files\Google\Chrome\Application\chrome.exe",
            r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            os.path.expandvars(r"%LOCALAPPDATA%\Microsoft\Edge\Application\msedge.exe"),
            os.path.expandvars(r"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"),
        ])
    else:
        candidates.extend([
            "google-chrome",
            "google-chrome-stable",
            "chromium",
            "chromium-browser",
            "microsoft-edge",
            "microsoft-edge-stable",
        ])

    for candidate in candidates:
        if sys.platform == "win32":
            if os.path.isfile(candidate):
                return candidate
        else:
            path = shutil.which(candidate)
            if path:
                return path

    return None


def capture_screenshot(
    browser_path: str,
    source_html: Path,
    target_png: Path,
    width: int = 1440,
    height: int = 900,
) -> bool:
    if not source_html.is_file():
        print(f"[-] Source HTML not found: {source_html}", file=sys.stderr)
        return False

    target_png.parent.mkdir(parents=True, exist_ok=True)
    file_url = source_html.resolve().as_uri()

    cmd = [
        browser_path,
        "--headless",
        "--disable-gpu",
        "--hide-scrollbars",
        "--force-device-scale-factor=1",
        f"--window-size={width},{height}",
        f"--screenshot={target_png.resolve()}",
        file_url,
    ]

    try:
        proc = subprocess.run(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=35,
        )
        if target_png.is_file() and target_png.stat().st_size > 1024:
            size_kb = target_png.stat().st_size / 1024
            print(f"[+] Captured {target_png.name} ({width}x{height}, {size_kb:.1f} KB)")
            return True
        else:
            print(
                f"[-] Browser exited with code {proc.returncode} but {target_png.name} was not written properly:\n{proc.stderr}",
                file=sys.stderr,
            )
            return False
    except Exception as ex:
        print(f"[-] Failed to execute browser capture: {ex}", file=sys.stderr)
        return False


def verify_image_integrity(image_path: Path, min_bytes: int = 2048) -> bool:
    if not image_path.is_file():
        print(f"[-] Missing required screenshot: {image_path}", file=sys.stderr)
        return False

    size = image_path.stat().st_size
    if size < min_bytes:
        print(f"[-] Screenshot too small ({size} bytes): {image_path}", file=sys.stderr)
        return False

    with open(image_path, "rb") as f:
        header = f.read(8)
        if header != b"\x89PNG\r\n\x1a\n":
            print(f"[-] File {image_path.name} does not have valid PNG header", file=sys.stderr)
            return False

    return True


def run_all_captures(browser_path: str | None = None) -> int:
    print("=== WinCare Automated Screenshot Capture Pipeline ===")
    IMAGES_DIR.mkdir(parents=True, exist_ok=True)

    browser = browser_path or find_browser_executable()
    captured_count = 0
    failed_count = 0

    if browser:
        print(f"Using browser executable: {browser}")
        for src, dest, w, h in CAPTURE_TARGETS:
            print(f"Rendering {src.name} -> {dest.name}...")
            if capture_screenshot(browser, src, dest, w, h):
                captured_count += 1
            else:
                failed_count += 1
    else:
        print("[!] No headless browser found on system. Skipping fresh render pass.")

    print("\n--- Verifying Documentation Screenshots ---")
    verified = 0
    total_expected = len(CAPTURE_TARGETS) + len(REQUIRED_RUNTIME_IMAGES)

    all_targets = [dest for _, dest, _, _ in CAPTURE_TARGETS] + [
        IMAGES_DIR / name for name in REQUIRED_RUNTIME_IMAGES
    ]

    for img in all_targets:
        if verify_image_integrity(img):
            print(f"  [OK] {img.name:28} ({img.stat().st_size / 1024:6.1f} KB)")
            verified += 1
        else:
            print(f"  [FAIL] {img.name}")

    print(f"\nSummary: {verified}/{total_expected} screenshots verified, {captured_count} refreshed.")

    if verified < total_expected:
        print("[-] Screenshot verification failed: one or more required images are missing or corrupt.", file=sys.stderr)
        return 1

    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Capture and verify WinCare documentation screenshots.")
    parser.add_argument("--browser", help="Explicit path to browser binary")
    parser.add_argument("--verify-only", action="store_true", help="Only verify existing screenshots without re-rendering")
    args = parser.parse_args()

    if args.verify_only:
        print("--- Verifying Documentation Screenshots (Verify-Only Mode) ---")
        all_targets = [dest for _, dest, _, _ in CAPTURE_TARGETS] + [
            IMAGES_DIR / name for name in REQUIRED_RUNTIME_IMAGES
        ]
        failed = [img for img in all_targets if not verify_image_integrity(img)]
        if failed:
            print(f"[-] {len(failed)} screenshots failed integrity verification.", file=sys.stderr)
            return 1
        print("[+] All documentation screenshots verified successfully.")
        return 0

    return run_all_captures(args.browser)


if __name__ == "__main__":
    sys.exit(main())
