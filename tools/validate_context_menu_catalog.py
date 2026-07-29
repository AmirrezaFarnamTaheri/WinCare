#!/usr/bin/env python3
"""Validate context-menu registry commands without executing them."""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

SHELL_EVAL_FLAGS = re.compile(r"(?i)(?:^|\s)-(?:command|encodedcommand|ec)(?:\s|$)")
# cmd shell placeholders and the /c /k switches are case-insensitive on Windows, so plain
# "cmd /c" (no .exe) and mixed-case forms must be caught as readily as "cmd.exe /C".
DANGEROUS_TOKENS = re.compile(r"(?i)\b(?:invoke-expression|iex|cmd(?:\.exe)?\s+/[ck])\b")
PLACEHOLDERS = ("%1", "%V", "%L", "%*")


def validate(root: Path) -> dict[str, object]:
    path = root / "src/WinCare/Data/Catalog/context-menu-rules.json"
    errors: list[str] = []
    commands: list[dict[str, str]] = []
    try:
        document = json.loads(path.read_text(encoding="utf-8-sig"))
    except Exception as exc:
        return {
            "schema": "wincare.context-menu-catalog-validation/v1",
            "status": "failed",
            "errors": [f"Catalog could not be parsed: {exc}"],
            "commands": [],
        }

    if document.get("schemaVersion") != 1:
        errors.append("Unsupported context-menu catalog schemaVersion.")
    rules = document.get("rules")
    if not isinstance(rules, list) or not rules:
        errors.append("Context-menu catalog must contain a non-empty rules array.")
        rules = []

    seen_ids: set[str] = set()
    for rule in rules:
        rule_id = str(rule.get("id", ""))
        if not rule_id:
            errors.append("Context-menu rule is missing an id.")
        elif rule_id.casefold() in seen_ids:
            errors.append(f"Duplicate context-menu rule id: {rule_id}")
        else:
            seen_ids.add(rule_id.casefold())
        for tree in rule.get("trees", []):
            registry_path = str(tree.get("path", ""))
            for value in tree.get("values", []):
                if value.get("name", "") != "":
                    continue
                command = value.get("value")
                if not isinstance(command, str):
                    continue
                commands.append({"rule": rule_id, "registryPath": registry_path, "command": command})
                if "\x00" in command or "\r" in command or "\n" in command:
                    errors.append(f"{rule_id}: registry command contains a control character.")
                if SHELL_EVAL_FLAGS.search(command):
                    errors.append(f"{rule_id}: registry command invokes a PowerShell command string.")
                if DANGEROUS_TOKENS.search(command):
                    errors.append(f"{rule_id}: registry command contains a shell-evaluation primitive.")
                folded_command = command.casefold()
                for placeholder in PLACEHOLDERS:
                    folded_placeholder = placeholder.casefold()
                    if folded_placeholder in folded_command and f'"{folded_placeholder}"' not in folded_command:
                        errors.append(f"{rule_id}: placeholder {placeholder} is not double-quoted.")
                if command.lower().startswith(("pwsh.exe ", "powershell.exe ")):
                    if "-WorkingDirectory" in command and not re.search(
                        r'(?i)-WorkingDirectory\s+"%(?:1|V|L)"', command
                    ):
                        errors.append(f"{rule_id}: PowerShell working directory is not a quoted shell placeholder.")

    return {
        "schema": "wincare.context-menu-catalog-validation/v1",
        "status": "passed" if not errors else "failed",
        "catalog": str(path.relative_to(root)),
        "rules": len(rules),
        "commands": commands,
        "errors": errors,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    report = validate(root)
    rendered = json.dumps(report, indent=2)
    print(rendered)
    if args.output:
        output = args.output if args.output.is_absolute() else root / args.output
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(rendered + "\n", encoding="utf-8")
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
