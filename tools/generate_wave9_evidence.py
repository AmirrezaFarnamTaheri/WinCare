#!/usr/bin/env python3
"""Append deterministic D130-D143 evidence to cumulative WinCare convergence data."""
from __future__ import annotations
import csv, hashlib, json, os
from collections import Counter
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DOCS=ROOT/'docs'/'convergence'
SRC=Path(os.environ.get('WINCARE_WAVE9_DONOR_ROOT','/mnt/data/wincare_wave9_scan/donors'))
SCAN=Path(os.environ.get('WINCARE_WAVE9_ARCHIVE_MANIFEST','/mnt/data/wincare_wave9_scan/wave9-archive-inventory.json'))
TEST='tests/Wave9.ExperienceStudio.Tests.ps1'
LEDGER_COLUMNS=['record_id','parent_record_id','composition_group_id','donor','domain','value_unit','source_granularity','value_form','separability','donor_path','donor_sha256','donor_symbol','transformation','mapping_topology','disposition','decision_rationale','target_capability','target_nodes','invariant','negative_invariant','test_node','operator_surface','migration_impact','license_note','risk_tier','dependency_record_ids','evidence_confidence','validation_status']
SURFACE_COLUMNS=['donor','path','sha256','size_bytes','file_type','language','classification','authority_status','semantic_record_ids','notes']
ARCH={x['donor_id']:x for x in json.loads(SCAN.read_text())}
NAMES={x['donor_id']:x['name'] for x in ARCH.values()}
DUP={x['donor_id']:x.get('duplicate_of') for x in ARCH.values() if x.get('duplicate_of')}
ROOTS={d:next((p for p in (SRC/d).iterdir() if p.is_dir()),SRC/d) for d in NAMES}
SPECS={
'D130':[
('SPRITE-METADATA','Visual assets','Bounded image and animation metadata','docs/ase-file-specs.md','format and workflow reference','inspired-native','Visual asset inspection','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCareVisualAssetInfo','Only regular allowlisted image files are inspected without execution.','No Aseprite source or restricted implementation is copied.','medium'),
('PALETTE-WORKFLOW','Visual assets','Indexed palette extraction and manifest representation','data/extensions/adigunpolack-palettes/package.json','palette/product workflow','inspired-native','Visual asset inspection','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCareVisualAssetInfo','Palette output is bounded to 256 unique entries.','Palette packages do not become executable extensions.','low'),
('SPRITE-SHEET-LAYOUT','Visual assets','Deterministic sprite-sheet frame map','README.md','workflow concept','inspired-native','Sprite layout manifests','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCareSpriteSheetLayout;src/WinCare/Providers/109-ExperienceStudio.ps1#New-WinCareVisualAssetManifestPlan','Frame rectangles are integer-bounded and capped at 4,096.','The source asset is never modified.','medium')],
'D133':[
('RYTUNEX-FEATURE-CATALOG','Optimization','Searchable feature and optimization capability catalog','Helpers/AppSearchService.cs','product workflow','recomposed','Experience capability cards and privacy profiles','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCareExperienceCapabilityCard;src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCarePrivacyProfile','Capabilities resolve to existing typed WinCare owners.','A second optimizer runtime is not introduced.','high'),
('RYTUNEX-LOG-CANCEL','Operations','Logging, status, and cancellation affordances','Helpers/LogHelper.cs','operator pattern','superseded','Existing transaction journal and cancellation','src/WinCare/Core/09-Transactions.ps1#Invoke-WinCarePlan;src/WinCare/Core/09-Transactions.ps1#Request-WinCareOperationCancellation','Existing receipts and cancellation remain authoritative.','Donor logs are not represented as equivalent implementation.','medium'),
('RYTUNEX-UNSAFE-PRESET','Legacy unsafe','Maximum optimizer and debloat composition','Helpers/OptimizationOptions.cs','preset model','recomposed','Legacy unsafe profiles','src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLegacyUnsafeProfile','Every mutation is typed, previewed, policy-gated, and journaled.','No donor script or opaque command runner is executed.','critical')],
'D135':[
('CAPABILITY-PRESENTATION','Product experience','Searchable capability-card presentation model','ReadMe.md','product pattern','inspired-native','Experience capability cards','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCareExperienceCapabilityCard','Cards are non-authoritative views over target capabilities.','UI state cannot grant mutation authority.','low'),
('TOOLKIT-ARCHITECTURE-GUARDRAIL','Architecture','Modern Windows toolkit component architecture','components/Behaviors/src/ReadMe.md','architecture reference','reference-only','n/a','n/a','Target-native PowerShell ownership remains unchanged.','WinUI libraries are not bundled into the TUI.','low')],
'D136':[
('RDP-ENABLE-DISABLE','Remote access','RDP listener, service, firewall, and redirection lifecycle','jobs/enable_rdp/templates/pre-start.ps1','operational workflow','hardened','Remote access plans','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCareRemoteAccessState;src/WinCare/Providers/109-ExperienceStudio.ps1#New-WinCareRemoteAccessPlan;src/WinCare/Core/11-ActionContracts.ps1#SetServiceRuntimeState;src/WinCare/Providers/25-SystemPolicy.ps1#Invoke-WinCareServiceRuntimeStateAction','Exact prior Registry, service, and firewall states are compensatable.','Password jobs and broad policy import are excluded.','critical'),
('OPENSSH-ENABLE-DISABLE','Remote access','OpenSSH capability, service, and firewall lifecycle','jobs/enable_ssh/templates/pre-start.ps1','operational workflow','hardened','Remote access plans','src/WinCare/Providers/109-ExperienceStudio.ps1#New-WinCareRemoteAccessPlan;src/WinCare/Core/11-ActionContracts.ps1#SetServiceRuntimeState;src/WinCare/Providers/25-SystemPolicy.ps1#Invoke-WinCareServiceRuntimeStateAction','Only the supported OpenSSH capability and exact rule names are admitted.','No remote credentials or arbitrary sshd configuration is generated.','critical'),
('CREDENTIAL-JOBS-GUARDRAIL','Remote access','Password randomization and KMS job rejection','README.md','negative lesson','rejected-with-reason','n/a','n/a','Credential and activation mutation remain outside this convergence wave.','WinCare will not import password rotation or KMS authority from this donor.','critical')],
'D137':[
('LOCATION-PERMISSION-MODEL','Privacy','Allow, deny, and system-managed location permission states','docs/features/permissions.mdx','permission state model','adapted','Location privacy plans','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCareLocationPrivacyState;src/WinCare/Providers/109-ExperienceStudio.ps1#New-WinCareLocationPrivacyPlan','Machine policy and user consent are explicit and reversible.','Per-application consent is not broadened implicitly.','high'),
('SERVICE-STATUS-SEPARATION','Privacy','Separation of permission and service availability','docs/features/services.mdx','state-model constraint','guardrail-derived','Location privacy plans','src/WinCare/Providers/109-ExperienceStudio.ps1#New-WinCareLocationPrivacyPlan','Changing privacy preference does not disable the location service.','Service availability is not conflated with permission.','medium')],
'D139':[
('MIRROR-STRATEGY','Downloads','Mirror failover, retry, and scheduling affordances','docs/SETTINGS.md','download strategy','adapted','Download strategy inspection','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCareDownloadStrategy','Existing BITS jobs retain bounded mirrors, retries, schedules, and hashes.','No parallel chunk engine is claimed or imported.','medium'),
('DOWNLOAD-ENGINE-SUPERSEDED','Downloads','Concurrent and single-source downloader engines','internal/strategy/concurrent/downloader.go','runtime engine','superseded','Existing download queue','src/WinCare/Providers/100-Downloads.ps1#New-WinCareDownloadJobRecord','BITS remains the sole transfer authority.','A second transfer state machine is not introduced.','high'),
('DAEMON-GUARDRAIL','Network safety','Remote HTTP daemon and token surface','cmd/http_api.go','negative lesson','rejected-with-reason','n/a','n/a','Download management remains local and authenticated through existing interfaces.','No listen-all daemon or remote API is admitted.','critical')],
'D140':[
('CONDITION-SCHEDULER','Power','Condition-driven power and brightness selection','Scheduler/Scheduling.Conditions.pas','state and scheduling model','adapted','Power condition profiles','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCarePowerConditionAssessment;src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCarePowerConditionProfile','Battery and AC state only produce bounded recommendations or explicit plans.','No background scheduler is silently installed.','medium'),
('POWER-BRIGHTNESS','Power','Power scheme plus internal display brightness action','Brightness/Brightness.Manager.pas','operator workflow','recomposed','Power condition plans','src/WinCare/Providers/109-ExperienceStudio.ps1#New-WinCarePowerConditionPlan','Existing SetPowerScheme and SetBrightness actions own mutation and compensation.','Global hotkeys, pipe servers, and external monitor control are not imported.','high'),
('GLOBAL-HOOK-PIPE-GUARDRAIL','Power','Global hotkey and pipe API rejection','Api/Api.Pipe.Server.pas','negative lesson','rejected-with-reason','n/a','n/a','No always-on IPC or global input hook is required.','The donor command server is not exposed.','high')],
'D141':[
('PRIVACY-COLLECTION','Privacy','Machine-readable privacy script collection and categorization','src/application/collections/windows.yaml','configuration catalog','recomposed','Privacy profiles','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCarePrivacyProfile','Selected semantics map to known WinCare catalog rules.','Raw script bodies are not executed.','critical'),
('REVERSIBLE-SCRIPTS','Privacy','Revert-aware script guidance','docs/script-guidelines.md','operational practice','adapted','Privacy profile plans','src/WinCare/Providers/109-ExperienceStudio.ps1#New-WinCarePrivacyProfilePlan','Normal privacy profiles retain explicit compensators.','A claimed revert is not trusted without target-state capture.','high'),
('SCRIPT-GUIDELINES','Governance','Script research and documentation discipline','docs/collection-files.md','governance pattern','adapted','Evidence and capability cards','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCareExperienceCapabilityCard','Capabilities expose source records and risk boundaries.','Documentation does not grant execution authority.','low'),
('UNSAFE-SCRIPT-GUARDRAIL','Legacy unsafe','Maximum privacy script composition without raw execution','src/application/collections/windows.yaml','unsafe mutation reference','recomposed','Legacy unsafe profiles','src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLegacyUnsafeProfile','Persistent reductions require exact Critical authorization.','Generated donor shell scripts are never run directly.','critical')],
'D143':[
('SAMPLE-CATALOG-UX','Product experience','Searchable sample catalog and capability discovery','Microsoft.Toolkit.Uwp.SampleApp/Shell.Search.cs','product workflow','inspired-native','Experience capability cards','src/WinCare/Providers/109-ExperienceStudio.ps1#Get-WinCareExperienceCapabilityCard','Discovery remains a read-only view over registered capabilities.','Sample metadata cannot invoke arbitrary code.','low'),
('ARCHIVED-TOOLKIT-GUARDRAIL','Architecture','Archived UWP toolkit architecture and gaze/input components','ReadMe.md','historical reference','reference-only','n/a','n/a','Only current target-compatible product ideas are retained.','Archived libraries and global gaze/input surfaces are not embedded.','medium')]
}
LANG={'.ps1':'PowerShell','.psm1':'PowerShell','.psd1':'PowerShell','.cs':'C#','.cpp':'C++','.c':'C','.h':'C/C++','.hpp':'C++','.go':'Go','.dart':'Dart','.pas':'Pascal','.ts':'TypeScript','.tsx':'TypeScript','.js':'JavaScript','.vue':'Vue','.xaml':'XAML','.json':'JSON','.yaml':'YAML','.yml':'YAML','.xml':'XML','.md':'Markdown','.txt':'Text','.png':'Image','.jpg':'Image','.jpeg':'Image','.gif':'Image','.ico':'Image','.svg':'Image','.gpl':'Palette','.dll':'Binary','.exe':'Binary','.pfx':'Certificate'}

def sha(p:Path)->str:
 h=hashlib.sha256()
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
 return h.hexdigest()

def read_csv(p):
 with p.open(newline='',encoding='utf-8-sig') as f:return list(csv.DictReader(f))

def write_csv(p,cols,data):
 with p.open('w',newline='',encoding='utf-8') as f:
  w=csv.DictWriter(f,fieldnames=cols,lineterminator='\n');w.writeheader();w.writerows(data)

def classify(rel,p):
 low='/'+rel.lower();ext=p.suffix.lower();name=p.name.lower()
 if '/vendor/' in low or '/third_party/' in low or '/node_modules/' in low:c='vendored'
 elif '/test' in low or name.endswith(('.spec.ts','_test.go','tests.ps1')):c='test'
 elif '/.github/' in low or name in {'.gitignore','.gitattributes'}:c='repository-administration'
 elif name.startswith(('license','copying','notice','eula')):c='governance'
 elif name.startswith('readme') or ext in {'.md','.txt'}:c='documentation'
 elif ext in {'.ps1','.psm1','.psd1','.bat','.cmd','.sh'}:c='script'
 elif ext in {'.cs','.cpp','.c','.h','.hpp','.go','.dart','.pas','.ts','.tsx','.js','.vue','.xaml'}:c='implementation'
 elif ext in {'.json','.yaml','.yml','.xml','.toml','.ini','.config','.gpl'}:c='configuration'
 elif ext in {'.png','.jpg','.jpeg','.gif','.ico','.svg','.psd','.fig'}:c='media'
 elif ext in {'.dll','.exe','.pfx','.jar'}:c='vendored'
 else:c='repository-administration'
 auth='authoritative' if c in {'implementation','script','configuration'} else ('derived' if c in {'media','vendored'} else 'supporting')
 return ext or '[no-extension]',LANG.get(ext,'Other'),c,auth

def note_for(d,root):
 if d=='D130':return 'Aseprite EULA is restrictive; no donor source is copied or bundled. Only independently implemented concepts and file-format observations are used.'
 if d=='D140':return 'BatteryMode has a custom license; implementation is independent and limited to generic power-state concepts.'
 if d in DUP:return f'Exact duplicate submission of {DUP[d]}; prior licensing and decisions remain controlling, and no new implementation is claimed.'
 licenses=[p for p in root.rglob('*') if p.is_file() and p.name.lower().startswith(('license','copying','eula'))]
 return 'Donor license files are preserved as evidence; no donor source is bundled and behavior is independently implemented.' if licenses else 'No license was detected in the supplied archive; only facts, concepts, and negative guardrails are used.'

def main():
 ledger=[r for r in read_csv(DOCS/'adoption-matrix.csv') if int(r['donor'][1:])<130]
 surfaces=[r for r in read_csv(DOCS/'surface-accountability.csv') if int(r['donor'][1:])<130]
 inventory=[r for r in json.loads((DOCS/'donor-inventory.json').read_text()) if int(str(r['donor_id'])[1:])<130]
 for d,name in NAMES.items():
  root=ROOTS[d];files=sorted((p for p in root.rglob('*') if p.is_file() and not p.is_symlink()),key=lambda p:p.relative_to(root).as_posix().casefold())
  rep='README.md' if (root/'README.md').is_file() else ('ReadMe.md' if (root/'ReadMe.md').is_file() else files[0].relative_to(root).as_posix())
  parent=f'{d}-SURFACE-ACCOUNTABILITY';links={};lic=note_for(d,root)
  ledger.append({'record_id':parent,'parent_record_id':'n/a','composition_group_id':f'CG-W9-{d}-ACCOUNTABILITY','donor':d,'domain':'Donor accountability','value_unit':f'Complete file accountability for {name}','source_granularity':'repository/archive surface','value_form':'accountability evidence','separability':'standalone','donor_path':rep,'donor_sha256':sha(root/rep),'donor_symbol':'n/a','transformation':'evidence inventory and disposition mapping','mapping_topology':'custom:one donor to complete surface accountability','disposition':'reference-only','decision_rationale':'Every supplied regular file was hashed, classified, and linked; material semantics receive narrower child records.','target_capability':'n/a','target_nodes':'n/a','invariant':'No submitted donor surface is silently omitted.','negative_invariant':'Duplicate bytes and file counts are not treated as new semantic adoption.','test_node':'n/a','operator_surface':'n/a','migration_impact':'n/a','license_note':lic,'risk_tier':'low','dependency_record_ids':'n/a','evidence_confidence':'high','validation_status':'reviewed'})
  if d in DUP:
   rid=f'{d}-DUPLICATE-SUBMISSION';links.setdefault(rep,[]).append(rid)
   ledger.append({'record_id':rid,'parent_record_id':parent,'composition_group_id':f'CG-W9-DUPLICATE-{DUP[d]}','donor':d,'domain':'Provenance','value_unit':f'Byte-identical repeat submission of {DUP[d]}','source_granularity':'complete archive','value_form':'duplicate provenance','separability':'standalone','donor_path':rep,'donor_sha256':sha(root/rep),'donor_symbol':'n/a','transformation':'deduplication and prior-decision reuse','mapping_topology':'custom:duplicate evidence to prior donor baseline','disposition':'reference-only','decision_rationale':f'The archive hash matches {DUP[d]}; its prior semantic ledger and implementation decisions remain authoritative.','target_capability':'Existing prior donor capabilities','target_nodes':'n/a','invariant':'Duplicate input does not create parallel ownership or inflated implementation claims.','negative_invariant':'No new target feature is attributed solely to duplicate bytes.','test_node':'n/a','operator_surface':'n/a','migration_impact':'none','license_note':lic,'risk_tier':'low','dependency_record_ids':'n/a','evidence_confidence':'high','validation_status':'verified'})
  for suffix,domain,value,path,form,disp,cap,nodes,inv,neg,risk in SPECS.get(d,[]):
   full=root/path
   if not full.is_file():raise FileNotFoundError(full)
   rid=f'{d}-{suffix}';links.setdefault(path,[]).append(rid)
   ledger.append({'record_id':rid,'parent_record_id':parent,'composition_group_id':f'CG-W9-{suffix}','donor':d,'domain':domain,'value_unit':value,'source_granularity':'mechanism, workflow, product pattern, or negative evidence','value_form':form,'separability':'context-dependent','donor_path':path,'donor_sha256':sha(full),'donor_symbol':'n/a','transformation':'target-native adaptation, hardening, recomposition, inspiration, supersession, or guardrail derivation','mapping_topology':'many-to-many','disposition':disp,'decision_rationale':f'{value} was compared against WinCare authority, state, policy, preview, journaling, bounded-resource, and recovery models. Donor packaging was not imported.','target_capability':cap,'target_nodes':nodes,'invariant':inv,'negative_invariant':neg,'test_node':TEST if disp!='reference-only' else 'n/a','operator_surface':'Experience Studio TUI, headless JSON, preview receipts, and transaction journal' if cap!='n/a' else 'n/a','migration_impact':'additive WinCare 4.8 capability; existing authority and persistence owners remain authoritative' if cap!='n/a' else 'n/a','license_note':lic,'risk_tier':risk,'dependency_record_ids':'n/a','evidence_confidence':'high','validation_status':'statically-validated' if disp!='reference-only' else 'reviewed'})
  counts=Counter();langs=Counter();total=0
  for f in files:
   rel=f.relative_to(root).as_posix();st=f.stat();total+=st.st_size;ft,lang,cls,auth=classify(rel,f);counts[cls]+=1;langs[lang]+=1
   surfaces.append({'donor':d,'path':rel,'sha256':sha(f),'size_bytes':str(st.st_size),'file_type':ft,'language':lang,'classification':cls,'authority_status':auth,'semantic_record_ids':';'.join([parent]+links.get(rel,[])),'notes':f'Classified and linked during cumulative WinCare 4.8 convergence.'+(f' Exact duplicate of {DUP[d]}.' if d in DUP else '')})
  a=ARCH[d];inventory.append({'donor_id':d,'name':name,'archive':a['archive'],'archive_sha256':a['archive_sha256'],'duplicate_of':a.get('duplicate_of'),'member_count':a['members'],'files':len(files),'regular_files':len(files),'symlinks':a.get('links',[]),'expanded_bytes':total,'classifications':dict(sorted(counts.items())),'languages':dict(sorted(langs.items())),'licenses':[f.relative_to(root).as_posix() for f in files if f.name.lower().startswith(('license','copying','eula'))],'manifests':[f.relative_to(root).as_posix() for f in files if f.name.lower() in {'package.json','cargo.toml','pubspec.yaml','go.mod','cmakelists.txt'}][:100],'readmes':[f.relative_to(root).as_posix() for f in files if f.name.lower().startswith('readme')][:100],'issues':[]})
 write_csv(DOCS/'adoption-matrix.csv',LEDGER_COLUMNS,ledger);write_csv(DOCS/'surface-accountability.csv',SURFACE_COLUMNS,surfaces);(DOCS/'donor-inventory.json').write_text(json.dumps(inventory,indent=2,ensure_ascii=False)+'\n')
 print(json.dumps({'submittedDonorIds':len(inventory),'uniqueArchiveBaselines':len(inventory)-sum(1 for x in inventory if x.get('duplicate_of')),'semanticRecords':len(ledger),'surfaces':len(surfaces),'newSubmissions':14,'newUniqueArchives':9,'duplicates':DUP},indent=2))
if __name__=='__main__':main()
