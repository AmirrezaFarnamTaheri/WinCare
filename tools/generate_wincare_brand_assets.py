#!/usr/bin/env python3
"""Generate and verify WinCare's deterministic visual identity assets.

The generator intentionally uses only the Python standard library so the
committed SVG, PNG, and multi-resolution ICO can be reproduced on every
supported build host without downloading design tooling or fonts.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import struct
import tempfile
import zlib
from pathlib import Path
from typing import Iterable

PALETTE = {
    "ink": "#0B1026",
    "deep_blue": "#123A72",
    "blue": "#2F80ED",
    "cyan": "#34D6E9",
    "mint": "#68E0B5",
    "white": "#FFFFFF",
}
ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)
MAX_ASSET_BYTES = 16 * 1024 * 1024


def _inside_rounded_square(x: float, y: float, half: float = 0.92, radius: float = 0.25) -> bool:
    ax, ay = abs(x), abs(y)
    inner = half - radius
    if ax <= inner and ay <= half:
        return True
    if ay <= inner and ax <= half:
        return True
    return (ax - inner) ** 2 + (ay - inner) ** 2 <= radius**2


def _inside_polygon(x: float, y: float, points: Iterable[tuple[float, float]]) -> bool:
    vertices = tuple(points)
    inside = False
    previous = len(vertices) - 1
    for current, (xi, yi) in enumerate(vertices):
        xj, yj = vertices[previous]
        crossing = (yi > y) != (yj > y)
        if crossing:
            denominator = yj - yi
            boundary = (xj - xi) * (y - yi) / denominator + xi
            if x < boundary:
                inside = not inside
        previous = current
    return inside


def _segment_distance(
    x: float,
    y: float,
    start: tuple[float, float],
    end: tuple[float, float],
) -> float:
    sx, sy = start
    ex, ey = end
    dx, dy = ex - sx, ey - sy
    length_squared = dx * dx + dy * dy
    if length_squared == 0:
        return math.hypot(x - sx, y - sy)
    projection = max(0.0, min(1.0, ((x - sx) * dx + (y - sy) * dy) / length_squared))
    closest_x = sx + projection * dx
    closest_y = sy + projection * dy
    return math.hypot(x - closest_x, y - closest_y)


def _mix(first: tuple[int, int, int], second: tuple[int, int, int], amount: float) -> tuple[int, int, int]:
    clamped = max(0.0, min(1.0, amount))
    return tuple(round(a + (b - a) * clamped) for a, b in zip(first, second))


def _sample_mark(x: float, y: float, size: int) -> tuple[int, int, int, int]:
    if not _inside_rounded_square(x, y):
        return (0, 0, 0, 0)

    ink = (11, 16, 38)
    deep = (18, 58, 114)
    background = _mix(deep, ink, (y + 1.0) / 2.0)
    color = (*background, 255)

    shield = ((-0.57, -0.50), (0.57, -0.50), (0.52, 0.18), (0.0, 0.69), (-0.52, 0.18))
    if _inside_polygon(x, y, shield):
        cyan = (52, 214, 233)
        blue = (47, 128, 237)
        shield_color = _mix(cyan, blue, max(0.0, min(1.0, (y + 0.50) / 1.19)))
        color = (*shield_color, 255)

    w_points = ((-0.38, -0.09), (-0.19, 0.31), (0.0, -0.01), (0.19, 0.31), (0.38, -0.09))
    width = 0.105 if size <= 24 else 0.090
    if any(_segment_distance(x, y, start, end) <= width for start, end in zip(w_points, w_points[1:])):
        color = (255, 255, 255, 255)

    if size >= 24:
        mint = (104, 224, 181)
        sparkle_center = (0.50, -0.50)
        sparkle = min(
            _segment_distance(x, y, (sparkle_center[0] - 0.11, sparkle_center[1]), (sparkle_center[0] + 0.11, sparkle_center[1])),
            _segment_distance(x, y, (sparkle_center[0], sparkle_center[1] - 0.11), (sparkle_center[0], sparkle_center[1] + 0.11)),
        )
        if sparkle <= 0.035:
            color = (*mint, 255)

    return color


def render_rgba(size: int, supersample: int = 4) -> bytes:
    """Render the mark into an antialiased RGBA raster."""
    if size < 1 or size > 1024:
        raise ValueError("size must be between 1 and 1024")
    samples = supersample * supersample
    output = bytearray(size * size * 4)
    offset = 0
    for row in range(size):
        for column in range(size):
            totals = [0, 0, 0, 0]
            for sy in range(supersample):
                for sx in range(supersample):
                    px = ((column + (sx + 0.5) / supersample) / size) * 2.0 - 1.0
                    py = ((row + (sy + 0.5) / supersample) / size) * 2.0 - 1.0
                    sample = _sample_mark(px, py, size)
                    for channel, value in enumerate(sample):
                        totals[channel] += value
            for value in totals:
                output[offset] = round(value / samples)
                offset += 1
    return bytes(output)


def _png_chunk(kind: bytes, payload: bytes) -> bytes:
    checksum = zlib.crc32(kind)
    checksum = zlib.crc32(payload, checksum) & 0xFFFFFFFF
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", checksum)


def encode_png(size: int, rgba: bytes) -> bytes:
    """Encode an RGBA raster as a deterministic PNG."""
    expected = size * size * 4
    if len(rgba) != expected:
        raise ValueError(f"RGBA payload length must be {expected}")
    stride = size * 4
    scanlines = bytearray((stride + 1) * size)
    target = 0
    for row in range(size):
        scanlines[target] = 0
        target += 1
        start = row * stride
        scanlines[target : target + stride] = rgba[start : start + stride]
        target += stride
    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + _png_chunk(b"IHDR", header) + _png_chunk(
        b"IDAT", zlib.compress(bytes(scanlines), level=9)
    ) + _png_chunk(b"IEND", b"")


def build_ico() -> bytes:
    """Build a PNG-backed ICO containing all required Windows resolutions."""
    frames = [(size, encode_png(size, render_rgba(size))) for size in ICON_SIZES]
    header = struct.pack("<HHH", 0, 1, len(frames))
    directory = bytearray()
    payload = bytearray()
    offset = len(header) + 16 * len(frames)
    for size, frame in frames:
        encoded_size = 0 if size == 256 else size
        directory.extend(struct.pack("<BBBBHHII", encoded_size, encoded_size, 0, 0, 1, 32, len(frame), offset))
        payload.extend(frame)
        offset += len(frame)
    return header + bytes(directory) + bytes(payload)


def mark_svg() -> bytes:
    """Return the accessible vector master for the app mark."""
    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-labelledby="title desc">
  <title id="title">WinCare logo</title>
  <desc id="desc">A rounded navy app tile containing a blue care shield and a white W mark.</desc>
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="{PALETTE['deep_blue']}"/>
      <stop offset="1" stop-color="{PALETTE['ink']}"/>
    </linearGradient>
    <linearGradient id="shield" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="{PALETTE['cyan']}"/>
      <stop offset="1" stop-color="{PALETTE['blue']}"/>
    </linearGradient>
  </defs>
  <rect x="20" y="20" width="472" height="472" rx="112" fill="url(#tile)"/>
  <path d="M110 128h292l-13 174c-3 42-34 76-133 143-99-67-130-101-133-143z" fill="url(#shield)"/>
  <path d="M158 230l49 103 49-82 49 82 49-103" fill="none" stroke="{PALETTE['white']}" stroke-width="43" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M369 102h54M396 75v54" stroke="{PALETTE['mint']}" stroke-width="18" stroke-linecap="round"/>
</svg>
"""
    return svg.encode("utf-8")


def wordmark_svg() -> bytes:
    """Return a horizontal logo lockup suitable for docs and installers."""
    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1280 320" role="img" aria-labelledby="title desc">
  <title id="title">WinCare wordmark</title>
  <desc id="desc">The WinCare shield mark followed by the WinCare product name.</desc>
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{PALETTE['deep_blue']}"/><stop offset="1" stop-color="{PALETTE['ink']}"/></linearGradient>
    <linearGradient id="shield" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{PALETTE['cyan']}"/><stop offset="1" stop-color="{PALETTE['blue']}"/></linearGradient>
  </defs>
  <g transform="translate(20 20) scale(.546875)">
    <rect x="20" y="20" width="472" height="472" rx="112" fill="url(#tile)"/>
    <path d="M110 128h292l-13 174c-3 42-34 76-133 143-99-67-130-101-133-143z" fill="url(#shield)"/>
    <path d="M158 230l49 103 49-82 49 82 49-103" fill="none" stroke="{PALETTE['white']}" stroke-width="43" stroke-linecap="round" stroke-linejoin="round"/>
    <path d="M369 102h54M396 75v54" stroke="{PALETTE['mint']}" stroke-width="18" stroke-linecap="round"/>
  </g>
  <text x="350" y="215" fill="{PALETTE['ink']}" font-family="Segoe UI, Inter, Arial, sans-serif" font-size="168" font-weight="700" letter-spacing="-5">WinCare</text>
</svg>
"""
    return svg.encode("utf-8")


def _brand_guide() -> bytes:
    text = f"""# WinCare visual identity

The WinCare mark combines a protective shield with a high-legibility `W` to communicate dependable Windows care without imitating Microsoft's Windows trademark.

## Canonical assets

- `WinCare-Logo.svg`: accessible vector master for the app mark.
- `WinCare-Wordmark.svg`: horizontal product lockup for documentation and installer surfaces.
- `WinCare-Logo-512.png`: transparent high-resolution raster preview.
- `WinCare.ico`: deterministic multi-resolution Windows application icon.
- `WinCare-Brand.manifest.json`: SHA-256 identity manifest for release evidence.

## Palette

- Ink: `{PALETTE['ink']}`
- Deep blue: `{PALETTE['deep_blue']}`
- Blue: `{PALETTE['blue']}`
- Cyan: `{PALETTE['cyan']}`
- Mint: `{PALETTE['mint']}`
- White: `{PALETTE['white']}`

Do not stretch, recolor, rotate, add effects, or place the mark on a background that prevents the rounded tile silhouette from remaining clear. Use the icon at 16 px or larger and the wordmark at 120 px wide or larger.
"""
    return text.encode("utf-8")


def build_assets() -> dict[str, bytes]:
    """Build every canonical brand asset and its closed identity manifest."""
    assets = {
        "design/WinCare-Logo.svg": mark_svg(),
        "design/WinCare-Wordmark.svg": wordmark_svg(),
        "design/WinCare-Logo-512.png": encode_png(512, render_rgba(512)),
        "design/WinCare.ico": build_ico(),
        "design/BRAND.md": _brand_guide(),
    }
    manifest = {
        "schema": "wincare.brand.identity/v1",
        "product": "WinCare",
        "palette": PALETTE,
        "iconSizes": list(ICON_SIZES),
        "assets": [
            {
                "path": path,
                "bytes": len(payload),
                "sha256": hashlib.sha256(payload).hexdigest(),
            }
            for path, payload in sorted(assets.items(), key=lambda item: item[0].casefold())
        ],
    }
    assets["design/WinCare-Brand.manifest.json"] = (
        json.dumps(manifest, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    ).encode("utf-8")
    return assets


def _read_bounded(path: Path, maximum_bytes: int = MAX_ASSET_BYTES) -> bytes:
    metadata = path.stat(follow_symlinks=False)
    if not path.is_file() or path.is_symlink() or metadata.st_size > maximum_bytes:
        raise ValueError(f"unsafe or oversized brand asset: {path}")
    with path.open("rb") as handle:
        payload = handle.read(maximum_bytes + 1)
    after = path.stat(follow_symlinks=False)
    if len(payload) > maximum_bytes or len(payload) != metadata.st_size:
        raise ValueError(f"brand asset size changed while reading: {path}")
    if (metadata.st_size, metadata.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError(f"brand asset changed while reading: {path}")
    return payload


def write_assets(root: Path) -> None:
    """Atomically write the deterministic assets beneath a trusted repository root."""
    root = root.resolve()
    for relative, payload in build_assets().items():
        destination = (root / relative).resolve()
        if root not in destination.parents:
            raise ValueError(f"asset path escapes repository root: {relative}")
        destination.parent.mkdir(parents=True, exist_ok=True)
        descriptor, temporary_name = tempfile.mkstemp(prefix=destination.name + ".", suffix=".tmp", dir=destination.parent)
        try:
            with os.fdopen(descriptor, "wb") as handle:
                handle.write(payload)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary_name, destination)
        finally:
            if os.path.exists(temporary_name):
                os.unlink(temporary_name)


def check_assets(root: Path) -> None:
    """Verify that committed assets match a fresh deterministic generation."""
    root = root.resolve()
    errors: list[str] = []
    for relative, expected in build_assets().items():
        path = root / relative
        if not path.exists():
            errors.append(f"missing: {relative}")
            continue
        observed = _read_bounded(path)
        if observed != expected:
            errors.append(f"content mismatch: {relative}")
    if errors:
        raise ValueError("brand verification failed: " + "; ".join(errors))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--write", action="store_true")
    mode.add_argument("--check", action="store_true")
    arguments = parser.parse_args()
    if arguments.write:
        write_assets(arguments.root)
    else:
        check_assets(arguments.root)
    print(json.dumps({"schema": "wincare.brand.generator/v1", "status": "passed", "root": str(arguments.root.resolve())}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
