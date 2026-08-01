#!/usr/bin/env python3
"""Validate named parameters on WinCare calls across runtime source and public entry points."""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from validate_action_bindings import parse_functions, strip_powershell

COMMON_PARAMETERS = {
    'verbose','debug','erroraction','warningaction','informationaction','progressaction',
    'errorvariable','warningvariable','informationvariable','outvariable','outbuffer',
    'pipelinevariable','whatif','confirm',
}
CALL_RE = re.compile(r'(?<![A-Za-z0-9_-])((?:Get|Set|New|Invoke|Import|Export|Start|Stop|Remove|Restore|Reset|Test|Measure|ConvertTo|ConvertFrom|Request|Initialize|Complete|Save|Open|Close|Write|Read)-WinCare[A-Za-z0-9_-]+)\b')
PARAM_RE = re.compile(r'(?<![A-Za-z0-9_])-([A-Z][A-Za-z0-9_]*)\b')
FUNCTION_DEF_RE = re.compile(r'(?im)^\s*function\s+([A-Za-z][A-Za-z0-9_-]*)\s*\{')


def call_files(root: Path):
    fixed = [
        root / 'WinCare.ps1',
        root / 'WinCare-GUI.ps1',
        root / 'WinCare-TUI.ps1',
        root / 'Install-WinCare.ps1',
        root / 'Uninstall-WinCare.ps1',
    ]
    seen: set[Path] = set()
    for path in fixed:
        if path.is_file():
            resolved = path.resolve()
            if resolved not in seen:
                seen.add(resolved)
                yield path
    source_root = root / 'src' / 'WinCare'
    for directory in ('Core', 'Providers', 'Host', 'Install', 'UI'):
        for path in sorted((source_root / directory).rglob('*.ps1')):
            if path.name.startswith('.wincare-'):
                continue
            resolved = path.resolve()
            if resolved not in seen:
                seen.add(resolved)
                yield path



def statement_tail(code: str, start: int) -> str:
    end = start
    depth = 0
    while end < len(code):
        char = code[end]
        if char in '({[':
            depth += 1
        elif char in ')}]':
            if depth == 0:
                break
            depth -= 1
        elif depth == 0 and char in ';|\r\n':
            prefix = code[start:end].rstrip()
            if char in '\r\n' and prefix.endswith('`'):
                end += 1
                continue
            break
        end += 1
    return code[start:end]


def direct_parameters(tail: str) -> tuple[list[str], list[str]]:
    parameters: dict[str, str] = {}
    counts: dict[str, int] = {}
    depth = 0
    index = 0
    while index < len(tail):
        char = tail[index]
        if char in '({[':
            depth += 1
            index += 1
            continue
        if char in ')}]':
            depth = max(0, depth - 1)
            index += 1
            continue
        if depth == 0 and char == '-' and (index == 0 or not (tail[index - 1].isalnum() or tail[index - 1] == '_')):
            match = re.match(r'-([A-Z][A-Za-z0-9_]*)\b', tail[index:])
            if match:
                name = match.group(1)
                key = name.casefold()
                parameters.setdefault(key, name)
                counts[key] = counts.get(key, 0) + 1
                index += len(match.group(0))
                continue
        index += 1
    unique = sorted(parameters.values(), key=str.casefold)
    duplicates = sorted(
        (parameters[key] for key, count in counts.items() if count > 1),
        key=str.casefold,
    )
    return unique, duplicates


def validate(root: Path) -> dict[str, object]:
    functions = parse_functions(root)
    errors: list[dict[str, object]] = []
    calls = 0
    checked_parameters = 0
    for path in call_files(root):
        text = path.read_text(encoding='utf-8-sig', errors='replace')
        code = strip_powershell(text)
        local_functions = {item.casefold() for item in FUNCTION_DEF_RE.findall(code)}
        for match in CALL_RE.finditer(code):
            # Ignore the function declaration itself.
            preceding = code[max(0, match.start() - 16):match.start()]
            if re.search(r'function\s*$', preceding, re.I):
                continue
            name = match.group(1)
            definition = functions.get(name.casefold())
            if definition is None and name.casefold() in local_functions:
                continue
            if definition is None:
                errors.append({
                    'path': path.relative_to(root).as_posix(),
                    'line': code[:match.start()].count('\n') + 1,
                    'command': name,
                    'error': 'missing-function',
                })
                continue
            calls += 1
            tail = statement_tail(code, match.end())
            passed, duplicates = direct_parameters(tail)
            checked_parameters += len(passed)
            if duplicates:
                errors.append({
                    'path': path.relative_to(root).as_posix(),
                    'line': code[:match.start()].count('\n') + 1,
                    'command': name,
                    'duplicateParameters': duplicates,
                })
            accepted = {item.casefold() for item in definition['params']} | COMMON_PARAMETERS
            unsupported = [item for item in passed if item.casefold() not in accepted]
            if unsupported:
                errors.append({
                    'path': path.relative_to(root).as_posix(),
                    'line': code[:match.start()].count('\n') + 1,
                    'command': name,
                    'unsupportedParameters': unsupported,
                    'acceptedParameters': definition['params'],
                })
    return {
        'schema': 'wincare.public-call-bindings/v1',
        'root': str(root),
        'filesChecked': len(list(call_files(root))),
        'callsChecked': calls,
        'namedParametersChecked': checked_parameters,
        'errors': errors,
        'status': 'passed' if not errors else 'failed',
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('root', nargs='?', type=Path, default=Path.cwd())
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    report = validate(root)
    rendered = json.dumps(report, indent=2)
    print(rendered)
    if args.output:
        output = args.output if args.output.is_absolute() else root / args.output
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(rendered + '\n', encoding='utf-8')
    return 0 if report['status'] == 'passed' else 1


if __name__ == '__main__':
    sys.exit(main())
