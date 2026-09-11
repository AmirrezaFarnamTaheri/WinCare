"""Read-only source inventory. Does not execute catalog commands."""
import csv
import json
import re
import subprocess
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent

def write_csv(name, rows):
    if not rows:
        return
    with (OUT / name).open('w', newline='', encoding='utf-8-sig') as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

tracked = subprocess.check_output(['git', 'ls-files'], cwd=ROOT, text=True).splitlines()
files = []
for relative in tracked:
    p = ROOT / relative
    kind = ('runtime' if relative.startswith('src/') else
            'native-runtime/tests' if relative.startswith('native/') else
            'tests' if relative.startswith('tests/') else
            'tooling' if relative.startswith('tools/') else
            'historical-oracle' if relative.startswith('migration/') else
            'CI' if relative.startswith('.github/') else
            'documentation/design/configuration')
    files.append(dict(path=relative, classification=kind, bytes=p.stat().st_size,
                      inspected='inventory only; see coverage ledger for depth'))
write_csv('repository-inventory.csv', files)

sources = {p: (ROOT / p).read_text(encoding='utf-8-sig') for p in tracked
           if p.endswith(('.cs', '.rs', '.xaml', '.py', '.js'))}
executor_files = {p: s for p, s in sources.items() if '/Commands/WindowsCommandExecutor' in p}
executor = sources['src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs']
catalog = json.loads((ROOT / 'src/WinCare.CommandCatalog/Data/commands.json').read_text())['commands']
commands = []
for c in catalog:
    route = re.search(r'^\s*"' + re.escape(c['id']) + r'"\s*=>\s*(.*)', executor, re.M)
    line = executor[:route.start()].count('\n') + 1 if route else ''
    expression = route.group(1).strip() if route else 'NO SWITCH ROUTE FOUND'
    method = re.search(r'(\w+)\(', expression)
    implementations = []
    if method:
        for p, s in executor_files.items():
            m = re.search(r'\b(?:private|public|internal)\s+[^\n=;]+\b' + re.escape(method[1]) + r'\s*\(', s)
            if m:
                implementations.append(f'{p}:{s[:m.start()].count(chr(10))+1}')
    mentions = [p for p, s in sources.items() if p.startswith('tests/') and ('"' + c['id'] + '"') in s]
    commands.append(dict(id=c['id'],title=c['title'],area=c['area'],section=c['section'],
                         read_only=c['readOnly'],risk=c['risk'],administrator=c['administratorAccess'],
                         catalog_status=c['migrationStatus'],entry='All Tools / ExecuteSelectedToolCommand',
                         route_line=line,route=expression,implementation='; '.join(implementations),
                         tests_mentioning_id='; '.join(mentions),
                         runtime_status='unverified; a route/test mention is not behavior verification'))
write_csv('command-matrix.csv', commands)

controls=[]
interactive={'Button','HyperlinkButton','ToggleSwitch','CheckBox','ComboBox','TextBox','AutoSuggestBox',
             'SelectorBarItem','NavigationViewItem','KeyboardAccelerator','NumberBox','Slider','RadioButton'}
for p,s in sources.items():
    if not p.endswith('.xaml') or not p.startswith('src/WinCare.App/'):
        continue
    tree=ET.fromstring(s)
    for el in tree.iter():
        tag=el.tag.split('}')[-1]
        if tag not in interactive:
            continue
        a={k.split('}')[-1]:v for k,v in el.attrib.items()}
        controls.append(dict(file=p,type=tag,name=a.get('Name',''),
          automation_id=a.get('AutomationProperties.AutomationId',''),
          label=a.get('AutomationProperties.Name',a.get('Content',a.get('Text',a.get('Header','')))),
          handler=a.get('Click',a.get('Invoked',a.get('QuerySubmitted',a.get('SelectionChanged',a.get('TextChanged',''))))),
          command=a.get('Command',''),value_binding=a.get('SelectedItem',a.get('IsChecked',a.get('IsOn',a.get('Text','')))),
          enabled=a.get('IsEnabled','default'),runtime_status='unverified; source inventoried'))
write_csv('control-inventory.csv',controls)
summary={'tracked_files':len(files),'commands':len(commands),'xaml_controls':len(controls),
         'read_only_commands':sum(c['readOnly'] for c in catalog),
         'migration_statuses':{k:sum(c['migrationStatus']==k for c in catalog) for k in sorted({c['migrationStatus'] for c in catalog})}}
(OUT/'evidence'/'inventory-summary.json').write_text(json.dumps(summary,indent=2))
print(json.dumps(summary,indent=2))
