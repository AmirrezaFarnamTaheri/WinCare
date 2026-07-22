#!/usr/bin/env python3
"""Append deterministic D164-D166 evidence to the cumulative WinCare convergence graph."""
from __future__ import annotations
import csv, hashlib, json, os
from collections import Counter
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DOCS=ROOT/'docs/convergence'
EVIDENCE=ROOT/'evidence'
SRC=Path(os.environ.get('WINCARE_WAVE11_DONOR_ROOT','/mnt/data/wincare_520_donors'))
AUDIT=Path(os.environ.get('WINCARE_WAVE11_ARCHIVE_MANIFEST','/mnt/data/wincare_520_donors/archive-audit.json'))
TEST='tests/Wave11.CleanerPreview.Tests.ps1'
LEDGER_COLUMNS=['record_id','parent_record_id','composition_group_id','donor','domain','value_unit','source_granularity','value_form','separability','donor_path','donor_sha256','donor_symbol','transformation','mapping_topology','disposition','decision_rationale','target_capability','target_nodes','invariant','negative_invariant','test_node','operator_surface','migration_impact','license_note','risk_tier','dependency_record_ids','evidence_confidence','validation_status']
SURFACE_COLUMNS=['donor','path','sha256','size_bytes','file_type','language','classification','authority_status','semantic_record_ids','notes']

DONORS={
 'D164':{'name':'WindowsCleaner','root':SRC/'WindowsCleaner-2','archive':'WindowsCleaner-2.zip','archive_sha256':'dfbfecd11afef113f8eea487b7f4ec87e44f02076d3dfaebd721d063618facca','members':122,'duplicate_of':None,'license':'CC-BY-NC-SA-4.0; independent target-native reimplementation only'},
 'D165':{'name':'Windows Community Toolkit repeat submission','root':SRC/'WindowsCommunityToolkit-main','archive':'WindowsCommunityToolkit-main(1).zip','archive_sha256':'beaf2574280404f7647dff0c7e7c956260b2fdbb19c10203855dd3d40a73d92e','members':2697,'duplicate_of':'D143','license':'Same byte-identical baseline as D143; prior licensing and decisions remain authoritative'},
 'D166':{'name':'QuickLook','root':SRC/'QuickLook-master','archive':'QuickLook-master.zip','archive_sha256':'6405f67eee72bf66e96f9fd43b6c8dd8b7a4a48178f6108e95c20f84ff5ef4df','members':1152,'duplicate_of':None,'license':'GPL-3.0-or-later; independent target-native reimplementation only'},
}

def sha(path:Path)->str:
 h=hashlib.sha256()
 with path.open('rb') as f:
  for chunk in iter(lambda:f.read(1024*1024),b''):h.update(chunk)
 return h.hexdigest()

def read_csv(path):
 with path.open(newline='',encoding='utf-8') as f:return list(csv.DictReader(f))
def write_csv(path,cols,rows):
 path.parent.mkdir(parents=True,exist_ok=True)
 with path.open('w',newline='',encoding='utf-8') as f:
  w=csv.DictWriter(f,fieldnames=cols,lineterminator='\n');w.writeheader();w.writerows(rows)

def lang_class(rel:str):
 low=rel.lower();ext=Path(rel).suffix.lower()
 if '/.github/' in '/'+low or low.startswith('.github/') or Path(rel).name in {'.gitignore','.gitattributes','.gitmodules'}: return ('text','YAML' if ext in {'.yml','.yaml'} else 'Other','repository-administration','supporting')
 if any(x in low for x in ('/bin/','/obj/','/packages/','/vendor/','/thirdparty/','/third-party/','/external/')): return ('binary' if ext in {'.dll','.exe','.pdb','.lib'} else 'text','Binary' if ext in {'.dll','.exe','.pdb','.lib'} else 'Other','vendored','derived')
 if ext in {'.png','.jpg','.jpeg','.gif','.ico','.svg','.webp','.bmp','.ttf','.woff','.woff2'}: return ('media','Image','media','supporting')
 if ext in {'.md','.rst','.txt'}: return ('text','Markdown' if ext=='.md' else 'Text','documentation','supporting')
 if ext in {'.cs','.xaml','.cpp','.c','.h','.hpp','.rs','.ts','.tsx','.js','.jsx','.ps1','.psm1','.py'}:
  language={'.cs':'C#','.xaml':'XAML','.cpp':'C++','.c':'C','.h':'C/C++','.hpp':'C++','.rs':'Rust','.ts':'TypeScript','.tsx':'TypeScript','.js':'JavaScript','.jsx':'JavaScript','.ps1':'PowerShell','.psm1':'PowerShell','.py':'Python'}[ext]
  classification='test' if any(x in low for x in ('test','spec','fixture','sample')) else 'implementation'
  return ('source',language,classification,'authoritative' if classification=='implementation' else 'supporting')
 if ext in {'.json','.xml','.config','.toml','.ini','.yaml','.yml','.props','.targets','.csproj','.sln','.slnx','.wixproj','.wapproj'}: return ('text','JSON' if ext=='.json' else 'XML' if ext in {'.xml','.config','.props','.targets','.csproj','.wixproj','.wapproj'} else 'YAML' if ext in {'.yml','.yaml'} else 'Other','configuration','authoritative')
 if ext in {'.bat','.cmd','.sh','.mjs'}: return ('text','Script','script','authoritative')
 if Path(rel).name.lower().startswith(('license','copying','notice')): return ('text','Text','governance','authoritative')
 return ('binary' if ext in {'.dll','.exe','.pdb','.pfx','.key','.crt'} else 'text','Binary' if ext in {'.dll','.exe','.pdb','.pfx','.key','.crt'} else 'Other','generated' if ext in {'.lock'} else 'configuration','derived' if ext in {'.lock'} else 'supporting')

def record(donor,suffix,path,domain,value,form,disposition,capability,nodes,invariant,negative,risk,test,transformation='independent target-native reimplementation and hardening',topology='many-to-many'):
 info=DONORS[donor]; full=info['root']/path
 if not full.is_file(): raise FileNotFoundError(full)
 return {'record_id':f'{donor}-{suffix}','parent_record_id':f'{donor}-SURFACE-ACCOUNTABILITY','composition_group_id':f'CG-W11-{suffix}','donor':donor,'domain':domain,'value_unit':value,'source_granularity':'mechanism, workflow, product pattern, or negative evidence','value_form':form,'separability':'context-dependent','donor_path':path,'donor_sha256':sha(full),'donor_symbol':'n/a','transformation':transformation,'mapping_topology':topology,'disposition':disposition,'decision_rationale':f'{value} was compared against WinCare path confinement, typed plans, policy, preview, journaling, recovery, privacy, GUI, and release invariants. Donor source and runtime packaging were not copied.','target_capability':capability,'target_nodes':nodes,'invariant':invariant,'negative_invariant':negative,'test_node':f'{TEST}#{test}','operator_surface':'Cleaner and File Preview Studio TUI; V5 GUI action catalog; headless JSON; preview receipts and transaction journal' if capability!='n/a' else 'n/a','migration_impact':'additive WinCare 5.2 capability; existing authority and persistence owners remain authoritative' if capability!='n/a' else 'none','license_note':info['license'],'risk_tier':risk,'dependency_record_ids':'n/a','evidence_confidence':'high','validation_status':'statically-validated' if disposition not in {'reference-only','rejected-with-reason'} else 'reviewed'}

SPECS={
'D164':[
 ('WINDOWS-CLEANER-DISK-PRESSURE','src-tauri/src/clean.rs','Storage maintenance','Disk-pressure-aware cleanup profiles','cleanup policy','hardened','Bounded disk-pressure cleanup','src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCareDiskPressureAssessment;src/WinCare/Providers/111-CleanerPreviewStudio.ps1#New-WinCareDiskPressureCleanupPlan','Only canonical target IDs selected from measured free-space pressure are eligible.','No root-drive wildcard scan, Prefetch purge, restore-point deletion, Defender history deletion, or Installer patch deletion is admitted.','high','keeps disk-pressure cleanup on canonical target identifiers'),
 ('WINDOWS-CLEANER-AUTO-CLEAN','src-tauri/src/lib.rs','Automation','Scheduled low-space cleanup workflow','operator workflow','adapted','Disk-pressure cleanup automation','src/WinCare/Providers/111-CleanerPreviewStudio.ps1#New-WinCareDiskPressureAutomationPlan','Scheduled cleanup uses the existing validated automation profile contract.','No autonomous cleanup runs outside a registered bounded task.','high','publishes all cleaner and preview headless routes'),
 ('WINDOWS-CLEANER-WINAPP2','src-tauri/src/winapp2.rs','Cleanup admission','Winapp2 FileKey parsing and cleanup targeting','parser and manifest','hardened','Bounded Winapp2 admission','src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Import-WinCareWinapp2Catalog;src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCareWinapp2Assessment;src/WinCare/Providers/111-CleanerPreviewStudio.ps1#New-WinCareWinapp2CleanupPlan','Only FileKey directives under approved local roots become exact hash-bound file entries.','Registry directives, exclusions, arbitrary recursion, scripts, unsafe patterns, and broad paths are rejected.','critical','admits only bounded FileKey Winapp2 rules'),
 ('WINDOWS-CLEANER-SOFTWARE-MOVE','src-tauri/src/software_move.rs','Storage relocation','Application data relocation behind a junction','mutation workflow','hardened','Verified application-data relocation','src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCareApplicationDataRelocationAssessment;src/WinCare/Providers/111-CleanerPreviewStudio.ps1#New-WinCareApplicationDataRelocationPlan;src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Invoke-WinCareRelocateDirectoryAction','Copy, full-tree hash, exact junction target, and recovery state are verified before commit.','WinCare never terminates processes, moves arbitrary paths, nests source/destination, or accepts stale state.','critical','verifies relocation copy, source hash, junction target, and recovery hash'),
 ('WINDOWS-CLEANER-GUARDRAILS','src-tauri/src/clean.rs','Trust boundary','Unsafe broad deletion and system-evidence cleanup lessons','negative evidence','guardrail-derived','Cleaner safety constraints','src/WinCare/Providers/111-CleanerPreviewStudio.ps1#New-WinCareDiskPressureCleanupPlan;src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Import-WinCareWinapp2Catalog','All cleanup paths and manifests remain bounded, canonical, and reviewable.','Drive-root traversal, automatic process killing, Prefetch deletion, Defender evidence deletion, restore-point deletion, and opaque toggles are forbidden.','critical','keeps disk-pressure cleanup on canonical target identifiers'),
 ('WINDOWS-CLEANER-UI-SUPERSEDED','src/App.tsx','Product architecture','Standalone Tauri cleaner interface','product surface','superseded','Existing WinCare V5 GUI and TUI','src/WinCare/UI/Gui/10-GuiRuntime.ps1#Start-WinCareGui;src/WinCare/UI/Screens/95-CleanerPreviewStudio.ps1#Show-WinCareCleanerPreviewStudioScreen','Cleaner workflows appear inside the existing non-authoritative operator surfaces.','No parallel Tauri authority path or second settings store is imported.','medium','integrates the dedicated TUI and curated GUI surfaces')],
'D166':[
 ('QUICKLOOK-PREVIEW-PIPELINE','QuickLook.Common/Plugin/IViewer.cs','File inspection','Prepare-view-cleanup preview lifecycle and format dispatch','lifecycle and product workflow','adapted','Bounded local file preview','src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCareFilePreviewProfile;src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCareFilePreview','Preview selection is deterministic, bounded, local, redacted by default, and non-executing.','Preview cannot launch files, navigate active content, fetch remote resources, or retain plugin state.','high','keeps preview local bounded and non-executing'),
 ('QUICKLOOK-PLUGIN-PRIORITY','QuickLook/PluginManager.cs','File inspection','Priority-based format handler selection','selection mechanism','adapted','Deterministic preview profiles','src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCareFilePreviewProfile;src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCarePreviewHandlerAssessment','Known extensions map to an ordered built-in profile catalog; registered handlers are assessment-only.','No arbitrary plugin assembly or COM preview handler is loaded.','high','selects deterministic priority-based preview profiles'),
 ('QUICKLOOK-TEXT-TABLE','QuickLook.Plugin/QuickLook.Plugin.TextViewer/Plugin.cs','File inspection','Text and structured-table preview affordances','product affordance','recomposed','Redacted text and table metadata preview','src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCareFilePreview','Text is opt-in, bounded by bytes and lines, and passed through redaction.','Preview does not render scripts, macros, HTML, Markdown active content, or formulas.','high','redacts text by default and reports active content'),
 ('QUICKLOOK-ARCHIVE-PE','QuickLook.Plugin/QuickLook.Plugin.ArchiveViewer/Plugin.cs','File inspection','Archive and binary metadata viewers','format workflow','recomposed','Safe archive, PE, image, and PDF metadata preview','src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCareFilePreview','Existing WinCare archive and PE inspectors remain the authoritative implementations.','No extraction, decompression bomb, executable loading, or embedded active content is performed.','high','keeps preview local bounded and non-executing'),
 ('QUICKLOOK-ACTIVE-CONTENT-GUARDRAIL','QuickLook.Plugin/QuickLook.Plugin.HtmlViewer/Plugin.cs','Trust boundary','Active web and document rendering risk','negative evidence','guardrail-derived','Non-executing preview boundary','src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCareFilePreview;src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCarePreviewHandlerAssessment','Potentially active formats are labeled and parsed only through bounded metadata or text paths.','No WebView, browser navigation, Office handler hosting, shell extension execution, or remote content is admitted.','critical','assesses preview handlers without loading them'),
 ('QUICKLOOK-GLOBAL-HOOK-GUARDRAIL','QuickLook/Helpers/GlobalKeyboardHook.cs','Trust boundary','Global space-key hook and resident preview runtime','negative evidence','rejected-with-reason','n/a','n/a','WinCare retains explicit operator invocation through GUI, TUI, and headless routes.','No global keyboard hook, Explorer injection, resident tray process, or donor updater is installed.','critical','does not import QuickLook global keyboard hooks or plugin runtime'),
 ('QUICKLOOK-GPL-NATIVE','LICENSE-GPL.txt','Licensing','GPL donor separation and independent implementation','license constraint','guardrail-derived','Independent target-native preview implementation','src/WinCare/Providers/111-CleanerPreviewStudio.ps1#Get-WinCareFilePreview','Only independently implemented semantics and public platform behavior are retained.','No QuickLook source, plugin binary, icon, font, or GPL runtime is copied into WinCare.','high','does not import QuickLook global keyboard hooks or plugin runtime')]
}

def main():
 ledger=[r for r in read_csv(DOCS/'adoption-matrix.csv') if r['donor'] not in DONORS]
 surfaces=[r for r in read_csv(DOCS/'surface-accountability.csv') if r['donor'] not in DONORS]
 inventory=[d for d in json.loads((DOCS/'donor-inventory.json').read_text('utf-8')) if d['donor_id'] not in DONORS]
 links={d:{} for d in DONORS}
 for donor,info in DONORS.items():
  root=info['root'];files=sorted(p for p in root.rglob('*') if p.is_file())
  representative='README.md' if (root/'README.md').is_file() else files[0].relative_to(root).as_posix()
  parent=f'{donor}-SURFACE-ACCOUNTABILITY'
  ledger.append({'record_id':parent,'parent_record_id':'n/a','composition_group_id':f'CG-W11-{donor}-ACCOUNTABILITY','donor':donor,'domain':'Evidence accountability','value_unit':f'Complete file accountability for {info["name"]}','source_granularity':'complete donor tree','value_form':'surface inventory','separability':'standalone','donor_path':representative,'donor_sha256':sha(root/representative),'donor_symbol':'n/a','transformation':'classification and provenance capture','mapping_topology':'one-to-many','disposition':'reference-only','decision_rationale':'Every regular donor surface is hashed, classified, and linked before semantic adoption decisions are claimed.','target_capability':'Convergence evidence graph','target_nodes':'docs/convergence/surface-accountability.csv;docs/convergence/donor-inventory.json','invariant':'No donor surface is silently skipped.','negative_invariant':'File counts are not represented as semantic implementation coverage.','test_node':f'{TEST}#retains the duplicate Toolkit submission as provenance only' if donor=='D165' else f'{TEST}#publishes all cleaner and preview headless routes','operator_surface':'Evidence reports','migration_impact':'none','license_note':info['license'],'risk_tier':'low','dependency_record_ids':'n/a','evidence_confidence':'high','validation_status':'verified'})
  if donor=='D165':
   rid='D165-DUPLICATE-SUBMISSION';links[donor].setdefault(representative,[]).append(rid)
   ledger.append({'record_id':rid,'parent_record_id':parent,'composition_group_id':'CG-W11-DUPLICATE-D143','donor':donor,'domain':'Provenance','value_unit':'Byte-identical repeat submission of D143 Windows Community Toolkit','source_granularity':'complete archive','value_form':'duplicate provenance','separability':'standalone','donor_path':representative,'donor_sha256':sha(root/representative),'donor_symbol':'n/a','transformation':'deduplication and prior-decision reuse','mapping_topology':'custom:duplicate evidence to prior donor baseline','disposition':'reference-only','decision_rationale':'Archive SHA-256 beaf2574280404f7647dff0c7e7c956260b2fdbb19c10203855dd3d40a73d92e is byte-identical to D143; D143 remains the authoritative semantic and implementation baseline.','target_capability':'Existing D143 Windows Community Toolkit-derived capability cards and GUI synthesis','target_nodes':'n/a','invariant':'Duplicate input preserves provenance without creating parallel target ownership.','negative_invariant':'No new feature, component, or coverage claim is attributed to duplicate bytes.','test_node':f'{TEST}#retains the duplicate Toolkit submission as provenance only','operator_surface':'n/a','migration_impact':'none','license_note':info['license'],'risk_tier':'low','dependency_record_ids':'n/a','evidence_confidence':'high','validation_status':'verified'})
  for spec in SPECS.get(donor,[]):
   rec=record(donor,*spec); ledger.append(rec);links[donor].setdefault(spec[1],[]).append(rec['record_id'])
  counts=Counter();langs=Counter();total=0
  for fp in files:
   rel=fp.relative_to(root).as_posix();st=fp.stat();total+=st.st_size
   ft,lang,cls,auth=lang_class(rel);counts[cls]+=1;langs[lang]+=1
   ids=[parent]+links[donor].get(rel,[])
   if donor=='D165' and 'D165-DUPLICATE-SUBMISSION' not in ids: ids.append('D165-DUPLICATE-SUBMISSION')
   surfaces.append({'donor':donor,'path':rel,'sha256':sha(fp),'size_bytes':str(st.st_size),'file_type':ft,'language':lang,'classification':cls,'authority_status':auth,'semantic_record_ids':';'.join(ids),'notes':'Classified and linked during WinCare 5.2 convergence.'+(' Exact duplicate of D143.' if donor=='D165' else '')})
  inventory.append({'donor_id':donor,'name':info['name'],'archive':info['archive'],'archive_sha256':info['archive_sha256'],'duplicate_of':info['duplicate_of'],'member_count':info['members'],'files':len(files),'regular_files':len(files),'symlinks':[],'expanded_bytes':total,'classifications':dict(sorted(counts.items())),'languages':dict(sorted(langs.items())),'licenses':[p.relative_to(root).as_posix() for p in files if p.name.lower().startswith(('license','copying','notice'))],'manifests':[p.relative_to(root).as_posix() for p in files if p.name.lower() in {'package.json','cargo.toml','go.mod','cmakelists.txt','directory.build.props','quicklook.slnx'}][:100],'readmes':[p.relative_to(root).as_posix() for p in files if p.name.lower().startswith('readme')][:100],'issues':[]})
 ledger.sort(key=lambda r:(int(r['donor'][1:]),r['record_id']))
 surfaces.sort(key=lambda r:(int(r['donor'][1:]),r['path'].lower(),r['path']))
 inventory.sort(key=lambda d:int(d['donor_id'][1:]))
 write_csv(DOCS/'adoption-matrix.csv',LEDGER_COLUMNS,ledger);write_csv(DOCS/'surface-accountability.csv',SURFACE_COLUMNS,surfaces)
 (DOCS/'donor-inventory.json').write_text(json.dumps(inventory,indent=2,ensure_ascii=False)+'\n','utf-8')
 EVIDENCE.mkdir(exist_ok=True)
 for name in ('adoption-matrix.csv','surface-accountability.csv','donor-inventory.json'):(EVIDENCE/name).write_bytes((DOCS/name).read_bytes())
 audit=json.loads(AUDIT.read_text('utf-8'))
 (DOCS/'wave11-archive-inventory.json').write_text(json.dumps(audit,indent=2,ensure_ascii=False)+'\n','utf-8')
 print(json.dumps({'donors':len(inventory),'unique_baselines':sum(1 for d in inventory if not d.get('duplicate_of')),'semantic_records':len(ledger),'surfaces':len(surfaces),'new_regular_files':sum(len([p for p in d['root'].rglob('*') if p.is_file()]) for d in DONORS.values())},indent=2))
if __name__=='__main__':main()
