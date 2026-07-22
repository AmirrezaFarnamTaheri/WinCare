#!/usr/bin/env python3
"""Append deterministic D87-D105 evidence to cumulative WinCare convergence data."""
from __future__ import annotations

import csv
import hashlib
import json
import os
from collections import Counter
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DOCS=ROOT/'docs'/'convergence'
SRC=Path(os.environ.get('WINCARE_DONOR_ROOT','/mnt/data/wincare_450_convergence/src'))
ARCHIVE_MANIFEST=Path(os.environ.get('WINCARE_ARCHIVE_MANIFEST','/mnt/data/wincare_450_convergence/analysis/archive_manifest.json'))

LEDGER_COLUMNS=['record_id','parent_record_id','composition_group_id','donor','domain','value_unit','source_granularity','value_form','separability','donor_path','donor_sha256','donor_symbol','transformation','mapping_topology','disposition','decision_rationale','target_capability','target_nodes','invariant','negative_invariant','test_node','operator_surface','migration_impact','license_note','risk_tier','dependency_record_ids','evidence_confidence','validation_status']
SURFACE_COLUMNS=['donor','path','sha256','size_bytes','file_type','language','classification','authority_status','semantic_record_ids','notes']

DONORS=[
 {'id':'D87','name':'Monitoring','archive':'Monitoring-master.zip','root':'Monitoring-master/Monitoring-master','license':'GPL-3.0 donor; dashboard/config concepts independently represented; no donor source bundled.'},
 {'id':'D88','name':'Explorer++','archive':'explorerplusplus-master(1).zip','root':'explorerplusplus-master(1)/explorerplusplus-master','license':'GPL-3.0 donor; file-workspace outcomes independently reimplemented.'},
 {'id':'D89','name':'United Sets','archive':'UnitedSets-master.zip','root':'UnitedSets-master/UnitedSets-master','license':'MIT donor; window grouping concepts adapted to existing WinCare layouts.'},
 {'id':'D90','name':'12Bar','archive':'12Bar-main.zip','root':'12Bar-main/12Bar-main','license':'License unclear in supplied archive; only docked workspace concepts were rederived.'},
 {'id':'D91','name':'Twinkle Tray','archive':'twinkle-tray-master (1).zip','root':'twinkle-tray-master (1)/twinkle-tray-master','license':'MIT donor; brightness scheduling semantics independently implemented with native WMI actions.'},
 {'id':'D92','name':'System Explorer','archive':'SystemExplorer-master.zip','root':'SystemExplorer-master/SystemExplorer-master','license':'MIT donor; process/service inventory concepts adapted to bounded local snapshots.'},
 {'id':'D93','name':'ADB Explorer','archive':'ADB-Explorer-master.zip','root':'ADB-Explorer-master/ADB-Explorer-master','license':'MIT donor; archive admission and exact-tool verification primitives independently implemented.'},
 {'id':'D94','name':'Kanata','archive':'kanata-main.zip','root':'kanata-main/kanata-main','license':'LGPL-3.0-or-later donor; configuration admission only; no global input hook is bundled or started.'},
 {'id':'D95','name':'Taskbar Monitor','archive':'taskbar-monitor-master.zip','root':'taskbar-monitor-master/taskbar-monitor-master','license':'GPL-3.0 donor; resource-counter presentation concepts adapted to WinCare telemetry.'},
 {'id':'D96','name':'Folcolor','archive':'Folcolor-main.zip','root':'Folcolor-main/Folcolor-main','license':'MIT donor; desktop.ini behavior independently implemented with exact recovery.'},
 {'id':'D97','name':'Comet','archive':'comet-master.zip','root':'comet-master/comet-master','license':'MIT donor; deprecated UWP controls used only as product-flow inspiration.'},
 {'id':'D98','name':'bar.wezterm','archive':'bar.wezterm-main.zip','root':'bar.wezterm-main/bar.wezterm-main','license':'MIT donor; local status-module behavior independently generated.'},
 {'id':'D99','name':'SyncTrayzor','archive':'SyncTrayzor-main.zip','root':'SyncTrayzor-main/SyncTrayzor-main','license':'MIT donor; local Syncthing state inspection adapted with secret redaction.'},
 {'id':'D100','name':'FloatWindowUtils','archive':'FloatWindowUtils-master.zip','root':'FloatWindowUtils-master/FloatWindowUtils-master','license':'License unclear in supplied archive; floating/auto-align idea adapted to target-native window layouts.'},
 {'id':'D101','name':'Notion','archive':'notion-main.zip','root':'notion-main/notion-main','license':'GPL donor; tiling concepts independently rederived; three repository symlinks explicitly accounted.'},
 {'id':'D102','name':'Xbox Full Screen Experience Tool','archive':'XboxFullScreenExperienceTool-main.zip','root':'XboxFullScreenExperienceTool-main/XboxFullScreenExperienceTool-main','license':'GPL-3.0 donor; exact feature IDs and registry semantics independently modeled; donor driver/service not bundled.'},
 {'id':'D103','name':'ApkShellExt2','archive':'apkshellext-ApkShellext2.zip','root':'apkshellext-ApkShellext2/apkshellext-ApkShellext2','license':'License supplied; package metadata behavior independently implemented without shell extension installation.'},
 {'id':'D104','name':'FileExplorer','archive':'FileExplorer-master.zip','root':'FileExplorer-master/FileExplorer-master','license':'MIT donor; file workspace and preview-admission concepts independently implemented.'},
 {'id':'D105','name':'Flogo','archive':'flogo-master.zip','root':'flogo-master/flogo-master','license':'License supplied; flow visualization used as inspiration for a read-only plan graph.'},
]

TEST='tests/Wave6.WorkspaceStudio.Tests.ps1#publishes every Workspace Studio headless route'
TEST_ARCHIVE='tests/Wave6.WorkspaceStudio.Tests.ps1#admits archives without extracting or executing them'
TEST_TOOL='tests/Wave6.WorkspaceStudio.Tests.ps1#uses exact-hash no-shell external tool admission'
TEST_XBOX='tests/Wave6.WorkspaceStudio.Tests.ps1#includes the requested unsafe Xbox one-click mutation with exact evidence values'
TEST_FOLDER='tests/Wave6.WorkspaceStudio.Tests.ps1#retains exact conflict-aware folder appearance recovery'
TEST_GATES='tests/Wave6.WorkspaceStudio.Tests.ps1#keeps external and Xbox mutations default-deny'

SPECS={
'D87':[('MONITORING-EXPORT','Monitoring templates','Windows Telegraf/Grafana input and dashboard templates','Grafana Dashboards/Windows dashboard.json','configuration and dashboard','adapted','Monitoring export','src/WinCare/Providers/107-WorkspaceStudio.ps1#New-WinCareMonitoringExportPlan','Generated templates contain no output destination, credential, or listener.',TEST,'medium'),('UNIFIED-SNAPSHOT','Operations telemetry','Unified bounded Windows operations snapshot','README.md','operational pattern','recomposed','Unified system snapshot','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCareUnifiedSystemSnapshot','Snapshot collection is local, bounded, and read-only.',TEST,'low')],
'D88':[('FILE-WORKSPACES','File management','Tabbed roots, bookmarks, and persisted file workspaces','Explorer++/Explorer++/BrowserWindow.cpp','product workflow','recomposed','File workspace catalog','src/WinCare/Providers/107-WorkspaceStudio.ps1#New-WinCareFileWorkspacePlan','File workspace state is bounded and atomically replaced under WinCare state.',TEST,'low')],
'D89':[('WINDOW-GROUPING','Window management','Window grouping and tab-oriented workspace composition','UnitedSets/Cells/Data/WindowCell.cs','product workflow','inspired-native','Workspace layout profiles','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCareWorkspaceStudioProfile','UI grouping does not become an alternate source of window authority.',TEST,'medium')],
'D90':[('DOCKED-SIDE','Window management','Docked utility side-panel layout','main.js','layout behavior','inspired-native','Docked-side layout preset','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCareWorkspaceStudioProfile','No donor appbar process or DesktopCoral dependency is installed.',TEST,'medium')],
'D91':[('BRIGHTNESS-SCHEDULES','Display control','Time-based brightness schedules','src/electron.js','workflow and state model','adapted','Brightness schedule catalog','src/WinCare/Providers/107-WorkspaceStudio.ps1#New-WinCareBrightnessSchedulePlan;src/WinCare/Providers/107-WorkspaceStudio.ps1#New-WinCareBrightnessScheduleApplyPlan','Schedules are bounded records and apply through the existing reversible brightness action.',TEST,'medium')],
'D92':[('SYSTEM-INVENTORY','System inspection','Process and service exploration','ObjExpCore/ProcessManager.cpp','inspection mechanism','recomposed','Unified system snapshot','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCareUnifiedSystemSnapshot','System exploration remains read-only and process counts are bounded.',TEST,'low')],
'D93':[('ARCHIVE-ADMISSION','Archive safety','Archive path, collision, and expansion admission rules','ADB_Test/ArchiveTests.cs','test oracle','hardened','Package archive admission','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCarePackageArchiveMetadata','No archive member is extracted or executed during metadata inspection.',TEST_ARCHIVE,'high'),('ADB-VERIFIED-TOOL','Device tooling','Exact-hash ADB command admission','OFFICIAL_ADB_VERSIONS.md','supply-chain evidence','hardened','Verified ADB execution','src/WinCare/Providers/107-WorkspaceStudio.ps1#New-WinCareAdbInventoryPlan;src/WinCare/Providers/107-WorkspaceStudio.ps1#Invoke-WinCareRunVerifiedPeerToolAction','ADB runs without a shell and only admitted arguments reach exact approved bytes.',TEST_TOOL,'high')],
'D94':[('KANATA-CONFIG-ADMISSION','Input configuration','Kanata layer and include admission','cfg_samples/kanata.kbd','configuration grammar','adapted','Kanata configuration admission','src/WinCare/Providers/107-WorkspaceStudio.ps1#Test-WinCareKanataConfiguration','Configuration is inspected only; no global hook or input interception is started.',TEST,'high')],
'D95':[('RESOURCE-COUNTERS','Operations telemetry','CPU, memory, disk, network, and process counter presentation','TaskbarMonitor/Counters/PerformanceCounterReader.cs','telemetry pattern','recomposed','Unified system snapshot and dashboard','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCareUnifiedSystemSnapshot;src/WinCare/Providers/107-WorkspaceStudio.ps1#New-WinCareMonitoringExportPlan','Resource observations do not gain mutation authority.',TEST,'low')],
'D96':[('FOLDER-APPEARANCE','Shell customization','Shell-extension-free desktop.ini folder appearance','src/Controller/FolderColorize.cpp','executable behavior','hardened','Folder appearance mutation and recovery','src/WinCare/Providers/107-WorkspaceStudio.ps1#New-WinCareFolderAppearancePlan;src/WinCare/Providers/107-WorkspaceStudio.ps1#Invoke-WinCareRestoreFolderAppearanceAction','Exact prior desktop.ini bytes and attributes are preserved and conflicts block recovery.',TEST_FOLDER,'high')],
'D97':[('CONTROL-AFFORDANCES','Product workflow','Undoable control and progress affordances','src/Controls/SlidableListItem/SlidableListItem.cs','product affordance','inspired-native','Plan graph and recovery visibility','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCarePlaybookGraph','Product affordances remain non-authoritative views over typed plans.',TEST,'low')],
'D98':[('STATUS-MODULE','Terminal workspace','Workspace, hostname, cwd, and time status module','plugin/bar/config.lua','configuration template','adapted','WezTerm status generator','src/WinCare/Providers/107-WorkspaceStudio.ps1#New-WinCareWezTermStatusBarPlan','Generated Lua is local-only and performs no network installation.',TEST,'low')],
'D99':[('SYNCTHING-STATE','Synchronization operations','Syncthing process, folder, device, and warning state','src/SyncTrayzor/NotifyIcon/NotifyIconViewModel.cs','operator workflow','adapted','Syncthing local state inspection','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCareSyncthingState','API keys remain redacted and configuration is not modified.',TEST,'medium')],
'D100':[('FLOAT-AUTOALIGN','Window management','Movable floating-window and auto-align workflow','README.md','concept','inspired-native','Docked and main-stack presets','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCareWorkspaceStudioProfile','No Android overlay permission or donor runtime is imported.',TEST,'low')],
'D101':[('TILING-LAYOUTS','Window management','Columns, rows, and main-stack tiling layouts','etc/cfg_layouts.lua','layout preset','inspired-native','Workspace layout profiles','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCareWorkspaceStudioProfile','Layouts use existing WinCare exact-window identity and rollback contracts.',TEST,'medium')],
'D102':[('XBOX-FSE-FEATURES','Windows hidden features','One-click enable or disable of exact Xbox FSE feature IDs','XboxFullScreenExperienceTool/MainForm.cs','unsafe executable behavior','hardened','Critical Xbox FSE mutation','src/WinCare/Providers/107-WorkspaceStudio.ps1#New-WinCareXboxFullscreenExperiencePlan','Only exact allowed feature IDs reach exact-hash ViVeTool bytes after policy and typed acknowledgement.',TEST_XBOX,'critical'),('XBOX-FSE-DEVICEFORM','Windows hidden features','DeviceForm DWORD 0x2E mutation and restoration','PhysPanelCPP/PanelManager.cpp','unsafe registry behavior','hardened','Critical Xbox DeviceForm mutation','src/WinCare/Providers/107-WorkspaceStudio.ps1#New-WinCareXboxFullscreenExperiencePlan','The prior DeviceForm value is captured and restored through the typed registry contract.',TEST_XBOX,'critical')],
'D103':[('PACKAGE-METADATA','Package inspection','APK, IPA, AppX, and MSIX metadata inspection','ApkShellext2/AppPackageReader.cs','metadata parser','recomposed','Package archive metadata','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCarePackageArchiveMetadata','Packages are never installed, registered, extracted, or executed.',TEST_ARCHIVE,'high')],
'D104':[('FILE-WORKSPACE','File management','Portable tab roots, bookmarks, and search roots','FileExplorer/Core/PackageManager.cs','product workflow','recomposed','File workspace catalog','src/WinCare/Providers/107-WorkspaceStudio.ps1#New-WinCareFileWorkspacePlan','File workspace records do not perform file mutations.',TEST,'low'),('PREVIEW-ADMISSION','File inspection','Preview-extension boundary and bounded metadata surfaces','FileExplorer.Extension/IPreviewExtension.cs','interface contract','guardrail-derived','Package archive admission','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCarePackageArchiveMetadata','No untrusted preview extension is loaded in-process.',TEST_ARCHIVE,'high')],
'D105':[('ACTION-GRAPH','Workflow visualization','Flowchart representation of sequential actions and rollback edges','src/ui-flowchart.js','visual interaction concept','inspired-native','Read-only playbook graph','src/WinCare/Providers/107-WorkspaceStudio.ps1#Get-WinCarePlaybookGraph','Graph nodes cannot execute or mutate plans; authority remains in the transaction engine.',TEST,'low')],
}

LANG={'.ps1':'PowerShell','.psm1':'PowerShell','.psd1':'PowerShell','.cs':'CSharp','.xaml':'XAML','.csproj':'MSBuild','.sln':'Visual Studio Solution','.cpp':'C++','.cc':'C++','.c':'C','.h':'C/C++ header','.hpp':'C++ header','.rs':'Rust','.lua':'Lua','.js':'JavaScript','.jsx':'JavaScript','.ts':'TypeScript','.tsx':'TypeScript','.html':'HTML','.css':'CSS','.scss':'SCSS','.json':'JSON','.xml':'XML','.resx':'XML','.config':'XML','.manifest':'XML','.yaml':'YAML','.yml':'YAML','.toml':'TOML','.ini':'INI','.md':'Markdown','.txt':'Text','.sh':'Shell','.bat':'Batch','.cmd':'Batch','.py':'Python'}
MEDIA={'.png','.jpg','.jpeg','.gif','.bmp','.ico','.svg','.cur','.ttf','.eot','.woff','.wav','.mp3','.pfx','.dll','.exe','.zip'}
CONFIG={'.json','.xml','.resx','.config','.manifest','.yaml','.yml','.toml','.ini','.sln','.csproj','.vcxproj','.filters','.props','.targets','.rc','.lock'}
SCRIPT={'.ps1','.psm1','.bat','.cmd','.sh','.py'}
UI={'.xaml','.html','.css','.scss','.jsx','.tsx'}
SOURCE={'.cs','.cpp','.cc','.c','.h','.hpp','.rs','.lua','.js','.ts'}

def sha256(path:Path)->str:
 h=hashlib.sha256()
 with path.open('rb') as f:
  for chunk in iter(lambda:f.read(1024*1024),b''):h.update(chunk)
 return h.hexdigest()

def sha256_text(text:str)->str:return hashlib.sha256(text.encode()).hexdigest()

def classify(rel:str,path:Path)->tuple[str,str,str,str]:
 low=rel.lower();ext=path.suffix.lower();parts={p.lower() for p in Path(rel).parts};name=path.name.lower()
 if low.startswith('.github/') or '/.git/' in f'/{low}/':cls='repository-administration'
 elif 'test' in parts or '/tests/' in f'/{low}/' or name.startswith('test'):cls='test'
 elif name.startswith(('license','copying','notice')) or 'code_of_conduct' in low or 'contributing' in low:cls='governance'
 elif 'localization' in low or ext=='.resx' or any(p.lower().startswith('strings.') for p in Path(rel).parts):cls='localization'
 elif ext in MEDIA:cls='vendored' if ext in {'.dll','.exe','.pfx','.zip'} else 'media'
 elif '/bin/' in f'/{low}/' or '/obj/' in f'/{low}/' or 'node_modules' in parts or 'packages' in parts:cls='vendored'
 elif name.startswith('readme') or low.startswith('docs/') or ext in {'.md','.txt','.adoc'}:cls='documentation'
 elif ext in SCRIPT:cls='script'
 elif ext in UI:cls='ui-or-product'
 elif ext in CONFIG or name in {'makefile','package.json','package-lock.json','cargo.lock','cargo.toml'}:cls='configuration'
 elif ext in SOURCE:cls='implementation'
 else:cls='repository-administration'
 auth='authoritative' if cls in {'implementation','script','configuration','ui-or-product'} else 'supporting'
 if cls in {'media','vendored','localization'}:auth='derived'
 return ext or '[no-extension]',LANG.get(ext,'Other'),cls,auth

def read_csv(path):
 with path.open(newline='',encoding='utf-8-sig') as f:return list(csv.DictReader(f))

def write_csv(path,cols,rows):
 with path.open('w',newline='',encoding='utf-8') as f:
  w=csv.DictWriter(f,fieldnames=cols,lineterminator='\n');w.writeheader();w.writerows(rows)

def main():
 manifests={Path(x['archive']).name:x for x in json.loads(ARCHIVE_MANIFEST.read_text())}
 ledger=[r for r in read_csv(DOCS/'adoption-matrix.csv') if int(r['donor'][1:])<87]
 surfaces=[r for r in read_csv(DOCS/'surface-accountability.csv') if int(r['donor'][1:])<87]
 inventory=[r for r in json.loads((DOCS/'donor-inventory.json').read_text()) if int(str(r.get('donor_id',r.get('donor')))[1:])<87]
 for donor in DONORS:
  root=SRC/donor['root'];manifest=manifests[donor['archive']]
  files=sorted((p for p in root.rglob('*') if p.is_file() and not p.is_symlink()),key=lambda p:p.relative_to(root).as_posix().casefold())
  parent=f"{donor['id']}-SURFACE-ACCOUNTABILITY";rep='README.md' if (root/'README.md').is_file() else files[0].relative_to(root).as_posix()
  ledger.append({'record_id':parent,'parent_record_id':'n/a','composition_group_id':f"CG-W6-{donor['id']}-ACCOUNTABILITY",'donor':donor['id'],'domain':'Donor accountability','value_unit':f"Complete regular-file and symlink accountability for {donor['name']}",'source_granularity':'repository/archive surface','value_form':'accountability evidence','separability':'standalone','donor_path':rep,'donor_sha256':sha256(root/rep),'donor_symbol':'n/a','transformation':'evidence inventory and disposition mapping','mapping_topology':'custom:one donor to complete surface accountability','disposition':'reference-only','decision_rationale':'Every supplied regular file and repository symlink was hashed or target-recorded, classified, and linked; material semantics receive narrower child records.','target_capability':'n/a','target_nodes':'n/a','invariant':'No donor surface or symlink is silently omitted.','negative_invariant':'File counts and donor packaging are not treated as semantic adoption.','test_node':'n/a','operator_surface':'n/a','migration_impact':'n/a','license_note':donor['license'],'risk_tier':'low','dependency_record_ids':'n/a','evidence_confidence':'high','validation_status':'reviewed'})
  links={}
  for suffix,domain,value,path,form,disp,cap,nodes,invariant,test,risk in SPECS[donor['id']]:
   full=root/path
   if not full.is_file():raise FileNotFoundError(full)
   rid=f"{donor['id']}-{suffix}";links.setdefault(path,[]).append(rid)
   ledger.append({'record_id':rid,'parent_record_id':parent,'composition_group_id':f"CG-W6-{suffix}",'donor':donor['id'],'domain':domain,'value_unit':value,'source_granularity':'mechanism or evidence cluster','value_form':form,'separability':'context-dependent','donor_path':path,'donor_sha256':sha256(full),'donor_symbol':'n/a','transformation':'target-native synthesis and hardening','mapping_topology':'many-to-many','disposition':disp,'decision_rationale':f"{value} was selected from {donor['name']} and fitted to WinCare policy, typed actions, bounded state, preview, journaling, and recovery; donor packaging and alternate authority paths were not imported.",'target_capability':cap,'target_nodes':nodes,'invariant':invariant,'negative_invariant':'Donor code, background services, shell extensions, drivers, or alternate mutation paths are not imported unless explicitly stated.','test_node':test,'operator_surface':'Workspace Studio TUI, headless JSON, preview receipts, and transaction journal','migration_impact':'additive WinCare 4.5 capability; existing action and state owners remain authoritative','license_note':donor['license'],'risk_tier':risk,'dependency_record_ids':'n/a','evidence_confidence':'high','validation_status':'statically-validated'})
  counts=Counter();langs=Counter();total=0
  for f in files:
   rel=f.relative_to(root).as_posix();st=f.stat();total+=st.st_size;ft,lang,cls,auth=classify(rel,f);counts[cls]+=1;langs[lang]+=1
   surfaces.append({'donor':donor['id'],'path':rel,'sha256':sha256(f),'size_bytes':str(st.st_size),'file_type':ft,'language':lang,'classification':cls,'authority_status':auth,'semantic_record_ids':';'.join([parent]+links.get(rel,[])),'notes':'Classified and linked during cumulative WinCare 4.5 convergence.'})
  symlinks=manifest.get('symlinks',[])
  for link in symlinks:
   rel=link['path'].split('/',1)[1] if '/' in link['path'] else link['path'];target=link['target'];raw=target.encode();total+=len(raw);counts['repository-administration']+=1;langs['Symlink']+=1
   surfaces.append({'donor':donor['id'],'path':rel,'sha256':sha256_text(target),'size_bytes':str(len(raw)),'file_type':'symlink','language':'Symlink','classification':'repository-administration','authority_status':'supporting','semantic_record_ids':parent,'notes':f"Repository symlink recorded without materialization; target={target}"})
  license_paths=[f.relative_to(root).as_posix() for f in files if f.name.lower().startswith(('license','copying'))]
  inventory.append({'donor_id':donor['id'],'name':donor['name'],'archive':donor['archive'],'archive_sha256':manifest['sha256'],'duplicate_of':None,'member_count':manifest['members'],'files':len(files)+len(symlinks),'regular_files':len(files),'symlinks':symlinks,'expanded_bytes':total,'classifications':dict(sorted(counts.items())),'languages':dict(sorted(langs.items())),'licenses':license_paths,'manifests':[],'readmes':[f.relative_to(root).as_posix() for f in files if f.name.lower().startswith('readme')],'issues':manifest.get('warnings',[])})
 write_csv(DOCS/'adoption-matrix.csv',LEDGER_COLUMNS,ledger);write_csv(DOCS/'surface-accountability.csv',SURFACE_COLUMNS,surfaces);(DOCS/'donor-inventory.json').write_text(json.dumps(inventory,indent=2,ensure_ascii=False)+'\n')
 print(json.dumps({'donors':len(inventory),'semanticRecords':len(ledger),'surfaces':len(surfaces),'newDonors':len(DONORS),'newRegularFiles':sum(x['regular_files'] for x in inventory if int(x['donor_id'][1:])>=87),'newSymlinks':sum(len(x.get('symlinks',[])) for x in inventory if int(x['donor_id'][1:])>=87)},indent=2))
if __name__=='__main__':main()
