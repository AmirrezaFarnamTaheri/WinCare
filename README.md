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
| Typed command contracts routed | 259 of 259 |
| Native command routes implemented | 259 of 259 |
| Behavior verified on Windows | 0 of 259 |
| Native implementation blockers | 0 |
| Production behavior-verification blockers | 259 |

All 259 stable command IDs now route through one fail-closed native executor rather than
copy-pasted handlers. The executor validates mutating parameters before approval, distinguishes
preview from apply, uses bounded native process/file operations, and returns blocked/failed results
instead of fabricated success when a dependency, platform capability, or safety precondition is
unavailable.

`Implemented` is intentionally not the same as `BehaviorVerified`. Command-by-command Windows
comparison against the historical oracle has not run in this Linux environment, so production
promotion remains blocked. Temporary security-control reduction is also intentionally blocked until
a separately launchable native recovery host can prove authenticated automatic restoration; the
previous incorrect firewall substitution was removed rather than weakening that safety contract.

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
```

These checks verify frozen 259-ID parity, native source boundaries, command status accounting,
deterministic archive rules, WinUI source structure, accessibility metadata, packaging metadata,
and the fail-closed production gate. In the full migration workspace, additional legacy-oracle GUI
compatibility gates also run. Finalized native-source archives intentionally omit executable legacy
PowerShell, so the two finalization tests that require that executable oracle report an explicit skip.
These checks do not substitute for a Windows build or runtime test.

## Automated CI release gate

The repository's GitHub Actions workflow (`Native WinUI`) includes an automated **`release-gate`** job. When code is pushed or merged to `master`, CI executes native verification, compiles Rust and .NET 8 binaries, runs unit tests, packages MSIX installers and portable ZIP distributions for both `x64` and `ARM64`, and automatically publishes the compiled release packages directly to [GitHub Releases](https://github.com/AmirrezaFarnamTaheri/WinCare/releases).

- Rust formatting, Clippy, unit tests, and C-ABI DLL compilation (`x64` / `ARM64`)
- .NET restore, C# test suites, and WinUI 3 build
- Packaged MSIX installers (`WinCare.App.Package`)
- Self-contained portable executable packages (`WinCare-x64-portable.zip` / `WinCare-ARM64-portable.zip`)
- Deterministic native source finalization archives
- Automatic GitHub Release deployment (`release-gate`)

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

- File and cleanup operations canonicalize user-selected paths, reject reparse-point operation roots,
  skip reparse-point descendants during recursive traversal, and use bounded iterative enumeration so
  junctions and inaccessible trees cannot silently escape the admitted boundary or exhaust recursion.
- `CommandDispatcher` exception journals emit only the exception type name — `ex.Message` is never
  written to the activity journal, avoiding file-path or PII leakage through user-visible history.
- One application-scoped `ActivityJournalService` is injected into the dispatcher and Activity page
  through `AppRuntime`; cached Activity navigation refreshes from that same journal instead of relying
  on mutable global fallback state.
- Temporary security-control reduction fails closed until a native recovery host can prove authenticated
  automatic restoration; no unrelated firewall control is substituted.
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
