#!/usr/bin/env python3
"""Apply the remaining deterministic PR #5 runtime-contract corrections."""
from __future__ import annotations

import os
import tempfile
from pathlib import Path

ROOT = Path.cwd().resolve()
MAX_TEXT_BYTES = 8 * 1024 * 1024


def read_text(relative: str) -> str:
    path = (ROOT / relative).resolve()
    if ROOT not in path.parents or not path.is_file() or path.is_symlink():
        raise RuntimeError(f"unsafe or missing source file: {relative}")
    before = path.stat(follow_symlinks=False)
    if before.st_size <= 0 or before.st_size > MAX_TEXT_BYTES:
        raise RuntimeError(f"source file is empty or oversized: {relative}")
    payload = path.read_bytes()
    after = path.stat(follow_symlinks=False)
    if len(payload) != before.st_size or (
        before.st_size,
        before.st_mtime_ns,
    ) != (
        after.st_size,
        after.st_mtime_ns,
    ):
        raise RuntimeError(f"source file changed while reading: {relative}")
    return payload.decode("utf-8-sig")


def write_text(relative: str, text: str) -> None:
    path = (ROOT / relative).resolve()
    if ROOT not in path.parents or path.is_symlink():
        raise RuntimeError(f"unsafe source destination: {relative}")
    descriptor, temporary = tempfile.mkstemp(
        prefix=path.name + ".",
        suffix=".tmp",
        dir=path.parent,
    )
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(text.encode("utf-8"))
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def replace_exact(relative: str, old: str, new: str, expected: int = 1) -> None:
    text = read_text(relative)
    observed = text.count(old)
    if observed != expected:
        raise RuntimeError(
            f"{relative}: expected {expected} exact occurrence(s), found {observed}: "
            f"{old[:160]!r}"
        )
    write_text(relative, text.replace(old, new, expected))


# A deliberately initialized null cache means "not built yet", not "return null".
# Preserve the historically capped source-file line count.
replace_exact(
    "src/WinCare/Core/11-ActionContracts.ps1",
    "    if($script:WinCareState.ContainsKey('ActionContracts')){return $script:WinCareState.ActionContracts}\n",
    "    if($script:WinCareState.ContainsKey('ActionContracts') -and "
    "$script:WinCareState.ActionContracts -is [Collections.IDictionary])"
    "{return $script:WinCareState.ActionContracts}\n",
)

# Bind strict-object arguments by name and make required schema fields explicit.
playbook_path = "src/WinCare/Core/00-08-PlaybookCatalog.ps1"
replace_exact(
    playbook_path,
    "    $null=Test-WinCareStrictObjectKeys $document @('schemaVersion','playbooks') 'playbook catalog'\n",
    "    $null=Test-WinCareStrictObjectKeys -InputObject $document `\n"
    "        -AllowedKeys @('schemaVersion','playbooks') `\n"
    "        -RequiredKeys @('schemaVersion','playbooks') -Context 'playbook catalog'\n",
)
replace_exact(
    playbook_path,
    "        'SourcePath',\n        'SourceSha256') -Context 'playbook'\n",
    "        'SourcePath',\n"
    "        'SourceSha256') `\n"
    "        -RequiredKeys @(\n"
    "            'id','title','description','minimumBuild','maximumBuild',\n"
    "            'architectures','requiresAdmin','sourceRecords','steps'\n"
    "        ) -Context 'playbook'\n",
)
replace_exact(
    playbook_path,
    "            'preset'{$null=Test-WinCareStrictObjectKeys $step @('kind','id') 'playbook preset step';\n"
    "                if([string]$step.id -notmatch '^[a-z0-9][a-z0-9._-]{2,80}$'){throw 'Playbook preset ID is invalid.'}}\n",
    "            'preset'{\n"
    "                $null=Test-WinCareStrictObjectKeys -InputObject $step `\n"
    "                    -AllowedKeys @('kind','id') -RequiredKeys @('kind','id') `\n"
    "                    -Context 'playbook preset step'\n"
    "                if([string]$step.id -notmatch '^[a-z0-9][a-z0-9._-]{2,80}$'){\n"
    "                    throw 'Playbook preset ID is invalid.'\n"
    "                }\n"
    "            }\n",
)
replace_exact(
    playbook_path,
    "            'catalog'{$null=Test-WinCareStrictObjectKeys $step @('kind','ids') 'playbook catalog step';if(@($step.ids).Count -lt 1){throw 'Playbook catalog step is empty.'}}\n",
    "            'catalog'{\n"
    "                $null=Test-WinCareStrictObjectKeys -InputObject $step `\n"
    "                    -AllowedKeys @('kind','ids') -RequiredKeys @('kind','ids') `\n"
    "                    -Context 'playbook catalog step'\n"
    "                if(@($step.ids).Count -lt 1){throw 'Playbook catalog step is empty.'}\n"
    "            }\n",
)
replace_exact(
    playbook_path,
    "            'context-menu'{$null=Test-WinCareStrictObjectKeys $step @('kind','id','enable') 'playbook context-menu step';\n"
    "                if($step.enable -isnot [bool]){throw 'Playbook context-menu enable must be Boolean.'}}\n",
    "            'context-menu'{\n"
    "                $null=Test-WinCareStrictObjectKeys -InputObject $step `\n"
    "                    -AllowedKeys @('kind','id','enable') `\n"
    "                    -RequiredKeys @('kind','id','enable') `\n"
    "                    -Context 'playbook context-menu step'\n"
    "                if($step.enable -isnot [bool]){\n"
    "                    throw 'Playbook context-menu enable must be Boolean.'\n"
    "                }\n"
    "            }\n",
)
replace_exact(
    playbook_path,
    "            'app-removal'{$null=Test-WinCareStrictObjectKeys $step @('kind','id','includeProvisioned') 'playbook app-removal step';\n"
    "                if($step.includeProvisioned -isnot [bool]){throw 'Playbook app-removal flag must be Boolean.'}}\n",
    "            'app-removal'{\n"
    "                $null=Test-WinCareStrictObjectKeys -InputObject $step `\n"
    "                    -AllowedKeys @('kind','id','includeProvisioned') `\n"
    "                    -RequiredKeys @('kind','id','includeProvisioned') `\n"
    "                    -Context 'playbook app-removal step'\n"
    "                if($step.includeProvisioned -isnot [bool]){\n"
    "                    throw 'Playbook app-removal flag must be Boolean.'\n"
    "                }\n"
    "            }\n",
)

# Keep array concatenation inside the named SourceRecords argument boundary.
replace_exact(
    "src/WinCare/Providers/72-Provisioning.ps1",
    "    $plan=New-WinCarePlan -Title (\"{0} [{1}]\" -f [string]$Blueprint.name,$Stage) -Description ([string]$Blueprint.description) -SourceRecords @($Blueprint.sourceRecords)+@('SRC:4e4b1fa0d3')\n",
    "    $plan=New-WinCarePlan -Title (\"{0} [{1}]\" -f [string]$Blueprint.name,$Stage) -Description ([string]$Blueprint.description) -SourceRecords (@($Blueprint.sourceRecords)+@('SRC:4e4b1fa0d3'))\n",
)

# Some import paths expose a placeholder module version of 0.0. Resolve the
# authoritative manifest version without hard-coding release metadata.
replace_exact(
    "src/WinCare/WinCare.psm1",
    """$moduleVersion = $ExecutionContext.SessionState.Module.Version
if ($null -eq $moduleVersion) {
    throw 'WinCare must be imported through WinCare.psd1 so the module version is authoritative.'
}
$script:WinCareVersion = $moduleVersion.ToString()
""",
    """$moduleVersion = $ExecutionContext.SessionState.Module.Version
if ($null -eq $moduleVersion -or $moduleVersion -eq [version]'0.0') {
    $moduleManifestPath = Join-Path $PSScriptRoot 'WinCare.psd1'
    $moduleManifest = Import-PowerShellDataFile -LiteralPath $moduleManifestPath
    $moduleVersion = [version][string]$moduleManifest.ModuleVersion
}
if ($null -eq $moduleVersion -or $moduleVersion -eq [version]'0.0') {
    throw 'WinCare module version could not be resolved from the module session or manifest.'
}
$script:WinCareVersion = $moduleVersion.ToString()
""",
)

# The generated previous-release fixture has a byte ceiling but used an
# unbounded decoded ReadToEnd. Stream and cap the decoded text as well.
fixture_path = "tools/Build-PreviousReleaseFixture.ps1"
replace_exact(
    fixture_path,
    "            try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }\n",
    "            try {\n"
    "                $builder = [Text.StringBuilder]::new(\n"
    "                    [int][Math]::Min([long]$entry.Length, 1048576L)\n"
    "                )\n"
    "                $buffer = [char[]]::new(4096)\n"
    "                try {\n"
    "                    $totalCharacters = 0\n"
    "                    while (($count = $reader.Read($buffer, 0, $buffer.Length)) -gt 0) {\n"
    "                        $totalCharacters += $count\n"
    "                        if ($totalCharacters -gt 1048576) {\n"
    "                            throw 'The archived module manifest exceeds the permitted decoded length.'\n"
    "                        }\n"
    "                        $null = $builder.Append($buffer, 0, $count)\n"
    "                    }\n"
    "                    $text = $builder.ToString()\n"
    "                } finally {\n"
    "                    [Array]::Clear($buffer, 0, $buffer.Length)\n"
    "                }\n"
    "            } finally { $reader.Dispose() }\n",
)

# Fail the remediation immediately if any known-bad construct survived.
checks = {
    "src/WinCare/Core/11-ActionContracts.ps1": (
        "$script:WinCareState.ActionContracts -is [Collections.IDictionary]",
    ),
    playbook_path: (
        "-RequiredKeys @('schemaVersion','playbooks')",
        "-AllowedKeys @('kind','id','enable')",
        "-AllowedKeys @('kind','id','includeProvisioned')",
        "-Context 'playbook app-removal step'",
    ),
    "src/WinCare/Providers/72-Provisioning.ps1": (
        "-SourceRecords (@($Blueprint.sourceRecords)+@('SRC:4e4b1fa0d3'))",
    ),
    "src/WinCare/WinCare.psm1": (
        "$moduleVersion -eq [version]'0.0'",
        "Import-PowerShellDataFile -LiteralPath $moduleManifestPath",
    ),
    fixture_path: (
        "$reader.Read($buffer, 0, $buffer.Length)",
        "wincare.previous-release.fixture/v2",
    ),
}
for relative, markers in checks.items():
    text = read_text(relative)
    for marker in markers:
        if marker not in text:
            raise RuntimeError(f"postcondition missing from {relative}: {marker}")
if "ReadToEnd" in read_text(fixture_path):
    raise RuntimeError("previous-release fixture still contains unbounded ReadToEnd")

print("Applied bounded deterministic PR #5 runtime-contract corrections.")
