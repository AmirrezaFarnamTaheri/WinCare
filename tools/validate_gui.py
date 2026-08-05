#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

X = "{http://schemas.microsoft.com/winfx/2006/xaml}"


def relative_luminance(hex_color: str) -> float:
    values = [int(hex_color[index:index + 2], 16) / 255 for index in (1, 3, 5)]
    linear = [value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4 for value in values]
    return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2]


def contrast(foreground: str, background: str) -> float:
    high, low = sorted((relative_luminance(foreground), relative_luminance(background)), reverse=True)
    return (high + 0.05) / (low + 0.05)


def extract_headless(root: Path) -> set[str]:
    definitions = (
        (root / "src/WinCare/UI/98-Headless.ps1", "Get-WinCareHeadlessCommandName"),
        (root / "src/WinCare/UI/97-AdvancedCapabilities.ps1", "Get-WinCareAdvancedHeadlessCommandName"),
    )
    commands: set[str] = set()
    for path, function_name in definitions:
        text = path.read_text("utf-8")
        pattern = rf"function {re.escape(function_name)}\s*\{{\s*@\((.*?)\)\s*\}}"
        match = re.search(pattern, text, re.S)
        if match:
            commands.update(re.findall(r"'([^']+)'", match.group(1)))
    return commands


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", default=".")
    parser.add_argument("--output")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    errors: list[str] = []
    warnings: list[str] = []

    gui_root = root / "src/WinCare/Data/Gui"
    xaml_path = gui_root / "WinCare.MainWindow.xaml"
    actions_path = gui_root / "actions.json"
    navigation_path = gui_root / "navigation.json"
    required_files = (
        xaml_path,
        actions_path,
        navigation_path,
        root / "WinCare-GUI.ps1",
        root / "run-wincare-gui.cmd",
        root / "tests/Gui.Contracts.Tests.ps1",
    )
    for path in required_files:
        if not path.is_file():
            errors.append(f"missing GUI artifact: {path.relative_to(root)}")

    names: list[str] = []
    resource_definitions: set[str] = set()
    resource_references: set[str] = set()
    missing_resources: list[str] = []
    if xaml_path.is_file():
        try:
            tree = ET.parse(xaml_path)
            names = [value for element in tree.iter() for key, value in element.attrib.items() if key == X + "Name"]
            if len(names) != len(set(names)):
                errors.append("duplicate x:Name values in GUI XAML")
            required_names = {
                "MainWindow", "NavigationList", "DashboardPanel", "ActionsPanel", "AssurancePanel",
                "SettingsPanel", "ConfirmOverlay", "BusyOverlay", "ActionList", "ArgumentsTextBox",
                "ActionOutputTextBox", "AssuranceGrid", "AssuranceSourceText", "AssuranceExportText",
                "AssuranceTestText", "AssuranceNativeText",
            }
            missing = required_names - set(names)
            if missing:
                errors.append(f"missing named GUI controls: {sorted(missing)}")
            if any((X + "Class") in element.attrib for element in tree.iter()):
                errors.append("GUI XAML must not use x:Class")
            xaml_text = xaml_path.read_text("utf-8")
            for token in ("BackgroundBrush", "SurfaceBrush", "AccentBrush", "TextBrush"):
                if "{DynamicResource " + token + "}" not in xaml_text:
                    errors.append(f"GUI theme token is not dynamically bound: {token}")
            if xaml_text.count("AutomationProperties.Name=") < 15:
                errors.append("GUI accessibility names are incomplete")
            resource_definitions = set(re.findall(r'x:Key\s*=\s*["\']([^"\']+)["\']', xaml_text))
            resource_references = set(re.findall(r"\{(?:StaticResource|DynamicResource)\s+([^},\s]+)", xaml_text))
            missing_resources = sorted(resource_references - resource_definitions)
            if missing_resources:
                errors.append(f"GUI XAML references undefined resources: {missing_resources}")
        except Exception as error:
            errors.append(f"invalid GUI XAML: {error}")

    try:
        actions = json.loads(actions_path.read_text("utf-8"))
        navigation = json.loads(navigation_path.read_text("utf-8"))
    except Exception as error:
        actions = []
        navigation = []
        errors.append(f"invalid GUI JSON: {error}")

    action_ids = [item.get("id") for item in actions]
    navigation_ids = [item.get("id") for item in navigation]
    if len(action_ids) != len(set(action_ids)) or None in action_ids:
        errors.append("GUI action IDs are missing or duplicated")
    if len(navigation_ids) != len(set(navigation_ids)) or len(navigation_ids) < 9:
        errors.append("GUI navigation is incomplete or duplicated")
    if "assurance" not in navigation_ids or "evidence" in navigation_ids:
        errors.append("GUI navigation must expose the current assurance workspace")

    commands = extract_headless(root)
    unknown = sorted({item.get("command") for item in actions if item.get("command") not in commands})
    if unknown:
        errors.append(f"GUI curated actions reference unknown commands: {unknown}")

    catalog_path = root / "src/WinCare/UI/Gui/00-GuiCatalog.ps1"
    catalog_text = catalog_path.read_text("utf-8", errors="replace") if catalog_path.is_file() else ""
    if "foreach($command in $commands)" not in catalog_text:
        errors.append("GUI does not generate coverage for all headless commands")

    runtime_path = root / "src/WinCare/UI/Gui/10-GuiRuntime.ps1"
    runtime_text = runtime_path.read_text("utf-8", errors="replace") if runtime_path.is_file() else ""
    control_block = re.search(r"function Get-WinCareGuiControlTable.*?\$names=@\((.*?)\)\s*\$table", runtime_text, re.S)
    bound_controls = set(re.findall(r"'([^']+)'", control_block.group(1))) if control_block else set()
    if not control_block:
        errors.append("GUI runtime control binding table is missing")
    missing_bound_controls = sorted(bound_controls - set(names))
    if missing_bound_controls:
        errors.append(f"GUI runtime binds controls absent from XAML: {missing_bound_controls}")

    critical = [item for item in actions if item.get("critical")]
    if not critical or any(item.get("risk") != "Critical" for item in critical):
        errors.append("GUI Critical action labeling is incomplete")
    if "else{'High'}" not in catalog_text:
        errors.append("generated mutating GUI commands are not conservatively labeled High")
    for command in (
        "pagefile-set", "wua-uninstall", "wdac-deploy", "injection-surface-quarantine",
        "run-automation", "group-policy-import", "sysmon-uninstall", "offline-package-add",
    ):
        if command not in catalog_text:
            errors.append(f"critical generated command is missing from GUI risk map: {command}")

    for marker, message in (
        ("I ACCEPT LEGACY UNSAFE MUTATIONS", "GUI exact unsafe acknowledgement is missing"),
        ("[Diagnostics.ProcessStartInfo]::new()", "GUI command execution does not use ProcessStartInfo"),
        ("$psi.ArgumentList.Add", "GUI command execution does not use argument-safe ArgumentList"),
        ("$psi.WorkingDirectory=$root", "GUI child process working directory is not pinned to the repository root"),
        ("24576 bytes", "GUI command argument payload is not explicitly bounded"),
        ("ValidateSet('Normal','HighContrast','Monochrome')", "GUI accessibility theme modes are incomplete"),
        ("[Windows.SystemParameters]::HighContrast", "GUI does not honor system high-contrast mode"),
        ("Update-WinCareGuiAssurance", "GUI current-assurance refresh is missing"),
    ):
        if marker not in runtime_text:
            errors.append(message)
    if re.search(r"Invoke-WinCarePlan\s+-Plan", runtime_text):
        errors.append("GUI bypasses the headless/typed authority path")

    pairs = [
        ("#E6EDF3", "#0B1020", 4.5), ("#8B949E", "#121A2B", 4.5),
        ("#F85149", "#3D141B", 4.5), ("#34D6E9", "#173B46", 4.5),
        ("#3FB950", "#173D35", 4.5), ("#D29922", "#3D3520", 4.5),
    ]
    contrast_results = []
    for foreground, background, minimum in pairs:
        ratio = contrast(foreground, background)
        contrast_results.append({"foreground": foreground, "background": background, "ratio": round(ratio, 3), "minimum": minimum})
        if ratio < minimum:
            errors.append(f"GUI contrast {foreground} on {background} is {ratio:.2f}, below {minimum}")

    test_files = sorted((root / "tests").rglob("*.ps1"))
    pester_cases = sum(len(re.findall(r"^\s*It\s+['\"]", path.read_text("utf-8", errors="replace"), re.M)) for path in test_files)
    gui_test_path = root / "tests/Gui.Contracts.Tests.ps1"
    gui_pester_cases = len(re.findall(r"^\s*It\s+['\"]", gui_test_path.read_text("utf-8", errors="replace"), re.M)) if gui_test_path.is_file() else 0
    if len(test_files) < 15 or pester_cases < 190:
        errors.append(f"Pester inventory regressed: suites={len(test_files)} cases={pester_cases}")
    if gui_pester_cases < 10:
        errors.append(f"GUI Pester coverage regressed: {gui_pester_cases}")

    report = {
        "schema": "wincare.gui.validation/v1",
        "root": ".",
        "status": "passed" if not errors else "failed",
        "gui": {
            "namedControls": len(names),
            "runtimeControlBindings": len(bound_controls),
            "xamlResourceDefinitions": len(resource_definitions),
            "xamlResourceReferences": len(resource_references),
            "missingXamlResources": missing_resources,
            "navigationItems": len(navigation),
            "curatedActions": len(actions),
            "headlessCommands": len(commands),
            "criticalCuratedActions": len(critical),
            "pesterSuites": len(test_files),
            "pesterCases": pester_cases,
            "guiPesterCases": gui_pester_cases,
            "contrast": contrast_results,
        },
        "errors": errors,
        "warnings": warnings,
    }
    payload = json.dumps(report, indent=2)
    print(payload)
    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(payload + "\n", encoding="utf-8")
    return 0 if not errors else 1


if __name__ == "__main__":
    raise SystemExit(main())
