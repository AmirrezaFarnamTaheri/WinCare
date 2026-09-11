"""Validate audit document consistency without running product experiments."""
import csv
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[1]
findings = json.loads((OUT / 'findings.json').read_text(encoding='utf-8'))
summary = json.loads((OUT / 'command-coverage-summary.json').read_text(encoding='utf-8'))
with (OUT / 'command-matrix.csv').open(encoding='utf-8-sig', newline='') as stream:
    commands = list(csv.DictReader(stream))
report = (OUT / 'report.md').read_text(encoding='utf-8')
assert [f['id'] for f in findings] == [f'F-{i:03}' for i in range(1, 41)]
assert {s: sum(f['severity'] == s for f in findings) for s in ('High', 'Medium', 'Low')} == {'High': 13, 'Medium': 25, 'Low': 2}
assert len(commands) == summary['total_commands'] == 259
assert sum(c['individual_command_executed_by_audit'] == 'yes' for c in commands) == 11
assert summary['read_only_not_executed'] == 144 and summary['mutating_total'] == 104
assert len(summary['shared_helper_source_impacted_not_executed']) == 9
for row in commands:
    if row['id'] in summary['shared_helper_source_impacted_not_executed']:
        assert row['individual_command_executed_by_audit'] == 'no'
        assert 'NOT individual-command runtime failure' in row['evidence_class']
assert re.findall(r'^## ([A-Z]+)\.', report, re.M) == list('ABCDEFGHIJKLMNOPQRSTUVWXYZ') + ['AA', 'AB', 'AC', 'AD', 'AE', 'AF']
assert '{{COUNTS}}' not in report
assert 'Forensic scope correction' in report
assert 'Superseded historical review' in (ROOT / 'docs/review-2026-09-11.md').read_text(encoding='utf-8')
for finding in findings:
    assert finding['id'] in report
    assert (ROOT / re.sub(r':\d+$', '', finding['source'])).is_file(), finding['source']
    assert set(re.findall(r'F-\d{3}', finding['related'])) <= {f['id'] for f in findings}
for document in OUT.glob('*.md'):
    for target in re.findall(r'\]\(([^)]+)\)', document.read_text(encoding='utf-8')):
        if re.match(r'^[A-Za-z]:/', target):
            assert Path(re.sub(r':\d+$', '', target)).exists(), (document.name, target)
generated = ['report.md', 'findings.json', 'command-matrix.csv', 'command-coverage-summary.json']
before = {name: hashlib.sha256((OUT / name).read_bytes()).hexdigest() for name in generated}
subprocess.run([sys.executable, str(OUT / 'build_report.py')], cwd=ROOT, check=True, capture_output=True)
after = {name: hashlib.sha256((OUT / name).read_bytes()).hexdigest() for name in generated}
assert before == after, 'Regeneration changed finalized outputs'
print('PASS: findings, coverage, references, A-AF sections, source links and regeneration consistency.')
