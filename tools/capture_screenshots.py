#!/usr/bin/env python3
"""Automated screenshot capture tool for WinCare documentation, interactive showcase, TUI previews,
and end-to-end runtime captures taken from a built portable executable.

Discovers available headless browsers (Edge, Chrome, Chromium, or Playwright)
and renders high-fidelity screenshots for docs/images/*. Runtime images are e2e
evidence: `--runtime` launches the portable build in `--capture-screens` mode, which
renders each documented route from inside the app, and the provenance manifest it
writes is the single source that docs/Screenshots.md is rendered from.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import zlib
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

# Image name -> (docs/Screenshots.md section title, app route rendered by --capture-screens).
# The route set must equal App.CaptureRoutes in the app (contract-gated in
# tests/native/test_screenshot_pipeline.py), so this tool can never document a render
# the built executable does not actually produce.
RUNTIME_CAPTURE_IMAGES = {
    "runtime-dashboard.png": ("Home", "home"),
    "runtime-checkup.png": ("Checkup", "checkup"),
}

CONCEPT_BLOCKS = {
    "runtime-dashboard.png": (
        "### Original concept\n\n"
        "![Conceptual WinCare dashboard showing system status, health cards, and recent activity]"
        "(images/dashboard-preview.png)"
    ),
    "runtime-checkup.png": (
        "### Original concept\n\n"
        "![Conceptual WinCare system checkup showing a health score and review-before-apply results]"
        "(images/checkup-preview.png)"
    ),
}

RUNTIME_MANIFEST = IMAGES_DIR / "runtime-captures.json"
SCREENSHOTS_DOC = DOCS_DIR / "Screenshots.md"

# Docs-truth fallback: what the checked-in images may claim when no runtime capture
# has been recorded against a built artifact yet. The checked-in images outlive any single
# candidate, so the fallback names no version itself; the current product version is rendered
# from Directory.Build.props where the page needs it.
FALLBACK_STATUS_LINES = {
    "runtime-dashboard.png": (
        "**Capture status:** historical; no runtime capture has been recorded for the current "
        "build yet. Run `--runtime` against a built portable executable to recapture. The "
        "current Home is recommendation-led, derives evidence coverage from shared Activity "
        "records, exposes one primary Checkup CTA, and no longer uses the older "
        "instrument-panel hierarchy."
    ),
    "runtime-checkup.png": (
        "**Capture status:** historical; no runtime capture has been recorded for the current "
        "build yet. Run `--runtime` against a built portable executable to recapture. The "
        "current source reports checked-area evidence rather than a synthetic machine-health "
        "claim. Its fast read-only probes run concurrently with bounded concurrency, while "
        "Windows Update readiness is checked in the background; compact layouts stack below the "
        "shared 920-DIP breakpoint."
    ),
}


def _git_commit() -> str:
    """Short HEAD id, flagged when the working tree is dirty.

    A capture records the tree that rendered its images. When that tree carries
    uncommitted changes the bare HEAD id misattributes them — a reader who checks
    out that commit does not see what the image shows.
    """
    try:
        head = subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"],
            cwd=REPO_ROOT, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, timeout=10,
        ).stdout.strip()
        if not head:
            return "unknown"
        dirty = subprocess.run(
            ["git", "status", "--porcelain"],
            cwd=REPO_ROOT, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, timeout=10,
        ).stdout.split()
        return f"{head}+uncommitted" if dirty else head
    except Exception:
        return "unknown"


# PE machine types this pipeline can capture. The portable build ships one of these.
_PE_MACHINE_TYPES = {
    0x014C: "x64",    # i386 — the .NET host for an x64 portable build
    0x8664: "x64",    # x64
    0x01C0: "ARM64",  # ARM
    0xAA64: "ARM64",  # ARM64
}


def _exe_architecture_from_pe(exe_path: Path) -> str | None:
    """Reads the PE machine type straight out of the executable header.

    Shells out to nothing, so it works on any host and, unlike reading the checkout,
    it describes the artifact actually being captured.
    """
    try:
        with open(exe_path, "rb") as handle:
            if handle.read(2) != b"MZ":
                return None
            handle.seek(0x3C)
            pe_offset = struct.unpack("<I", handle.read(4))[0]
            handle.seek(pe_offset)
            if handle.read(4) != b"PE\0\0":
                return None
            machine = struct.unpack("<H", handle.read(2))[0]
        return _PE_MACHINE_TYPES.get(machine)
    except Exception:
        return None


def _normalize_architecture(value: str | None) -> str | None:
    if not value:
        return None
    lowered = value.strip().lower()
    if "arm64" in lowered or "aarch64" in lowered:
        return "ARM64"
    if "x64" in lowered or "amd64" in lowered or "x86_64" in lowered:
        return "x64"
    return None


def _product_version() -> str:
    import xml.etree.ElementTree as ET

    root = ET.parse(REPO_ROOT / "Directory.Build.props").getroot()
    prefix = root.findtext(".//VersionPrefix") or "0.0.0"
    suffix = root.findtext(".//VersionSuffix") or ""
    return f"{prefix}-{suffix}" if suffix else prefix


def load_runtime_manifest() -> dict | None:
    if not RUNTIME_MANIFEST.is_file():
        return None
    try:
        return json.loads(RUNTIME_MANIFEST.read_text(encoding="utf-8"))
    except Exception:
        return None


def _resolve_provenance(
    exe_path: Path,
    meta: dict,
    checkout_commit: str,
) -> dict:
    """Provenance originates from the artifact, cross-checked against the checkout.

    Version and architecture come from the executable's own sidecar (assembly version and
    RuntimeInformation.ProcessArchitecture), then the PE header independently confirms the
    architecture. A mismatch means the checkout and the artifact disagree — a real condition
    when someone points `--exe` at a build from a different tree — and it fails loudly rather
    than silently recording the checkout's facts next to another build's image.
    """
    exe_version = str(meta.get("version") or "").strip() or None
    exe_arch = _normalize_architecture(str(meta.get("architecture") or ""))
    pe_arch = _exe_architecture_from_pe(exe_path)

    provenance: dict = {
        "version": exe_version or _product_version(),
        "architecture": exe_arch or pe_arch or _normalize_architecture(
            os.environ.get("WINCARE_EXE_ARCH", "")) or "x64",
        "commit": checkout_commit,
    }
    provenance["architecture_checked"] = pe_arch
    provenance["version_source"] = "executable" if exe_version else "directory.build.props"
    return provenance


# Source roots that change what a documented capture depicts. Anything under src/ moves the UI
# or the data it renders; the capture tool itself is included because a pipeline change can
# alter what the images mean. docs/, tests/, and .github/ deliberately are not: they describe
# or verify the images without changing them.
_CAPTURE_AFFECTING_PATHS = (
    "src/",
    "tools/capture_screenshots.py",
)


def _paths_changed_since(commit: str) -> list[str]:
    """Capture-affecting source paths that changed between a recorded commit and HEAD."""
    if not commit or commit == "unknown":
        return []
    try:
        proc = subprocess.run(
            ["git", "diff", "--name-only", f"{commit}..HEAD", "--", *_CAPTURE_AFFECTING_PATHS],
            cwd=REPO_ROOT, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, timeout=20,
        )
    except Exception:
        return []
    return [line.strip() for line in proc.stdout.splitlines() if line.strip()]


def capture_freshness_report(manifest: dict | None) -> dict:
    """Decides whether a checked-in capture still depicts the current source.

    Freshness is decided from provenance metadata, not pixels: the screens render real machine
    data — drive sizes, Windows build numbers, memory amounts — that legitimately differs
    between the capture host and a CI runner, so a pixel diff cannot separate "the UI changed"
    from "the machine differs". The recorded commit, diffed against HEAD over the paths that
    change what a capture depicts, can.
    """
    head = _git_commit()
    if not manifest or not manifest.get("images"):
        return {"status": "unrecorded", "manifest_commit": None, "head": head, "changed": [],
                "detail": "no runtime capture has been recorded for this build yet"}
    manifest_commit = next(iter(manifest["images"].values())).get("commit")
    if not manifest_commit:
        return {"status": "unrecorded", "manifest_commit": None, "head": head, "changed": [],
                "detail": "the manifest records no source commit"}
    changed = _paths_changed_since(manifest_commit)
    if not changed:
        return {"status": "fresh", "manifest_commit": manifest_commit, "head": head, "changed": [],
                "detail": f"no capture-affecting source changed since {manifest_commit}"}
    return {"status": "stale", "manifest_commit": manifest_commit, "head": head, "changed": changed,
            "detail": f"{len(changed)} capture-affecting path(s) changed since {manifest_commit}"}


def capture_runtime_screenshots(exe_path: Path) -> bool:
    """Runs the built portable executable in `--capture-screens` mode (the e2e render path,
    same navigation loop discipline as `--smoke-test`), verifies each PNG, installs it into
    docs/images/, and writes the provenance manifest the documentation is rendered from.
    """
    if not exe_path.is_file():
        print(f"[-] Portable executable not found: {exe_path}", file=sys.stderr)
        return False

    checkout_commit = _git_commit()
    captured_utc = dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds")

    with tempfile.TemporaryDirectory(prefix="wincare-capture-") as tmp:
        cmd = [str(exe_path), "--capture-screens", tmp]
        print(f"Running e2e runtime capture: {' '.join(cmd)}")
        try:
            proc = subprocess.run(cmd, timeout=120)
        except Exception as ex:
            print(f"[-] Capture run failed: {ex}", file=sys.stderr)
            return False
        if proc.returncode != 0:
            print(f"[-] Capture run exited with code {proc.returncode}", file=sys.stderr)
            return False

        meta: dict = {}
        meta_path = Path(tmp) / "capture-meta.json"
        if meta_path.is_file():
            try:
                meta = json.loads(meta_path.read_text(encoding="utf-8"))
            except Exception:
                meta = {}
        appearance = meta.get("appearance", "unknown")
        provenance = _resolve_provenance(exe_path, meta, checkout_commit)

        architecture = provenance["architecture"]
        checked = provenance.get("architecture_checked")
        if checked and checked != architecture:
            print(
                f"[-] Architecture provenance conflict: the executable reports {architecture} "
                f"but its PE header reports {checked}. Refusing to record either.",
                file=sys.stderr,
            )
            return False
        version = provenance["version"]

        images: dict = {}
        for image_name, (_title, route) in RUNTIME_CAPTURE_IMAGES.items():
            rendered = Path(tmp) / f"{route}.png"
            if not rendered.is_file():
                print(f"[-] App did not render {route}.png for {image_name}", file=sys.stderr)
                return False
            if not verify_image_integrity(rendered, check_content=True):
                print(f"[-] Rendered capture failed integrity check: {rendered}", file=sys.stderr)
                return False
            dest = IMAGES_DIR / image_name
            dest.write_bytes(rendered.read_bytes())
            images[image_name] = {
                "route": route,
                "source": "portable",
                "architecture": architecture,
                "version": version,
                "version_source": provenance["version_source"],
                "commit": checkout_commit,
                "captured_utc": captured_utc,
                "appearance": appearance,
                "window_dips": meta.get("windowSizeDips", "unknown"),
                "exe": exe_path.name,
            }
            print(f"[+] Captured {image_name} from {route} (v{version}, {architecture})")

    RUNTIME_MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    RUNTIME_MANIFEST.write_text(
        json.dumps({"captured_utc": captured_utc, "images": images}, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"[+] Wrote provenance manifest: {RUNTIME_MANIFEST.relative_to(REPO_ROOT)}")
    return True


def format_runtime_status_line(image_name: str, manifest: dict | None) -> str:
    if manifest is None:
        return FALLBACK_STATUS_LINES[image_name]
    entry = manifest["images"][image_name]
    date = entry["captured_utc"][:10]
    extra = ""
    if image_name == "runtime-dashboard.png":
        extra = (
            " The Home is recommendation-led, derives evidence coverage from shared Activity "
            "records, and exposes one primary Checkup CTA."
        )
    elif image_name == "runtime-checkup.png":
        extra = (
            " Checkup reports checked-area evidence rather than a synthetic machine-health claim; "
            "fast read-only probes run concurrently while Windows Update readiness is checked in "
            "the background."
        )
    stale = capture_freshness_report(manifest)
    if stale["status"] == "stale":
        extra = (
            f" **Stale:** capture-affecting source changed since commit {stale['manifest_commit']}"
            f" ({len(stale['changed'])} path(s) at {stale['head']}); this image is historical "
            f"evidence until recaptured.{extra}"
        )
    return (
        f"**Capture status:** captured {date} from the v{entry['version']} portable build "
        f"({entry['architecture']}, commit {entry['commit']}, {entry['appearance']} appearance, "
        f"{entry.get('window_dips', 'unknown')} DIP window) by running `--capture-screens` against "
        f"that artifact. Authoritative for that exact build; any later UI change makes it "
        f"historical until recaptured.{extra}"
    )


DOC_INTRO = """\
# Interface screenshots

The runtime images below are **end-to-end captures**: each one is rendered by the app itself
when a built portable executable runs `--capture-screens` (the same in-app navigation loop the
packaged smoke test uses), then installed here by `tools/capture_screenshots.py --runtime`.
They are **runtime evidence for the exact build named in each section**, not a perpetual source
of truth for later source changes. The provenance manifest
[`images/runtime-captures.json`](images/runtime-captures.json) is the source that this page's
capture lines are rendered from, and CI re-checks that sync on every build.
"""

RUNTIME_INTRO_LINES = {
    "runtime-dashboard.png": "### E2E runtime capture",
    "runtime-checkup.png": "### E2E runtime capture",
}
RUNTIME_FALLBACK_CAPTIONS = {
    "runtime-dashboard.png": "### Historical runtime",
    "runtime-checkup.png": "### Historical runtime",
}

DOC_TAIL = """\
## Terminal REPL (Historical Concept)

### Headless Terminal Exploration Prototype

![Historical concept for a WinCare headless terminal REPL](images/tui-preview.png)

*Design concept only. WinCare ships exclusively as a native WinUI 3 desktop shell; the standalone terminal interface was an exploratory prototype and is not part of the active product distribution.*

## Platform Architecture

### C4 Interactive Architecture Model

![WinCare platform C4 interactive architecture diagram and governance pipeline](images/architecture-preview.png)

## Interactive Web Showcase

### Diagnostic Core Showcase

![WinCare interactive web showcase featuring holographic diagnostic topology, live telemetry dials, and tactile inspection](images/showcase-preview.png)

**Interactive experience:** Open [`docs/showcase.html`](showcase.html) in any modern browser for the live holographic diagnostic topology, command simulator, and responsive telemetry panels.

## Capture policy

Capturing requires a Windows host with a built portable executable of the exact version being
recorded; the source tree alone cannot produce runtime evidence. (Installed-MSIX capture is
not yet implemented — only the portable build is supported.) Run:

```text
python tools/capture_screenshots.py --runtime --exe artifacts/portable/win-x64/WinCare.App.exe
```

The tool launches that build in `--capture-screens` mode, verifies every rendered PNG, rewrites `images/runtime-captures.json`, and re-renders this page from the manifest.

Every runtime image must record:

- the exact package/product version;
- architecture (`x64` or `ARM64`);
- the source commit SHA or release tag;
- the packaging source of the image (`portable`; installed MSIX is not yet implemented);
- the Windows appearance used when visually relevant.

Runtime captures must be taken from a known built artifact and kept free of machine names, account names, paths, license keys, tokens, or other personal data. The in-app capture path renders the XAML content surface only, so window chrome and shell titles are never included. Concept imagery must never be presented as a runtime capture.

A screenshot remains authoritative only for the exact build it names. Any UI-affecting change after that build automatically turns the screenshot into **historical runtime evidence** that **needs recapture** and a fresh visual check before it can be cited as current again.
"""


def render_screenshots_doc(manifest: dict | None) -> str:
    """Renders docs/Screenshots.md deterministically from the provenance manifest.

    With a manifest, every runtime image carries its recorded version, architecture,
    commit, appearance, and capture date; without one, the checked-in images keep their
    honest historical label. Static prose is fixed here so the doc, the images, and the
    manifest can never disagree.
    """
    if manifest is None:
        intro = DOC_INTRO.replace(
            "The runtime images below are **end-to-end captures**: each one is rendered by the app itself\n"
            "when a built portable executable runs `--capture-screens` (the same in-app navigation loop the\n"
            "packaged smoke test uses), then installed here by `tools/capture_screenshots.py --runtime`.\n"
            "They are **runtime evidence for the exact build named in each section**, not a perpetual source\n"
            "of truth for later source changes. The provenance manifest\n"
            "[`images/runtime-captures.json`](images/runtime-captures.json) is the source that this page's\n"
            "capture lines are rendered from, and CI re-checks that sync on every build.\n",
            "The checked-in runtime images below are **historical captures**; no e2e runtime "
            "capture has been recorded for the current build (v"
            + _product_version()
            + ") yet.\n"
            "They are runtime evidence for that exact package only, not a perpetual source of truth\n"
            "for later source changes. Run `--runtime` against a built portable executable to replace\n"
            "them and regenerate this page.\n",
        )
    else:
        intro = DOC_INTRO
    parts = [intro]
    for image_name, (title, _route) in RUNTIME_CAPTURE_IMAGES.items():
        entry = (manifest or {}).get("images", {}).get(image_name)
        caption = (
            RUNTIME_INTRO_LINES[image_name]
            if manifest is not None
            else RUNTIME_FALLBACK_CAPTIONS[image_name]
        )
        if entry is not None:
            label = (
                f"WinCare {title} screen captured from the v{entry['version']} portable build "
                f"({entry['architecture']}, commit {entry['commit']})"
            )
        else:
            label = f"WinCare {title} screen captured from the installed v{_product_version()} candidate package"
        parts.append(
            f"## {title}\n\n"
            f"{caption}\n\n"
            f"![{label}](images/{image_name})\n\n"
            f"{format_runtime_status_line(image_name, manifest)}\n\n"
            f"{CONCEPT_BLOCKS[image_name]}\n"
        )
    parts.append(DOC_TAIL)
    return "\n".join(parts)


def sync_screenshots_doc(manifest: dict | None) -> bool:
    """True when docs/Screenshots.md already equals the generator's rendering."""
    rendered = render_screenshots_doc(manifest)
    current = SCREENSHOTS_DOC.read_text(encoding="utf-8") if SCREENSHOTS_DOC.is_file() else None
    if current == rendered:
        return True
    print(
        f"[-] {SCREENSHOTS_DOC.relative_to(REPO_ROOT)} is out of sync with "
        f"{RUNTIME_MANIFEST.relative_to(REPO_ROOT)}; run without --check-doc to regenerate.",
        file=sys.stderr,
    )
    return False


def write_screenshots_doc(manifest: dict | None) -> None:
    SCREENSHOTS_DOC.write_text(render_screenshots_doc(manifest), encoding="utf-8")
    print(f"[+] Regenerated {SCREENSHOTS_DOC.relative_to(REPO_ROOT)} from the capture manifest")


def find_browser_executable() -> str | None:
    """Locates an installed headless-capable Chromium or Edge browser executable on the host system."""
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
    """Renders a source HTML document to a target PNG file using a headless browser instance.

    Removes any pre-existing target file prior to launch, supplies virtual-time and compositor
    flags for deterministic rendering, and asserts process return code 0, file creation,
    size threshold, PNG structural integrity, and content non-emptiness.
    """
    if not source_html.is_file():
        print(f"[-] Source HTML not found: {source_html}", file=sys.stderr)
        return False

    target_png.parent.mkdir(parents=True, exist_ok=True)
    if target_png.is_file():
        try:
            target_png.unlink()
        except OSError:
            pass

    file_url = source_html.resolve().as_uri()

    cmd = [
        browser_path,
        "--headless=new",
        "--hide-scrollbars",
        "--force-device-scale-factor=1",
        "--virtual-time-budget=5000",
        "--run-all-compositor-stages-before-draw",
        "--enable-webgl",
        "--use-gl=angle",
        "--allow-file-access-from-files",
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
        # cmd always carries --headless=new; the only live condition for falling back is a
        # non-zero exit from the newer headless mode.
        if proc.returncode != 0:
            fallback_cmd = [
                browser_path,
                "--headless",
                "--hide-scrollbars",
                "--force-device-scale-factor=1",
                "--virtual-time-budget=5000",
                "--run-all-compositor-stages-before-draw",
                "--enable-webgl",
                f"--window-size={width},{height}",
                f"--screenshot={target_png.resolve()}",
                file_url,
            ]
            proc = subprocess.run(
                fallback_cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                timeout=35,
            )

        if (
            proc.returncode == 0
            and target_png.is_file()
            and verify_image_integrity(target_png, check_content=True)
        ):
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


def verify_image_integrity(image_path: Path, min_bytes: int = 2048, check_content: bool = True) -> bool:
    """Validates structural integrity, chunk stream, CRC32, and visual content of a PNG file.

    Checks file existence, minimum byte size, standard 8-byte PNG magic header, parses
    all chunks (including IHDR and IEND) to verify chunk lengths and chunk CRC values,
    and inspects decompressed IDAT bytes to verify non-empty visual content and color entropy.
    """
    if not image_path.is_file():
        print(f"[-] Missing required screenshot: {image_path}", file=sys.stderr)
        return False

    size = image_path.stat().st_size
    if size < min_bytes:
        print(f"[-] Screenshot too small ({size} bytes): {image_path}", file=sys.stderr)
        return False

    try:
        with open(image_path, "rb") as f:
            signature = f.read(8)
            if signature != b"\x89PNG\r\n\x1a\n":
                print(f"[-] File {image_path.name} does not have valid PNG header", file=sys.stderr)
                return False

            saw_ihdr = False
            saw_iend = False
            ihdr_data = b""
            idat_chunks = []

            while True:
                length_bytes = f.read(4)
                if not length_bytes:
                    break
                if len(length_bytes) < 4:
                    print(f"[-] Truncated chunk length in {image_path.name}", file=sys.stderr)
                    return False

                chunk_len = struct.unpack(">I", length_bytes)[0]
                chunk_type = f.read(4)
                if len(chunk_type) < 4:
                    print(f"[-] Truncated chunk type in {image_path.name}", file=sys.stderr)
                    return False

                if not saw_ihdr:
                    if chunk_type != b"IHDR":
                        print(f"[-] First PNG chunk is not IHDR in {image_path.name}", file=sys.stderr)
                        return False
                    saw_ihdr = True

                chunk_data = f.read(chunk_len)
                if len(chunk_data) != chunk_len:
                    print(f"[-] Truncated chunk data in {image_path.name}", file=sys.stderr)
                    return False

                crc_bytes = f.read(4)
                if len(crc_bytes) < 4:
                    print(f"[-] Missing chunk CRC in {image_path.name}", file=sys.stderr)
                    return False

                expected_crc = struct.unpack(">I", crc_bytes)[0]
                computed_crc = zlib.crc32(chunk_type + chunk_data) & 0xFFFFFFFF
                if computed_crc != expected_crc:
                    print(
                        f"[-] CRC mismatch in chunk {chunk_type.decode('latin1', errors='replace')} in {image_path.name}",
                        file=sys.stderr,
                    )
                    return False

                if chunk_type == b"IHDR":
                    ihdr_data = chunk_data
                elif chunk_type == b"IDAT":
                    idat_chunks.append(chunk_data)
                elif chunk_type == b"IEND":
                    saw_iend = True
                    break

            if not (saw_ihdr and saw_iend):
                print(f"[-] PNG missing IHDR or IEND chunk in {image_path.name}", file=sys.stderr)
                return False

            if check_content and ihdr_data and idat_chunks:
                raw = zlib.decompress(b"".join(idat_chunks))
                width, height, _, color_type = struct.unpack(">IIBB", ihdr_data[:10])
                bpp = 3 if color_type == 2 else 4 if color_type == 6 else 1 if color_type in (0, 3) else 2
                stride = 1 + width * bpp
                expected_min_len = stride * height
                if len(raw) < expected_min_len:
                    print(
                        f"[-] Image data truncated for {image_path.name}: {len(raw)} < {expected_min_len} bytes",
                        file=sys.stderr,
                    )
                    return False

                # Sample pixel colors across grid
                sampled_colors = set()
                step_y = max(1, height // 100)
                step_x = max(1, width // 100)
                for y in range(0, height, step_y):
                    line_start = y * stride + 1
                    for x in range(0, width, step_x):
                        px_start = line_start + x * bpp
                        sampled_colors.add(tuple(raw[px_start:px_start + min(bpp, 3)]))

                if len(sampled_colors) < 30:
                    print(
                        f"[-] Visual content check failed for {image_path.name}: only {len(sampled_colors)} unique sampled colors (solid or blank image)",
                        file=sys.stderr,
                    )
                    return False

                # Target-specific ROI verification for showcase preview canvas
                if "showcase" in image_path.name.lower():
                    canvas_colors = set()
                    for y in range(180, min(620, height), 4):
                        line_start = y * stride + 1
                        for x in range(850, min(1350, width), 4):
                            px_start = line_start + x * bpp
                            canvas_colors.add(tuple(raw[px_start:px_start + min(bpp, 3)]))

                    if len(canvas_colors) < 25:
                        print(
                            f"[-] Visual content check failed for {image_path.name}: telemetry canvas region is empty (only {len(canvas_colors)} distinct colors)",
                            file=sys.stderr,
                        )
                        return False

    except Exception as ex:
        print(f"[-] Failed to read or parse PNG {image_path.name}: {ex}", file=sys.stderr)
        return False

    return True


def run_all_captures(browser_path: str | None = None) -> int:
    """Executes the full screenshot capture and verification workflow for documentation images.

    Requires an available headless browser. If no browser is detected, explicitly reports failure
    rather than validating stale existing images.
    """
    print("=== WinCare Automated Screenshot Capture Pipeline ===")
    IMAGES_DIR.mkdir(parents=True, exist_ok=True)

    browser = browser_path or find_browser_executable()
    if not browser:
        print(
            "[-] Error: No headless browser found on system to perform fresh capture pass.\n"
            "    Install Edge/Chrome or run with --verify-only to validate existing images.",
            file=sys.stderr,
        )
        return 1

    print(f"Using browser executable: {browser}")
    captured_count = 0
    failed_count = 0

    for src, dest, w, h in CAPTURE_TARGETS:
        print(f"Rendering {src.name} -> {dest.name}...")
        if capture_screenshot(browser, src, dest, w, h):
            captured_count += 1
        else:
            failed_count += 1

    if failed_count > 0:
        print(f"[-] Capture failed for {failed_count} targets.", file=sys.stderr)
        return 1

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

    if not sync_screenshots_doc(load_runtime_manifest()):
        return 1
    return 0


def main() -> int:
    """Parses command-line arguments and routes between verify-only and full capture workflows."""
    parser = argparse.ArgumentParser(description="Capture and verify WinCare documentation screenshots.")
    parser.add_argument("--browser", help="Explicit path to browser binary")
    parser.add_argument("--verify-only", action="store_true", help="Only verify existing screenshots without re-rendering")
    parser.add_argument("--runtime", action="store_true", help="Capture runtime images e2e from a built portable executable and regenerate docs/Screenshots.md from the manifest")
    parser.add_argument("--exe", help="Path to the portable WinCare.App.exe to capture from (default: artifacts/portable/win-x64/WinCare.App.exe)")
    parser.add_argument("--check-doc", action="store_true", help="Fail if docs/Screenshots.md is out of sync with the capture manifest")
    parser.add_argument(
        "--check-freshness",
        action="store_true",
        help="Fail if the checked-in capture depicts a source revision behind HEAD on capture-affecting paths",
    )
    args = parser.parse_args()

    if args.check_freshness:
        report = capture_freshness_report(load_runtime_manifest())
        print(f"capture freshness: {report['status']}")
        print(f"  manifest commit: {report['manifest_commit']}")
        print(f"  head:            {report['head']}")
        for path in report["changed"][:25]:
            print(f"  changed:         {path}")
        if len(report["changed"]) > 25:
            print(f"  ... and {len(report['changed']) - 25} more")
        print(f"  {report['detail']}")
        return 0 if report["status"] in ("fresh", "unrecorded") else 1

    if args.verify_only:
        print("--- Verifying Documentation Screenshots (Verify-Only Mode) ---")
        all_targets = [dest for _, dest, _, _ in CAPTURE_TARGETS] + [
            IMAGES_DIR / name for name in REQUIRED_RUNTIME_IMAGES
        ]
        failed = [img for img in all_targets if not verify_image_integrity(img)]
        if failed:
            print(f"[-] {len(failed)} screenshots failed integrity verification.", file=sys.stderr)
            return 1
        if not sync_screenshots_doc(load_runtime_manifest()):
            return 1
        print("[+] All documentation screenshots verified successfully.")
        return 0

    if args.runtime:
        exe = Path(args.exe) if args.exe else REPO_ROOT / "artifacts/portable/win-x64/WinCare.App.exe"
        if not capture_runtime_screenshots(exe):
            return 1
        write_screenshots_doc(load_runtime_manifest())
        return 0

    if args.check_doc:
        return 0 if sync_screenshots_doc(load_runtime_manifest()) else 1

    return run_all_captures(args.browser)


if __name__ == "__main__":
    sys.exit(main())
