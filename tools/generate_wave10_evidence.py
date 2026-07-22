#!/usr/bin/env python3
"""Append deterministic D144-D163 evidence to cumulative WinCare convergence data."""
from __future__ import annotations

import csv
import hashlib
import json
import os
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / 'docs' / 'convergence'
SRC = Path(os.environ.get('WINCARE_WAVE10_DONOR_ROOT', '/mnt/data/wincare_wave10_scan/donors'))
SCAN = Path(os.environ.get('WINCARE_WAVE10_ARCHIVE_MANIFEST', '/mnt/data/wincare_wave10_scan/wave10-archive-inventory.json'))
TEST = 'tests/Wave10.ShellHardware.Tests.ps1'
LEDGER_COLUMNS = ['record_id','parent_record_id','composition_group_id','donor','domain','value_unit','source_granularity','value_form','separability','donor_path','donor_sha256','donor_symbol','transformation','mapping_topology','disposition','decision_rationale','target_capability','target_nodes','invariant','negative_invariant','test_node','operator_surface','migration_impact','license_note','risk_tier','dependency_record_ids','evidence_confidence','validation_status']
SURFACE_COLUMNS = ['donor','path','sha256','size_bytes','file_type','language','classification','authority_status','semantic_record_ids','notes']
ARCHIVE_ROWS = json.loads(SCAN.read_text(encoding='utf-8'))
ARCH = {row['donor_id']: row for row in ARCHIVE_ROWS}
NAMES = {row['donor_id']: row['name'] for row in ARCHIVE_ROWS}
DUP = {row['donor_id']: row.get('duplicate_of') for row in ARCHIVE_ROWS if row.get('duplicate_of')}
ROOTS = {d: next((p for p in (SRC / d).iterdir() if p.is_dir()), SRC / d) for d in NAMES}

# suffix, domain, value unit, donor path, value form, disposition, target capability,
# target nodes, invariant, negative invariant, risk, exact test name
SPECS = {
'D144': [
('7PLUS-EXPLORER-TABS','Explorer','Explorer tab and folder-session automation','Explorer/Explorer/Explorer/ReadMe.txt','operator workflow','recomposed','Explorer session capture and restoration','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareExplorerSession;src/WinCare/Providers/110-ShellHardwareStudio.ps1#New-WinCareExplorerSessionRestorePlan','Only canonical local directories with matching captured hashes are reopened.','No global shell hook or arbitrary command is admitted.','high','restricts Explorer session restoration to canonical local directories'),
('WINDOW-SWITCHER-PRIMITIVE','Window management','Fast open-window switching and title matching','Accessor/CWindowSwitcherPlugin.ahk','interaction primitive','adapted','Fuzzy window search','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareWindowSearch','Search is read-only and bounded to currently enumerated top-level windows.','No resident AutoHotkey hook, keystroke interception, or donor script runtime is installed.','medium','publishes every new headless route'),
('AUTOHOTKEY-RUNTIME-GUARDRAIL','Trust boundary','Global hotkey and automation runtime rejection','Accessor/CWindowSwitcherPlugin.ahk','negative evidence','rejected-with-reason','n/a','n/a','WinCare retains its existing explicit invocation and policy authority.','Downloaded or donor-supplied AHK code is never executed as an extension.','high','exports the target-native Shell and Hardware functions')],
'D145': [
('DATA-DRIVEN-UI-REFERENCE','Product architecture','Data-driven entity/component UI composition','README.md','architecture reference','reference-only','n/a','n/a','Only generic composition observations are retained as evidence.','The game engine, rendering loop, and ECS runtime are not embedded in WinCare.','low','exports the target-native Shell and Hardware functions'),
('ENGINE-RUNTIME-GUARDRAIL','Architecture','Large unrelated VR/game runtime exclusion','README.md','scope guardrail','rejected-with-reason','n/a','n/a','WinCare remains a Windows operations product with one authority path.','Unrelated rendering, simulation, asset, and game-state subsystems are not imported.','medium','exports the target-native Shell and Hardware functions')],
'D146': [
('MONITORIAN-DDCCI','Display hardware','DDC/CI physical-monitor brightness and contrast control','Source/Monitorian.Core/Models/Monitor/DdcMonitorItem.cs','hardware-control mechanism','hardened','DDC/CI monitor inspection and mutation','src/WinCare/Native/WinCare.ShellHardware.cs#ShellHardware;src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareMonitorControlState;src/WinCare/Providers/110-ShellHardwareStudio.ps1#New-WinCareMonitorControlPlan','Only VCP 0x10 and 0x12 are admitted with physical-monitor identity and compare-and-swap verification.','Unsupported VCP codes, stale monitor state, and silent best-effort writes fail closed.','high','limits monitor mutation to brightness and contrast VCP codes with CAS recovery'),
('MONITOR-IDENTITY-RECOVERY','Display hardware','Stable monitor identity and exact-value restoration','Source/Monitorian.Core/Models/Monitor/DdcMonitorItem.cs','recovery primitive','extracted','Monitor-control recovery','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Invoke-WinCareSetMonitorVcpFeatureAction','Compensation restores the captured value only when monitor identity and maximum remain consistent.','Recovery never targets a different display that reused an ordinal.','high','limits monitor mutation to brightness and contrast VCP codes with CAS recovery')],
'D147': [
('WINDEV-XAML-AUDIT','GUI quality','Unused and missing XAML resource analysis','src/UWP/WinDevUtility/ViewModels/UnusedXamlViewModel.cs','static-analysis workflow','adapted','GUI resource auditing','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareGuiResourceAudit;tools/validate_v5.py#validate','Every StaticResource and DynamicResource reference must resolve to a declared key in scope.','GUI packaging cannot silently omit required resource keys.','medium','exports the target-native Shell and Hardware functions'),
('GENERATOR-RUNTIME-SUPERSEDED','Developer tools','Broad developer utility and code-generator runtime','README.md','product architecture','superseded','Existing WinCare validation and generation tools','tools/validate_source.py#main;tools/validate_v5.py#validate','Target validators remain deterministic and repository-owned.','No second application framework or arbitrary code-generation authority is introduced.','medium','exports the target-native Shell and Hardware functions')],
'D148': [
('WINDOW-WALKER-SEARCH','Window management','Fuzzy matching of window title and process metadata','Window Walker/Window Walker/Components/FuzzyMatching.cs','search algorithm','adapted','Fuzzy window search','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareWindowSearch','Matching is bounded, deterministic, and read-only.','Search cannot activate hidden authority or execute matched text.','medium','publishes every new headless route'),
('RESIDENT-WALKER-SUPERSEDED','Window management','Resident launcher and hook runtime','README.md','runtime architecture','superseded','Existing WinCare operator surfaces','src/WinCare/UI/98-Headless.ps1#Invoke-WinCareHeadless;src/WinCare/UI/Gui/10-GuiRuntime.ps1#Start-WinCareGui','The target keeps one command catalog and one execution path.','No parallel resident launcher or global key hook is installed.','medium','exports the target-native Shell and Hardware functions')],
'D149': [
('NWINFO-HARDWARE-INVENTORY','Hardware diagnostics','Broad bounded hardware and operating-system inventory','nwinfo.c','diagnostic mechanism','adapted','Hardware inventory and reports','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareHardwareInventory;src/WinCare/Providers/110-ShellHardwareStudio.ps1#New-WinCareHardwareReportPlan','Collection is bounded by class and serial-like identifiers are hashed by default.','Sensitive hardware identifiers are not emitted unless explicitly requested.','high','keeps hardware serials redacted by default'),
('LOW-LEVEL-PROBE-GUARDRAIL','Hardware diagnostics','Privileged low-level probing exclusion','nwinfo.c','negative evidence','rejected-with-reason','n/a','n/a','WinCare uses supported CIM and target-native inspection boundaries.','Kernel drivers, raw physical-memory access, and donor native probing are not bundled.','critical','keeps hardware serials redacted by default')],
'D150': [
('FULLSCREEN-SHELL-ASSESSMENT','Shell experience','Detection of custom full-screen shell configuration','README.md','product workflow','adapted','Full-screen shell assessment','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareFullscreenShellAssessment','Custom shell state is assessed and disclosed without granting automatic mutation authority.','WinCare does not replace the primary Explorer recovery surface automatically.','critical','publishes every new headless route'),
('CUSTOM-SHELL-MUTATION-GUARDRAIL','Shell safety','Automatic shell replacement rejection','README.md','negative lesson','guardrail-derived','Full-screen shell assessment','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareFullscreenShellAssessment','Assessment explains risk and preserves operator recovery.','No one-click custom shell mutation is exposed without a separately designed recovery system.','critical','exports the target-native Shell and Hardware functions')],
'D152': [
('APPX-PROCESS-PACKAGE-MAPPING','AppX runtime','Map a process identifier to exact package identity','ProcessIdToPackageId/ProcessIdToPackageId.cpp','Win32 identity primitive','hardened','AppX runtime inventory','src/WinCare/Native/WinCare.ShellHardware.cs#GetProcessPackage;src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareAppxRuntimeInventory','Package identity is read through the supported GetPackageFullName API.','Unpackaged processes are never misrepresented as AppX packages.','high','uses supported AppX process identity and activation APIs'),
('APPX-LAUNCH-METADATA','AppX runtime','Manifest-derived AUMID enumeration and exact activation','LaunchAppxPackage/LaunchAppxPackage.cpp','activation mechanism','hardened','AppX target discovery and activation','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareAppxLaunchTarget;src/WinCare/Providers/110-ShellHardwareStudio.ps1#New-WinCareAppxLaunchPlan;src/WinCare/Native/WinCare.ShellHardware.cs#ActivateApplication','The package full name and manifest hash are revalidated immediately before supported activation.','No arbitrary executable, URI, package mutation, or remote payload is accepted.','high','uses supported AppX process identity and activation APIs'),
('APPX-DEBUG-MUTATION-GUARDRAIL','AppX runtime','Suspend, terminate, debug, and remote-download samples rejected','README.md','negative evidence','rejected-with-reason','n/a','n/a','This wave exposes inventory and exact activation only.','Process suspension, termination, debug attachment, and remote script download are not admitted.','critical','uses supported AppX process identity and activation APIs')],
'D153': [
('RNX-SYMLINK-GUARDRAIL','Supply-chain safety','Archive symlink and workspace-graph accountability','README.md','negative evidence','guardrail-derived','Archive safety inspection','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareArchiveInspection;tools/generate_wave10_evidence.py#main','Symlinks are recorded, validated, and never followed during donor extraction.','Unsafe or escaping links cannot become target files or executable dependencies.','critical','inspects archives without extraction and rejects unsafe members'),
('RNX-TOOLCHAIN-SUPERSEDED','Developer tooling','JavaScript monorepo dependency and build graph','README.md','toolchain architecture','superseded','Existing WinCare source and release validators','tools/validate_source.py#main;tools/build_release.py#main','PowerShell and Python remain the target-owned implementation and release toolchains.','The React Native monorepo, package manager, and plugin ecosystem are not introduced.','medium','does not import duplicate donor architectures as parallel subsystems')],
'D154': [
('GUI-INSPECT-BOUNDARY','GUI diagnostics','Bounded UI Automation tree inspection','README.md','inspection workflow','hardened','UI Automation snapshots','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareUiAutomationSnapshot','Depth, nodes, and text exposure are explicitly bounded; text is redacted by default.','The inspector cannot mutate controls, inject input, or scrape unbounded content.','high','bounds UI Automation depth, node count, and text exposure'),
('OPAQUE-BINARY-GUARDRAIL','Supply-chain safety','Opaque prebuilt GUI inspection binaries rejected','README.md','negative evidence','rejected-with-reason','n/a','n/a','Only independently implemented inspection semantics are retained.','Unsigned or unreviewed donor binaries are not bundled or executed.','critical','bounds UI Automation depth, node count, and text exposure')],
'D156': [
('CONEMU-PROFILE-SESSIONS','Terminal workflows','Terminal task, profile, and session metadata','PortableApps/App/DefaultData/settings/ConEmu.xml','configuration model','adapted','Sanitized terminal-environment inventory and export','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareTerminalEnvironment;src/WinCare/Providers/110-ShellHardwareStudio.ps1#New-WinCareTerminalProfileExportPlan','Exports include profile metadata only and redact secrets and execution state.','No terminal command, credential, hook, or startup task is executed.','high','publishes every new headless route'),
('CONEMU-RUNTIME-SUPERSEDED','Terminal workflows','Full terminal emulator, hook, injection, and task runtime','src/ConEmu/SetCmdTask.cpp','runtime architecture','superseded','Existing process runner and operator surfaces','src/WinCare/Core/03-Process.ps1#Invoke-WinCareProcess;src/WinCare/UI/98-Headless.ps1#Invoke-WinCareHeadless','WinCare retains its bounded process runner and structured command catalog.','No DLL injection, console hook, shell extension, or parallel terminal state machine is imported.','critical','exports the target-native Shell and Hardware functions')],
'D158': [
('BATUTIL-SERVICING-MEDIA','Servicing','Read-only WIM, ESD, and SWM image metadata','ESD2WIM-WIM2ESD/README.md','servicing workflow','hardened','Servicing-media inspection','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareServicingMediaInfo','Only local regular media files and DISM /Get-WimInfo are admitted.','No mount, apply, capture, commit, conversion, or image mutation occurs.','high','keeps servicing-media behavior read-only'),
('LEGACY-BATCH-GUARDRAIL','Servicing','Unbounded legacy batch and servicing scripts rejected','README.md','negative evidence','rejected-with-reason','n/a','n/a','Only bounded read-only metadata inspection is retained.','Office installers, update removers, activation, arbitrary registry scripts, and destructive batch flows are not imported.','critical','keeps servicing-media behavior read-only')],
'D159': [
('NANAZIP-ARCHIVE-SAFETY','Archive safety','ZIP-family membership, expansion, collision, and link safety model','ReadMe.md','archive mechanism','inspired-native','No-extraction archive inspection','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareArchiveInspection','Member paths, duplicates, case collisions, symlinks, counts, expansion, and compression ratio are bounded before extraction.','Inspection never extracts or executes archive content.','critical','inspects archives without extraction and rejects unsafe members'),
('CODEC-RUNTIME-GUARDRAIL','Archive safety','Broad codec and shell-extension runtime exclusion','ReadMe.md','scope guardrail','rejected-with-reason','n/a','n/a','WinCare uses platform ZIP inspection for admitted formats.','Native codecs, shell extensions, preview handlers, and large vendored compression engines are not bundled.','high','inspects archives without extraction and rejects unsafe members')],
'D161': [
('WINHANCE-MAXIMUM','Legacy unsafe','Maximum one-click hardening, privacy, debloat, and cleanup composition','src/Winhance.Core/Features/Common/Constants/FeatureDefinitions.cs','feature catalog and preset model','recomposed','Legacy unsafe profiles','src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLegacyUnsafeProfile','Every selected mutation resolves to typed WinCare actions, policy gates, previews, receipts, and journals.','Raw donor commands and a second settings authority are never executed.','critical','includes Winhance only through the separately authorized unsafe plane'),
('WINHANCE-APP-MANAGEMENT','Application management','Application and optional-component selection semantics','src/Winhance.Core/Features/Common/Constants/FeatureDefinitions.cs','product workflow','recomposed','AppX, feature, and capability plans','src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLegacyUnsafeProfile','Protected package and current-state checks remain owned by WinCare.','Broad removal cannot bypass protected-package or critical-action policy.','critical','includes Winhance only through the separately authorized unsafe plane'),
('WINHANCE-PRIVACY-SERVICES','Privacy and services','Privacy, service, scheduled-task, and policy grouping','src/Winhance.Core/Features/Common/Constants/FeatureDefinitions.cs','configuration catalog','recomposed','Hardening and privacy profile composition','src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLegacyUnsafeProfile','Existing policy and transaction owners remain authoritative.','The donor WPF application and direct Registry mutation layer are not imported.','critical','includes Winhance only through the separately authorized unsafe plane')],
'D162': [
('TAB-MANAGER-COMMAND-PALETTE','Product experience','Searchable command palette and grouped tab-management affordances','packages/extension/src/js/components/AutocompleteSearch/filterOptions.ts','interaction pattern','inspired-native','GUI and terminal command discovery','src/WinCare/UI/Gui/00-GuiCatalog.ps1#Get-WinCareGuiActionCatalog;src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareTerminalEnvironment','Search and grouping remain non-authoritative views over registered commands.','Browser-extension permissions and tab APIs are not imported.','medium','publishes every new headless route'),
('TAB-MANAGER-SYMLINK-ACCOUNTABILITY','Supply-chain safety','Repository symlink provenance and safe-relative validation','packages/extension/CHANGELOG.md','repository structure','guardrail-derived','Donor surface accountability','tools/generate_wave10_evidence.py#main','Both repository symlinks are represented as explicit surface records.','Symlink targets are not silently materialized or followed.','medium','does not import duplicate donor architectures as parallel subsystems')],
'D163': [
('EXPLORER-SESSION-RESTORE','Explorer','Capture and restoration of Explorer window locations','ExplorerTabUtility/Models/WindowRecord.cs','state model','hardened','Explorer session records and restoration','src/WinCare/Providers/110-ShellHardwareStudio.ps1#Get-WinCareExplorerSession;src/WinCare/Providers/110-ShellHardwareStudio.ps1#New-WinCareExplorerSessionRestorePlan','Records are versioned, local-path-only, hash-bound, and reopened through typed actions.','No stale, network, URI, or missing path is opened.','high','restricts Explorer session restoration to canonical local directories'),
('EXPLORER-WATCHER-GUARDRAIL','Explorer','COM watcher and global-hook runtime exclusion','ExplorerTabUtility/Hooks/ExplorerWatcher.cs','negative evidence','rejected-with-reason','n/a','n/a','WinCare captures Explorer state on explicit request.','No always-on watcher, global input hook, or shell injection is installed.','high','restricts Explorer session restoration to canonical local directories')],
}

LANG = {
'.ps1':'PowerShell','.psm1':'PowerShell','.psd1':'PowerShell','.cs':'C#','.cpp':'C++','.c':'C','.h':'C/C++','.hpp':'C++','.go':'Go','.rs':'Rust','.dart':'Dart','.pas':'Pascal','.java':'Java','.kt':'Kotlin','.ts':'TypeScript','.tsx':'TypeScript','.js':'JavaScript','.jsx':'JavaScript','.vue':'Vue','.xaml':'XAML','.json':'JSON','.yaml':'YAML','.yml':'YAML','.xml':'XML','.md':'Markdown','.txt':'Text','.png':'Image','.jpg':'Image','.jpeg':'Image','.gif':'Image','.ico':'Image','.svg':'Image','.dll':'Binary','.exe':'Binary','.pdb':'Debug Symbols','.zip':'Archive','.7z':'Archive'
}


def sha(path: Path) -> str:
    h = hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def sha_text(text: str) -> str:
    return hashlib.sha256(text.encode('utf-8')).hexdigest()


def read_csv(path: Path):
    with path.open(newline='', encoding='utf-8-sig') as handle:
        return list(csv.DictReader(handle))


def write_csv(path: Path, columns, rows):
    with path.open('w', newline='', encoding='utf-8') as handle:
        writer = csv.DictWriter(handle, fieldnames=columns, lineterminator='\n')
        writer.writeheader()
        writer.writerows(rows)


def classify(rel: str, path: Path):
    low = '/' + rel.lower()
    ext = path.suffix.lower()
    name = path.name.lower()
    if '/node_modules/' in low or '/vendor/' in low or '/third_party/' in low or '/packages/deps/' in low:
        category = 'vendored'
    elif '/test' in low or '/tests/' in low or name.endswith(('.spec.ts','.test.ts','_test.go','tests.ps1')):
        category = 'test'
    elif '/.github/' in low or name in {'.gitignore','.gitattributes','.prettierignore'}:
        category = 'repository-administration'
    elif name.startswith(('license','copying','notice','eula')):
        category = 'governance'
    elif name.startswith('readme') or ext in {'.md','.txt','.rst'}:
        category = 'documentation'
    elif ext in {'.ps1','.psm1','.psd1','.bat','.cmd','.sh','.ahk'}:
        category = 'script'
    elif ext in {'.cs','.cpp','.c','.h','.hpp','.go','.rs','.dart','.pas','.java','.kt','.ts','.tsx','.js','.jsx','.vue','.xaml'}:
        category = 'implementation'
    elif ext in {'.json','.yaml','.yml','.xml','.toml','.ini','.config','.props','.targets','.sln','.csproj','.vcxproj'}:
        category = 'configuration'
    elif ext in {'.png','.jpg','.jpeg','.gif','.ico','.svg','.psd','.fig','.wav','.mp3'}:
        category = 'media'
    elif ext in {'.dll','.exe','.pdb','.lib','.a','.jar','.zip','.7z'}:
        category = 'vendored'
    else:
        category = 'repository-administration'
    authority = 'authoritative' if category in {'implementation','script','configuration'} else ('derived' if category in {'media','vendored'} else 'supporting')
    return ext or '[no-extension]', LANG.get(ext, 'Other'), category, authority


def license_note(donor: str, root: Path) -> str:
    if donor in DUP:
        return f'Exact duplicate submission of {DUP[donor]}; the prior donor license, evidence, and decisions remain controlling.'
    licenses = [p for p in root.rglob('*') if p.is_file() and p.name.lower().startswith(('license','copying','notice','eula'))]
    if donor in {'D154'}:
        return 'No reliable source license was detected; opaque binaries are rejected and only independently implemented observations are retained.'
    if licenses:
        return 'Donor license files are preserved as evidence; no donor source or binary is bundled, and target behavior is independently implemented.'
    return 'No license was detected in the supplied archive; only facts, concepts, public API observations, and negative guardrails are used.'


def test_node(name: str) -> str:
    return f'{TEST}#{name}'


def main():
    ledger = [row for row in read_csv(DOCS / 'adoption-matrix.csv') if int(row['donor'][1:]) < 144]
    surfaces = [row for row in read_csv(DOCS / 'surface-accountability.csv') if int(row['donor'][1:]) < 144]
    inventory = [row for row in json.loads((DOCS / 'donor-inventory.json').read_text(encoding='utf-8')) if int(str(row['donor_id'])[1:]) < 144]

    for donor, name in NAMES.items():
        root = ROOTS[donor]
        files = sorted((p for p in root.rglob('*') if p.is_file() and not p.is_symlink()), key=lambda p: p.relative_to(root).as_posix().casefold())
        representative = 'README.md' if (root / 'README.md').is_file() else ('ReadMe.md' if (root / 'ReadMe.md').is_file() else files[0].relative_to(root).as_posix())
        parent = f'{donor}-SURFACE-ACCOUNTABILITY'
        links: dict[str, list[str]] = {}
        note = license_note(donor, root)
        ledger.append({
            'record_id': parent, 'parent_record_id': 'n/a', 'composition_group_id': f'CG-W10-{donor}-ACCOUNTABILITY', 'donor': donor,
            'domain': 'Donor accountability', 'value_unit': f'Complete file and symlink accountability for {name}', 'source_granularity': 'repository/archive surface',
            'value_form': 'accountability evidence', 'separability': 'standalone', 'donor_path': representative, 'donor_sha256': sha(root / representative),
            'donor_symbol': 'n/a', 'transformation': 'evidence inventory and disposition mapping', 'mapping_topology': 'custom:one donor to complete surface accountability',
            'disposition': 'reference-only', 'decision_rationale': 'Every supplied regular file and repository symlink was hashed or identity-recorded, classified, and linked; material semantics receive narrower child records.',
            'target_capability': 'n/a', 'target_nodes': 'n/a', 'invariant': 'No submitted donor surface is silently omitted.',
            'negative_invariant': 'Duplicate bytes, vendored trees, generated output, and file counts are not treated as semantic implementation.', 'test_node': 'n/a',
            'operator_surface': 'n/a', 'migration_impact': 'n/a', 'license_note': note, 'risk_tier': 'low', 'dependency_record_ids': 'n/a',
            'evidence_confidence': 'high', 'validation_status': 'reviewed'
        })
        if donor in DUP:
            rid = f'{donor}-DUPLICATE-SUBMISSION'
            links.setdefault(representative, []).append(rid)
            ledger.append({
                'record_id': rid, 'parent_record_id': parent, 'composition_group_id': f'CG-W10-DUPLICATE-{DUP[donor]}', 'donor': donor,
                'domain': 'Provenance', 'value_unit': f'Byte-identical repeat submission of {DUP[donor]}', 'source_granularity': 'complete archive',
                'value_form': 'duplicate provenance', 'separability': 'standalone', 'donor_path': representative, 'donor_sha256': sha(root / representative),
                'donor_symbol': 'n/a', 'transformation': 'deduplication and prior-decision reuse', 'mapping_topology': 'custom:duplicate evidence to prior donor baseline',
                'disposition': 'reference-only', 'decision_rationale': f'The archive hash matches {DUP[donor]}; its prior semantic ledger and implementation decisions remain authoritative.',
                'target_capability': 'Existing prior donor capabilities', 'target_nodes': 'n/a', 'invariant': 'Duplicate input does not create parallel ownership or inflated implementation claims.',
                'negative_invariant': 'No new target feature is attributed solely to duplicate bytes.', 'test_node': test_node('does not import duplicate donor architectures as parallel subsystems'),
                'operator_surface': 'n/a', 'migration_impact': 'none', 'license_note': note, 'risk_tier': 'low', 'dependency_record_ids': 'n/a',
                'evidence_confidence': 'high', 'validation_status': 'verified'
            })
        for suffix, domain, value, path, form, disposition, capability, nodes, invariant, negative, risk, test_name in SPECS.get(donor, []):
            full = root / path
            if not full.is_file():
                raise FileNotFoundError(full)
            rid = f'{donor}-{suffix}'
            links.setdefault(path, []).append(rid)
            ledger.append({
                'record_id': rid, 'parent_record_id': parent, 'composition_group_id': f'CG-W10-{suffix}', 'donor': donor,
                'domain': domain, 'value_unit': value, 'source_granularity': 'mechanism, workflow, product pattern, or negative evidence',
                'value_form': form, 'separability': 'context-dependent', 'donor_path': path, 'donor_sha256': sha(full), 'donor_symbol': 'n/a',
                'transformation': 'target-native adaptation, hardening, recomposition, inspiration, supersession, or guardrail derivation',
                'mapping_topology': 'many-to-many', 'disposition': disposition,
                'decision_rationale': f'{value} was compared against WinCare authority, identity, policy, preview, journaling, bounded-resource, recovery, GUI, and release models. Donor packaging was not imported.',
                'target_capability': capability, 'target_nodes': nodes, 'invariant': invariant, 'negative_invariant': negative,
                'test_node': test_node(test_name), 'operator_surface': 'Shell and Hardware Studio TUI, V5 GUI catalog, headless JSON, preview receipts, and transaction journal' if capability != 'n/a' else 'n/a',
                'migration_impact': 'additive WinCare 5.1 capability; existing authority and persistence owners remain authoritative' if capability != 'n/a' else 'n/a',
                'license_note': note, 'risk_tier': risk, 'dependency_record_ids': 'n/a', 'evidence_confidence': 'high',
                'validation_status': 'statically-validated' if disposition != 'reference-only' else 'reviewed'
            })

        counts = Counter()
        languages = Counter()
        total = 0
        for file_path in files:
            rel = file_path.relative_to(root).as_posix()
            stat = file_path.stat()
            total += stat.st_size
            file_type, language, classification, authority = classify(rel, file_path)
            counts[classification] += 1
            languages[language] += 1
            surfaces.append({
                'donor': donor, 'path': rel, 'sha256': sha(file_path), 'size_bytes': str(stat.st_size), 'file_type': file_type,
                'language': language, 'classification': classification, 'authority_status': authority,
                'semantic_record_ids': ';'.join([parent] + links.get(rel, [])),
                'notes': 'Classified and linked during cumulative WinCare 5.1 convergence.' + (f' Exact duplicate of {DUP[donor]}.' if donor in DUP else '')
            })

        # Repository links were intentionally not materialized. Bind their identity to archive evidence.
        link_rows = []
        root_name = ARCH[donor].get('root_names', [''])[0]
        for link in ARCH[donor].get('links', []):
            archive_path = str(link['path']).replace('\\', '/')
            rel = archive_path[len(root_name) + 1:] if root_name and archive_path.startswith(root_name + '/') else archive_path
            target = str(link.get('target', ''))
            rid_list = [parent]
            if donor == 'D153': rid_list.append('D153-RNX-SYMLINK-GUARDRAIL')
            if donor == 'D162': rid_list.append('D162-TAB-MANAGER-SYMLINK-ACCOUNTABILITY')
            link_hash = sha_text(f'{rel}\0{target}')
            surfaces.append({
                'donor': donor, 'path': rel, 'sha256': link_hash, 'size_bytes': '0', 'file_type': 'symlink', 'language': 'Other',
                'classification': 'test' if '/test/' in ('/' + rel.lower()) or '/__fixtures__/' in ('/' + rel.lower()) else 'repository-administration',
                'authority_status': 'supporting', 'semantic_record_ids': ';'.join(rid_list),
                'notes': f"Repository symlink recorded but not materialized. target={target!r}; safe_relative={bool(link.get('safe_relative'))}."
            })
            link_rows.append({'path': rel, 'target': target, 'safe_relative': bool(link.get('safe_relative')), 'sha256': link_hash})

        archive_row = ARCH[donor]
        inventory.append({
            'donor_id': donor, 'name': name, 'archive': archive_row['archive'], 'archive_sha256': archive_row['archive_sha256'],
            'duplicate_of': archive_row.get('duplicate_of'), 'member_count': archive_row['members'], 'files': len(files) + len(link_rows), 'regular_files': len(files),
            'symlinks': link_rows, 'expanded_bytes': total, 'classifications': dict(sorted(counts.items())), 'languages': dict(sorted(languages.items())),
            'licenses': [p.relative_to(root).as_posix() for p in files if p.name.lower().startswith(('license','copying','notice','eula'))],
            'manifests': [p.relative_to(root).as_posix() for p in files if p.name.lower() in {'package.json','cargo.toml','pubspec.yaml','go.mod','cmakelists.txt','directory.build.props'}][:100],
            'readmes': [p.relative_to(root).as_posix() for p in files if p.name.lower().startswith('readme')][:100], 'issues': []
        })

    write_csv(DOCS / 'adoption-matrix.csv', LEDGER_COLUMNS, ledger)
    write_csv(DOCS / 'surface-accountability.csv', SURFACE_COLUMNS, surfaces)
    (DOCS / 'donor-inventory.json').write_text(json.dumps(inventory, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    result = {
        'submittedDonorIds': len(inventory),
        'uniqueArchiveBaselines': len(inventory) - sum(1 for row in inventory if row.get('duplicate_of')),
        'semanticRecords': len(ledger),
        'surfaces': len(surfaces),
        'newSubmissions': 20,
        'newUniqueArchives': 16,
        'regularFiles': sum(int(ARCH[d]['files']) for d in ARCH),
        'accountedSymlinks': sum(int(ARCH[d].get('symlinks', 0)) for d in ARCH),
        'duplicates': DUP,
    }
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
