#!/usr/bin/env python3
"""Fail-closed WinCare v3 release verifier."""
from __future__ import annotations
import argparse
import hashlib
import json
import re
import zipfile
from pathlib import Path, PurePosixPath

EXPECTED={'WinCare.exe','WinCare-GUI.exe','WinCare-TUI.exe'}
BUILD='wincare.build.receipt/v3'

def sha(data:bytes)->str:return hashlib.sha256(data).hexdigest()

def validate(path:Path)->dict:
    errors=[]
    try:
        with zipfile.ZipFile(path) as z:
            files={}
            roots={PurePosixPath(i.filename).parts[0] for i in z.infolist() if PurePosixPath(i.filename).parts}
            if len(roots)!=1: errors.append('root mismatch')
            for i in z.infolist():
                p=PurePosixPath(i.filename)
                if len(p.parts)>1 and p.parts[0] in roots and not i.is_dir(): files[str(PurePosixPath(*p.parts[1:]))]=z.read(i)
            for n in EXPECTED:
                if n not in files or len(files[n]) < 20*1024*1024 or not files[n].startswith(b'MZ'): errors.append('invalid standalone '+n)
            receipt=json.loads(files.get('BUILD-RECEIPT.json',b'{}'))
            if receipt.get('schema')!=BUILD: errors.append('invalid receipt schema')
            if any('Launcher' in n or 'TuiLauncher' in n for n in files): errors.append('legacy launcher artifacts')
    except Exception as e: errors.append(str(e))
    return {'schema':'wincare.release.validation/v3','status':'passed' if not errors else 'failed','errors':errors}

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('archive',type=Path);a=p.parse_args();print(json.dumps(validate(a.archive),indent=2));raise SystemExit(0 if validate(a.archive)['status']=='passed' else 1)
