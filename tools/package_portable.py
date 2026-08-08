#!/usr/bin/env python3
"""Create a deterministic, symlink-free portable WinCare ZIP."""

from __future__ import annotations

import argparse
import shutil
import stat
import tempfile
import zipfile
from pathlib import Path

_FIXED_TIMESTAMP = (1980, 1, 1, 0, 0, 0)
_COPY_BUFFER_BYTES = 1024 * 1024


def _archive_files(source: Path) -> list[Path]:
    files: list[Path] = []
    for path in source.rglob("*"):
        if path.is_symlink():
            raise ValueError(f"symbolic link is not allowed in portable packages: {path}")
        if path.is_file():
            files.append(path)
    return sorted(files, key=lambda path: path.relative_to(source).as_posix())


def create_portable_archive(source: Path, output: Path) -> Path:
    """Write a reproducible ZIP and atomically replace *output*.

    Entries are sorted, timestamps are fixed, permissions are normalized, and
    symbolic links are rejected so the archive does not depend on host state.
    """

    source = source.resolve()
    output = output.resolve()
    if not source.is_dir():
        raise FileNotFoundError(f"publish directory not found: {source}")

    output.parent.mkdir(parents=True, exist_ok=True)
    files = _archive_files(source)

    temporary_path: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            prefix=f".{output.name}.", suffix=".tmp", dir=output.parent, delete=False
        ) as temporary:
            temporary_path = Path(temporary.name)

        with zipfile.ZipFile(
            temporary_path,
            mode="w",
            compression=zipfile.ZIP_DEFLATED,
            compresslevel=9,
            strict_timestamps=True,
        ) as archive:
            for path in files:
                relative = path.relative_to(source).as_posix()
                info = zipfile.ZipInfo(relative, date_time=_FIXED_TIMESTAMP)
                info.create_system = 3
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = (stat.S_IFREG | 0o644) << 16
                with path.open("rb") as source_stream, archive.open(info, "w") as destination_stream:
                    shutil.copyfileobj(source_stream, destination_stream, length=_COPY_BUFFER_BYTES)

        temporary_path.replace(output)
        temporary_path = None
        return output
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser(description="Create a deterministic WinCare portable ZIP.")
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    try:
        output = create_portable_archive(args.source, args.output)
    except (FileNotFoundError, ValueError, OSError, zipfile.BadZipFile) as error:
        raise SystemExit(str(error)) from error

    print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
