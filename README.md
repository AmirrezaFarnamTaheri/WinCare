<p align="center">
  <img src="design/WinCare-Wordmark.svg" width="520" alt="WinCare" />
</p>

# WinCare

WinCare is a native Windows care, diagnostics, security, repair, and recovery application built
with WinUI 3, C#, and Rust.

This repository snapshot is a **native source release candidate**, not a production-ready
replacement for version 2.2.0. It finalizes the native architecture, modular plugin platform,
AI System Doctor, user interface, command catalog, source packaging, and promotion controls while
refusing to hide incomplete command migration.

## Exact readiness

| Measure | Count |
|---|---:|
| Stable command IDs preserved | 259 of 259 |
| Typed command contracts routed | 259 of 259 |
| Native command routes implemented | 259 of 259 |
| Behavior verified on Windows | 0 of 259 |
| Native implementation blockers | 0 |
| Production behavior-verification blockers | 259 |

All 259 stable command IDs route through one fail-closed native executor. The executor validates
mutating parameters before approval, distinguishes preview from apply, uses bounded native
process/file operations, and returns blocked/failed results instead of fabricated success when a
dependency, platform capability, or safety precondition is unavailable.

## Native architecture

```text
WinCare.App                 WinUI 3 shell, pages, accessibility, lifecycle, AI Doctor UI
WinCare.Application         search, navigation, dynamic command dispatch, plugins, AI diagnostics
WinCare.Domain              typed requests, results, activity, sync models, recovery contracts
WinCare.Infrastructure      Windows integration, telemetry, process runner, Rust interop, cloud sync
WinCare.CommandCatalog      all 259 stable command definitions + built-in plugin manifests
native/wincare-core         bounded native primitives through a versioned C ABI
native/wincare-guard        Rust 2024 background health daemon (RAM/disk/thermal + Toast notifications)
tools/wincare-plugin-cli    Node.js developer CLI for creating, validating, and packaging plugins
```

C# owns product policy, workflows, approvals, evidence, journaling, recovery, and packaging.
Rust owns bounded native primitives and background health monitoring that provide measured
performance or safety benefits.

## Modular Plugin Store & Architecture

WinCare provides a high-security modular plugin platform:
- **Dynamic Command Dispatcher**: Plugins register tools directly into All Tools dynamically with zero UI freezing.
- **Native Script Execution**: Safe execution of script-based extensions (`.cmd`, `.bat`, native binaries) via `BoundedProcessRunner` with process limits and strict path traversal isolation.
- **Package Integrity & Admission**: Enforces strict package ID matching and full-stream SHA-256 digest validation on all remote and local packages.
- **Declared Capabilities & Security Disclosure**: Full-trust in-process execution warning modals and capability consent reviews before any installation.
- **Developer CLI SDK**: Complete developer toolkit (`tools/wincare-plugin-cli`) for linting, validating, and bundling plugins.

## AI System Doctor

- Local ONNX DirectML inference runtime with sub-1.5s cold-start latency.
- Natural-language `IntentTranslator` mapping user diagnostic queries into verified two-phase `DoctorActionPlan` sequences.
- Cyber-Teal diagnostic interface with real-time status chat bubbles and 1-click execution cards.

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

The approved native shell provides:

- **Home**
- **Checkup**
- **System care**
- **Security**
- **Repair & recovery**
- **All tools**
- **AI Doctor** (Local ONNX-powered intelligent system diagnostics)
- **Plugin Store** (Browse, install, and manage extensions)
- **Activity**

Settings, Help and documentation, and About WinCare are in the navigation footer.

## Local source verification

```bash
# Verify native catalog parity & frozen oracle
python tools/verify_native_foundation.py

# Run Python regression test suite
python -m unittest discover -s tests/native -v

# Run Rust native core & guard test suites
cargo test --manifest-path native/Cargo.toml

# Run Community Plugin CLI tests
python tests/tools/test_plugin_cli.py -v

# Verify design tokens & WCAG accessibility contrast
python tools/verify_visual_tokens.py
python tools/verify_pill_contrast.py
```

## Automated CI release gate

The repository's GitHub Actions workflow (`Native WinUI`) includes an automated **`release-gate`** job. When code is pushed or merged to `master`, CI executes native verification, compiles Rust and .NET 8 binaries, runs unit tests, packages MSIX installers and portable ZIP distributions for both `x64` and `ARM64`, and automatically publishes the compiled release packages directly to [GitHub Releases](https://github.com/AmirrezaFarnamTaheri/WinCare/releases).

## Security invariants

- File and cleanup operations canonicalize user-selected paths, reject reparse-point operation roots, skip reparse-point descendants during recursive traversal, and use bounded iterative enumeration.
- `CommandDispatcher` exception journals emit only the exception type name — `ex.Message` is never written to the activity journal, avoiding file-path or PII leakage.
- Native plugins execute in-process and are gated behind explicit trust disclosures and package SHA-256 verification. Core namespaces (`wincare.core.*`, `system.*`) are strictly reserved.
- All Rust FFI exports are wrapped in `catch_unwind` — panics cannot cross the FFI boundary.

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
