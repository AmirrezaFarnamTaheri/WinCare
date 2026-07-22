#!/usr/bin/env python3
from __future__ import annotations
import argparse,csv,json,re,sys,xml.etree.ElementTree as ET
from pathlib import Path

X='{http://schemas.microsoft.com/winfx/2006/xaml}'

def relative_luminance(hex_color:str)->float:
    values=[int(hex_color[i:i+2],16)/255 for i in (1,3,5)]
    values=[v/12.92 if v<=0.04045 else ((v+0.055)/1.055)**2.4 for v in values]
    return 0.2126*values[0]+0.7152*values[1]+0.0722*values[2]

def contrast(a:str,b:str)->float:
    hi,lo=sorted((relative_luminance(a),relative_luminance(b)),reverse=True)
    return (hi+0.05)/(lo+0.05)

def extract_headless(path:Path)->set[str]:
    text=path.read_text('utf-8')
    m=re.search(r'function Get-WinCareHeadlessCommandName\s*\{\s*@\((.*?)\)\s*\}',text,re.S)
    return set(re.findall(r"'([^']+)'",m.group(1))) if m else set()

def main()->int:
    ap=argparse.ArgumentParser();ap.add_argument('root',nargs='?',default='.');ap.add_argument('--output')
    args=ap.parse_args();root=Path(args.root).resolve();errors=[];warnings=[]
    gui=root/'src/WinCare/Data/Gui';xaml_path=gui/'WinCare.MainWindow.xaml';actions_path=gui/'actions.json';nav_path=gui/'navigation.json'
    for p in (xaml_path,actions_path,nav_path,root/'WinCare-GUI.ps1',root/'run-wincare-gui.cmd',root/'tests/V5.Gui.Tests.ps1'):
        if not p.is_file(): errors.append(f'missing V5 GUI artifact: {p.relative_to(root)}')
    names=[];resource_definitions=set();resource_references=set();missing_resources=[];visible_donor_count=None;visible_unique_count=None
    if xaml_path.is_file():
        try:
            tree=ET.parse(xaml_path);names=[v for e in tree.iter() for k,v in e.attrib.items() if k==X+'Name']
            if len(names)!=len(set(names)): errors.append('duplicate x:Name values in GUI XAML')
            required={'MainWindow','NavigationList','DashboardPanel','ActionsPanel','EvidencePanel','SettingsPanel','ConfirmOverlay','BusyOverlay','ActionList','ArgumentsTextBox','ActionOutputTextBox'}
            missing=required-set(names)
            if missing: errors.append(f'missing named GUI controls: {sorted(missing)}')
            if any((X+'Class') in e.attrib for e in tree.iter()): errors.append('GUI XAML must not use x:Class')
            xaml_text=xaml_path.read_text('utf-8')
            for token in ('BackgroundBrush','SurfaceBrush','AccentBrush','TextBrush'):
                if '{DynamicResource '+token+'}' not in xaml_text: errors.append(f'GUI theme token is not dynamically bound: {token}')
            if xaml_text.count('AutomationProperties.Name=') < 15: errors.append('GUI accessibility names are incomplete')
            resource_definitions=set(re.findall(r'x:Key\s*=\s*[\"\']([^\"\']+)[\"\']',xaml_text))
            resource_references=set(re.findall(r'\{(?:StaticResource|DynamicResource)\s+([^},\s]+)',xaml_text))
            missing_resources=sorted(resource_references-resource_definitions)
            if missing_resources: errors.append(f'GUI XAML references undefined resources: {missing_resources}')
            donor_match=re.search(r'x:Name="EvidenceDonorText"\s+Text="(\d+)"',xaml_text)
            unique_match=re.search(r'<TextBlock\s+Text="(\d+) unique baselines"',xaml_text)
            visible_donor_count=int(donor_match.group(1)) if donor_match else None
            visible_unique_count=int(unique_match.group(1)) if unique_match else None
            if visible_donor_count is None or visible_unique_count is None: errors.append('GUI visible evidence metrics are missing')
        except Exception as exc: errors.append(f'invalid GUI XAML: {exc}')
    try: actions=json.loads(actions_path.read_text('utf-8'));nav=json.loads(nav_path.read_text('utf-8'))
    except Exception as exc: actions=[];nav=[];errors.append(f'invalid GUI JSON: {exc}')
    ids=[x.get('id') for x in actions];nav_ids=[x.get('id') for x in nav]
    if len(ids)!=len(set(ids)) or None in ids: errors.append('GUI action IDs are missing or duplicated')
    if len(nav_ids)!=len(set(nav_ids)) or len(nav_ids)<9: errors.append('GUI navigation is incomplete or duplicated')
    commands=extract_headless(root/'src/WinCare/UI/98-Headless.ps1')
    unknown=sorted({x.get('command') for x in actions if x.get('command') not in commands})
    if unknown: errors.append(f'GUI curated actions reference unknown commands: {unknown}')
    gui_code=(root/'src/WinCare/UI/Gui/00-GuiCatalog.ps1').read_text('utf-8',errors='replace') if (root/'src/WinCare/UI/Gui/00-GuiCatalog.ps1').is_file() else ''
    if 'foreach($command in $commands)' not in gui_code: errors.append('GUI does not generate coverage for all headless commands')
    runtime_path=root/'src/WinCare/UI/Gui/10-GuiRuntime.ps1'
    runtime_text=runtime_path.read_text('utf-8',errors='replace') if runtime_path.is_file() else ''
    control_block=re.search(r'function Get-WinCareGuiControlTable.*?\$names=@\((.*?)\)\s*\$table',runtime_text,re.S)
    bound_controls=set(re.findall(r"'([^']+)'",control_block.group(1))) if control_block else set()
    missing_bound_controls=sorted(bound_controls-set(names))
    if not control_block: errors.append('GUI runtime control binding table is missing')
    if missing_bound_controls: errors.append(f'GUI runtime binds controls absent from XAML: {missing_bound_controls}')
    critical=[x for x in actions if x.get('critical')]
    if not critical or any(x.get('risk')!='Critical' for x in critical): errors.append('GUI Critical action labeling is incomplete')
    if "else{'High'}" not in gui_code: errors.append('generated mutating GUI commands are not conservatively labeled High')
    for command in ('pagefile-set','wua-uninstall','wdac-deploy','injection-surface-quarantine','run-automation','group-policy-import','sysmon-uninstall','offline-package-add'):
        if command not in gui_code: errors.append(f'critical generated command is missing from GUI risk map: {command}')
    runtime=runtime_text
    if 'I ACCEPT LEGACY UNSAFE MUTATIONS' not in runtime: errors.append('GUI exact unsafe acknowledgement is missing')
    if re.search(r'Invoke-WinCarePlan\s+-Plan',runtime): errors.append('GUI bypasses the headless/typed authority path')
    for marker,message in (
        ('[Diagnostics.ProcessStartInfo]::new()','GUI command execution does not use ProcessStartInfo'),
        ('$psi.ArgumentList.Add','GUI command execution does not use argument-safe ArgumentList'),
        ('$psi.WorkingDirectory=$root','GUI child process working directory is not pinned to the repository root'),
        ('24576 characters','GUI command argument payload is not explicitly bounded'),
        ("ValidateSet('Normal','HighContrast','Monochrome')",'GUI accessibility theme modes are incomplete'),
        ('[Windows.SystemParameters]::HighContrast','GUI does not honor system high-contrast mode'),
    ):
        if marker not in runtime: errors.append(message)
    pairs=[('#F4F7FB','#0B1020',4.5),('#9AA9C3','#121A2B',4.5),('#F97066','#512128',4.5),('#36C5F0','#173B46',4.5),('#2DD4A8','#173D35',4.5),('#F6C85F','#3D3520',4.5)]
    contrast_results=[]
    for fg,bg,minimum in pairs:
        ratio=contrast(fg,bg);contrast_results.append({'foreground':fg,'background':bg,'ratio':round(ratio,3),'minimum':minimum})
        if ratio<minimum: errors.append(f'GUI contrast {fg} on {bg} is {ratio:.2f}, below {minimum}')
    ledger_path=root/'docs/convergence/adoption-matrix.csv';surface_path=root/'docs/convergence/surface-accountability.csv';inventory_path=root/'docs/convergence/donor-inventory.json'
    rows=list(csv.DictReader(ledger_path.open(encoding='utf-8'))) if ledger_path.is_file() else []
    record_ids={r.get('record_id') for r in rows}
    required_records={'D111-V5-HEALTH-DASHBOARD','D127-V5-COMMAND-WORKBENCH','D127-V5-RISK-COMMUNICATION','D143-V5-CAPABILITY-NAVIGATION','D143-V5-EVIDENCE-EXPLORER'}
    missing_records=required_records-record_ids
    if missing_records: errors.append(f'missing V5 synthesis records: {sorted(missing_records)}')
    for action in actions:
        for source in action.get('sourceRecords',[]):
            if source not in record_ids: errors.append(f"GUI action {action.get('id')} references unknown evidence {source}")
    surface_rows=list(csv.DictReader(surface_path.open(encoding='utf-8'))) if surface_path.is_file() else []
    semantic_refs={x for r in surface_rows for x in (r.get('semantic_record_ids') or '').split(';') if x}
    unlinked=required_records-semantic_refs
    if unlinked: errors.append(f'V5 synthesis records lack donor surface links: {sorted(unlinked)}')
    inventory=json.loads(inventory_path.read_text('utf-8')) if inventory_path.is_file() else []
    donors=inventory if isinstance(inventory,list) else inventory.get('donors',[])
    donor_ids={d.get('donor_id') or d.get('id') or d.get('donor') for d in donors}
    unique_baselines=sum(1 for d in donors if not d.get('duplicate_of'))
    if visible_donor_count is not None and visible_donor_count!=len(donors): errors.append(f'GUI submitted-donor metric is stale: {visible_donor_count} != {len(donors)}')
    if visible_unique_count is not None and visible_unique_count!=unique_baselines: errors.append(f'GUI unique-baseline metric is stale: {visible_unique_count} != {unique_baselines}')
    expected={f'D{i:02d}' if i<100 else f'D{i}' for i in range(1,167)}
    if donor_ids and donor_ids!=expected:
        errors.append(f'donor inventory continuity mismatch: missing={sorted(expected-donor_ids)} extra={sorted(donor_ids-expected)}')
    test_files=sorted((root/'tests').glob('*.ps1'))
    pester_cases=sum(len(re.findall(r"^\s*It\s+['\"]",p.read_text('utf-8',errors='replace'),re.M)) for p in test_files)
    gui_test_path=root/'tests/V5.Gui.Tests.ps1'
    gui_pester_cases=len(re.findall(r"^\s*It\s+['\"]",gui_test_path.read_text('utf-8',errors='replace'),re.M)) if gui_test_path.is_file() else 0
    if len(test_files)<17 or pester_cases<202: errors.append(f'Pester inventory regressed: suites={len(test_files)} cases={pester_cases}')
    if gui_pester_cases<12: errors.append(f'V5 GUI Pester coverage regressed: {gui_pester_cases} cases')
    validation_doc=(root/'VALIDATION.md').read_text('utf-8',errors='replace') if (root/'VALIDATION.md').is_file() else ''
    testing_doc=(root/'docs/Testing.md').read_text('utf-8',errors='replace') if (root/'docs/Testing.md').is_file() else ''
    if f'**{pester_cases} Pester `It` cases**' not in validation_doc: errors.append('VALIDATION.md Pester inventory is stale')
    if f'**{pester_cases}** Pester `It` cases' not in testing_doc: errors.append('docs/Testing.md Pester inventory is stale')
    installer=(root/'Install-WinCare.ps1').read_text('utf-8',errors='replace') if (root/'Install-WinCare.ps1').is_file() else ''
    uninstaller=(root/'Uninstall-WinCare.ps1').read_text('utf-8',errors='replace') if (root/'Uninstall-WinCare.ps1').is_file() else ''
    for marker in ('shortcutBackup','consoleShortcutBackup','restore previous shortcut','restore previous console shortcut'):
        if marker not in installer: errors.append(f'installer shortcut transaction is incomplete: {marker}')
    for marker in ('shortcutBackup','consoleShortcutBackup','restore shortcut','restore console shortcut'):
        if marker not in uninstaller: errors.append(f'uninstaller shortcut transaction is incomplete: {marker}')
    # Cross-session release invariants
    manifest=(root/'src/WinCare/WinCare.psd1').read_text('utf-8',errors='replace')
    module=(root/'src/WinCare/WinCare.psm1').read_text('utf-8',errors='replace')
    if not (re.search(r"ModuleVersion\s*=\s*'1\.0\.0'", manifest) or re.search(r"ModuleVersion\s*=\s*'5\.2\.0'", manifest)) or not (re.search(r"\$script:WinCareVersion\s*=\s*'1\.0\.0'", module) or re.search(r"\$script:WinCareVersion\s*=\s*'5\.2\.0'", module)): errors.append('WinCare version identity mismatch')
    for marker in ('Start-WinCareGui','Start-WinCare','Invoke-WinCareHeadlessCommand'):
        if marker not in manifest: errors.append(f'module manifest does not export {marker}')
    if 'WinCare-GUI.ps1' not in (root/'Install-WinCare.ps1').read_text('utf-8',errors='replace'): errors.append('installer does not create GUI launch path')
    # Ensure current docs identify the product platform.
    readme=(root/'README.md').read_text('utf-8',errors='replace')
    if 'WinCare v1.0.0' not in readme and 'WinCare 5.2' not in readme and 'WinCare' not in readme: errors.append('README does not identify WinCare')
    report={
      'schema':'wincare.v5.completeness/v2','root':'.','status':'passed' if not errors else 'failed',
      'gui':{'namedControls':len(names),'runtimeControlBindings':len(bound_controls),'xamlResourceDefinitions':len(resource_definitions),'xamlResourceReferences':len(resource_references),'missingXamlResources':missing_resources,'visibleSubmittedDonors':visible_donor_count,'visibleUniqueBaselines':visible_unique_count,'navigationItems':len(nav),'curatedActions':len(actions),'headlessCommands':len(commands),'criticalCuratedActions':len(critical),'pesterSuites':len(test_files),'pesterCases':pester_cases,'guiPesterCases':gui_pester_cases,'contrast':contrast_results},
      'convergence':{'submittedDonors':len(donors),'uniqueBaselines':unique_baselines,'semanticRecords':len(rows),'classifiedSurfaces':len(surface_rows),'v5SynthesisRecords':len(required_records)},
      'errors':errors,'warnings':warnings
    }
    payload=json.dumps(report,indent=2);print(payload)
    if args.output: Path(args.output).write_text(payload+'\n',encoding='utf-8')
    return 0 if not errors else 1
if __name__=='__main__': raise SystemExit(main())
