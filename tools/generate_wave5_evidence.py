#!/usr/bin/env python3
"""Append deterministic D70-D86 evidence to the cumulative WinCare convergence set."""
from __future__ import annotations

import csv
import hashlib
import json
import os
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "docs" / "convergence"
SRC = Path(os.environ.get("WINCARE_DONOR_ROOT", "/mnt/data/wincare_convergence/src"))
ARCHIVE_MANIFEST = Path(os.environ.get("WINCARE_ARCHIVE_MANIFEST", "/mnt/data/wincare_convergence/analysis/archive_manifest.json"))

LEDGER_COLUMNS = [
    "record_id","parent_record_id","composition_group_id","donor","domain","value_unit",
    "source_granularity","value_form","separability","donor_path","donor_sha256","donor_symbol",
    "transformation","mapping_topology","disposition","decision_rationale","target_capability",
    "target_nodes","invariant","negative_invariant","test_node","operator_surface","migration_impact",
    "license_note","risk_tier","dependency_record_ids","evidence_confidence","validation_status",
]
SURFACE_COLUMNS = [
    "donor","path","sha256","size_bytes","file_type","language","classification",
    "authority_status","semantic_record_ids","notes",
]

DONORS = [
    dict(id="D70", name="Custom Resolution Utility", archive="Custom-Resolution-Utility-ToastyX-master(2).zip", root="Custom-Resolution-Utility-ToastyX-master(2)/Custom-Resolution-Utility-ToastyX-master", license="License not explicit in the archive; no source copied; behavior independently rederived from observable registry semantics."),
    dict(id="D71", name="ET Optimizer", archive="ET-Optimizer-master(2).zip", root="ET-Optimizer-master(2)/ET-Optimizer-master", license="GPL-3.0 donor; no donor source copied; behavior independently reimplemented behind WinCare contracts."),
    dict(id="D72", name="LazTermUtils", archive="LazTermUtils-master(2).zip", root="LazTermUtils-master(2)/LazTermUtils-master", license="BSD-style donor license retained as provenance; target implementation uses existing WinCare console primitives."),
    dict(id="D73", name="Windows On Reins", archive="Windows-On-Reins-master(1).zip", root="Windows-On-Reins-master(1)/Windows-On-Reins-master", license="GPL-3.0 donor; no donor script copied or executed; behavior independently recomposed."),
    dict(id="D74", name="WindowsToolbox", archive="WindowsToolbox-main(2).zip", root="WindowsToolbox-main(2)/WindowsToolbox-main", license="MIT donor; provenance retained; target implementation remains target-native."),
    dict(id="D75", name="WindowsUtil", archive="WindowsUtil-master(2).zip", root="WindowsUtil-master(2)/WindowsUtil-master", license="License unclear in supplied archive; no source copied; safe inspection behavior independently derived."),
    dict(id="D76", name="Always On Top", archive="always-on-top-main(1).zip", root="always-on-top-main(1)/always-on-top-main", license="MIT donor; provenance retained; existing WinCare window contract subsumes the workflow."),
    dict(id="D77", name="FingerTips", archive="fingertips-master(1).zip", root="fingertips-master(1)/fingertips-master", license="README indicates MIT; provenance retained; task-board behavior independently implemented."),
    dict(id="D78", name="Pleasant Windows", archive="pleasant-windows-master(2).zip", root="pleasant-windows-master(2)/pleasant-windows-master", license="License unclear in supplied archive; scripts not copied; mutations independently modeled as typed actions."),
    dict(id="D79", name="SnakeTail.NET", archive="snaketail-net-master(2).zip", root="snaketail-net-master(2)/snaketail-net-master", license="GPL-3.0 donor; no source copied; bounded log behavior independently implemented."),
    dict(id="D80", name="Tab Counter", archive="tab-counter-master(4).zip", root="tab-counter-master(4)/tab-counter-master", license="Apache-2.0 donor; no extension bundled; product affordance retained as a guarded browser-summary rationale."),
    dict(id="D81", name="Tab Resize", archive="tab-resize-master(1).zip", root="tab-resize-master(1)/tab-resize-master", license="GPL-3.0 donor; no source copied; existing WinCare workspace-layout capability subsumes the outcome."),
    dict(id="D82", name="Window Switcher", archive="window-switcher-main(5).zip", root="window-switcher-main(5)/window-switcher-main", license="MIT donor; provenance retained; existing typed window activation contracts subsume the workflow."),
    dict(id="D83", name="Windows Container Tools", archive="windows-container-tools-main(1).zip", root="windows-container-tools-main(1)/windows-container-tools-main", license="MIT donor; strict log-source schema independently represented in WinCare state."),
    dict(id="D84", name="Windows Utilities", archive="windows-utilities-main(2).zip", root="windows-utilities-main(2)/windows-utilities-main", license="License unclear in supplied archive; telemetry concepts independently adapted."),
    dict(id="D85", name="Windows WLAN Util", archive="windows-wlan-util-master(5).zip", root="windows-wlan-util-master(5)/windows-wlan-util-master", license="MIT donor; WLAN survey behavior independently implemented using Windows netsh output."),
    dict(id="D86", name="xd-AntiSpy", archive="xd-AntiSpy-main(2).zip", root="xd-AntiSpy-main(2)/xd-AntiSpy-main", license="MIT donor; provenance retained; profiles and plugin lessons recomposed behind WinCare policy and typed actions."),
]

TEST = "tests/Wave5.PeerUtilities.Tests.ps1#routes peer utilities and destructive profiles through typed target-native surfaces"
TEST_RECOVERY = "tests/Wave5.PeerUtilities.Tests.ps1#keeps exact conflict-aware recovery for permanent security and display mutations"
TEST_GATES = "tests/Wave5.PeerUtilities.Tests.ps1#keeps all destructive profile gates default-deny"

RECORDS = {
"D70": [
 dict(id="D70-DISPLAY-OVERRIDE-INVENTORY", group="CG-W5-DISPLAY", domain="Display configuration", value="EDID override and display-device registry inventory", path="CRU/DisplayClass.cpp", form="defensive logic", transform="target-native rederivation", topology="many-to-one", disposition="adapted", capability="Display override diagnostics", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareDisplayOverrideState", invariant="Display override state is inventoried without editing it.", negative="No display registry key is removed during inventory.", test=TEST, risk="high"),
 dict(id="D70-DISPLAY-OVERRIDE-RESET", group="CG-W5-DISPLAY", domain="Display configuration", value="One-click display override and graphics cache reset", path="CRU/DisplayClass.cpp", form="executable behavior", transform="hardened target-native adaptation", topology="one-to-many", disposition="hardened", capability="Critical display reset", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareDisplayOverrideResetPlan;src/WinCare/Providers/106-PeerUtilities.ps1#Invoke-WinCareResetDisplayOverridesAction", invariant="The destructive display reset is previewed, policy-gated, elevated, journaled, and snapshot-backed.", negative="No display reset runs without AllowDisplayOverrideReset and Critical authorization.", test=TEST_GATES, risk="critical"),
 dict(id="D70-DISPLAY-RECOVERY", group="CG-W5-DISPLAY", domain="Display recovery", value="Exact display override snapshot restoration with conflict detection", path="CRU/DisplayClass.cpp", form="recovery rule", transform="synthesis and hardening", topology="idea-to-native", disposition="synthesized", capability="Display reset recovery", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Invoke-WinCareRestoreDisplayOverridesAction", invariant="Recovery restores the captured state only when current state still matches the expected post-mutation hash.", negative="Recovery never overwrites newer display changes silently.", test=TEST_RECOVERY, risk="critical"),
],
"D71": [
 dict(id="D71-ET-EXTREME-PROFILE", group="CG-W5-LEGACY-UNSAFE", domain="Extreme optimization", value="Extreme gaming one-click mutation profile", path="ET/Form1.cs", form="workflow", transform="many-to-one recomposition", topology="many-to-one", disposition="recomposed", capability="Legacy Unsafe profiles", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLegacyUnsafeProfile;src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareLegacyUnsafeProfilePlan", invariant="The requested aggressive mutations execute through typed WinCare actions with exact acknowledgement.", negative="No raw donor command blob or hidden mutation bypasses preview, policy, journal, or UAC brokering.", test=TEST, risk="critical"),
 dict(id="D71-UNATTEND-EVIDENCE", group="CG-W5-LEGACY-UNSAFE", domain="Unattended provisioning", value="Unattended-system optimization evidence converted into an authority guardrail", path="ET/Resources/autounattend.xml", form="configuration and negative lesson", transform="negative-to-guardrail derivation", topology="negative-to-guardrail", disposition="guardrail-derived", capability="Legacy mutation authority", nodes="src/WinCare/Core/01-Config.ps1#Get-WinCareDefaultPolicy;src/WinCare/Core/08-Policy.ps1#Test-WinCareActionPolicy", invariant="Permanent reductions require explicit local policy opt-in even when bundled in a profile.", negative="An imported unattended file never grants mutation authority.", test=TEST_GATES, risk="critical"),
],
"D72": [
 dict(id="D72-TERMINAL-UX", group="CG-W5-TERMINAL", domain="Terminal interaction", value="Non-blocking key and terminal interaction patterns", path="src/terminalkeys.pas", form="product/UX primitive", transform="supersession by existing target mechanism", topology="many-to-one", disposition="superseded", capability="Portable TUI input", nodes="src/WinCare/UI/00-Console.ps1#Read-WinCareKey", invariant="TUI input remains bounded and target-native.", negative="No donor terminal runtime or platform-specific input loop is introduced.", test=TEST, risk="low"),
],
"D73": [
 dict(id="D73-REINS-NUCLEAR-PROFILE", group="CG-W5-LEGACY-UNSAFE", domain="System debloat", value="Nuclear one-click Defender, firewall, update, service, task, hosts, and AppX reductions", path="wor.ps1", form="workflow", transform="hardened recomposition", topology="many-to-many", disposition="recomposed", capability="Legacy Unsafe profiles", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareLegacyUnsafeProfilePlan", invariant="The nuclear profile remains executable but every side effect is a typed, previewed action.", negative="No broad script execution or implicit consent replaces operation-specific authorization.", test=TEST, risk="critical"),
 dict(id="D73-BLANKET-DISABLE-GUARDRAIL", group="CG-W5-LEGACY-UNSAFE", domain="Security authority", value="Blanket security-disable behavior converted into explicit permanent-reduction contracts", path="wor.ps1", form="negative lesson", transform="negative-to-guardrail derivation", topology="negative-to-guardrail", disposition="guardrail-derived", capability="Permanent security reduction", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Invoke-WinCareSetLegacySecurityControlStateAction;src/WinCare/Core/08-Policy.ps1#Test-WinCareActionPolicy", invariant="Permanent reductions are separately policy-gated and state-hash checked.", negative="A profile cannot silently weaken Defender, SmartScreen, UAC, or Windows Update.", test=TEST_GATES, risk="critical"),
],
"D74": [
 dict(id="D74-TOOLBOX-AGGRESSIVE-PROFILE", group="CG-W5-LEGACY-UNSAFE", domain="Debloat and privacy", value="WindowsToolbox aggressive one-click profile", path="main.ps1", form="workflow", transform="many-to-one target-native recomposition", topology="many-to-one", disposition="recomposed", capability="Legacy Unsafe profiles", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLegacyUnsafeProfile;src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareLegacyUnsafeProfilePlan", invariant="Debloat, privacy, service, task, hosts, AppX, update, and hibernation mutations share one audited action plan.", negative="The donor menu and module loader do not become an alternate authority path.", test=TEST, risk="critical"),
 dict(id="D74-UNDO-EVIDENCE", group="CG-W5-LEGACY-UNSAFE", domain="Recovery", value="Undo workflows converted into exact typed compensators", path="library/UndoFunctions.psm1", form="recovery practice", transform="hardening and synthesis", topology="many-to-many", disposition="hardened", capability="Mutation recovery", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Invoke-WinCareRestoreLegacySecurityControlStateAction;src/WinCare/Providers/106-PeerUtilities.ps1#Invoke-WinCareRestoreDisplayOverridesAction", invariant="Reversible mutations capture exact before state and reject conflicting recovery.", negative="A generic undo preset never overwrites unrelated newer state.", test=TEST_RECOVERY, risk="critical"),
],
"D75": [
 dict(id="D75-PE-INSPECTION", group="CG-W5-PE", domain="Binary inspection", value="Bounded PE header and signature inspection", path="PeImage/HeaderFactory.cpp", form="parser behavior", transform="independent safe reimplementation", topology="many-to-one", disposition="adapted", capability="PE metadata inspection", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCarePeMetadata", invariant="PE inspection is read-only, bounded by file size and section count, and hashes the inspected file.", negative="Inspection never loads, maps, executes, or injects the PE image.", test=TEST, risk="high"),
 dict(id="D75-HOOKING-GUARDRAIL", group="CG-W5-PROCESS-GUARDRAILS", domain="Process instrumentation", value="IAT/EAT/API hooking mechanisms converted into a categorical non-execution guardrail", path="Hook/HookIat.cpp", form="negative lesson", transform="negative-to-guardrail derivation", topology="negative-to-guardrail", disposition="guardrail-derived", capability="Safe binary diagnostics", nodes="src/WinCare/Core/01-Config.ps1#Get-WinCareDefaultPolicy", invariant="WinCare may inspect binary metadata without adding process-hooking authority.", negative="No remote thread, process injection, IAT/EAT patch, or global hook is executed.", test=TEST_GATES, risk="critical"),
],
"D76": [
 dict(id="D76-TOPMOST-WORKFLOW", group="CG-W5-WINDOWS", domain="Window management", value="Interactive always-on-top window selection", path="lite.au3", form="product workflow", transform="supersession by existing typed action", topology="many-to-one", disposition="superseded", capability="Window topmost state", nodes="src/WinCare/Providers/95-WindowWorkspace.ps1#New-WinCareWindowTopmostPlan", invariant="Topmost changes target an explicitly inventoried local window.", negative="No arbitrary pointer picker or opaque AutoIt runtime gains authority.", test=TEST, risk="medium"),
],
"D77": [
 dict(id="D77-LOCAL-TASK-BOARD", group="CG-W5-TASKS", domain="Personal productivity", value="Local lists, cards, labels, and members task-board model", path="FingerTips/DataStore.cs", form="data model and workflow", transform="inspired-native synthesis", topology="idea-to-native", disposition="inspired-native", capability="Local task board", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareTaskBoard;src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareTaskPlan", invariant="Task-board state remains local, schema-bounded, revisioned, and written through managed-file actions.", negative="UI state, database migrations, or donor ORM state never become authoritative.", test=TEST, risk="low"),
],
"D78": [
 dict(id="D78-PLEASANT-LEGACY-PROFILE", group="CG-W5-LEGACY-UNSAFE", domain="Legacy Windows tuning", value="Batch-based telemetry, update, service, shell, hibernation, and app mutation collection", path="Win 10 Disable Telemetry.bat", form="workflow collection", transform="many-to-one target-native recomposition", topology="many-to-one", disposition="recomposed", capability="Legacy Unsafe profiles", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLegacyUnsafeProfile;src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareLegacyUnsafeProfilePlan", invariant="Legacy batch outcomes are represented as visible typed actions and can be previewed before execution.", negative="No bundled BAT, EXE, registry.pol, or opaque command chain is executed directly.", test=TEST, risk="critical"),
 dict(id="D78-AGGRESSIVE-APPX", group="CG-W5-LEGACY-UNSAFE", domain="Application removal", value="Broad built-in application removal list", path="Win 10 Uninstall Unnecessary Apps.bat", form="preset", transform="hardened preset adaptation", topology="many-to-one", disposition="adapted", capability="Critical AppX removal preset", nodes="src/WinCare/Data/Catalog/app-removal-profiles.json#legacy-aggressive", invariant="The aggressive AppX list is explicit and classified Critical before package discovery and removal.", negative="Provisioned-package removal is never implied unless the operator selects it.", test=TEST, risk="critical"),
],
"D79": [
 dict(id="D79-BOUNDED-LOG-SNAPSHOT", group="CG-W5-LOGS", domain="Log diagnostics", value="Tail-style file and Windows Event Log snapshots", path="SnakeTail/LogFileStream.cs", form="diagnostic behavior", transform="bounded independent reimplementation", topology="many-to-one", disposition="adapted", capability="Bounded log snapshot", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLogSnapshot", invariant="Log reads are bounded by source size and result count.", negative="No unbounded follow loop or external-tool invocation runs during a snapshot.", test=TEST, risk="medium"),
 dict(id="D79-REGEX-HIGHLIGHT", group="CG-W5-LOGS", domain="Log diagnostics", value="Keyword and regular-expression filtering", path="SnakeTail/KeywordConfigForm.cs", form="product affordance", transform="hardened primitive extraction", topology="many-to-one", disposition="extracted", capability="Bounded log filtering", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLogSnapshot", invariant="Regular expressions compile with a fixed timeout before filtering bounded records.", negative="Catastrophic regex backtracking cannot run without a timeout.", test=TEST, risk="medium"),
],
"D80": [
 dict(id="D80-TAB-COUNT-AFFORDANCE", group="CG-W5-BROWSER", domain="Browser workspace", value="Visible tab-count affordance and its privacy tradeoff", path="src/background.js", form="product/UX surface", transform="negative-to-product guardrail", topology="negative-to-guardrail", disposition="guardrail-derived", capability="Browser workspace summary", nodes="src/WinCare/Providers/99-BrowserWorkspace.ps1#Get-WinCareBrowserWorkspaceSummary", invariant="WinCare reports browser process, window, profile, and extension counts without silently installing an extension.", negative="Browser debugging or extension permissions are not enabled merely to count tabs.", test=TEST, risk="medium"),
],
"D81": [
 dict(id="D81-WORKSPACE-LAYOUTS", group="CG-W5-WINDOWS", domain="Window layouts", value="Named multi-window layout presets", path="js/layout.js", form="product workflow", transform="supersession by existing target-native capability", topology="many-to-one", disposition="superseded", capability="Workspace layouts", nodes="src/WinCare/Providers/105-WorkspaceLayouts.ps1#New-WinCareWorkspaceLayoutApplyPlan", invariant="Layouts persist as revisioned target-native records and apply through window-bound actions.", negative="A browser extension does not become the source of truth for desktop layout state.", test=TEST, risk="medium"),
],
"D82": [
 dict(id="D82-SAME-APP-WINDOW-SWITCH", group="CG-W5-WINDOWS", domain="Window navigation", value="Same-application window inventory and foreground activation", path="src/utils/window.rs", form="interaction primitive", transform="supersession by existing typed actions", topology="many-to-one", disposition="superseded", capability="Window inventory and activation", nodes="src/WinCare/Providers/95-WindowWorkspace.ps1#Get-WinCareWindowInventory;src/WinCare/Providers/95-WindowWorkspace.ps1#New-WinCareWindowActivatePlan", invariant="Activation operates on a currently inventoried local window identity.", negative="No global keyboard hook or donor resident process is installed.", test=TEST, risk="medium"),
],
"D83": [
 dict(id="D83-CONTAINER-LOG-PIPELINE", group="CG-W5-LOGS", domain="Container diagnostics", value="Event Log, ETW, and file-to-stdout monitor configuration", path="LogMonitor/src/LogMonitor/JsonProcessor.cpp", form="schema and operational behavior", transform="target-native schema adaptation", topology="many-to-one", disposition="adapted", capability="Container log monitor configuration", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareContainerLogMonitorConfiguration;src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareContainerLogMonitorPlan", invariant="Only validated bounded source definitions are serialized under WinCare state.", negative="No arbitrary command, sink, remote destination, or donor binary is admitted.", test=TEST, risk="high"),
 dict(id="D83-LOG-CONFIG-STRICTNESS", group="CG-W5-LOGS", domain="Container diagnostics", value="Strict source-count and input schema validation", path="LogMonitor/LogMonitorTests/JsonProcessorTests.cpp", form="test oracle", transform="test-oracle extraction", topology="many-to-one", disposition="extracted", capability="Container log configuration validation", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareContainerLogMonitorConfiguration", invariant="Event, file, and ETW source counts and names are bounded before persistence.", negative="Malformed, oversized, or command-bearing monitor configuration is rejected.", test=TEST, risk="high"),
],
"D84": [
 dict(id="D84-NETWORK-STABILITY-TELEMETRY", group="CG-W5-TELEMETRY", domain="Network diagnostics", value="Network stability sampling and operator-readable status", path="NetworkStabilityMonitor.ps1", form="operational practice", transform="recomposition into existing telemetry and WLAN diagnostics", topology="many-to-one", disposition="recomposed", capability="Network telemetry", nodes="src/WinCare/Providers/101-Telemetry.ps1#Get-WinCareTelemetrySnapshot;src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareWlanSurvey", invariant="Network diagnostics are read-only snapshots with bounded output.", negative="Monitoring does not silently alter adapters, DNS, routes, or firewall state.", test=TEST, risk="medium"),
 dict(id="D84-PERFORMANCE-TELEMETRY", group="CG-W5-TELEMETRY", domain="Performance diagnostics", value="CPU, memory, disk, and process performance snapshots", path="PerformanceMonitor.ps1", form="operational practice", transform="supersession by existing telemetry plane", topology="many-to-one", disposition="superseded", capability="Performance telemetry", nodes="src/WinCare/Providers/101-Telemetry.ps1#Get-WinCareTelemetrySnapshot", invariant="Performance samples are bounded, normalized, and non-authoritative.", negative="A sampling helper cannot mutate system configuration.", test=TEST, risk="low"),
],
"D85": [
 dict(id="D85-WLAN-SURVEY", group="CG-W5-WLAN", domain="Wireless diagnostics", value="WLAN interface, SSID, BSSID, signal, security, and channel survey", path="windows-wlan-util/windows-wlan-util.cpp", form="diagnostic behavior", transform="portable target-native adaptation", topology="many-to-one", disposition="adapted", capability="WLAN survey", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareWlanSurvey", invariant="WLAN survey is read-only and bounds process duration and parsed results.", negative="No profile credentials are exported and no network is joined or changed.", test=TEST, risk="medium"),
],
"D86": [
 dict(id="D86-XD-ANTISPY-MAX-PROFILE", group="CG-W5-LEGACY-UNSAFE", domain="Privacy and debloat", value="Maximum privacy and aggressive application-removal profile", path="xd-AntiSpy/ProfileManager.cs", form="profile workflow", transform="many-to-one target-native recomposition", topology="many-to-one", disposition="recomposed", capability="Legacy Unsafe profiles", nodes="src/WinCare/Providers/106-PeerUtilities.ps1#Get-WinCareLegacyUnsafeProfile;src/WinCare/Providers/106-PeerUtilities.ps1#New-WinCareLegacyUnsafeProfilePlan", invariant="The maximum profile remains executable through visible Critical actions and exact acknowledgement.", negative="A profile file or UI selection cannot bypass current policy or action-state validation.", test=TEST, risk="critical"),
 dict(id="D86-PLUGIN-QUARANTINE-GUARDRAIL", group="CG-W5-PLUGIN-GUARDRAILS", domain="Extension governance", value="PowerShell, JSON, and DLL plugin system converted into a quarantine guardrail", path="xd-AntiSpy/PluginsBase.cs", form="negative lesson", transform="negative-to-guardrail derivation", topology="negative-to-guardrail", disposition="guardrail-derived", capability="Extension admission", nodes="src/WinCare/Core/01-Config.ps1#Get-WinCareDefaultPolicy;src/WinCare/Core/08-Policy.ps1#Test-WinCareActionPolicy", invariant="Only built-in typed actions or separately admitted target-native extensions may mutate state.", negative="Bundled donor scripts, DLLs, or JSON commands never execute merely because they were discovered.", test=TEST_GATES, risk="critical"),
 dict(id="D86-AGGRESSIVE-APPX", group="CG-W5-LEGACY-UNSAFE", domain="Application removal", value="Plugin-based default-app removal and restoration workflow", path="plugins/Remove default apps.ps1", form="workflow and preset", transform="hardened preset recomposition", topology="many-to-one", disposition="adapted", capability="Critical AppX removal preset", nodes="src/WinCare/Data/Catalog/app-removal-profiles.json#legacy-aggressive", invariant="Aggressive package patterns are explicit, previewed against installed packages, and classified Critical.", negative="The donor removal script is never executed and package restoration is not falsely guaranteed.", test=TEST, risk="critical"),
],
}

LANG = {
    ".ps1":"PowerShell", ".psm1":"PowerShell", ".psd1":"PowerShell", ".bat":"Batch", ".cmd":"Batch",
    ".cs":"CSharp", ".xaml":"XAML", ".csproj":"MSBuild", ".sln":"Visual Studio Solution",
    ".cpp":"C++", ".cc":"C++", ".c":"C", ".h":"C/C++ header", ".hpp":"C++ header",
    ".rs":"Rust", ".pas":"Pascal", ".lpr":"Pascal", ".lpi":"Lazarus project", ".lpk":"Lazarus package",
    ".au3":"AutoIt", ".js":"JavaScript", ".html":"HTML", ".css":"CSS", ".less":"Less",
    ".json":"JSON", ".xml":"XML", ".resx":"XML", ".config":"XML", ".manifest":"XML",
    ".yaml":"YAML", ".yml":"YAML", ".toml":"TOML", ".ini":"INI", ".md":"Markdown", ".txt":"Text",
    ".dfm":"Delphi form", ".rc":"Resource script", ".wxs":"WiX", ".wixproj":"MSBuild", ".vcxproj":"MSBuild",
}
MEDIA = {".png",".jpg",".jpeg",".gif",".bmp",".ico",".svg",".cur",".ttf",".eot",".woff",".wav",".pfx",".dll",".exe"}
CONFIG = {".json",".xml",".resx",".config",".manifest",".yaml",".yml",".toml",".ini",".sln",".csproj",".vcxproj",".filters",".props",".targets",".lpi",".lpk",".bdsproj",".rc",".wxs",".wixproj"}
SCRIPT = {".ps1",".psm1",".bat",".cmd",".sh"}
UI = {".xaml",".html",".css",".less",".dfm"}
SOURCE = {".cs",".cpp",".cc",".c",".h",".hpp",".rs",".pas",".lpr",".au3",".js"}


def sha256(path: Path) -> str:
    h=hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda:f.read(1024*1024), b""):
            h.update(chunk)
    return h.hexdigest()


def classify(rel: str, path: Path) -> tuple[str,str,str,str]:
    low=rel.lower(); ext=path.suffix.lower(); name=path.name.lower()
    if "/.git/" in f"/{low}/" or low.startswith(".github/"):
        cls="repository-administration"
    elif "test" in {p.lower() for p in Path(rel).parts} or "/tests/" in f"/{low}/" or "test" in name:
        cls="test"
    elif name.startswith("license") or name in {"copying","notice"} or "code_of_conduct" in low or "contributing" in low:
        cls="governance"
    elif low.startswith("languages/") or low.startswith("loc/") or "localization" in low or ext==".resx":
        cls="localization"
    elif ext in MEDIA:
        cls="media" if ext not in {".dll",".exe"} else "vendored"
    elif "packages/" in low or "/bin/" in f"/{low}/" or "/obj/" in f"/{low}/":
        cls="vendored"
    elif name.startswith("readme") or low.startswith("docs/") or ext in {".md",".txt"}:
        cls="documentation"
    elif ext in SCRIPT:
        cls="script"
    elif ext in UI:
        cls="ui-or-product"
    elif ext in CONFIG or name in {"makefile","gruntfile.js","gulpfile.js"}:
        cls="configuration"
    elif ext in SOURCE:
        cls="implementation"
    else:
        cls="repository-administration"
    authority="authoritative" if cls in {"implementation","script","configuration","ui-or-product"} else "supporting"
    if cls in {"media","vendored","localization"}: authority="derived"
    language=LANG.get(ext,"Other")
    file_type=ext or "[no-extension]"
    return file_type,language,cls,authority


def read_csv(path: Path) -> list[dict[str,str]]:
    with path.open(newline="",encoding="utf-8-sig") as f: return list(csv.DictReader(f))


def write_csv(path: Path, columns: list[str], rows: list[dict[str,object]]) -> None:
    with path.open("w",newline="",encoding="utf-8") as f:
        w=csv.DictWriter(f,fieldnames=columns,lineterminator="\n")
        w.writeheader(); w.writerows(rows)


def make_record(donor: dict[str,str], spec: dict[str,str], parent: str) -> dict[str,str]:
    source=SRC/donor["root"]/spec["path"]
    if not source.is_file(): raise FileNotFoundError(source)
    return {
      "record_id":spec["id"],"parent_record_id":parent,"composition_group_id":spec["group"],"donor":donor["id"],
      "domain":spec["domain"],"value_unit":spec["value"],"source_granularity":"mechanism or evidence cluster",
      "value_form":spec["form"],"separability":"context-dependent","donor_path":spec["path"],"donor_sha256":sha256(source),
      "donor_symbol":"n/a","transformation":spec["transform"],"mapping_topology":spec["topology"],"disposition":spec["disposition"],
      "decision_rationale":f"{spec['value']} was selected from {donor['name']} and fitted to WinCare's existing authority, state, preview, policy, journaling, and recovery model; donor-specific packaging and alternate execution paths were not imported.",
      "target_capability":spec["capability"],"target_nodes":spec["nodes"],"invariant":spec["invariant"],"negative_invariant":spec["negative"],
      "test_node":spec["test"],"operator_surface":"Peer Utilities TUI, headless JSON, preview receipts, transaction journal",
      "migration_impact":"additive WinCare 4.4 capability; existing target state and action dispatcher remain authoritative",
      "license_note":donor["license"],"risk_tier":spec["risk"],"dependency_record_ids":"n/a","evidence_confidence":"high","validation_status":"statically-validated",
    }


def main() -> None:
    DOCS.mkdir(parents=True,exist_ok=True)
    old_ledger=[r for r in read_csv(DOCS/"adoption-matrix.csv") if int(r["donor"][1:])<70]
    old_surfaces=[r for r in read_csv(DOCS/"surface-accountability.csv") if int(r["donor"][1:])<70]
    old_inventory=[r for r in json.loads((DOCS/"donor-inventory.json").read_text(encoding="utf-8")) if int(str(r.get("donor_id",r.get("donor")))[1:])<70]
    manifests={Path(x["archive"]).name:x for x in json.loads(ARCHIVE_MANIFEST.read_text(encoding="utf-8"))}

    ledger=list(old_ledger); surfaces=list(old_surfaces); inventory=list(old_inventory)
    for donor in DONORS:
        root=SRC/donor["root"]
        if not root.is_dir(): raise FileNotFoundError(root)
        files=sorted((p for p in root.rglob("*") if p.is_file() and not p.is_symlink()), key=lambda p:p.relative_to(root).as_posix().casefold())
        if not files: raise RuntimeError(f"No files for {donor['id']}")
        parent_id=f"{donor['id']}-SURFACE-ACCOUNTABILITY"
        readmes=[p.relative_to(root).as_posix() for p in files if p.name.lower().startswith("readme")]
        representative=readmes[0] if readmes else files[0].relative_to(root).as_posix()
        rep_path=root/representative
        ledger.append({
          "record_id":parent_id,"parent_record_id":"n/a","composition_group_id":f"CG-W5-{donor['id']}-ACCOUNTABILITY","donor":donor["id"],
          "domain":"Donor accountability","value_unit":f"Complete file-surface accountability for {donor['name']}","source_granularity":"repository/archive surface",
          "value_form":"accountability evidence","separability":"standalone","donor_path":representative,"donor_sha256":sha256(rep_path),"donor_symbol":"n/a",
          "transformation":"evidence inventory and disposition mapping","mapping_topology":"custom:one donor to complete surface accountability","disposition":"reference-only",
          "decision_rationale":"Every regular file in the supplied donor archive was hashed, classified, and linked to this parent record; material semantics receive narrower child records.",
          "target_capability":"n/a","target_nodes":"n/a","invariant":"No donor surface is silently omitted from convergence accountability.",
          "negative_invariant":"File counts, popularity, or donor packaging are not treated as semantic adoption.","test_node":"n/a","operator_surface":"n/a",
          "migration_impact":"n/a","license_note":donor["license"],"risk_tier":"low","dependency_record_ids":"n/a","evidence_confidence":"high","validation_status":"reviewed",
        })
        children=[make_record(donor,s,parent_id) for s in RECORDS[donor["id"]]]
        ledger.extend(children)
        link_map: dict[str,list[str]]={}
        for child in children: link_map.setdefault(child["donor_path"],[]).append(child["record_id"])
        class_count=Counter(); lang_count=Counter(); total_bytes=0
        for p in files:
            rel=p.relative_to(root).as_posix(); st=p.stat(); total_bytes+=st.st_size
            ft,lang,cls,auth=classify(rel,p); class_count[cls]+=1; lang_count[lang]+=1
            ids=[parent_id]+link_map.get(rel,[])
            surfaces.append({"donor":donor["id"],"path":rel,"sha256":sha256(p),"size_bytes":str(st.st_size),"file_type":ft,"language":lang,
                             "classification":cls,"authority_status":auth,"semantic_record_ids":";".join(ids),
                             "notes":"Classified and linked during cumulative WinCare 4.4 convergence."})
        manifest=manifests[donor["archive"]]
        licenses=[p.relative_to(root).as_posix() for p in files if p.name.lower().startswith(("license","copying"))]
        manifests_found=[p.relative_to(root).as_posix() for p in files if p.name.lower() in {"package.json","packages.config","cargo.toml","requirements.txt","makefile"} or p.suffix.lower() in {".csproj",".vcxproj",".lpi",".lpk",".sln"}]
        inventory.append({
          "donor_id":donor["id"],"name":donor["name"],"archive":donor["archive"],"archive_sha256":manifest["sha256"],"duplicate_of":None,
          "member_count":manifest["members"],"files":len(files),"expanded_bytes":total_bytes,"classifications":dict(sorted(class_count.items())),
          "languages":dict(sorted(lang_count.items())),"licenses":licenses,"manifests":manifests_found,"readmes":readmes,"issues":manifest.get("warnings",[]),
        })

    write_csv(DOCS/"adoption-matrix.csv",LEDGER_COLUMNS,ledger)
    write_csv(DOCS/"surface-accountability.csv",SURFACE_COLUMNS,surfaces)
    (DOCS/"donor-inventory.json").write_text(json.dumps(inventory,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
    print(json.dumps({"donors":len(inventory),"semanticRecords":len(ledger),"surfaces":len(surfaces),"newDonors":len(DONORS),"newRecords":sum(1+len(RECORDS[d['id']]) for d in DONORS)},indent=2))

if __name__=="__main__": main()
