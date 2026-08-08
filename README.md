<p align="center">
  <img src="design/WinCare-Wordmark.svg" width="520" alt="WinCare" />
</p>

# WinCare

WinCare is a native Windows care, diagnostics, security, repair, and recovery application built with WinUI 3, C#, and Rust.

This repository snapshot is a **native source release candidate**, not a production-ready replacement for version 2.2.0. It finalizes the native architecture, user interface, command catalog, source packaging, and promotion controls while refusing to hide incomplete command migration.

## Exact readiness

| Measure | Count |
|---|---:|
| Stable command IDs preserved | 259 of 259 |
| Typed contracts verified | 2 of 259 |
| Native handlers implemented | 2 of 259 |
| Behavior verified on Windows | 0 of 259 |
| Native implementation blockers | 257 |
| Production behavior-verification blockers | 259 |

The implemented read-only handlers are `catalog` and `presets`. Every other command remains visible in All tools but is disabled by the dispatcher and returns an explicit not-implemented result.

## Finalized artifact model

The finalizer produces two deterministic source archives:

1. **Native source** contains WinUI 3, C#, Rust, tests, native workflows, design assets, frozen JSON parity fixtures, and zero `.ps1`, `.psm1`, or `.psd1` files.
2. **Legacy oracle** contains the historical PowerShell implementation and provenance required for controlled parity work. It is not a runtime dependency of the native application.

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

C# owns product policy, workflows, approvals, evidence, journaling, recovery, and packaging. Rust owns only bounded native primitives that have a measured performance or safety benefit. The UI cannot bypass command admission.

## Product navigation

The approved native shell uses:

- Home
- Checkup
- System care
- Security
- Repair & recovery
- All tools
- Activity

Settings, Help and documentation, and About WinCare are in the navigation footer. Each destination uses focused horizontal tabs. Data-heavy surfaces use virtualized tabular lists with responsive compact records.

## Local source verification

```text
python tools/verify_native_foundation.py
python -m unittest discover -s tests/native -v
```

These checks verify frozen 259-ID parity, native source boundaries, command status accounting, deterministic archive rules, WinUI source structure, accessibility metadata, packaging metadata, and the fail-closed production gate. They do not substitute for a Windows build or runtime test.

## Windows release evidence still required

A promotable release must pass the Windows matrix for x64 and ARM64:

- Rust formatting, Clippy, tests, and release DLL build
- .NET restore, tests, and WinUI build
- app launch through WinApp CLI
- UI Automation and keyboard flows
- light, dark, and Windows Contrast screenshots
- signed and timestamped MSIX packages
- self-contained portable packages
- command-by-command behavioral parity

See [Windows validation](docs/migration/windows-validation.md) and [finalization status](docs/migration/finalization-status.md).

## Safety model

Every mutating capability must follow one governed path:

```text
request
  -> current-state observation
  -> typed plan
  -> policy and compatibility checks
  -> review and approval
  -> admitted operation
  -> evidence and postcondition
  -> journal and receipt
  -> compensation when supported
```

Missing evidence, missing prerequisites, unsupported environments, native errors, or incomplete migration never become success.

## Documentation

| Document | Purpose |
|---|---|
| `DESIGN.md` | approved native interaction and visual system |
| `docs/Architecture.md` | native component and trust boundaries |
| `docs/migration/command-parity-ledger.md` | exact command migration accounting |
| `docs/migration/finalization-status.md` | release-candidate and production status |
| `docs/migration/windows-validation.md` | Windows build, UI, and package validation |
| `VALIDATION.md` | evidence vocabulary and promotion gates |

## License

See `LICENSE`.
