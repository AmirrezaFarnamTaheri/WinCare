#!/usr/bin/env python3
"""Create the verified runtime payload embedded into standalone WinCare hosts."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
import zipfile
from pathlib import Path, PurePosixPath

LEGACY = {'bin/WinCare.Launcher.exe','bin/WinCare.Launcher.dll','bin/WinCare.Launcher.deps.json','bin/WinCare.Launcher.runtimeconfig.json','bin/WinCare.TuiLauncher.exe','bin/WinCare.TuiLauncher.dll','bin/WinCare.TuiLauncher.deps.json','bin/WinCare.TuiLauncher.runtimeconfig.json'}
META = {'SBOM.spdx.json','BUILD-RECEIPT.json','RELEASE-MANIFEST.sha256'}

def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

def manifest(files: dict[str, bytes]) -> bytes:
    return ('\n'.join(f'{digest(files[k])}  {k}' for k in sorted(files, key=str.casefold))+'\n').encode('ascii')

def main() -> int:
    parser=argparse.ArgumentParser()
    parser.add_argument('archive',type=Path)
    parser.add_argument('output',type=Path)
    args=parser.parse_args()
    files={}
    with zipfile.ZipFile(args.archive) as z:
        roots={PurePosixPath(i.filename).parts[0] for i in z.infolist()}
        if len(roots)!=1: raise SystemExit('archive root mismatch')
        root=next(iter(roots))
        for i in z.infolist():
            p=PurePosixPath(i.filename)
            if len(p.parts)<2 or p.parts[0]!=root: continue
            name=str(PurePosixPath(*p.parts[1:]))
            if name in META or name in LEGACY: continue
            if not i.is_dir(): files[name]=z.read(i)
    files['PAYLOAD-MANIFEST.sha256']=manifest(files)
    with zipfile.ZipFile(args.output,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for name,data in sorted(files.items(),key=lambda x:x[0].casefold()):
            z.writestr(f'{root}/{name}',data)
    print(json.dumps({'schema':'wincare.standalone.payload/v1','sha256':digest(args.output.read_bytes()),'members':len(files)}))
    return 0
if __name__=='__main__':
    raise SystemExit(main())
