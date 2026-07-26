from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

SCHEMA = "wincare.external-process-validation/v2"
SOURCE_ROOTS = ("src/WinCare/Core", "src/WinCare/Providers", "src/WinCare/UI", "src/WinCare/Host", "src/WinCare/Native", "tools")
ROOT_FILES = ("WinCare.ps1", "WinCare-GUI.ps1", "WinCare-TUI.ps1", "Install-WinCare.ps1", "Uninstall-WinCare.ps1")
PROCESS_BROKER_ALLOWLIST = {
    "src/WinCare/Core/03-Process.ps1",
    "src/WinCare/Providers/123-AutonomousPatchAgent.ps1",
    "src/WinCare/UI/Gui/10-GuiRuntime.ps1",
    "WinCare.ps1",
    "WinCare-GUI.ps1",
    "Install-WinCare.ps1",
    "src/WinCare/Native/Build-WinCareNativePolyglot.ps1",
    "tools/WinCare.Tooling.ps1",
    "tools/Invoke-WindowsValidation.ps1",
}
DIRECT_EXTERNAL = re.compile(r"&\s+(?:\$[A-Za-z_][\w]*\.(?:Source|Path)\b|\([^\r\n)]*Get-Command[^\r\n)]*\)\.Source\b)", re.IGNORECASE)
START_PROCESS = re.compile(r"(?:^|[;{}])\s*Start-Process\b", re.IGNORECASE)
READ_TO_END_ASYNC = re.compile(r"\.ReadToEndAsync\s*\(")
PROCESS_START_INFO = re.compile(r"ProcessStartInfo")


def _strip_comments_and_strings(line: str) -> str:
    line = re.sub(r"#.*$", "", line)
    line = re.sub(r"'(?:''|[^'])*'", "''", line)
    line = re.sub(r'"(?:`.|[^"`])*"', '""', line)
    return line


def _files(root: Path) -> list[Path]:
    result: list[Path] = []
    for relative_root in SOURCE_ROOTS:
        directory = root / relative_root
        if directory.is_dir():
            result.extend(sorted(directory.rglob("*.ps1")))
    result.extend(root / name for name in ROOT_FILES if (root / name).is_file())
    return sorted(set(result))


def validate(root: Path) -> dict[str, object]:
    errors: list[dict[str, object]] = []
    files_checked = 0
    for path in _files(root):
        files_checked += 1
        relative = path.relative_to(root).as_posix()
        text = path.read_text(encoding="utf-8-sig")
        scrubbed_lines = [_strip_comments_and_strings(line) for line in text.splitlines()]
        scrubbed = "\n".join(scrubbed_lines)
        for number, line in enumerate(scrubbed_lines, 1):
            if DIRECT_EXTERNAL.search(line):
                errors.append({"file": relative, "line": number, "code": "DirectExternalCallOperator"})
            if START_PROCESS.search(line):
                errors.append({"file": relative, "line": number, "code": "StartProcessBypass"})
            if READ_TO_END_ASYNC.search(line):
                errors.append({"file": relative, "line": number, "code": "UnboundedAsyncReadToEnd"})
        if PROCESS_START_INFO.search(scrubbed) and relative not in PROCESS_BROKER_ALLOWLIST:
            errors.append({"file": relative, "line": 0, "code": "DuplicateProcessBroker"})
    return {
        "schema": SCHEMA,
        "root": str(root.resolve()),
        "filesChecked": files_checked,
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
