#!/usr/bin/env python3
"""Converge generated PR #5 candidate on maintainability and bounded-I/O contracts."""
from __future__ import annotations

import os
import re
import tempfile
from pathlib import Path

ROOT = Path.cwd().resolve()
MAX_BYTES = 8 * 1024 * 1024


def read_text(relative: str) -> str:
    path = (ROOT / relative).resolve()
    if ROOT not in path.parents or not path.is_file() or path.is_symlink():
        raise RuntimeError(f"unsafe or missing source: {relative}")
    before = path.stat(follow_symlinks=False)
    if before.st_size <= 0 or before.st_size > MAX_BYTES:
        raise RuntimeError(f"source outside size bounds: {relative}")
    payload = path.read_bytes()
    after = path.stat(follow_symlinks=False)
    if len(payload) != before.st_size or (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise RuntimeError(f"source changed while reading: {relative}")
    return payload.decode("utf-8-sig")


def write_text(relative: str, text: str) -> None:
    path = (ROOT / relative).resolve()
    if ROOT not in path.parents or path.is_symlink():
        raise RuntimeError(f"unsafe destination: {relative}")
    descriptor, temporary = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(text.encode("utf-8"))
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def replace_once(relative: str, old: str, new: str) -> None:
    text = read_text(relative)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{relative}: expected one occurrence, found {count}: {old[:160]!r}")
    write_text(relative, text.replace(old, new, 1))


def assert_source_bounds(relative: str, maximum_lines: int | None = None) -> None:
    text = read_text(relative)
    lines = text.splitlines()
    if maximum_lines is not None and len(lines) > maximum_lines:
        raise RuntimeError(f"{relative}: {len(lines)} lines exceeds {maximum_lines}")
    excessive = [(index, len(line)) for index, line in enumerate(lines, 1) if len(line) > 200]
    if excessive:
        raise RuntimeError(f"{relative}: lines exceed 200 characters: {excessive[:8]}")


# Preserve the cache-type guard without growing the historically capped file.
action_path = "src/WinCare/Core/11-ActionContracts.ps1"
replace_once(
    action_path,
    "    if($script:WinCareState.ContainsKey('ActionContracts') -and "
    "$script:WinCareState.ActionContracts -is [Collections.IDictionary]){\n"
    "        return $script:WinCareState.ActionContracts\n"
    "    }\n",
    "    if($script:WinCareState.ContainsKey('ActionContracts') -and "
    "$script:WinCareState.ActionContracts -is [Collections.IDictionary])"
    "{return $script:WinCareState.ActionContracts}\n",
)

# Keep named strict-object contracts while wrapping them below the 200-character limit.
playbook_path = "src/WinCare/Core/00-08-PlaybookCatalog.ps1"
replacements = (
    (
        "    $null=Test-WinCareStrictObjectKeys -InputObject $document -AllowedKeys "
        "@('schemaVersion','playbooks') -RequiredKeys @('schemaVersion','playbooks') "
        "-Context 'playbook catalog'\n",
        "    $null=Test-WinCareStrictObjectKeys -InputObject $document `\n"
        "        -AllowedKeys @('schemaVersion','playbooks') `\n"
        "        -RequiredKeys @('schemaVersion','playbooks') -Context 'playbook catalog'\n",
    ),
    (
        "        'SourcePath',\n        'SourceSha256') -RequiredKeys "
        "@('id','title','description','minimumBuild','maximumBuild','architectures',"
        "'requiresAdmin','sourceRecords','steps') -Context 'playbook'\n",
        "        'SourcePath',\n        'SourceSha256') `\n"
        "        -RequiredKeys @('id','title','description','minimumBuild','maximumBuild', `\n"
        "            'architectures','requiresAdmin','sourceRecords','steps') -Context 'playbook'\n",
    ),
    (
        "            'preset'{$null=Test-WinCareStrictObjectKeys -InputObject $step "
        "-AllowedKeys @('kind','id') -RequiredKeys @('kind','id') "
        "-Context 'playbook preset step';\n",
        "            'preset'{\n"
        "                $null=Test-WinCareStrictObjectKeys -InputObject $step `\n"
        "                    -AllowedKeys @('kind','id') -RequiredKeys @('kind','id') `\n"
        "                    -Context 'playbook preset step'\n",
    ),
    (
        "            'catalog'{$null=Test-WinCareStrictObjectKeys -InputObject $step "
        "-AllowedKeys @('kind','ids') -RequiredKeys @('kind','ids') "
        "-Context 'playbook catalog step';if(@($step.ids).Count -lt 1){throw "
        "'Playbook catalog step is empty.'}}\n",
        "            'catalog'{\n"
        "                $null=Test-WinCareStrictObjectKeys -InputObject $step `\n"
        "                    -AllowedKeys @('kind','ids') -RequiredKeys @('kind','ids') `\n"
        "                    -Context 'playbook catalog step'\n"
        "                if(@($step.ids).Count -lt 1){throw 'Playbook catalog step is empty.'}\n"
        "            }\n",
    ),
    (
        "            'context-menu'{$null=Test-WinCareStrictObjectKeys -InputObject $step "
        "-AllowedKeys @('kind','id','operation') -RequiredKeys @('kind','id','operation') "
        "-Context 'playbook context-menu step';\n",
        "            'context-menu'{\n"
        "                $null=Test-WinCareStrictObjectKeys -InputObject $step `\n"
        "                    -AllowedKeys @('kind','id','operation') `\n"
        "                    -RequiredKeys @('kind','id','operation') -Context 'playbook context-menu step'\n",
    ),
    (
        "            'app-removal'{$null=Test-WinCareStrictObjectKeys -InputObject $step "
        "-AllowedKeys @('kind','query','provider') -RequiredKeys @('kind','query') "
        "-Context 'playbook app-removal step';if($step.PSObject.Properties['provider'] "
        "-and [string]$step.provider -notin @('Winget','Appx','Auto')){throw "
        "'Playbook app-removal provider is invalid.'}}\n",
        "            'app-removal'{\n"
        "                $null=Test-WinCareStrictObjectKeys -InputObject $step `\n"
        "                    -AllowedKeys @('kind','query','provider') `\n"
        "                    -RequiredKeys @('kind','query') -Context 'playbook app-removal step'\n"
        "                if($step.PSObject.Properties['provider'] -and `\n"
        "                    [string]$step.provider -notin @('Winget','Appx','Auto')){\n"
        "                    throw 'Playbook app-removal provider is invalid.'\n"
        "                }\n"
        "            }\n",
    ),
)
for old, new in replacements:
    replace_once(playbook_path, old, new)

# Replace the only unbounded archive-manifest read with capped streaming I/O.
fixture_path = "tools/Build-PreviousReleaseFixture.ps1"
replace_once(
    fixture_path,
    "            try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }\n",
    "            try {\n"
    "                $builder = [Text.StringBuilder]::new([int][Math]::Min([long]$entry.Length, 1048576L))\n"
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

assert_source_bounds(action_path, maximum_lines=600)
assert_source_bounds(playbook_path)
fixture_text = read_text(fixture_path)
if "ReadToEnd" in fixture_text:
    raise RuntimeError("previous-release fixture still contains ReadToEnd")
if "wincare.previous-release.fixture/v2" not in fixture_text:
    raise RuntimeError("previous-release fixture evidence contract is missing")

print("Converged generated candidate on maintainability and bounded-I/O contracts.")
