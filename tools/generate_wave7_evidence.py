#!/usr/bin/env python3
"""Append deterministic D106-D109 evidence to cumulative WinCare convergence data."""
from __future__ import annotations
import csv, hashlib, json, os
from collections import Counter
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DOCS=ROOT/'docs'/'convergence'
SRC=Path(os.environ.get('WINCARE_WAVE7_DONOR_ROOT','/mnt/data/wincare_wave7_scan/donors'))
SCAN=Path(os.environ.get('WINCARE_WAVE7_ARCHIVE_MANIFEST','/mnt/data/wincare_wave7_scan/archive_scan.json'))
LEDGER_COLUMNS=['record_id','parent_record_id','composition_group_id','donor','domain','value_unit','source_granularity','value_form','separability','donor_path','donor_sha256','donor_symbol','transformation','mapping_topology','disposition','decision_rationale','target_capability','target_nodes','invariant','negative_invariant','test_node','operator_surface','migration_impact','license_note','risk_tier','dependency_record_ids','evidence_confidence','validation_status']
SURFACE_COLUMNS=['donor','path','sha256','size_bytes','file_type','language','classification','authority_status','semantic_record_ids','notes']
TEST='tests/Wave7.CleanupMods.Tests.ps1'
DONORS=[
 {'id':'D106','name':'WindowsMods','archive':'WindowsMods-master(3).zip','root':SRC/'D106-WindowsMods'/'WindowsMods-master','license':'GPL-3.0 donor; behavior independently reimplemented; no donor source is bundled.'},
 {'id':'D107','name':'WindowsCleanerUtility','archive':'WindowsCleanerUtility-main(2).zip','root':SRC/'D107-WindowsCleanerUtility'/'WindowsCleanerUtility-main','license':'GPL-3.0 donor; cleanup intent independently reimplemented through bounded typed actions.'},
 {'id':'D108','name':'Remove-MS-Store-Apps','archive':'Remove-MS-Store-Apps-master(2).zip','root':SRC/'D108-Remove-MS-Store-Apps'/'Remove-MS-Store-Apps-master','license':'MIT donor; list and allowlist semantics adapted to WinCare typed actions.'},
 {'id':'D109','name':'windows','archive':'windows-master (2).zip','root':SRC/'D109-windows'/'windows-master','license':'No license detected in supplied archive; only independently implemented ideas, data points, and guardrails are used.'},
]
SPECS={
'D106':[
 ('EXPLORER-VISIBILITY','Explorer preferences','File-extension and hidden-file toggles','windowsmods.ahk','product behavior','adapted','Explorer quick settings','src/WinCare/Providers/108-CleanupMods.ps1#New-WinCareExplorerQuickPlan','Explorer preferences are explicit reversible Registry actions, not global hooks.','Global input hooks are not installed or started.','high'),
 ('EXPLORER-NAVIGATION','Explorer preferences','This PC versus Home launch preference','InstantExplorer.ahk','product behavior','inspired-native','Explorer quick settings','src/WinCare/Providers/108-CleanupMods.ps1#New-WinCareExplorerQuickPlan','Navigation preference is target-native and reversible.','Donor dialog interception and keystroke injection are not imported.','medium'),
 ('GLOBAL-HOTKEY-GUARDRAIL','Input safety','Global Explorer undo interception as a negative guardrail','CTRLZ.ahk','negative lesson','guardrail-derived','Input-hook exclusion','src/WinCare/Providers/108-CleanupMods.ps1#New-WinCareExplorerQuickPlan','WinCare changes preferences without suppressing Ctrl+Z or installing hooks.','No global input interception is admitted.','high'),
 ('MEDIA-AUTOMATION','Media workflow','FFmpeg and backup helper scripts','FFMPEG Scripts.txt','operational reference','reference-only','n/a','n/a','Donor media workflows remain available as evidence.','WinCare does not absorb unrelated hard-coded media paths.','low'),
],
'D107':[
 ('BOUNDED-DEEP-CLEAN','Cleanup','Aggressive cleanup decomposed into bounded canonical targets','WindowsCleanerUtility.bat','executable behavior','hardened','Deep cleanup profiles','src/WinCare/Providers/108-CleanupMods.ps1#New-WinCareDeepCleanupPlan;src/WinCare/Providers/20-Cleanup.ps1#Get-WinCareCleanupTarget','Every deletion resolves to a visible canonical target and excludes reparse points.','No drive-root wildcard deletion is performed.','critical'),
 ('UPDATE-CACHE-COORDINATION','Cleanup','Windows Update cache service coordination','WindowsCleanerUtility.bat','recovery mechanism','hardened','Windows Update cache cleanup','src/WinCare/Providers/108-CleanupMods.ps1#Invoke-WinCareClearWindowsUpdateDownloadCacheAction','BITS and Windows Update service states are captured and restored after bounded deletion.','Services are not left stopped merely because cleanup fails.','high'),
 ('COMPONENT-CLEANUP','Servicing','Supported component-store cleanup without ResetBase','WindowsCleanerUtility.bat','servicing mechanism','adapted','Online component cleanup','src/WinCare/Providers/108-CleanupMods.ps1#Invoke-WinCareStartComponentCleanupAction','Supported servicing APIs run without ResetBase.','Installed-update removability is not intentionally destroyed.','high'),
 ('ROOT-SWEEP-GUARDRAIL','Cleanup safety','Drive-root, event-log, Prefetch, Defender-history, and blanket log sweeps','WindowsCleanerUtility.bat','negative lesson','guardrail-derived','Cleanup exclusion guardrails','src/WinCare/Providers/108-CleanupMods.ps1#Get-WinCareDeepCleanupProfile','Unsafe donor sweeps are explicitly documented and excluded.','Generic action types cannot disguise event-log or Prefetch purges.','critical'),
 ('HEALTH-REPAIR','System health','SFC and DISM repair sequence','WindowsCleanerUtility.bat','workflow reference','reference-only','n/a','n/a','The existing Windows health provider already owns supported health repair.','Cleanup convergence does not duplicate the health authority path.','low'),
],
'D108':[
 ('LIST-REMOVE','Application removal','Bounded list-based AppX selection','Remove-MS-Store-Apps.ps1','selection mechanism','adapted','AppX selection profiles','src/WinCare/Providers/108-CleanupMods.ps1#Get-WinCareAppxSelectionAssessment','Only bounded validated wildcard patterns select packages.','No regex or donor script evaluation occurs.','high'),
 ('ALLOWLIST-REMOVE','Application removal','Keep-list mode that removes nonmatching removable packages','Remove-MS-Store-Apps.ps1','selection mechanism','hardened','AppX allowlist removal','src/WinCare/Providers/108-CleanupMods.ps1#New-WinCareAppxSelectionPlan','Allowlist mode excludes frameworks, resources, and explicitly non-removable packages.','UI state or unvalidated text cannot authorize removal.','critical'),
 ('ALL-USERS-PROVISIONED','Application removal','Current-user, all-user, and provisioned package scopes','Remove-MS-Store-Apps.ps1','scope model','hardened','Typed AppX removal scopes','src/WinCare/Providers/108-CleanupMods.ps1#Invoke-WinCareRemoveAppxPackageAllUsersAction;src/WinCare/Providers/108-CleanupMods.ps1#New-WinCareAppxSelectionPlan','All-user and provisioning mutations require administrator actions and exact package identities.','Package wildcards never reach mutation cmdlets directly.','critical'),
 ('OFFLINE-MOUNTED-IMAGE','Offline servicing','Offline provisioned-package selection','Remove-MS-Store-Apps.ps1','offline workflow','recomposed','Mounted-image AppX selection','src/WinCare/Providers/108-CleanupMods.ps1#New-WinCareOfflineAppxSelectionPlan','WinCare operates only on an already mounted read-write image through existing offline policy.','No implicit WIM mount, commit, or path guessing occurs.','critical'),
 ('W10-PRESET','Application removal','Windows 10 curated package list','w10-21H2-apps-provisioned.txt','data preset','adapted','Windows 10 AppX selection preset','src/WinCare/Providers/108-CleanupMods.ps1#Get-WinCareAppxSelectionProfile','The preset is represented as validated exact patterns.','The donor list is not executed as code.','medium'),
 ('W11-PRESET','Application removal','Windows 11 curated package list','w11-22H2-apps-provisioned.txt','data preset','adapted','Windows 11 AppX selection preset','src/WinCare/Providers/108-CleanupMods.ps1#Get-WinCareAppxSelectionProfile','UTF-16 donor data was normalized into a validated target-native preset.','Encoding artifacts cannot become package patterns.','medium'),
],
'D109':[
 ('WINDOWS-REPO-NUCLEAR','Legacy unsafe','Composite destructive Windows reduction workflow','windows_10/cleanup-windows.ps1','workflow synthesis','synthesized','Windows repository nuclear profile','src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareLegacyUnsafeProfilePlan','One authoritative typed plan composes selected donor mutations.','Raw scripts and alternate mutation paths are not executed.','critical'),
 ('SERVICES-TASKS','Legacy unsafe','Telemetry and background service/task suppression lists','windows_10/disable-services.ps1','configuration data','adapted','Service and task reduction','src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLegacyUnsafeProfile','Only discovered exact services and tasks produce actions with compensators.','Absent or renamed system items are not fabricated.','high'),
 ('TELEMETRY-POLICY','Privacy policy','AllowTelemetry, DisableUAR, AITEnable, and CEIP policy values','windows_10/disable-telemetry.ps1','policy data','adapted','Diagnostic policy catalog','src/WinCare/Data/Catalog/rules.json#privacy.telemetry-security-level','Each policy value is explicit, previewed, and reversible.','Edition-dependent behavior is not claimed as universally effective.','high'),
 ('APPX-CAPABILITY-REDUCTION','System reduction','All-removable AppX, optional feature, and capability removal','windows_10/old/remove-apps.ps1','mutation set','recomposed','AppX and component reduction','src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareLegacyUnsafeProfilePlan;src/WinCare/Providers/108-CleanupMods.ps1#New-WinCareAppxSelectionPlan','Exact installed identities are resolved before typed mutations.','Wildcards do not reach servicing removal commands.','critical'),
 ('OPTIONAL-FEATURES','System reduction','Legacy optional-feature disable list','windows_10/old/disable-optional.ps1','configuration data','adapted','Optional feature reduction','src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareLegacyUnsafeProfilePlan','Only currently enabled exact features create reversible actions.','ResetBase and unsupported component deletion are not admitted.','high'),
 ('FIREWALL-HOSTS','Network policy','Telemetry host blocking references','windows_10/firewall/hosts-blocklist.ps1','network policy reference','hardened','Managed hosts block','src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareLegacyUnsafeProfilePlan','Host entries are validated and confined to a managed WinCare block.','Arbitrary remote downloads or unmanaged hosts rewrites are not used.','high'),
 ('COPILOT-REMOVAL','Application removal','Copilot package removal pattern','windows_10/remove-copilot.ps1','package selection data','adapted','Copilot AppX profile','src/WinCare/Providers/108-CleanupMods.ps1#Get-WinCareAppxSelectionProfile','Copilot removal uses resolved package identities.','No package name is passed from untrusted shell text.','high'),
 ('STARTUP-AUDIT','Startup inspection','Startup Registry audit','powershell/check-startup.ps1','inspection reference','superseded','Existing startup inventory','src/WinCare/Providers/40-Startup.ps1#Get-WinCareStartupItem','The existing WinCare startup provider remains authoritative.','A second startup inventory is not introduced.','low'),
 ('OBSOLETE-KB-REMOVAL','Update safety','Static removal of historical telemetry KBs','tweaks/rem_spy_updates.cmd','negative lesson','rejected-with-reason','n/a','n/a','Static KB removal is rejected as stale and build-unsafe.','No historical KB is uninstalled without live Windows Update identity and compatibility evidence.','critical'),
 ('NETWORK-TWEAK','Network tuning','Static per-adapter TcpAckFrequency and TCPNoDelay mutations','tweaks/net_tweaks.cmd','experimental reference','reference-only','n/a','n/a','Network tweaks remain reference-only pending measured experiment support.','No unmeasured global network mutation is added to a one-click profile.','high'),
],
}
LANG={'.ps1':'PowerShell','.psm1':'PowerShell','.ahk':'AutoHotkey','.bat':'Batch','.cmd':'Batch','.py':'Python','.json':'JSON','.yml':'YAML','.yaml':'YAML','.md':'Markdown','.txt':'Text','.png':'Image'}

def sha(p:Path)->str:
 h=hashlib.sha256()
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
 return h.hexdigest()
def read_csv(p):
 with p.open(newline='',encoding='utf-8-sig') as f:return list(csv.DictReader(f))
def write_csv(p,cols,rows):
 with p.open('w',newline='',encoding='utf-8') as f:
  w=csv.DictWriter(f,fieldnames=cols,lineterminator='\n');w.writeheader();w.writerows(rows)
def classify(rel,p):
 low=rel.lower();ext=p.suffix.lower();name=p.name.lower()
 if low.startswith('.github/') or name in {'.gitignore','.gitattributes'}: c='repository-administration'
 elif name.startswith(('license','copying','notice')): c='governance'
 elif name.startswith('readme') or ext in {'.md','.txt'}: c='documentation'
 elif ext in {'.ps1','.psm1','.ahk','.bat','.cmd','.py'}: c='script'
 elif ext in {'.json','.yml','.yaml'}: c='configuration'
 elif ext in {'.png','.jpg','.jpeg','.gif','.ico','.svg'}: c='media'
 else: c='repository-administration'
 auth='authoritative' if c in {'script','configuration'} else ('derived' if c=='media' else 'supporting')
 return ext or '[no-extension]',LANG.get(ext,'Other'),c,auth

def main():
 scan={Path(x['path']).name:x for x in json.loads(SCAN.read_text())}
 ledger=[r for r in read_csv(DOCS/'adoption-matrix.csv') if int(r['donor'][1:])<106]
 surfaces=[r for r in read_csv(DOCS/'surface-accountability.csv') if int(r['donor'][1:])<106]
 inventory=[r for r in json.loads((DOCS/'donor-inventory.json').read_text()) if int(str(r['donor_id'])[1:])<106]
 for d in DONORS:
  root=d['root']; files=sorted((p for p in root.rglob('*') if p.is_file() and not p.is_symlink()),key=lambda p:p.relative_to(root).as_posix().casefold())
  manifest=scan[d['archive']]; parent=f"{d['id']}-SURFACE-ACCOUNTABILITY"; rep='README.md' if (root/'README.md').is_file() else files[0].relative_to(root).as_posix()
  ledger.append({'record_id':parent,'parent_record_id':'n/a','composition_group_id':f"CG-W7-{d['id']}-ACCOUNTABILITY",'donor':d['id'],'domain':'Donor accountability','value_unit':f"Complete file accountability for {d['name']}",'source_granularity':'repository/archive surface','value_form':'accountability evidence','separability':'standalone','donor_path':rep,'donor_sha256':sha(root/rep),'donor_symbol':'n/a','transformation':'evidence inventory and disposition mapping','mapping_topology':'custom:one donor to complete surface accountability','disposition':'reference-only','decision_rationale':'Every supplied regular file was hashed, classified, and linked; material semantics receive narrower child records.','target_capability':'n/a','target_nodes':'n/a','invariant':'No donor surface is silently omitted.','negative_invariant':'File counts are not treated as semantic adoption.','test_node':'n/a','operator_surface':'n/a','migration_impact':'n/a','license_note':d['license'],'risk_tier':'low','dependency_record_ids':'n/a','evidence_confidence':'high','validation_status':'reviewed'})
  links={}
  for suffix,domain,value,path,form,disp,cap,nodes,inv,neg,risk in SPECS[d['id']]:
   full=root/path
   if not full.is_file(): raise FileNotFoundError(full)
   rid=f"{d['id']}-{suffix}";links.setdefault(path,[]).append(rid)
   ledger.append({'record_id':rid,'parent_record_id':parent,'composition_group_id':f"CG-W7-{suffix}",'donor':d['id'],'domain':domain,'value_unit':value,'source_granularity':'mechanism, preset, workflow, or negative evidence','value_form':form,'separability':'context-dependent','donor_path':path,'donor_sha256':sha(full),'donor_symbol':'n/a','transformation':'target-native synthesis, adaptation, hardening, or guardrail derivation','mapping_topology':'many-to-many','disposition':disp,'decision_rationale':f"{value} was evaluated against WinCare typed actions, policy, preview, journaling, bounded paths, and recovery. Donor packaging and unbounded mutation paths were not imported.",'target_capability':cap,'target_nodes':nodes,'invariant':inv,'negative_invariant':neg,'test_node':TEST if disp!='reference-only' else 'n/a','operator_surface':'Peer Utilities TUI, headless JSON, preview receipts, and transaction journal' if disp not in {'reference-only','rejected-with-reason'} else 'n/a','migration_impact':'additive WinCare 4.6 capability; existing transaction and policy owners remain authoritative' if disp!='reference-only' else 'n/a','license_note':d['license'],'risk_tier':risk,'dependency_record_ids':'n/a','evidence_confidence':'high','validation_status':'statically-validated' if disp not in {'reference-only','rejected-with-reason'} else ('reviewed' if disp=='reference-only' else 'statically-validated')})
  counts=Counter();langs=Counter();total=0
  for f in files:
   rel=f.relative_to(root).as_posix();st=f.stat();total+=st.st_size;ft,lang,cls,auth=classify(rel,f);counts[cls]+=1;langs[lang]+=1
   surfaces.append({'donor':d['id'],'path':rel,'sha256':sha(f),'size_bytes':str(st.st_size),'file_type':ft,'language':lang,'classification':cls,'authority_status':auth,'semantic_record_ids':';'.join([parent]+links.get(rel,[])),'notes':'Classified and linked during cumulative WinCare 4.6 convergence.'})
  inventory.append({'donor_id':d['id'],'name':d['name'],'archive':d['archive'],'archive_sha256':manifest['sha256'],'duplicate_of':None,'member_count':manifest['member_count'],'files':len(files),'regular_files':len(files),'symlinks':[],'expanded_bytes':total,'classifications':dict(sorted(counts.items())),'languages':dict(sorted(langs.items())),'licenses':[f.relative_to(root).as_posix() for f in files if f.name.lower().startswith(('license','copying'))],'manifests':[],'readmes':[f.relative_to(root).as_posix() for f in files if f.name.lower().startswith('readme')],'issues':manifest.get('warnings',[])})
 write_csv(DOCS/'adoption-matrix.csv',LEDGER_COLUMNS,ledger);write_csv(DOCS/'surface-accountability.csv',SURFACE_COLUMNS,surfaces);(DOCS/'donor-inventory.json').write_text(json.dumps(inventory,indent=2,ensure_ascii=False)+'\n')
 print(json.dumps({'donors':len(inventory),'semanticRecords':len(ledger),'surfaces':len(surfaces),'newDonors':4,'newRegularFiles':sum(1 for d in DONORS for p in d['root'].rglob('*') if p.is_file() and not p.is_symlink())},indent=2))
if __name__=='__main__':main()
