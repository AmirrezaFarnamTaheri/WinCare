#!/usr/bin/env python3
"""Validate WinCare action contracts, dispatcher bindings, and implementation presence."""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


def strip_powershell(text: str) -> str:
    out: list[str] = []
    i = 0
    state = "code"
    line_start = True
    while i < len(text):
        char = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ""
        if state == "code":
            if char == "<" and nxt == "#":
                state = "block-comment"; out.extend("  "); i += 2; line_start = False; continue
            if char == "#":
                state = "line-comment"; out.append(" "); i += 1; line_start = False; continue
            if char == "@" and nxt in ("'", '"') and (i == 0 or text[i - 1] in " \t\r\n=,("):
                quote = nxt
                line_end = text.find("\n", i + 2)
                if line_end == -1: line_end = len(text)
                if text[i + 2:line_end].strip() == "":
                    state = "here-single" if quote == "'" else "here-double"
                    out.extend("  "); i += 2; line_start = False; continue
            if char == "'":
                state = "single"; out.append(" "); i += 1; line_start = False; continue
            if char == '"':
                state = "double"; out.append(" "); i += 1; line_start = False; continue
            out.append(char)
            line_start = char == "\n"
            i += 1; continue
        if state == "line-comment":
            out.append("\n" if char == "\n" else " ")
            if char == "\n": state = "code"; line_start = True
            i += 1; continue
        if state == "block-comment":
            if char == "#" and nxt == ">":
                state = "code"; out.extend("  "); i += 2; line_start = False
            else:
                out.append("\n" if char == "\n" else " "); line_start = char == "\n"; i += 1
            continue
        if state == "single":
            if char == "'" and nxt == "'": out.extend("  "); i += 2
            elif char == "'": state = "code"; out.append(" "); i += 1
            else: out.append("\n" if char == "\n" else " "); i += 1
            continue
        if state == "double":
            if char == "`" and nxt: out.extend("  "); i += 2
            elif char == '"': state = "code"; out.append(" "); i += 1
            else: out.append("\n" if char == "\n" else " "); i += 1
            continue
        if state in ("here-single", "here-double"):
            terminator = "'@" if state == "here-single" else '"@'
            if (i == 0 or text[i - 1] == "\n") and text.startswith(terminator, i):
                out.extend("  "); i += 2; state = "code"; line_start = False
            else:
                out.append("\n" if char == "\n" else " "); line_start = char == "\n"; i += 1
    return "".join(out)

def balanced_end(code: str, start: int, opening: str = "{", closing: str = "}") -> int:
    depth = 0
    for index in range(start, len(code)):
        if code[index] == opening: depth += 1
        elif code[index] == closing:
            depth -= 1
            if depth == 0: return index + 1
    raise ValueError(f"Unbalanced {opening}{closing} block at offset {start}")


def split_top_level(text: str, delimiter: str = ",") -> list[str]:
    parts: list[str] = []
    start = 0
    depth = 0
    quote = ""
    index = 0
    while index < len(text):
        char = text[index]
        if quote:
            if char == "`" and index + 1 < len(text):
                index += 2
                continue
            if char == quote:
                quote = ""
            index += 1
            continue
        if char in ("'", '"'):
            quote = char
        elif char in "([{":
            depth += 1
        elif char in ")]}":
            depth = max(0, depth - 1)
        elif char == delimiter and depth == 0:
            parts.append(text[start:index])
            start = index + 1
        index += 1
    parts.append(text[start:])
    return parts


def parameter_names(raw_param_block: str) -> list[str]:
    names: list[str] = []
    inner = raw_param_block[1:-1]
    for segment in split_top_level(inner):
        variables = re.findall(r"\$([A-Za-z_][A-Za-z0-9_]*)", strip_powershell(segment))
        if not variables:
            continue
        names.append(variables[0])
        for alias_match in re.finditer(r"(?is)\[\s*Alias\s*\((.*?)\)\s*\]", segment):
            payload = alias_match.group(1)
            quoted = re.findall(r"['\"]([^'\"]+)['\"]", payload)
            if quoted:
                names.extend(quoted)
            else:
                names.extend(item.strip() for item in payload.split(',') if item.strip())
    return names


def parse_functions(root: Path) -> dict[str, dict]:
    functions: dict[str, dict] = {}
    for path in sorted((root / "src" / "WinCare").rglob("*.ps1")):
        text = path.read_text(encoding="utf-8", errors="replace")
        code = strip_powershell(text)
        for match in re.finditer(r"(?im)^\s*function\s+([A-Za-z][A-Za-z0-9_-]*)\s*\{", code):
            opening = code.find("{", match.start())
            end = balanced_end(code, opening)
            body = code[opening + 1:end - 1]
            raw_body = text[opening + 1:end - 1]
            params: list[str] = []
            param_match = re.search(r"(?is)\bparam\s*\(", body)
            if param_match:
                param_opening = body.find("(", param_match.start())
                param_end = balanced_end(body, param_opening, "(", ")")
                params = parameter_names(raw_body[param_opening:param_end])
            name = match.group(1)
            key = name.casefold()
            if key in functions:
                raise ValueError(f"Duplicate function definition: {name}")
            functions[key] = {
                "name": name,
                "path": path.relative_to(root).as_posix(),
                "line": text[:match.start()].count("\n") + 1,
                "params": sorted(set(params), key=str.casefold),
                "body": body,
            }
    return functions


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    errors: list[str] = []
    warnings: list[str] = []

    try:
        functions = parse_functions(root)
    except Exception as exc:
        functions = {}
        errors.append(f"Function parsing failed: {exc}")

    contract_path = root / "src/WinCare/Core/11-ActionContracts.ps1"
    transaction_path = root / "src/WinCare/Core/09-Transactions.ps1"
    contract_text = contract_path.read_text(encoding="utf-8", errors="replace")
    transaction_text = transaction_path.read_text(encoding="utf-8", errors="replace")
    contracts = sorted(set(re.findall(r"Add-Contract\s+'([^']+)'", contract_text)), key=str.casefold)

    dispatcher = functions.get("invoke-wincareactioninternal")
    cases: list[dict] = []
    if not dispatcher:
        errors.append("Invoke-WinCareActionInternal was not found.")
    else:
        raw_match = re.search(r"(?is)function\s+Invoke-WinCareActionInternal\s*\{", transaction_text)
        if not raw_match:
            errors.append("Dispatcher source block was not found.")
        else:
            opening = strip_powershell(transaction_text).find("{", raw_match.start())
            end = balanced_end(strip_powershell(transaction_text), opening)
            raw = transaction_text[opening + 1:end - 1]
            for match in re.finditer(r"(?m)^\s*'([^']+)'\s*\{\s*([A-Za-z][A-Za-z0-9_-]+)(.*?)\}\s*$", raw):
                action_type, handler, arguments = match.group(1), match.group(2), match.group(3)
                passed = sorted(set(re.findall(r"-([A-Za-z][A-Za-z0-9_]*)\b", arguments)), key=str.casefold)
                definition = functions.get(handler.casefold())
                if definition is None:
                    errors.append(f"Dispatcher action {action_type} references missing handler {handler}.")
                    accepted: list[str] = []
                else:
                    accepted = definition["params"]
                    missing = [name for name in passed if name.casefold() not in {item.casefold() for item in accepted}]
                    if missing:
                        errors.append(f"Dispatcher action {action_type} passes unsupported parameter(s) to {handler}: {', '.join(missing)}")
                    body = definition["body"]
                    if re.search(r"(?i)\b(NotImplemented|TODO|FIXME|TBD)\b", body):
                        errors.append(f"Handler {handler} contains a placeholder marker.")
                    if not re.search(r"(?i)\b(New-WinCare(?:Bridge)?Result|Invoke-WinCareBridgeProcess|Start-Process|Get-|Set-|Remove-|New-|Copy-|Move-|Clear-|Enable-|Disable-|Restart-|Stop-)\b", body):
                        warnings.append(f"Handler {handler} has no recognizable evidence or operation token; review manually.")
                cases.append({"type": action_type, "handler": handler, "passed": passed, "accepted": accepted})

    case_types = sorted({case["type"] for case in cases}, key=str.casefold)
    for item in sorted(set(contracts) - set(case_types), key=str.casefold):
        errors.append(f"Action contract has no dispatcher case: {item}")
    for item in sorted(set(case_types) - set(contracts), key=str.casefold):
        errors.append(f"Dispatcher case has no action contract: {item}")

    bridge_paths = sorted((root / "src/WinCare/Core").glob("00-??-*.ps1"), key=lambda item: item.name.casefold())
    bridge_text = "\n".join(path.read_text(encoding="utf-8", errors="replace") for path in bridge_paths)
    for marker in ("NotImplemented", "TODO", "FIXME", "TBD"):
        if re.search(rf"(?i)\b{re.escape(marker)}\b", strip_powershell(bridge_text)):
            errors.append(f"Compatibility bridge subsystem contains placeholder marker: {marker}")

    report = {
        "schema": "wincare.action-binding-validation/v1",
        "status": "failed" if errors else "passed",
        "functions": len(functions),
        "contracts": len(contracts),
        "dispatcherCases": len(cases),
        "errors": errors,
        "warnings": warnings,
        "cases": cases,
    }
    output = args.output.resolve() if args.output else root / "artifacts/action-binding-validation.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: report[key] for key in ("status", "functions", "contracts", "dispatcherCases", "errors", "warnings")}, indent=2))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
