#!/usr/bin/env python3
from __future__ import annotations
import argparse,csv,json,re,sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from wincare_ps_source import strip_powershell

PSEXT={'.ps1','.psm1','.psd1'}

def strip_ps(text:str)->str:
    # Delegates to the shared, here-string-aware scrubber (tools/wincare_ps_source.py).
    # The previous inline state machine had no here-string handling: a single
    # quote anywhere inside an @'...'@ block (common in embedded help/example
    # text) prematurely ended the "single-quoted string" state, silently
    # dropping every function definition that followed from the scanned output.
    return strip_powershell(text)

def check_balance(path:Path,text:str,errors:list[str]):
    stripped=strip_ps(text);stack=[];pairs={')':'(',']':'[','}':'{'}
    for idx,c in enumerate(stripped):
        if c in '([{': stack.append((c,idx))
        elif c in ')]}':
            if not stack or stack[-1][0]!=pairs[c]:
                errors.append(f'{path}: delimiter mismatch near line {text[:idx].count(chr(10))+1}')
                return
            stack.pop()
    if stack:
        errors.append(f'{path}: unclosed delimiter near line {text[:stack[-1][1]].count(chr(10))+1}')

def main()->int:
    ap=argparse.ArgumentParser();ap.add_argument('root',nargs='?',default='.');ap.add_argument('--json-output')
    args=ap.parse_args();root=Path(args.root).resolve();errors=[];warnings=[]
    files=sorted(p for p in root.rglob('*') if p.is_file() and p.suffix.lower() in PSEXT and '.git' not in p.parts)
    funcs={};refs={}
    for p in files:
        text=p.read_text('utf-8-sig',errors='replace');check_balance(p,text,errors);code=strip_ps(text)
        for m in re.finditer(r'(?im)^\s*function\s+([A-Za-z][A-Za-z0-9_-]*)',code): funcs.setdefault(m.group(1).lower(),[]).append((p,text[:m.start()].count('\n')+1,m.group(1)))
        for m in re.finditer(r'(?<![-\w])([A-Za-z][A-Za-z0-9]*-WinCare[A-Za-z0-9_-]*)\b',code): refs.setdefault(m.group(1).lower(),[]).append((p,text[:m.start()].count('\n')+1,m.group(1)))
        if p.name not in {'Invoke-StaticChecks.ps1'}:
            for pat,label in [(r'(?i)\bInvoke-Expression\b|(?<![\w-])iex(?![\w-])','dynamic evaluation'),(r'(?i)\bcmd(?:\.exe)?\s+/c\b','shell string execution'),(r'(?i)\bTODO\b|\bFIXME\b|\bTBD\b|notimplemented','placeholder marker')]:
                for m in re.finditer(pat,code): errors.append(f'{p}:{text[:m.start()].count(chr(10))+1}: {label}')
    for name,defs in funcs.items():
        if len(defs)>1: errors.append('duplicate function '+name+': '+', '.join(f'{p}:{line}' for p,line,_ in defs))
    for name,uses in refs.items():
        if name not in funcs: errors.append('unresolved WinCare function '+uses[0][2]+': '+', '.join(f'{p}:{line}' for p,line,_ in uses[:8]))
    contract=(root/'src/WinCare/Core/11-ActionContracts.ps1').read_text('utf-8-sig')
    transaction=(root/'src/WinCare/Core/09-Transactions.ps1').read_text('utf-8-sig')
    contracts=set(re.findall(r"Add-Contract\s+'([^']+)'",contract));dispatch=set(re.findall(r"(?m)^\s*'([^']+)'\s*\{Invoke-WinCare",transaction))
    if contracts!=dispatch: errors.append(f'action parity mismatch: missing dispatch {sorted(contracts-dispatch)}; missing contracts {sorted(dispatch-contracts)}')
    literal_actions=set()
    for p in files:
        text=p.read_text('utf-8-sig',errors='replace')
        literal_actions.update(re.findall(r"New-WinCareAction\s+[^\r\n]*?-Type\s+(?:'([^']+)'|\"([^\"]+)\"|([A-Za-z][A-Za-z0-9_-]*))",text))
    literal_action_names={next(value for value in group if value) for group in literal_actions}
    unknown_literal_actions=literal_action_names-contracts
    if unknown_literal_actions: errors.append(f'literal action constructors without contracts: {sorted(unknown_literal_actions)}')
    headless=(root/'src/WinCare/UI/98-Headless.ps1').read_text('utf-8-sig')
    advanced_headless=(root/'src/WinCare/UI/97-AdvancedCapabilities.ps1').read_text('utf-8-sig')
    list_match=re.search(r'function Get-WinCareHeadlessCommandName\s*\{\s*@\((.*?)\)\s*\}',headless,re.S)
    advanced_list_match=re.search(r'function Get-WinCareAdvancedHeadlessCommandName\s*\{\s*@\((.*?)\)\s*\}',advanced_headless,re.S)
    listed=set(re.findall(r"'([^']+)'",list_match.group(1))) if list_match else set()
    advanced_listed=set(re.findall(r"'([^']+)'",advanced_list_match.group(1))) if advanced_list_match else set()
    cases=set(re.findall(r"(?m)^\s*'([^']+)'\s*\{",headless))
    advanced_cases=set(re.findall(r"(?m)^\s*'([^']+)'\s*\{",advanced_headless))
    listed|=advanced_listed
    cases|=advanced_cases
    if listed!=cases: errors.append(f'headless declaration/case mismatch: missing cases {sorted(listed-cases)}; unlisted cases {sorted(cases-listed)}')
    main=(root/'src/WinCare/UI/99-Main.ps1').read_text('utf-8-sig');palette=(root/'src/WinCare/UI/97-CommandPalette.ps1').read_text('utf-8-sig')
    menu=set(re.findall(r"Action='([^']+)'",main));routes=set(re.findall(r"'([^']+)'\s*\{Show-WinCare",palette));route_special={'Palette'}
    missing_routes=(menu-{'Exit'})-(routes|route_special)
    if missing_routes: errors.append(f'menu actions without routes: {sorted(missing_routes)}')
    for p in sorted(root.rglob('*.json')):
        if '.git' in p.parts: continue
        try: json.loads(p.read_text('utf-8-sig'))
        except Exception as e: errors.append(f'{p}: invalid JSON: {e}')
    psm=(root/'src/WinCare/WinCare.psm1').read_text('utf-8-sig');psd=(root/'src/WinCare/WinCare.psd1').read_text('utf-8-sig')
    manifest_versions=re.findall(r"ModuleVersion\s*=\s*'([^']+)'",psd)
    if len(manifest_versions)!=1:
        errors.append(f'module manifest must contain exactly one ModuleVersion: {manifest_versions}')
        product_version=None
    else:
        product_version=manifest_versions[0]
        if re.search(r"WinCareVersion\s*=\s*'[^']+'",psm):
            errors.append('WinCare.psm1 hardcodes a product version instead of deriving it from module metadata')
        if '$ExecutionContext.SessionState.Module.Version' not in psm:
            errors.append('WinCare.psm1 does not derive its runtime version from the imported module manifest')
        metadata_checks={
            root/'README.md': '# WinCare',
            root/'CHANGELOG.md': f'## {product_version}',
            root/'docs/Architecture.md': '# Architecture',
        }
        for metadata_path,marker in metadata_checks.items():
            if not metadata_path.is_file(): errors.append(f'missing release metadata file: {metadata_path.relative_to(root)}')
            elif marker not in metadata_path.read_text('utf-8-sig',errors='replace'):
                errors.append(f'{metadata_path.relative_to(root)} is missing current metadata marker {marker!r}')
        stale_product_pattern=re.compile(r'(?i)\bWinCare\s+v?\d+\.\d+(?:\.\d+)?\b')
        for candidate in root.rglob('*'):
            if not candidate.is_file() or candidate.suffix.lower() not in {'.md','.ps1','.psm1','.psd1','.xaml','.json','.py','.yml','.yaml'}:
                continue
            if candidate.name in {'BUILD-RECEIPT.json','SBOM.spdx.json'}:
                continue
            candidate_text=candidate.read_text('utf-8-sig',errors='replace')
            match=stale_product_pattern.search(candidate_text)
            if match:
                errors.append(f'{candidate.relative_to(root)} contains a hardcoded product display version: {match.group(0)!r}')
        ci_path=root/'.github/workflows/ci.yml'
        if not ci_path.is_file(): errors.append('missing Windows/packaging CI workflow')
        else:
            ci_text=ci_path.read_text('utf-8-sig',errors='replace')
            hardcoded=set(re.findall(r'WinCare-(\d+\.\d+\.\d+)\.zip',ci_text))
            if hardcoded and hardcoded!={product_version}: errors.append(f'CI contains stale hardcoded release versions: {sorted(hardcoded)}')
            if 'Invoke-WindowsValidation.ps1' not in ci_text or 'tools/verify_release.py' not in ci_text:
                errors.append('CI does not include Windows-native and independent archive validation gates')
    # T2.1: psm1 now uses Export-ModuleMember -Function * (wildcard).
    # In wildcard mode the psm1 carries no redundant explicit list; the psd1
    # FunctionsToExport array is the sole contract. Skip the psm-vs-psd diff
    # and validate only the psd1 list against the actual function definitions.
    psm_wildcard=bool(re.search(r'Export-ModuleMember\s+-Function\s+\*',psm))
    exports=set() if psm_wildcard else set(re.findall(r"(?m)^\s*'([A-Za-z][A-Za-z0-9_-]+)'[,]?\s*$",psm[psm.find('Export-ModuleMember'):]))
    psd_export_match=re.search(r'FunctionsToExport\s*=\s*@\((.*?)\)\s*CmdletsToExport',psd,re.S)
    psd_exports=set(re.findall(r"'([A-Za-z][A-Za-z0-9_-]+)'",psd_export_match.group(1))) if psd_export_match else set()
    if not psd_export_match: errors.append('module manifest FunctionsToExport block was not found')
    elif not psm_wildcard and exports!=psd_exports: errors.append(f'module export mismatch: psm-only {sorted(exports-psd_exports)}; psd-only {sorted(psd_exports-exports)}')
    if 'Invoke-WinCarePlan' in exports or 'Invoke-WinCarePlan' in psd_exports: errors.append('unrestricted internal plan executor must not be exported')
    # Use psd1 as the contract in both explicit and wildcard modes.
    contract_exports=psd_exports
    missing_defs=sorted(x for x in contract_exports if x.lower() not in funcs)
    if missing_defs: errors.append(f'exports without functions: {missing_defs}')
    proc=(root/'src/WinCare/Core/03-Process.ps1').read_text('utf-8-sig')
    if 'Get-WinCareActionContractTable' not in proc or 'HMACSHA256' not in proc or 'FixedTimeEquals' not in proc: errors.append('elevation broker is not contract-derived and authenticated')
    wdac=(root/'src/WinCare/Providers/76-WDAC.ps1').read_text('utf-8-sig')
    if re.search(r'Copy-Item.*CodeIntegrity|Remove-Item.*CodeIntegrity',wdac,re.I): errors.append('WDAC provider writes policy directories directly')
    for p in files:
        text=p.read_text('utf-8-sig',errors='replace')
        emits_network = bool(re.search(r'(?i)\b(?:Invoke-RestMethod|Invoke-WebRequest|System\.Net\.HttpClient|WebClient)\b', text))
        telemetry_context = bool(re.search(r'(?i)\b(?:analytics|telemetry)\b', text))
        explicit_boundary = 'WINCARE_NETWORK_BOUNDARY: explicit-user-invocation' in text
        if emits_network and telemetry_context and not explicit_boundary:
            errors.append(f'{p}: telemetry/network emission lacks an explicit user-invocation boundary')
    report={'root':'.','powershellFiles':len(files),'functions':len(funcs),'references':len(refs),'actionContracts':len(contracts),'headlessCommands':len(listed),'menuActions':len(menu),'errors':errors,'warnings':warnings,'status':'passed' if not errors else 'failed'}
    payload=json.dumps(report,indent=2)
    print(payload)
    if args.json_output:
        p = Path(args.json_output)
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(payload+'\n',encoding='utf-8')
    return 0 if not errors else 1
if __name__=='__main__': raise SystemExit(main())
