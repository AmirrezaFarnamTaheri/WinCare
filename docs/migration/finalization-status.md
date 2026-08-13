# Project finalization status

**Version:** 2.4.0-rc1  
**Classification:** native source release candidate  
**Production promotable:** no

## Current readiness

| Measure | Count |
|---|---:|
| Stable command IDs | 259 / 259 |
| Native executor routes | 259 / 259 |
| Catalog entries at `Implemented` | 259 / 259 |
| Mutating commands with explicit preview validation | 104 / 104 |
| `BehaviorVerified` on supported Windows hosts | 0 / 259 |
| Native implementation blockers | 0 |
| Production behavior-verification blockers | 259 |
| PowerShell files in native runtime roots | 0 |

## What changed in this candidate

- Removed the generated per-command handler sprawl and the fabricated-success result pattern.
- Routed every stable command through `ICommandOperationExecutor` and the Windows-native executor.
- Added typed JSON parameter entry in All Tools; invalid JSON and non-object payloads fail locally.
- Made preview/apply admission explicit for every mutating command.
- Replaced `CommandRuntime.LastJournal` with one app-scoped injected activity journal and refresh-on-navigation.
- Added bounded process execution, bounded file reads, reparse-point-safe traversal, atomic local state, and capability/dependency failures.
- Corrected Windows Update partial-result handling, resumable download semantics, firewall-state inspection, and hash-pinned Studio ADB/ViVeTool execution.
- Preserved the executable historical PowerShell implementation only as a separate migration oracle; it is excluded from native runtime roots and the native source artifact.
- Removed misleading generic `UndoAvailable` claims until an end-to-end recovery action is actually wired.

## Deliberate safety boundary

`security-control-reduce` and `security-control-restore` no longer substitute Windows Firewall for
the legacy security controls. Temporary reduction is fail-closed until the native product has a
separately launchable recovery host that can authenticate snapshots and restore protection even if
the WinUI process exits. No host mutation occurs on that blocked path.

## Verification available in this environment

The cross-platform source, GUI, contrast, finalization, and native regression gates are expected to
pass before an RC artifact is emitted. Windows-only .NET/WinUI, Rust MSVC, MSIX, UI Automation, and
command-oracle comparisons remain required evidence and are not replaced by source inspection.

## Artifact contract

`tools/finalize_native_release.py --mode rc` creates:

- `WinCare-<version>-native-source.zip`
- `WinCare-<version>-legacy-oracle.zip`
- `WinCare-<version>-finalization-report.md`
- `WinCare-<version>-finalization-manifest.json`

`--mode production` continues to exit non-zero until all 259 commands are `BehaviorVerified`.
