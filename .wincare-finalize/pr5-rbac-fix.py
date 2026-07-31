#!/usr/bin/env python3
"""Correct PR #5 RBAC aliases and fail closed for unknown action names."""
from __future__ import annotations

import os
import tempfile
from pathlib import Path

ROOT = Path.cwd().resolve()
SOURCE = "src/WinCare/Providers/25-SystemPolicy.ps1"
TEST = "tests/Core.Contracts.Tests.ps1"
MAX_TEXT_BYTES = 4 * 1024 * 1024


def read_text(relative: str) -> str:
    path = (ROOT / relative).resolve()
    if ROOT not in path.parents or not path.is_file() or path.is_symlink():
        raise RuntimeError(f"unsafe or missing file: {relative}")
    before = path.stat(follow_symlinks=False)
    if before.st_size <= 0 or before.st_size > MAX_TEXT_BYTES:
        raise RuntimeError(f"empty or oversized file: {relative}")
    payload = path.read_bytes()
    after = path.stat(follow_symlinks=False)
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise RuntimeError(f"file changed while reading: {relative}")
    if len(payload) != before.st_size:
        raise RuntimeError(f"short read: {relative}")
    return payload.decode("utf-8-sig")


def write_text(relative: str, text: str) -> None:
    path = (ROOT / relative).resolve()
    if ROOT not in path.parents or path.is_symlink():
        raise RuntimeError(f"unsafe destination: {relative}")
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


def replace_exact(relative: str, old: str, new: str) -> None:
    text = read_text(relative)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(
            f"{relative}: expected one exact replacement anchor, found {count}: {old!r}"
        )
    write_text(relative, text.replace(old, new, 1))


replace_exact(
    SOURCE,
    """    $highRiskActions=@('DisableWdac','UnloadKernelDriver','ModifyHvci','ForceSystemReboot')
    $mediumRiskActions=@('OptimizeStorage','TrimMemoryWorkingSets','ClearTemporaryFiles','ApplyGroupPolicy')
    $actionRiskLevel=if($ActionContractName -in $highRiskActions){3}elseif($ActionContractName -in $mediumRiskActions){2}else{1}
""",
    """    $actionRiskLevels=@{
        ClearTemporaryFiles=1
        OptimizeStorage=2
        TrimMemoryWorkingSets=2
        ApplyGroupPolicy=2
        DisableWdac=3
        UnloadKernelDriver=3
        ModifyHvci=3
        ForceSystemReboot=3
    }
    if(-not $actionRiskLevels.ContainsKey($ActionContractName)){
        Write-WinCareLog -Level Audit -Message 'Unknown RBAC action contract denied.' -Data @{
            requestedRole=$RequestedRole;action=$ActionContractName;user=$UserIdentity
        }
        return New-WinCareResult -Success $false -Status Blocked -Code 'UnknownActionContract' `
            -Message "Action contract '$ActionContractName' is not registered for RBAC evaluation." `
            -ExitCode 78 -Data @{RequestedRole=$RequestedRole;ActionContractName=$ActionContractName;
                UserIdentity=$UserIdentity;EvidenceType='UnknownRbacActionAuditRecord'}
    }
    $actionRiskLevel=[int]$actionRiskLevels[$ActionContractName]
""",
)

replace_exact(
    TEST,
    """      $denied.Success | Should -Be $false
      $denied.Code | Should -Be 'BlockedByPolicy'
      $denied.ExitCode | Should -Be 78
""",
    """      $denied.Success | Should -Be $false
      $denied.Code | Should -Be 'BlockedByPolicy'
      $denied.ExitCode | Should -Be 78

      $unknown = Assert-WinCareRolePermission -RequestedRole 'FleetLead' -ActionContractName 'UnregisteredMutation' -UserIdentity 'TestUser'
      $unknown.Success | Should -Be $false
      $unknown.Code | Should -Be 'UnknownActionContract'
      $unknown.ExitCode | Should -Be 78
""",
)

source = read_text(SOURCE)
test = read_text(TEST)
required_source_markers = (
    "ClearTemporaryFiles=1",
    "UnknownActionContract",
    "$actionRiskLevel=[int]$actionRiskLevels[$ActionContractName]",
)
for marker in required_source_markers:
    if marker not in source:
        raise RuntimeError(f"RBAC postcondition missing: {marker}")
if "ClearTemporaryFiles','ApplyGroupPolicy" in source:
    raise RuntimeError("ClearTemporaryFiles remains classified as medium risk")
for marker in ("UnregisteredMutation", "UnknownActionContract"):
    if marker not in test:
        raise RuntimeError(f"RBAC regression assertion missing: {marker}")

print("Applied fail-closed PR #5 RBAC classification correction.")
