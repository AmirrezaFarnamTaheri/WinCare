from __future__ import annotations

import re
from pathlib import Path

ROOT = Path.cwd()


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one occurrence, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


def regex_once(path: str, pattern: str, replacement: str, flags: int = 0) -> None:
    text = read(path)
    updated, count = re.subn(pattern, replacement, text, count=1, flags=flags)
    if count != 1:
        raise RuntimeError(f"{path}: expected one regex match, found {count}: {pattern!r}")
    write(path, updated)


# Pester compatibility in both execution paths.
for path in ("tools/Invoke-WindowsValidation.ps1", "tools/Build-Release.ps1"):
    replace_once(
        path,
        "Import-Module Pester -RequiredVersion 5.5.0 -ErrorAction Stop",
        "Import-Module Pester -MinimumVersion 5.5.0 -ErrorAction Stop",
    )

# Thread the previous-release archive into Windows validation.
replace_once(
    "tools/Invoke-WindowsValidation.ps1",
    "    [ValidateRange(60,7200)][int]$GateTimeoutSeconds = 1800,\n    [switch]$SkipReleaseBuild",
    "    [ValidateRange(60,7200)][int]$GateTimeoutSeconds = 1800,\n"
    "    [string]$PreviousReleaseArchivePath,\n"
    "    [switch]$SkipReleaseBuild",
)
replace_once(
    "tools/Invoke-WindowsValidation.ps1",
    "        $release = Invoke-ValidationGate -Name '05-release' -TimeoutSeconds ([math]::Max($GateTimeoutSeconds, 3600)) -Script \"& '$quotedRoot\\tools\\Build-Release.ps1' -Root '$quotedRoot' -OutputDirectory '$quotedOutput\\release' -SkipTests\"",
    "        $previousReleaseArgument = ''\n"
    "        if (-not [string]::IsNullOrWhiteSpace($PreviousReleaseArchivePath)) {\n"
    "            $resolvedPreviousRelease = (Resolve-Path -LiteralPath $PreviousReleaseArchivePath -ErrorAction Stop).Path\n"
    "            $quotedPreviousRelease = $resolvedPreviousRelease.Replace(\"'\", \"''\")\n"
    "            $previousReleaseArgument = \" -PreviousReleaseArchivePath '$quotedPreviousRelease'\"\n"
    "        }\n"
    "        $release = Invoke-ValidationGate -Name '05-release' -TimeoutSeconds ([math]::Max($GateTimeoutSeconds, 3600)) -Script \"& '$quotedRoot\\tools\\Build-Release.ps1' -Root '$quotedRoot' -OutputDirectory '$quotedOutput\\release'$previousReleaseArgument\"",
)

# Thread the previous archive into the canonical release lifecycle.
replace_once(
    "tools/Build-Release.ps1",
    "    [switch]$SkipTests,\n    [switch]$AllowDirty",
    "    [switch]$SkipTests,\n    [string]$PreviousReleaseArchivePath,\n    [switch]$AllowDirty",
)
replace_once(
    "tools/Build-Release.ps1",
    "    & (Join-Path $PSScriptRoot 'Test-InstallationLifecycle.ps1') -ArchivePath $archive -WorkRoot (Join-Path $workRoot 'install-lifecycle')",
    "    $lifecycleParameters = @{\n"
    "        ArchivePath = $archive\n"
    "        WorkRoot = (Join-Path $workRoot 'install-lifecycle')\n"
    "    }\n"
    "    if (-not [string]::IsNullOrWhiteSpace($PreviousReleaseArchivePath)) {\n"
    "        $lifecycleParameters.PreviousArchivePath = (Resolve-Path -LiteralPath $PreviousReleaseArchivePath -ErrorAction Stop).Path\n"
    "    }\n"
    "    & (Join-Path $PSScriptRoot 'Test-InstallationLifecycle.ps1') @lifecycleParameters",
)

# Keep the existing lifecycle focused and delegate only version transition.
replace_once(
    "tools/Test-InstallationLifecycle.ps1",
    "    [Parameter(Mandatory)][string]$ArchivePath,\n    [string]$WorkRoot = (Join-Path $env:TEMP ('WinCare-install-lifecycle-' + [guid]::NewGuid().ToString('N')))",
    "    [Parameter(Mandatory)][string]$ArchivePath,\n"
    "    [string]$PreviousArchivePath,\n"
    "    [string]$WorkRoot = (Join-Path $env:TEMP ('WinCare-install-lifecycle-' + [guid]::NewGuid().ToString('N')))",
)
replace_once(
    "tools/Test-InstallationLifecycle.ps1",
    "    Write-WinCareLifecyclePhase 'corrupt-installation-removal' passed 'strict rejection, trusted module loading, and explicit corrupt removal all passed'\n\n    [pscustomobject]@{",
    "    Write-WinCareLifecyclePhase 'corrupt-installation-removal' passed 'strict rejection, trusted module loading, and explicit corrupt removal all passed'\n\n"
    "    $upgrade = $null\n"
    "    if (-not [string]::IsNullOrWhiteSpace($PreviousArchivePath)) {\n"
    "        $upgrade = & (Join-Path $PSScriptRoot 'Test-UpgradeLifecycle.ps1') `\n"
    "            -CurrentArchivePath $archive `\n"
    "            -PreviousArchivePath $PreviousArchivePath `\n"
    "            -WorkRoot (Join-Path $work 'upgrade-lifecycle')\n"
    "        if ($upgrade.Status -ne 'passed' -or -not $upgrade.UpgradeVerified -or -not $upgrade.UpgradeRollbackVerified) {\n"
    "            throw 'Version-upgrade lifecycle validation did not report complete success.'\n"
    "        }\n"
    "    }\n\n"
    "    [pscustomobject]@{",
)
replace_once(
    "tools/Test-InstallationLifecycle.ps1",
    "        CorruptRemovalVerified = $true\n    } | ConvertTo-Json -Depth 8",
    "        CorruptRemovalVerified = $true\n"
    "        UpgradeVerified = [bool]($upgrade -and $upgrade.UpgradeVerified)\n"
    "        UpgradeRollbackVerified = [bool]($upgrade -and $upgrade.UpgradeRollbackVerified)\n"
    "        PreviousVersion = if ($upgrade) { [string]$upgrade.PreviousVersion } else { $null }\n"
    "        CurrentVersion = if ($upgrade) { [string]$upgrade.CurrentVersion } else { $null }\n"
    "    } | ConvertTo-Json -Depth 8",
)

# Volatile branch validation evidence is never embedded in immutable release bytes.
replace_once(
    "tools/finalize_release.py",
    "    for name, data in core_files.items():\n        folded = name.casefold()\n        base = Path(name).name.casefold()\n        if name in GENERATED or base in {item.casefold() for item in EXPECTED_EXES}:\n            continue",
    "    for name, data in core_files.items():\n        folded = name.casefold()\n        base = Path(name).name.casefold()\n        if folded == \"docs/windows-validation-evidence.md\":\n            continue\n        if name in GENERATED or base in {item.casefold() for item in EXPECTED_EXES}:\n            continue",
)

if (ROOT / "docs/RELEASE.md").exists():
    replace_once(
        "docs/RELEASE.md",
        "- Pester 5.5.0;",
        "- Pester 5.5.0 or newer;",
    )
    replace_once(
        "docs/RELEASE.md",
        "| `-SkipTests` | Skip selected local test phases | Development/diagnostic only; not production evidence |\n| `-AllowDirty`",
        "| `-SkipTests` | Skip selected local test phases | Development/diagnostic only; not production evidence |\n| `-PreviousReleaseArchivePath` | Verified lower-version archive used for upgrade and rollback rehearsal | Required by the complete Windows release workflow |\n| `-AllowDirty`",
    )
    replace_once(
        "docs/RELEASE.md",
        "`tools/Test-InstallationLifecycle.ps1` exercises the final immutable archive through the supported installation lifecycle, including clean installation, identity/integrity validation, repair behavior, shortcuts/launchers as applicable, and uninstall verification.",
        "`tools/Test-InstallationLifecycle.ps1` exercises the final immutable archive through clean installation, identity/integrity validation, repair, force recovery, shortcuts/launchers, and uninstall. When a verified lower-version archive is supplied, it delegates to `tools/Test-UpgradeLifecycle.ps1` to prove rejected-tamper rollback, installation-identity preservation, user-data preservation, version transition, executable self-tests, and post-upgrade uninstall.",
    )

# Native nullable contracts become warning-free and warnings are release-blocking.
replace_once(
    "src/WinCare/Native/WinCare.Native.csproj",
    "    <Nullable>enable</Nullable>",
    "    <Nullable>enable</Nullable>\n    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>",
)
for old, new in (
    ("public string DeviceName { get; set; }", "public string DeviceName { get; set; } = string.Empty;"),
    ("public string Description { get; set; }", "public string Description { get; set; } = string.Empty;"),
    ("public string PackageFullName { get; set; }", "public string PackageFullName { get; set; } = string.Empty;"),
    ("StringBuilder packageFullName", "StringBuilder? packageFullName"),
    ("string arguments,", "string? arguments,"),
    ("public string DeviceName;", "public string DeviceName = string.Empty;"),
):
    replace_once("src/WinCare/Native/WinCare.ShellHardware.cs", old, new)

print("Windows release finalization source changes applied.")
