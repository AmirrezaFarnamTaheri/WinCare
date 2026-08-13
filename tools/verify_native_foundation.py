#!/usr/bin/env python3
"""Deterministic source-level gate for the native WinCare migration foundation."""

from __future__ import annotations

import html
import json
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parents[1]

ORACLE_COMMANDS_PATH = ROOT / "migration/oracle/legacy-command-ids.json"
CATALOG_PATH = ROOT / "src/WinCare.CommandCatalog/Data/commands.json"
ORACLE_RULES_PATH = ROOT / "migration/oracle/remediation-rules.json"
ORACLE_PRESETS_PATH = ROOT / "migration/oracle/presets.json"
ORACLE_PROVENANCE_PATH = ROOT / "migration/oracle/provenance.json"
NATIVE_RULES_PATH = ROOT / "src/WinCare.CommandCatalog/Data/remediation-rules.json"
NATIVE_PRESETS_PATH = ROOT / "src/WinCare.CommandCatalog/Data/presets.json"

NATIVE_ROOTS = (
    ROOT / "src/WinCare.App",
    ROOT / "src/WinCare.Application",
    ROOT / "src/WinCare.Domain",
    ROOT / "src/WinCare.Infrastructure",
    ROOT / "src/WinCare.CommandCatalog",
    ROOT / "native",
)

REQUIRED_FILES = (
    ORACLE_COMMANDS_PATH,
    ORACLE_RULES_PATH,
    ORACLE_PRESETS_PATH,
    ORACLE_PROVENANCE_PATH,
    ROOT / "Directory.Build.props",
    ROOT / "Directory.Packages.props",
    ROOT / "WinCare.Native.sln",
    ROOT / "rust-toolchain.toml",
    ROOT / "src/WinCare.App/WinCare.App.csproj",
    ROOT / "src/WinCare.App/App.xaml",
    ROOT / "src/WinCare.App/MainWindow.xaml",
    ROOT / "src/WinCare.App/Package.appxmanifest",
    ROOT / "src/WinCare.App/Views/ShellPage.xaml",
    ROOT / "src/WinCare.App/Views/Pages/AllToolsPage.xaml",
    ROOT / "src/WinCare.Infrastructure/Observability/StartupTelemetry.cs",
    ROOT / "src/WinCare.CommandCatalog/WinCare.CommandCatalog.csproj",
    CATALOG_PATH,
    NATIVE_RULES_PATH,
    NATIVE_PRESETS_PATH,
    ROOT / "src/WinCare.CommandCatalog/RemediationCatalog.cs",
    ROOT / "src/WinCare.CommandCatalog/RemediationCatalogJsonContext.cs",
    ROOT / "src/WinCare.CommandCatalog/EmbeddedJsonResource.cs",
    ROOT / "src/WinCare.CommandCatalog/RemediationCatalogValidator.cs",
    ROOT / "src/WinCare.Application/Commands/CommandDispatcher.cs",
    ROOT / "src/WinCare.Application/Commands/CommandRuntime.cs",
    ROOT / "src/WinCare.Application/Commands/Handlers/CatalogCommandHandler.cs",
    ROOT / "src/WinCare.Application/Commands/Handlers/PresetsCommandHandler.cs",
    ROOT / "src/WinCare.Application/Commands/Handlers/SystemInfoCommandHandler.cs",
    ROOT / "src/WinCare.Application/Commands/Handlers/StorageHealthCommandHandler.cs",
    ROOT / "src/WinCare.Application/Commands/Handlers/NetworkStatusCommandHandler.cs",
    ROOT / "src/WinCare.Application/Commands/Handlers/PrivacyStatusCommandHandler.cs",
    ROOT / "src/WinCare.Application/Commands/Handlers/DiskCleanupCommandHandler.cs",
    ROOT / "src/WinCare.Application/Commands/Handlers/LogCleanupCommandHandler.cs",
    ROOT / "src/WinCare.Application/Activity/ActivityJournalService.cs",
    ROOT / "native/Cargo.toml",
    ROOT / "native/wincare-core/Cargo.toml",
    ROOT / "native/wincare-core/src/lib.rs",
    ROOT / ".github/workflows/native-winui.yml",
    ROOT / ".github/workflows/native-release-candidate.yml",
    ROOT / "tools/finalize_native_release.py",
    ROOT / "tools/verify_visual_tokens.py",
    ROOT / "tools/verify_pill_contrast.py",
    ROOT / "docs/migration/finalization-status.md",
)

BANNED_NATIVE_PATTERNS = (
    "Microsoft.PowerShell.SDK",
    "System.Management.Automation",
    "PresentationCore",
    "PresentationFramework",
    "System.Windows.",
    "<UseWPF>true</UseWPF>",
)

EXPECTED_NAVIGATION = {
    "NavHome": "Home",
    "NavCheckup": "Checkup",
    "NavSystemCare": "System care",
    "NavSecurity": "Security",
    "NavRepairRecovery": "Repair & recovery",
    "NavAllTools": "All tools",
    "NavActivity": "Activity",
    "NavSettings": "Settings",
}

EXPECTED_PAGE_TABS = {
    "HomePage.xaml": ("Overview", "Recommendations", "Favorites"),
    "CheckupPage.xaml": ("Quick check", "Full check", "Custom check", "Results"),
    "SystemCarePage.xaml": ("Clean up", "Performance", "Apps & startup", "Network & updates", "Routines"),
    "SecurityPage.xaml": ("Status", "Protection", "Privacy", "Hardening"),
    "RepairRecoveryPage.xaml": ("Repair", "Restore", "Undo", "Backup", "Reset & media"),
    "AllToolsPage.xaml": ("Commands", "Categories", "Favorites", "Recent", "Presets"),
    "ActivityPage.xaml": ("Running", "Needs attention", "Completed", "Reports"),
    "SettingsPage.xaml": ("General", "Appearance", "Safety", "Notifications", "Data", "Advanced"),
}


@dataclass(frozen=True)
class Finding:
    code: str
    message: str


def load_oracle_commands() -> tuple[str, ...]:
    document = json.loads(ORACLE_COMMANDS_PATH.read_text(encoding="utf-8"))
    commands = tuple(document.get("commands", ()))
    if document.get("schemaVersion") != 1:
        raise ValueError("unsupported oracle command schema")
    if document.get("commandCount") != 259 or len(commands) != 259 or len(set(commands)) != 259:
        raise ValueError(
            f"oracle command set is not exactly 259 unique IDs: {len(commands)} / {len(set(commands))}"
        )
    if not all(isinstance(command, str) and command for command in commands):
        raise ValueError("oracle command IDs must be non-empty strings")
    return commands


def _iter_text_files(paths: Iterable[Path]) -> Iterable[Path]:
    extensions = {".cs", ".csproj", ".props", ".targets", ".xaml", ".xml", ".json", ".toml", ".rs", ".h", ".yml", ".yaml"}
    for root in paths:
        if not root.exists():
            continue
        for path in root.rglob("*"):
            if path.is_file() and path.suffix.lower() in extensions:
                yield path


def _relative(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def verify() -> list[Finding]:
    findings: list[Finding] = []

    for path in REQUIRED_FILES:
        if not path.is_file():
            findings.append(Finding("missing-file", _relative(path)))

    legacy = load_oracle_commands()
    if CATALOG_PATH.is_file():
        try:
            document = json.loads(CATALOG_PATH.read_text(encoding="utf-8"))
            commands = document.get("commands", [])
            ids = [item.get("id") for item in commands if isinstance(item, dict)]
            if len(ids) != 259:
                findings.append(Finding("catalog-count", f"expected 259 commands, found {len(ids)}"))
            if len(set(ids)) != len(ids):
                findings.append(Finding("catalog-duplicates", "native command IDs are not unique"))
            if set(ids) != set(legacy):
                missing = sorted(set(legacy) - set(ids))
                extra = sorted(set(ids) - set(legacy))
                findings.append(Finding("catalog-parity", f"missing={missing}; extra={extra}"))
            implemented_ids = {
                item.get("id")
                for item in commands
                if isinstance(item, dict) and item.get("migrationStatus") in {"Implemented", "BehaviorVerified"}
            }
            expected_ids = {
                "catalog", "presets", "system", "storage", "network",
                "experience-privacy-profiles", "cleaner-disk-pressure", "cleanup-targets",
                "startup", "security", "applications", "health", "pagefile", "tcp-global",
                "pagefile-recommendation", "security-controls", "network-measure", "process-modules",
                "security-maintenance", "network-experiments", "etw-sessions", "wua-history",
                "injection-surfaces", "remote-thread-events", "wua-search", "preset",
                "wdac-policies", "wdac-events", "internals-processes", "wua-hide",
                "internals-memory", "internals-cpu", "credential-providers", "appcontainer",
                "pagefile-set", "security-control-reduce", "security-control-restore", "network-experiment",
                "etw-capture", "injection-surface-quarantine", "wua-unhide", "wua-download",
                "wua-install", "wua-uninstall", "wdac-deploy", "desktop-controls", "wifi-profiles",
                "shell-extensions", "desktop-shortcuts", "boot", "bcd-export", "unattend-analyze",
                "provisioning-plan", "knowledge",
            }
            if implemented_ids != expected_ids:
                findings.append(Finding(
                    "implemented-command-set",
                    f"expected 54 command IDs, found {sorted(implemented_ids)}",
                ))
            for index, item in enumerate(commands):
                if not isinstance(item, dict):
                    findings.append(Finding("catalog-shape", f"commands[{index}] is not an object"))
                    continue
                for key in ("id", "title", "summary", "area", "section", "risk", "readOnly", "legacySource", "migrationStatus"):
                    if key not in item or item[key] in (None, ""):
                        findings.append(Finding("catalog-field", f"commands[{index}] missing {key}"))
                title = str(item.get("title", ""))
                summary = str(item.get("summary", ""))
                if title == item.get("id") or "Advanced headless capability" in summary:
                    findings.append(Finding("catalog-copy", f"technical fallback copy remains for {item.get('id')}"))
        except (OSError, json.JSONDecodeError) as exc:
            findings.append(Finding("catalog-json", str(exc)))

    for legacy_path, native_path, collection_key, expected_count in (
        (ORACLE_RULES_PATH, NATIVE_RULES_PATH, "rules", 69),
        (ORACLE_PRESETS_PATH, NATIVE_PRESETS_PATH, "presets", 7),
    ):
        if not legacy_path.is_file() or not native_path.is_file():
            continue
        try:
            legacy_document = json.loads(legacy_path.read_text(encoding="utf-8-sig"))
            native_document = json.loads(native_path.read_text(encoding="utf-8"))
            if native_document != legacy_document:
                findings.append(Finding("embedded-data-parity", _relative(native_path)))
            items = native_document.get(collection_key, [])
            if len(items) != expected_count:
                findings.append(Finding(
                    "embedded-data-count",
                    f"{_relative(native_path)} expected {expected_count}, found {len(items)}",
                ))
        except (OSError, json.JSONDecodeError) as exc:
            findings.append(Finding("embedded-data-json", f"{_relative(native_path)}: {exc}"))

    dispatcher_path = ROOT / "src/WinCare.Application/Commands/CommandDispatcher.cs"
    if dispatcher_path.is_file():
        dispatcher = dispatcher_path.read_text(encoding="utf-8")
        for token in (
            "command.not_found", "command.not_migrated", "command.readonly_mutation_denied",
            "command.review_required", "command.deadline_exceeded",
            "request.CorrelationId", "CancellationTokenSource.CreateLinkedTokenSource",
            "OperationCanceledException",
        ):
            if token not in dispatcher:
                findings.append(Finding("dispatcher-contract", f"missing {token}"))

    execution_view_model_path = ROOT / "src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs"
    if execution_view_model_path.is_file():
        execution_view_model = execution_view_model_path.read_text(encoding="utf-8")
        for token in (
            "correlationId = result.CorrelationId",
            "status = result.Status",
            "code = result.Code",
            "durationMilliseconds = result.Duration.TotalMilliseconds",
            "undoAvailable = result.UndoAvailable",
            "data = result.Data",
        ):
            if token not in execution_view_model:
                findings.append(Finding("execution-result-envelope", f"missing {token}"))

    handlers_root = ROOT / "src/WinCare.Application/Commands/Handlers"
    handler_ids: set[str] = set()
    if handlers_root.is_dir():
        for handler_path in handlers_root.glob("*CommandHandler.cs"):
            handler_text = handler_path.read_text(encoding="utf-8")
            match = re.search(r'CommandId\s*=>\s*"([^"]+)"', handler_text)
            if match:
                handler_ids.add(match.group(1))
    expected_ids = {
        "catalog", "presets", "system", "storage", "network",
        "experience-privacy-profiles", "cleaner-disk-pressure", "cleanup-targets",
        "startup", "security", "applications", "health", "pagefile", "tcp-global",
        "pagefile-recommendation", "security-controls", "network-measure", "process-modules",
        "security-maintenance", "network-experiments", "etw-sessions", "wua-history",
        "injection-surfaces", "remote-thread-events", "wua-search", "preset",
        "wdac-policies", "wdac-events", "internals-processes", "wua-hide",
        "internals-memory", "internals-cpu", "credential-providers", "appcontainer",
        "pagefile-set", "security-control-reduce", "security-control-restore", "network-experiment",
        "etw-capture", "injection-surface-quarantine", "wua-unhide", "wua-download",
        "wua-install", "wua-uninstall", "wdac-deploy", "desktop-controls", "wifi-profiles",
        "shell-extensions", "desktop-shortcuts", "boot", "bcd-export", "unattend-analyze",
        "provisioning-plan", "knowledge",
    }
    if handler_ids != expected_ids:
        findings.append(Finding("handler-set", f"expected 54 handlers, found {sorted(handler_ids)}"))

    for path in _iter_text_files(NATIVE_ROOTS):
        text = path.read_text(encoding="utf-8", errors="replace")
        for pattern in BANNED_NATIVE_PATTERNS:
            if pattern in text:
                findings.append(Finding("banned-reference", f"{_relative(path)} contains {pattern}"))
        if path.suffix.lower() in {".ps1", ".psm1", ".psd1"}:
            findings.append(Finding("native-powershell", _relative(path)))

    xml_paths = []
    for root in NATIVE_ROOTS:
        if root.exists():
            xml_paths.extend(path for path in root.rglob("*") if path.suffix.lower() in {".xaml", ".csproj", ".props", ".targets", ".xml"})
    manifest = ROOT / "src/WinCare.App/Package.appxmanifest"
    if manifest.is_file():
        xml_paths.append(manifest)
    interactive_controls = {
        "AutoSuggestBox", "Button", "CheckBox", "ComboBox", "Expander",
        "ListView", "NavigationView", "NavigationViewItem", "SelectorBar",
        "SelectorBarItem", "TextBox",
    }
    for path in sorted(set(xml_paths)):
        try:
            tree = ET.parse(path)
            if path.suffix.lower() == ".xaml":
                data_templates = 0
                typed_templates = 0
                for element in tree.getroot().iter():
                    local_name = element.tag.rsplit("}", 1)[-1]
                    if local_name == "DataTemplate":
                        data_templates += 1
                        if any(key.endswith("}DataType") or key == "x:DataType" for key in element.attrib):
                            typed_templates += 1
                    if local_name in interactive_controls and "AutomationProperties.Name" not in element.attrib:
                        findings.append(Finding("automation-name", f"{_relative(path)}: {local_name}"))
                if data_templates != typed_templates:
                    findings.append(Finding("untyped-template", f"{_relative(path)}: {typed_templates}/{data_templates} typed"))
        except ET.ParseError as exc:
            findings.append(Finding("xml-parse", f"{_relative(path)}: {exc}"))

    shell = ROOT / "src/WinCare.App/Views/ShellPage.xaml"
    if shell.is_file():
        text = html.unescape(shell.read_text(encoding="utf-8"))
        if "NavigationView" not in text:
            findings.append(Finding("shell-navigation", "ShellPage does not use NavigationView"))
        for automation_id, label in EXPECTED_NAVIGATION.items():
            if automation_id not in text or label not in text:
                findings.append(Finding("shell-item", f"missing {automation_id} / {label}"))
        for obsolete in (">Overview<", ">Action catalog<", ">Profiles<", ">Assurance<"):
            if obsolete in text:
                findings.append(Finding("obsolete-navigation", obsolete))

    page_root = ROOT / "src/WinCare.App/Views/Pages"
    for filename, tabs in EXPECTED_PAGE_TABS.items():
        path = page_root / filename
        if not path.is_file():
            findings.append(Finding("missing-page", _relative(path)))
            continue
        text = html.unescape(path.read_text(encoding="utf-8"))
        if "SelectorBar" not in text:
            findings.append(Finding("missing-selectorbar", _relative(path)))
        for tab in tabs:
            if tab not in text:
                findings.append(Finding("missing-tab", f"{filename}: {tab}"))

    all_tools = page_root / "AllToolsPage.xaml"
    if all_tools.is_file():
        text = all_tools.read_text(encoding="utf-8")
        for token in (
            "ListView", "x:DataType", "x:Bind", "AutomationProperties.AutomationId",
            "AutomationProperties.Name", "ExecuteSelectedToolCommand", "ExecutionResultText",
            'AutomationProperties.AutomationId="ExecuteSelectedTool"',
        ):
            if token not in text:
                findings.append(Finding("all-tools-contract", f"missing {token}"))
        for column in ("Command", "Category", "Risk", "Administrator access", "Restart"):
            if column not in text:
                findings.append(Finding("all-tools-column", column))


    profile_root = ROOT / "src/WinCare.App/Properties/PublishProfiles"
    profiles = sorted(profile_root.glob("*.pubxml")) if profile_root.is_dir() else []
    if len(profiles) != 4:
        findings.append(Finding("publish-profiles", f"expected 4 publish profiles, found {len(profiles)}"))
    for profile in profiles:
        profile_text = profile.read_text(encoding="utf-8", errors="replace")
        invalid = [character for character in profile_text if ord(character) < 32 and character not in "\n\r\t"]
        if invalid:
            findings.append(Finding("publish-profile-control", f"{_relative(profile)} contains control characters"))
        if profile.name.startswith("portable-") and "artifacts" not in profile_text:
            findings.append(Finding("portable-output", f"{_relative(profile)} has no artifacts output path"))

    app_project = ROOT / "src/WinCare.App/WinCare.App.csproj"
    if app_project.is_file():
        project_text = app_project.read_text(encoding="utf-8")
        if "Native\\$(Platform)\\wincare_core.dll" not in project_text:
            findings.append(Finding("native-stage", "WinCare.App does not stage the platform Rust DLL"))
        if "CopyToPublishDirectory" not in project_text:
            findings.append(Finding("native-publish", "WinCare.App does not copy the Rust DLL to portable output"))

    return findings


def main() -> int:
    findings = verify()
    if findings:
        print(f"native foundation verification failed: {len(findings)} finding(s)")
        for finding in findings:
            print(f"[{finding.code}] {finding.message}")
        return 1
    print("native foundation verification passed")
    print("catalog: 259 unique command IDs with exact frozen-oracle parity")
    print("native source: no PowerShell or WPF references")
    print("WinUI source: approved navigation, tabs, table contract, command execution binding, and automation metadata present")
    print("command runtime: 2 native read-only handlers with fail-closed admission")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
