#!/usr/bin/env python3
"""Correct brand finalization for WinCare's canonical installer ownership."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path.cwd().resolve()


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def write(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8", newline="\n")


def replace_required(text: str, old: str, new: str, expected: int = 1) -> str:
    count = text.count(old)
    if count != expected:
        raise RuntimeError(
            f"expected {expected} occurrence(s), found {count}: {old[:120]!r}"
        )
    return text.replace(old, new)


def regex_required(text: str, pattern: str, replacement: str) -> str:
    updated, count = re.subn(pattern, replacement, text, count=1, flags=re.DOTALL)
    if count != 1:
        raise RuntimeError(f"expected one regex match: {pattern!r}")
    return updated


brand_path = ROOT / ".wincare-finalize" / "brand.py"
brand = read(brand_path)
new_patch_installer = r'''def patch_installer() -> None:
    installer = ROOT / "Install-WinCare.ps1"
    if not installer.is_file() or installer.is_symlink():
        raise ValueError("Install-WinCare.ps1 was not found or is unsafe.")
    text = read_text(installer)
    if "wincare.brand.package-check/v1" not in text:
        anchor = "$ErrorActionPreference = 'Stop'"
        if anchor not in text:
            raise ValueError("Installer error-policy anchor is missing.")
        block = """$ErrorActionPreference = 'Stop'
# wincare.brand.package-check/v1
$brandIconPath = Join-Path $PSScriptRoot 'design\\WinCare.ico'
$brandManifestPath = Join-Path $PSScriptRoot 'design\\WinCare-Brand.manifest.json'
if (-not (Test-Path -LiteralPath $brandIconPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $brandManifestPath -PathType Leaf)) {
    throw 'The WinCare installer package is missing its verified brand identity assets.'
}
"""
        text = text.replace(anchor, block, 1)
        write_text(installer, text)

    shortcut_owner = ROOT / "src" / "WinCare" / "Install" / "Private" / "30-Shortcuts.ps1"
    if not shortcut_owner.is_file() or shortcut_owner.is_symlink():
        raise ValueError("The canonical WinCare shortcut owner is missing or unsafe.")
    shortcuts = read_text(shortcut_owner)
    if "wincare.brand.shortcut-icon/v1" not in shortcuts:
        shortcuts = "# wincare.brand.shortcut-icon/v1\n" + shortcuts
        shortcuts = replace_required(
            shortcuts,
            "param([string]$ShortcutPath,[string]$ExpectedTarget,[AllowEmptyString()][string]$ExpectedArguments='',[string]$ExpectedWorkingDirectory)",
            "param([string]$ShortcutPath,[string]$ExpectedTarget,[AllowEmptyString()][string]$ExpectedArguments='',[string]$ExpectedWorkingDirectory,[AllowNull()][string]$ExpectedIconLocation)",
        )
        shortcuts = replace_required(
            shortcuts,
            "([IO.Path]::GetFullPath($shortcut.TargetPath) -eq [IO.Path]::GetFullPath($ExpectedTarget)) -and ([string]$shortcut.Arguments -eq $ExpectedArguments) -and ([IO.Path]::GetFullPath($shortcut.WorkingDirectory) -eq [IO.Path]::GetFullPath($ExpectedWorkingDirectory))",
            "([IO.Path]::GetFullPath($shortcut.TargetPath) -eq [IO.Path]::GetFullPath($ExpectedTarget)) -and ([string]$shortcut.Arguments -eq $ExpectedArguments) -and ([IO.Path]::GetFullPath($shortcut.WorkingDirectory) -eq [IO.Path]::GetFullPath($ExpectedWorkingDirectory)) -and ([string]::IsNullOrWhiteSpace($ExpectedIconLocation) -or [string]::Equals([string]$shortcut.IconLocation,$ExpectedIconLocation,[StringComparison]::OrdinalIgnoreCase))",
        )
        shortcuts = replace_required(
            shortcuts,
            "$records=@([ordered]@{Name='WinCare.lnk';Target=Join-Path $Destination 'WinCare-GUI.exe';Arguments = '';WorkingDirectory = $Destination},[ordered]@{Name='WinCare Console.lnk';Target=Join-Path $Destination 'WinCare-TUI.exe';Arguments = '';WorkingDirectory = $Destination});$shell=$null",
            "$records=@([ordered]@{Name='WinCare.lnk';Target=Join-Path $Destination 'WinCare-GUI.exe';Arguments='';WorkingDirectory=$Destination;IconLocation=(Join-Path $Destination 'design\\WinCare.ico')+',0'},[ordered]@{Name='WinCare Console.lnk';Target=Join-Path $Destination 'WinCare-TUI.exe';Arguments='';WorkingDirectory=$Destination;IconLocation=(Join-Path $Destination 'design\\WinCare.ico')+',0'});$shell=$null",
        )
        shortcuts = replace_required(
            shortcuts,
            "$shortcut.TargetPath=$r.Target;$shortcut.Arguments=$r.Arguments;$shortcut.WorkingDirectory=$r.WorkingDirectory;$shortcut.Save()",
            "$shortcut.TargetPath=$r.Target;$shortcut.Arguments=$r.Arguments;$shortcut.WorkingDirectory=$r.WorkingDirectory;$shortcut.IconLocation=$r.IconLocation;$shortcut.Save()",
        )
        shortcuts = replace_required(
            shortcuts,
            "Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $r.Arguments $r.WorkingDirectory",
            "Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $r.Arguments $r.WorkingDirectory $r.IconLocation",
        )
        legacy_identity = "$working=if($r.WorkingDirectory){[string]$r.WorkingDirectory}else{$Destination};if(!(Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $arguments $working))"
        compatible_identity = "$working=if($r.WorkingDirectory){[string]$r.WorkingDirectory}else{$Destination};$iconLocation=if($r.PSObject.Properties['IconLocation']){[string]$r.IconLocation}else{$null};if(!(Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $arguments $working $iconLocation))"
        shortcuts = replace_required(
            shortcuts,
            legacy_identity,
            compatible_identity,
            expected=3,
        )
        write_text(shortcut_owner, shortcuts)

'''
brand = regex_required(
    brand,
    r"def patch_installer\(\) -> None:\n.*?\n\ndef patch_readme\(\) -> None:",
    new_patch_installer + "\ndef patch_readme() -> None:",
)
new_verify = r'''def verify_project_icon_contract(icon: bytes) -> None:
    project_files = [
        path
        for path in ROOT.rglob("*.csproj")
        if ".git" not in path.parts and "artifacts" not in path.parts and not path.is_symlink()
    ]
    property_files = [
        path
        for path in ROOT.rglob("Directory.Build.props")
        if ".git" not in path.parts and "artifacts" not in path.parts and not path.is_symlink()
    ]
    references = 0
    for path in project_files + property_files:
        text = read_text(path)
        for reference in re.findall(
            r"<ApplicationIcon>([^<]+)</ApplicationIcon>",
            text,
            flags=re.IGNORECASE,
        ):
            references += 1
            candidate = reference.strip()
            if candidate.casefold().endswith("wincare.ico"):
                continue
            property_reference = re.fullmatch(r"\$\(([A-Za-z][A-Za-z0-9_.-]*)\)", candidate)
            if property_reference is None:
                raise ValueError(f"Unsupported ApplicationIcon reference in {path}: {candidate}")
            property_name = re.escape(property_reference.group(1))
            values = re.findall(
                rf"<{property_name}>([^<]+)</{property_name}>",
                text,
                flags=re.IGNORECASE,
            )
            if not any(value.strip().casefold().endswith("wincare.ico") for value in values):
                raise ValueError(
                    f"ApplicationIcon property does not resolve to WinCare.ico in {path}: {candidate}"
                )
    if references < 1:
        raise ValueError("No MSBuild ApplicationIcon contract exists for WinCare executables.")

    icons = [
        path
        for path in ROOT.rglob("WinCare.ico")
        if ".git" not in path.parts and "artifacts" not in path.parts and not path.is_symlink()
    ]
    if not icons:
        raise ValueError("No WinCare.ico files exist after brand generation.")
    for path in icons:
        if read_bounded(path, MAX_BINARY_BYTES) != icon:
            raise ValueError(f"Application icon copy differs from the canonical identity: {path}")

'''
brand = regex_required(
    brand,
    r"def verify_project_icon_contract\(icon: bytes\) -> None:\n.*?\n\ndef main\(\) -> int:",
    new_verify + "\ndef main() -> int:",
)
write(brand_path, brand)

# Correct the regression test to follow canonical ownership and MSBuild indirection.
test_path = ROOT / "tools" / "test_brand_identity.py"
test = read(test_path)
new_tests = r'''    def test_executable_projects_reference_the_canonical_icon(self) -> None:
        reference_count = 0
        paths = list(ROOT.rglob("*.csproj")) + list(ROOT.rglob("Directory.Build.props"))
        for path in paths:
            if ".git" in path.parts or "artifacts" in path.parts or path.is_symlink():
                continue
            text = read_text(path)
            for reference in re.findall(
                r"<ApplicationIcon>([^<]+)</ApplicationIcon>",
                text,
                flags=re.IGNORECASE,
            ):
                reference_count += 1
                candidate = reference.strip()
                if candidate.lower().endswith("wincare.ico"):
                    continue
                match = re.fullmatch(r"\$\(([A-Za-z][A-Za-z0-9_.-]*)\)", candidate)
                self.assertIsNotNone(match, candidate)
                property_name = re.escape(match.group(1))
                values = re.findall(
                    rf"<{property_name}>([^<]+)</{property_name}>",
                    text,
                    flags=re.IGNORECASE,
                )
                self.assertTrue(
                    any(value.strip().lower().endswith("wincare.ico") for value in values),
                    candidate,
                )
        self.assertGreaterEqual(reference_count, 1)

    def test_installer_uses_installed_brand_assets_for_shortcuts(self) -> None:
        installer = read_text(ROOT / "Install-WinCare.ps1")
        shortcuts = read_text(
            ROOT / "src" / "WinCare" / "Install" / "Private" / "30-Shortcuts.ps1"
        )
        self.assertIn("wincare.brand.package-check/v1", installer)
        self.assertIn("design\\WinCare-Brand.manifest.json", installer)
        self.assertIn("wincare.brand.shortcut-icon/v1", shortcuts)
        self.assertIn(".IconLocation", shortcuts)
        self.assertIn("Join-Path $Destination 'design\\WinCare.ico'", shortcuts)
        self.assertIn("ExpectedIconLocation", shortcuts)

'''
test = regex_required(
    test,
    r"    def test_executable_projects_reference_the_canonical_icon\(self\) -> None:\n.*?\n    def test_release_receipts_include_closed_brand_identity",
    new_tests + "    def test_release_receipts_include_closed_brand_identity",
)
write(test_path, test)
print("Aligned WinCare brand finalization with canonical installer ownership.")
