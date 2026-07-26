#!/usr/bin/env python3
"""Validate deterministic Pester source-string assertions against current source.

This does not execute Pester. It catches only assertions whose target and regex can
be resolved statically, and resolves reused variables to the latest preceding
assignment so stale checks are not confused with later assignments.
"""
from __future__ import annotations
import argparse, json, re
from pathlib import Path

PSEXT={'.ps1','.psm1'}

def function_bodies(root:Path):
    out={}
    for p in sorted(root.rglob('*')):
        if not p.is_file() or p.suffix.lower() not in PSEXT or '.git' in p.parts: continue
        t=p.read_text('utf-8',errors='replace')
        for m in re.finditer(r'(?im)^\s*function\s+([A-Za-z][\w-]*)\s*\{',t):
            oi=t.find('{',m.start()); depth=0; state='code'; i=oi
            while i<len(t):
                c=t[i]; n=t[i+1] if i+1<len(t) else ''
                if state=='code':
                    if c=='#': state='block' if n=='<' else 'line'; i+=2 if n=='<' else 1; continue
                    if c=="'": state='single'
                    elif c=='"': state='double'
                    elif c=='{': depth+=1
                    elif c=='}':
                        depth-=1
                        if depth==0: i+=1; break
                elif state=='line':
                    if c=='\n': state='code'
                elif state=='block':
                    if c=='#' and n=='>': state='code'; i+=2; continue
                elif state=='single':
                    if c=="'":
                        if n=="'": i+=2; continue
                        state='code'
                elif state=='double':
                    if c=='`': i+=2; continue
                    if c=='"': state='code'
                i+=1
            out[m.group(1).lower()]=(t[m.start():i],str(p.relative_to(root)),t[:m.start()].count('\n')+1)
    return out

def regex_pass(actual:str, pattern:str, negative:bool):
    try: matched=bool(re.search(pattern,actual,re.I|re.M))
    except re.error: return None
    return (not matched) if negative else matched

def main():
    ap=argparse.ArgumentParser();ap.add_argument('root',nargs='?',default='.');ap.add_argument('--output')
    args=ap.parse_args();root=Path(args.root).resolve(); funcs=function_bodies(root)
    checked=[]; failures=[]
    for tp in sorted((root/'tests').rglob('*.Tests.ps1')):
        t=tp.read_text('utf-8',errors='replace')
        assignments=[]
        for m in re.finditer(r"(?im)\$(\w+)\s*=\s*Get-Content\s*\(Join-Path\s+\$(?:root|script:WinCareModuleRoot)\s+'([^']+)'\)\s+-Raw",t):
            rel=m.group(2).replace('\\','/'); p=root/rel
            if p.exists(): assignments.append((m.start(),m.group(1).lower(),p.read_text('utf-8',errors='replace'),rel))
        for m in re.finditer(r"(?im)\$(\w+)\s*=\s*\(Get-Command\s+([A-Za-z][\w-]+)\)\.ScriptBlock\.ToString\(\)",t):
            f=funcs.get(m.group(2).lower())
            if f: assignments.append((m.start(),m.group(1).lower(),f[0],f'{f[1]}:{f[2]}'))
        assignments.sort()
        assertions=[]
        for m in re.finditer(r"(?im)\$(\w+)\s*\|\s*Should\s+(Not\s+)?-?Match\s+(['\"])(.*?)\3",t):
            assertions.append((m.start(),m.group(1).lower(),bool(m.group(2)),m.group(4)))
        for pos,var,neg,pat in assertions:
            candidates=[a for a in assignments if a[0]<pos and a[1]==var]
            if not candidates: continue
            _,_,actual,target=candidates[-1]
            passed=regex_pass(actual,pat,neg)
            if passed is None: continue
            rec={'test':str(tp.relative_to(root)),'testLine':t[:pos].count('\n')+1,'target':target,'kind':'NotMatch' if neg else 'Match','pattern':pat,'passed':passed}
            checked.append(rec)
            if not passed: failures.append(rec)
        for m in re.finditer(r"(?im)\(Get-Command\s+([A-Za-z][\w-]+)\)\.ScriptBlock\.ToString\(\)\s*\|\s*Should\s+(Not\s+)?-?Match\s+(['\"])(.*?)\3",t):
            f=funcs.get(m.group(1).lower())
            if not f: continue
            neg=bool(m.group(2)); pat=m.group(4); passed=regex_pass(f[0],pat,neg)
            if passed is None: continue
            rec={'test':str(tp.relative_to(root)),'testLine':t[:m.start()].count('\n')+1,'target':f'{f[1]}:{f[2]}','kind':'NotMatch' if neg else 'Match','pattern':pat,'passed':passed}
            checked.append(rec)
            if not passed: failures.append(rec)
    report={'schema':'wincare.test-source-assertions/v1','root':'.','checked':len(checked),'failures':failures,'status':'passed' if not failures else 'failed'}
    text=json.dumps(report,indent=2); print(text)
    if args.output: Path(args.output).write_text(text+'\n',encoding='utf-8')
    return 0 if not failures else 1
if __name__=='__main__': raise SystemExit(main())
