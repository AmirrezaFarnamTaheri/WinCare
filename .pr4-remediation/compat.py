#!/usr/bin/env python3
"""Preserve legacy development SBOM compatibility while enforcing production hashes."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "tools/verify_release.py"


def replace_once(text: str, old: str, new: str, label: str) -> str:
    """Replace one exact fragment or fail closed on source drift."""
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one source fragment, found {count}")
    return text.replace(old, new, 1)


def main() -> None:
    """Apply profile-aware SBOM checksum requirements to the transformed verifier."""
    text = PATH.read_text(encoding="utf-8")
    text = replace_once(
        text,
        "def _validate_sbom(archive: zipfile.ZipFile, relative_infos: dict[str, zipfile.ZipInfo], actual_names: set[str], relative_hashes: dict[str, str], errors: list[str]) -> None:\n",
        "def _validate_sbom(archive: zipfile.ZipFile, relative_infos: dict[str, zipfile.ZipInfo], actual_names: set[str], relative_hashes: dict[str, str], require_hashes: bool, errors: list[str]) -> None:\n",
        "SBOM validation strictness parameter",
    )
    text = replace_once(
        text,
        '            if declared is None:\n                errors.append(f"SBOM checksum is missing or malformed: {name}")\n            elif observed is not None and declared != observed:\n',
        '            if declared is None:\n                if require_hashes:\n                    errors.append(f"SBOM checksum is missing or malformed: {name}")\n            elif observed is not None and declared != observed:\n',
        "profile-aware missing checksum handling",
    )
    text = replace_once(
        text,
        "            _validate_sbom(archive, relative_infos, actual_names, relative_hashes, errors)\n\n            try:\n",
        "            require_sbom_hashes = True\n            try:\n                receipt_preview_info = relative_infos.get(\"BUILD-RECEIPT.json\")\n                if receipt_preview_info is not None:\n                    receipt_preview = json.loads(read_member_bounded(archive, receipt_preview_info, MAX_RECEIPT_BYTES).decode(\"utf-8\"))\n                    if isinstance(receipt_preview, dict):\n                        require_sbom_hashes = (\n                            receipt_preview.get(\"packageProfile\") == \"production\"\n                            or receipt_preview.get(\"schema\") == V3_SCHEMA\n                        )\n            except (UnicodeDecodeError, json.JSONDecodeError, ValueError):\n                require_sbom_hashes = True\n            _validate_sbom(archive, relative_infos, actual_names, relative_hashes, require_sbom_hashes, errors)\n\n            try:\n",
        "profile-aware SBOM validation call",
    )
    PATH.write_text(text, encoding="utf-8", newline="\n")
    compile(text, str(PATH), "exec")
    print("Applied profile-aware SBOM checksum compatibility.")


if __name__ == "__main__":
    main()
