<p align="center">
  <img src="design/WinCare-Wordmark.svg" width="520" alt="WinCare" />
</p>

# WinCare

WinCare is a native Windows care, diagnostics, security, repair, and recovery application built
with WinUI 3, C#, and Rust.

This repository snapshot is a **native source release candidate**, not a production-ready
replacement for version 2.2.0. It finalizes the native architecture, user interface, command
catalog, source packaging, and promotion controls while refusing to hide incomplete command
migration.

## Exact readiness

| Measure | Count |
|---|---:|
| Stable command IDs preserved | 259 of 259 |
| Typed contracts verified | 54 of 259 |
| Native handlers implemented | 54 of 259 |
| Behavior verified on Windows | 0 of 259 |
| Native implementation blockers | 205 |
| Production behavior-verification blockers | 259 |

The implemented native handlers are `catalog`, `presets`, `system`, `storage`, `network`, `experience-privacy-profiles`, `cleaner-disk-pressure`, `cleanup-targets`, `startup`, `security`, `applications`, `health`, `pagefile`, `tcp-global`, `pagefile-recommendation`, `security-controls`, `network-measure`, `process-modules`, `security-maintenance`, `network-experiments`, `etw-sessions`, `wua-history`, `injection-surfaces`, `remote-thread-events`, `wua-search`, `preset`, `wdac-policies`, `wdac-events`, `internals-processes`, `wua-hide`, `internals-memory`, `internals-cpu`, `credential-providers`, `appcontainer`, `pagefile-set`, `security-control-reduce`, `security-control-restore`, `network-experiment`, `etw-capture`, `injection-surface-quarantine`, `wua-unhide`, `wua-download`, `wua-install`, `wua-uninstall`, `wdac-deploy`, `desktop-controls`, `wifi-profiles`, `shell-extensions`, `desktop-shortcuts`, `boot`, `bcd-export`, `unattend-analyze`, `provisioning-plan`, and `knowledge`. Every other command remains visible in All tools but is disabled by the dispatcher and returns an explicit not-implemented result.

## Finalized artifact model

The finalizer produces two deterministic source archives:

1. **Native source** contains WinUI 3, C#, Rust, tests, native workflows, design assets, frozen
   JSON parity fixtures, and zero `.ps1`, `.psm1`, or `.psd1` files.
2. **Legacy oracle** contains the historical PowerShell implementation and provenance required for
   controlled parity work. It is not a runtime dependency of the native application.

Production finalization fails closed until all 259 commands are marked `BehaviorVerified`.

```text
python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version 2.4.0-rc1 \
  --mode rc

python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version 2.4.0 \
  --mode production
```

The second command currently exits non-zero by design.

## Native architecture

```text
WinCare.App                 WinUI 3 shell, pages, accessibility, lifecycle
WinCare.Application         search, navigation, command dispatch, use cases
WinCare.Domain              typed requests, results, activity, recovery contracts
WinCare.Infrastructure      Windows integration, telemetry, Rust interop
WinCare.CommandCatalog      all 259 stable command definitions
native/wincare-core         bounded native primitives through a versioned C ABI
```

C# owns product policy, workflows, approvals, evidence, journaling, recovery, and packaging.
Rust owns only bounded native primitives that have a measured performance or safety benefit.
The UI cannot bypass command admission.

## Visual design system

The Cyber-Teal UI implements a unified visual identity across WinUI 3 Light, Dark, and High
Contrast themes:

| Token | Light | Dark |
|---|---|---|
| `AccentTealBrush` | `#007A99` (5.1:1 contrast) | `#00D2B4` |
| `PillReadOnlyBgBrush` | `#047857` + white text (5.48:1) | `#047857` + white text |
| `PillMutatingBgBrush` | `#DC2626` + white text (4.83:1) | `#C41A1A` + white text (5.98:1) |
| `PillElevatedBgBrush` | `#D97706` + `#1A1A1A` (5.46:1) | `#F59E0B` + `#1A1A1A` (**8.10:1**) |
| `PillNotReadyBgBrush` | `#E2E8F0` + `#1A1A1A` (**14.12:1**) | `#1F2937` + `#94A3B8` |

All pairs verified by `tools/verify_pill_contrast.py` against WCAG 2.1 AA (≥ 4.5:1).

## Product navigation

The approved native shell uses:

- Home
- Checkup
- System care
- Security
- Repair & recovery
- All tools
- Activity

Settings, Help and documentation, and About WinCare are in the navigation footer. Each destination
uses focused horizontal tabs. Data-heavy surfaces use virtualized tabular lists with responsive
compact records and 920 DIP adaptive breakpoints.

## Local source verification

```text
python tools/verify_native_foundation.py
python -m unittest discover -s tests/native -v
python tools/verify_visual_tokens.py
python tools/verify_pill_contrast.py
python tools/validate_gui.py
python -m unittest tools.test_gui -v
```

These checks verify frozen 259-ID parity, native source boundaries, command status accounting,
deterministic archive rules, WinUI source structure, accessibility metadata, packaging metadata,
and the fail-closed production gate. They do not substitute for a Windows build or runtime test.

## Windows release evidence still required

A promotable release must pass the Windows matrix for x64 and ARM64:

- Rust formatting, Clippy, tests, and release DLL build
- .NET restore, tests, and WinUI 3 build
- App launch through WinApp CLI
- UI Automation and keyboard flows
- Light, dark, and Windows Contrast screenshots
- Signed and timestamped MSIX packages
- Self-contained portable packages
- Command-by-command behavioral parity

See [Windows validation](docs/migration/windows-validation.md) and
[finalization status](docs/migration/finalization-status.md).

## Safety model

Every mutating capability must follow one governed path:

```text
request
  -> current-state observation
  -> typed plan
  -> policy and compatibility checks
  -> review and approval        (gated by request.Apply + options.ReviewApproved)
  -> admitted operation
  -> evidence and postcondition
  -> journal and receipt
  -> compensation when supported
```

Missing evidence, missing prerequisites, unsupported environments, native errors, or incomplete
migration never become success. Dry-run (`Apply=false`) flows bypass the review gate but still
require migration status and handler registration.

## Security invariants

- `DiskCleanupCommandHandler` enforces an `AllowedBasePaths` safelist: only `%TEMP%` and
  `%LOCALAPPDATA%\Temp` are permitted targets. Path traversal attempts are silently dropped.
- `CommandDispatcher` exception logs emit only the exception type name — `ex.Message` is never
  written to the activity journal to prevent file-path or PII leakage.
- Thread-safe journal binding: `ActivityJournalService` instances are created per `CommandDispatcher` instance, with `CommandRuntime.LastJournal` providing design-time fallback.
- All Rust FFI exports are wrapped in `catch_unwind` — panics cannot cross the FFI boundary.

## CI

| Workflow | Trigger | Purpose |
|---|---|---|
| `validate` (ci.yml) | push / PR | Full evidence + Windows matrix (x64 + ARM64) |
| `Native WinUI` | push to master / PR | Rust fmt/clippy/test + .NET build + portable zip |
| `Native Python Gates` | push / PR | Python verification gates (tokens, contrast, GUI) |
| `Native Release Finalization` | manual dispatch | RC and production source finalization |
| `release` | tag / manual | Sealed, attested GitHub release |
| `release-dispatch` | master merge / manual | Dispatch the hardened release workflow |

## Documentation

| Document | Purpose |
|---|---|
| `CHANGELOG.md` | version history and release notes |
| `DESIGN.md` | approved native interaction and visual system |
| `docs/Architecture.md` | native component and trust boundaries |
| `docs/migration/command-parity-ledger.md` | exact command migration accounting |
| `docs/migration/finalization-status.md` | release-candidate and production status |
| `docs/migration/windows-validation.md` | Windows build, UI, and package validation |
| `VALIDATION.md` | evidence vocabulary and promotion gates |
| `CONTRIBUTING.md` | contribution guidelines |
| `SECURITY.md` | security policy and vulnerability reporting |

## License

See `LICENSE`.
