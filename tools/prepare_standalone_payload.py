#!/usr/bin/env python3
"""Create a deterministic, fail-closed payload for the WinCare standalone hosts."""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import stat
import tempfile
import zipfile
from pathlib import Path, PurePosixPath

LEGACY = {
    "bin/wincare.launcher.exe",
    "bin/wincare.launcher.dll",
    "bin/wincare.launcher.deps.json",
    "bin/wincare.launcher.runtimeconfig.json",
    "bin/wincare.tuilauncher.exe",
    "bin/wincare.tuilauncher.dll",
    "bin/wincare.tuilauncher.deps.json",
    "bin/wincare.tuilauncher.runtimeconfig.json",
}
META = {
    "sbom.spdx.json",
    "build-receipt.json",
    "release-manifest.sha256",
}
WINDOWS_RESERVED = {
    "con", "prn", "aux", "nul",
    *(f"com{i}" for i in range(1, 10)),
    *(f"lpt{i}" for i in range(1, 10)),
}
MAX_MEMBER_BYTES = 256 * 1024 * 1024
MAX_TOTAL_BYTES = 1024 * 1024 * 1024
MAX_MEMBERS = 20_000
FIXED_EPOCH = 315532800  # 1980-01-01T00:00:00Z, the ZIP minimum.


def digest(data: bytes) -> str:
    """Execute the digest operation with validated inputs."""
    return hashlib.sha256(data).hexdigest()


def safe_segment(segment: str) -> None:
    """Execute the safe segment operation with validated inputs."""
    if not segment or segment in {".", ".."}:
        raise ValueError(f"unsafe ZIP path segment: {segment!r}")
    if segment[-1] in {" ", "."}:
        raise ValueError(f"Windows-ambiguous ZIP path segment: {segment!r}")
    if any(ord(char) < 32 or char in '<>:"|?*' for char in segment):
        raise ValueError(f"unsupported ZIP path character in: {segment!r}")
    stem = segment.split(".", 1)[0].casefold()
    if stem in WINDOWS_RESERVED:
        raise ValueError(f"Windows reserved ZIP path segment: {segment!r}")


def normalize_member(name: str, *, allow_directory: bool = True) -> tuple[str, bool]:
    """Execute the normalize member operation with validated inputs."""
    if not name or "\x00" in name or "\\" in name:
        raise ValueError(f"unsafe ZIP member name: {name!r}")
    is_directory = name.endswith("/")
    candidate = name[:-1] if is_directory else name
    path = PurePosixPath(candidate)
    if path.is_absolute() or not path.parts or any(part in {"", ".", ".."} for part in path.parts):
        raise ValueError(f"unsafe ZIP member path: {name!r}")
    for part in path.parts:
        safe_segment(part)
    normalized = "/".join(path.parts)
    if is_directory:
        if not allow_directory:
            raise ValueError(f"directory member is not allowed: {name!r}")
        normalized += "/"
    return normalized, is_directory


def is_regular_zip_member(info: zipfile.ZipInfo, is_directory: bool) -> bool:
    """Execute the is regular zip member operation with validated inputs."""
    if info.create_system != 3:
        return True
    mode = (info.external_attr >> 16) & 0xFFFF
    if mode == 0:
        return True
    file_type = stat.S_IFMT(mode)
    if is_directory:
        return file_type in {0, stat.S_IFDIR}
    return file_type in {0, stat.S_IFREG}


def read_archive(path: Path) -> tuple[str, dict[str, bytes]]:
    """Execute the read archive operation with validated inputs."""
    files: dict[str, bytes] = {}
    full_names: set[str] = set()
    folded_names: set[str] = set()
    roots: set[str] = set()
    total_bytes = 0

    with zipfile.ZipFile(path) as archive:
        infos = archive.infolist()
        if len(infos) > MAX_MEMBERS:
            raise ValueError("release archive exceeds the member ceiling")
        for info in infos:
            raw_name = getattr(info, "orig_filename", info.filename)
            normalized, is_directory = normalize_member(raw_name)
            folded = normalized.casefold()
            if normalized in full_names or folded in folded_names:
                raise ValueError(f"duplicate or case-colliding ZIP member: {normalized}")
            full_names.add(normalized)
            folded_names.add(folded)
            if not is_regular_zip_member(info, is_directory):
                raise ValueError(f"non-regular ZIP member: {normalized}")
            roots.add(normalized.rstrip("/").split("/", 1)[0])

        if len(roots) != 1:
            raise ValueError("release archive must contain exactly one top-level directory")
        root = next(iter(roots))

        for info in infos:
            raw_name = getattr(info, "orig_filename", info.filename)
            normalized, is_directory = normalize_member(raw_name)
            if is_directory:
                continue
            parts = PurePosixPath(normalized).parts
            if len(parts) < 2 or parts[0] != root:
                raise ValueError(f"archive member is outside the release root: {normalized}")
            relative = "/".join(parts[1:])
            relative, _ = normalize_member(relative, allow_directory=False)
            folded_relative = relative.casefold()
            if folded_relative in META or folded_relative in LEGACY:
                continue
            if info.file_size < 0 or info.file_size > MAX_MEMBER_BYTES:
                raise ValueError(f"archive member exceeds the size ceiling: {relative}")
            data = archive.read(info)
            if len(data) != info.file_size:
                raise ValueError(f"archive member size changed while reading: {relative}")
            total_bytes += len(data)
            if total_bytes > MAX_TOTAL_BYTES:
                raise ValueError("standalone payload exceeds the aggregate size ceiling")
            if relative.casefold() in {key.casefold() for key in files}:
                raise ValueError(f"duplicate or case-colliding payload path: {relative}")
            files[relative] = data

    if not files:
        raise ValueError("standalone payload would be empty")
    return root, files


def payload_manifest(files: dict[str, bytes]) -> bytes:
    """Execute the payload manifest operation with validated inputs."""
    lines = [f"{digest(files[name])}  {name}" for name in sorted(files, key=str.casefold)]
    return ("\n".join(lines) + "\n").encode("ascii")


def zip_timestamp() -> tuple[int, int, int, int, int, int]:
    """Execute the zip timestamp operation with validated inputs."""
    raw = int(os.environ.get("SOURCE_DATE_EPOCH", FIXED_EPOCH))
    raw = max(raw, FIXED_EPOCH)
    value = dt.datetime.fromtimestamp(raw, dt.timezone.utc).replace(microsecond=0)
    second = value.second - (value.second % 2)
    return value.year, value.month, value.day, value.hour, value.minute, second


def write_payload(path: Path, root: str, files: dict[str, bytes]) -> None:
    """Execute the write payload operation with validated inputs."""
    path.parent.mkdir(parents=True, exist_ok=True)
    timestamp = zip_timestamp()
    with tempfile.NamedTemporaryFile(prefix=path.name + ".", suffix=".tmp", dir=path.parent, delete=False) as handle:
        temporary = Path(handle.name)
    try:
        with zipfile.ZipFile(
            temporary,
            "w",
            compression=zipfile.ZIP_DEFLATED,
            compresslevel=9,
            allowZip64=True,
        ) as archive:
            for name in sorted(files, key=str.casefold):
                info = zipfile.ZipInfo(f"{root}/{name}", date_time=timestamp)
                info.create_system = 3
                info.external_attr = (stat.S_IFREG | 0o644) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                info.flag_bits |= 0x800
                archive.writestr(info, files[name], compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def main() -> int:
    """Run the command-line entrypoint and return its exit status."""
    parser = argparse.ArgumentParser()
    parser.add_argument("archive", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    root, files = read_archive(args.archive)
    manifest = payload_manifest(files)
    files["PAYLOAD-MANIFEST.sha256"] = manifest
    write_payload(args.output, root, files)
    payload = args.output.read_bytes()
    result = {
        "schema": "wincare.standalone.payload/v2",
        "status": "passed",
        "sha256": digest(payload),
        "manifestSha256": digest(manifest),
        "members": len(files),
        "bytes": len(payload),
    }
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(json.dumps({"schema": "wincare.standalone.payload/v2", "status": "failed", "error": str(error)}))
        raise SystemExit(1)
