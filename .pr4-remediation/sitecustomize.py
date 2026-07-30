"""Ensure legacy-development SBOM compatibility is applied after the main patch script."""
from __future__ import annotations

import atexit
import runpy
import sys
from pathlib import Path


if Path(sys.argv[0]).name == "apply.py":
    @atexit.register
    def apply_profile_compatibility() -> None:
        """Run the idempotent compatibility patch after the primary transformation succeeds."""
        runpy.run_path(str(Path(__file__).with_name("compat.py")), run_name="__main__")
