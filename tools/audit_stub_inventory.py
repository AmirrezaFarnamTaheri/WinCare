#!/usr/bin/env python3
"""Fail release-blocking placeholder and synthetic-success patterns in shipped WinCare code."""
from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, asdict
from pathlib import Path

SHIPPED_SUFFIXES = {'.ps1', '.psm1', '.psd1', '.cs', '.py'}
EXCLUDED_PARTS = {'.git', '.code-review-graph', 'artifacts', '__pycache__', '.pytest_cache', '.mypy_cache'}

FORBIDDEN_PATTERNS: tuple[tuple[str, re.Pattern[str]], ...] = (
    ('placeholder-marker', re.compile(r'\b(?:TODO|FIXME|TBD)\b|NotImplementedException', re.I)),
    ('synthetic-self-contained-claim', re.compile(r'100%\s+Self-Inclusive', re.I)),
    ('synthetic-bypass-success', re.compile(r'Bypass\s+Confirmed', re.I)),
    ('synthetic-listener-success', re.compile(r'Active\s+on\s+Port', re.I)),
    ('synthetic-evidence-marker', re.compile(r'(?:Synthetic|Demonstration|Placeholder)(?:Result|Evidence|Success)', re.I)),
    ('forced-positive-default', re.compile(r'(?:Enabled|Available|Secure|Healthy)\s+by\s+default', re.I)),
)

FUNCTION_RE = re.compile(r'(?im)^\s*function\s+([A-Za-z0-9_-]+)\s*\{')
SUCCESS_RE = re.compile(r'New-WinCareResult\b[^\n\r]*-Success\s+\$true\b', re.I)
OBSERVATION_RE = re.compile(
    r'\b(?:Get|Test|Invoke|Set|Add|Remove|Move|Copy|Start|Stop|Restart|New|Write|Read|Import|Export|Measure|Resolve|Wait|Protect|Unprotect)-[A-Za-z0-9_-]+\b'
    r'|\b(?:Get-CimInstance|Get-Counter|Get-ItemProperty|Get-Process|Get-Service|Get-FileHash)\b'
    r'|\b(?:File|Directory|RegistryKey|Process|Socket|TcpClient|UdpClient|HttpClient|ZipArchive|ManagementClass)::?\b'
    r'|\[[A-Za-z0-9_.]+\]::',
    re.I,
)
EVIDENCE_RE = re.compile(r'\b(?:EvidenceType|Observed|Before|After|Sha256|ExitCode|Verification|Verified|Rollback|StatusCode|Bytes|Count)\b', re.I)


@dataclass(frozen=True)
class Finding:
    path: str
    line: int
    rule: str
    detail: str


def line_number(text: str, offset: int) -> int:
    return text.count('\n', 0, offset) + 1


def extract_functions(text: str):
    for match in FUNCTION_RE.finditer(text):
        depth = 1
        i = match.end()
        quote: str | None = None
        here: str | None = None
        while i < len(text) and depth:
            if here:
                end = "'@" if here == "@'" else '"@'
                pos = text.find(end, i)
                if pos < 0:
                    i = len(text)
                    break
                i = pos + 2
                here = None
                continue
            ch = text[i]
            nxt = text[i:i+2]
            if quote:
                if ch == '`':
                    i += 2
                    continue
                if ch == quote:
                    if quote == "'" and i + 1 < len(text) and text[i+1] == "'":
                        i += 2
                        continue
                    quote = None
                i += 1
                continue
            if nxt in ("@'", '@"') and (i == 0 or text[i-1] in '\r\n'):
                here = nxt
                i += 2
                continue
            if ch in ("'", '"'):
                quote = ch
            elif ch == '#':
                newline = text.find('\n', i)
                i = len(text) if newline < 0 else newline + 1
                continue
            elif ch == '{':
                depth += 1
            elif ch == '}':
                depth -= 1
            i += 1
        yield match.group(1), match.start(), text[match.end():max(match.end(), i - 1)]


def shipped_files(root: Path):
    roots = [root / 'src', root / 'Install-WinCare.ps1', root / 'Uninstall-WinCare.ps1', root / 'WinCare.ps1', root / 'WinCare-GUI.ps1', root / 'WinCare-TUI.ps1']
    for item in roots:
        if item.is_file():
            yield item
        elif item.is_dir():
            for path in item.rglob('*'):
                if path.is_file() and path.suffix.lower() in SHIPPED_SUFFIXES and not any(part in EXCLUDED_PARTS for part in path.parts):
                    yield path


def audit(root: Path) -> dict[str, object]:
    findings: list[Finding] = []
    files = sorted(set(shipped_files(root)))
    functions_checked = 0
    action_functions_checked = 0
    for path in files:
        try:
            text = path.read_text(encoding='utf-8-sig')
        except UnicodeDecodeError:
            continue
        relative = path.relative_to(root).as_posix()
        for rule, pattern in FORBIDDEN_PATTERNS:
            for match in pattern.finditer(text):
                findings.append(Finding(relative, line_number(text, match.start()), rule, match.group(0)))
        if path.suffix.lower() not in {'.ps1', '.psm1'}:
            continue
        for name, start, body in extract_functions(text):
            functions_checked += 1
            if not re.match(r'^Invoke-WinCare.*Action$', name, re.I):
                continue
            action_functions_checked += 1
            if not SUCCESS_RE.search(body):
                continue
            # A successful mutator must demonstrate observed execution and return evidence.
            without_result = SUCCESS_RE.sub('', body)
            if not OBSERVATION_RE.search(without_result) or not EVIDENCE_RE.search(body):
                findings.append(Finding(
                    relative,
                    line_number(text, start),
                    'literal-success-action',
                    f'{name} returns literal success without both observable execution and evidence-bearing output.',
                ))
    unique = sorted({(f.path, f.line, f.rule, f.detail) for f in findings})
    records = [Finding(*row) for row in unique]
    return {
        'schema': 'wincare.stub-inventory/v1',
        'root': str(root),
        'filesChecked': len(files),
        'functionsChecked': functions_checked,
        'actionFunctionsChecked': action_functions_checked,
        'findings': [asdict(item) for item in records],
        'status': 'passed' if not records else 'failed',
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('root', nargs='?', type=Path, default=Path.cwd())
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    report = audit(root)
    rendered = json.dumps(report, indent=2)
    print(rendered)
    if args.output:
        output = args.output if args.output.is_absolute() else root / args.output
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(rendered + '\n', encoding='utf-8')
    return 0 if report['status'] == 'passed' else 1


if __name__ == '__main__':
    sys.exit(main())
