#!/usr/bin/env python3
"""Enforce one bounded HTTP boundary and explicit policy-bound download egress."""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from wincare_ps_source import strip_powershell

SCHEMA = "wincare.network-egress-validation/v2"
BANNED_DIAGNOSTIC_DEFAULTS = (
    "msftconnecttest.com",
    "www.google.com",
    "www.microsoft.com",
    "www.cloudflare.com",
    "1.1.1.1",
    "8.8.8.8",
    "9.9.9.9",
)
HTTP_BROKER = "src/WinCare/Core/04-Http.ps1"
BITS_ADAPTER = "src/WinCare/Providers/100-Downloads.ps1"
DIRECT_HTTP_PATTERNS = {
    "DirectWebRequestCmdlet": re.compile(r"\bInvoke-(?:WebRequest|RestMethod)\b", re.I),
    "DuplicateHttpClient": re.compile(r"\b(?:Net\.)?Http\.HttpClient\b|\bSystem\.Net\.Http\.HttpClient\b", re.I),
    "LegacyWebClient": re.compile(r"\b(?:System\.Net\.)?WebClient\b", re.I),
    "LegacyWebRequestFactory": re.compile(r"\bWebRequest\s*::\s*Create(?:Http)?\b", re.I),
    "ShellHttpClient": re.compile(r"(?im)(?:^|[;&|{}]\s*)\b(?:curl|wget)(?:\.exe)?\b"),
    "BitsAdmin": re.compile(r"\bbitsadmin(?:\.exe)?\b", re.I),
}
BITS_PATTERN = re.compile(r"\bStart-BitsTransfer\b", re.I)


def _scrub_powershell(text: str) -> str:
    """Blank comments, quoted strings, and here-string bodies while preserving lines.

    Delegates to the shared scrubber (tools/wincare_ps_source.py) so every
    validator agrees on exactly what counts as code.
    """
    return strip_powershell(text)


def validate(root: Path) -> dict[str, object]:
    errors: list[dict[str, object] | str] = []
    scanned = 0
    source_root = root / "src/WinCare"
    for path in sorted(source_root.rglob("*.ps1")):
        if path.name.startswith(".wincare-"):
            continue
        relative = path.relative_to(root).as_posix()

        scanned += 1
        original = path.read_text(encoding="utf-8-sig", errors="replace")
        scrubbed = _scrub_powershell(original)
        for endpoint in BANNED_DIAGNOSTIC_DEFAULTS:
            if endpoint.casefold() in original.casefold() and relative != "src/WinCare/Providers/113-NetworkProxyControl.ps1":
                errors.append(f"Silent/public diagnostic endpoint literal in {relative}: {endpoint}")
        for code, pattern in DIRECT_HTTP_PATTERNS.items():
            if relative == HTTP_BROKER and code == "DuplicateHttpClient":
                continue
            for match in pattern.finditer(scrubbed):
                line = scrubbed[: match.start()].count("\n") + 1
                errors.append({"file": relative, "line": line, "code": code})
        for match in BITS_PATTERN.finditer(scrubbed):
            if relative != BITS_ADAPTER:
                line = scrubbed[: match.start()].count("\n") + 1
                errors.append({"file": relative, "line": line, "code": "BitsBoundaryBypass"})

    config = (root / "src/WinCare/Core/01-Config.ps1").read_text(encoding="utf-8-sig")
    if "NetworkExperimentTargets     = @()" not in config:
        errors.append("NetworkExperimentTargets must default to an empty array.")
    network = (root / "src/WinCare/Core/00-04-NetworkExperiments.ps1").read_text(encoding="utf-8-sig")
    if "will not silently probe public services" not in network:
        errors.append("Network experiment planning does not document fail-closed explicit targeting.")
    actions = json.loads((root / "src/WinCare/Data/Gui/actions.json").read_text(encoding="utf-8-sig"))
    measure = next((item for item in actions if item.get("id") == "network-measure"), None)
    if not measure or measure.get("defaultArguments", {}).get("TargetHosts") != []:
        errors.append("GUI network measurement must not ship public default TargetHosts.")
    downloads = (root / BITS_ADAPTER).read_text(encoding="utf-8-sig")
    for required in ("Assert-WinCareServiceUri", "AllowInsecureDownloads", "AllowPrivateDownloadTargets"):
        if required not in downloads:
            errors.append(f"BITS download admission omits {required}.")
    return {
        "schema": SCHEMA,
        "root": str(root.resolve()),
        "filesScanned": scanned,
        "errors": errors,
        "status": "failed" if errors else "passed",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", default=".")
    parser.add_argument("--output")
    args = parser.parse_args()
    report = validate(Path(args.root))
    rendered = json.dumps(report, indent=2)
    print(rendered)
    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(rendered + "\n", encoding="utf-8")
    return 1 if report["status"] == "failed" else 0


if __name__ == "__main__":
    raise SystemExit(main())
