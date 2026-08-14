# Native validation and promotion

WinCare separates source validation, Windows runtime evidence, package verification, and
command-by-command parity. Passing one evidence class never substitutes for another.

## Evidence terms

- **Verified:** behavior executed successfully in the named environment.
- **Statically validated:** source or artifact structure checked without executing Windows behavior.
- **Reviewed:** implementation or evidence inspected against its contract.
- **Pending:** required evidence was unavailable in the current environment.
- **Blocked:** a declared prerequisite or safety gate prevented execution.
- **Failed:** a check ran and did not satisfy its contract.

## Current 2.5.0-rc1 evidence

### Source, Rust, and Python verification checks in this workspace

- [x] `python tools/verify_native_foundation.py` — 259/259 unique command IDs verified with exact frozen oracle parity
- [x] `python -m unittest discover -s tests/native -v` — 56/56 unit tests passed (1 symlink test skipped on Windows)
- [x] `cargo test --manifest-path native/Cargo.toml` — 29/29 Rust workspace tests passed (18 unit + 5 FFI contract + 6 Guard daemon)
- [x] `cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings` — 0 warnings under `-D warnings`
- [x] `python tests/tools/test_plugin_cli.py -v` — 3/3 Plugin CLI tests passed (linter, scaffolding, packaging)
- [x] `python tools/verify_visual_tokens.py` — 33/33 visual tokens verified across Light, Dark, and High Contrast dictionaries
- [x] `python tools/verify_pill_contrast.py` — All 8 status pill pairs pass WCAG 2.1 AA (up to 14.68:1 contrast)
- [x] Native source scan contains no executable PowerShell/WPF runtime dependency
- [x] All 259 catalog IDs have an explicit executor route + dynamic plugin dispatch support
- [x] All 104 mutating IDs have explicit parameter preflight before approval
- [x] Plugin package admission strictly verifies manifest ID == target ID and stream SHA-256 integrity
- [x] Plugin backups isolated to `.staging/backups/` outside live discovery directory
- [x] ViewModels contain no `Microsoft.UI.Xaml` dependency

### Windows-only checks not executed in this environment

- [ ] WinUI launch with WinApp CLI — **Pending**
- [ ] UI Automation, keyboard flows, theme screenshots, and runtime accessibility — **Pending**
- [ ] MSIX install/launch/repair/upgrade/uninstall — **Pending**
- [ ] Command-by-command Windows comparison with historical oracle — **Pending**

The current execution environment does not provide the WinApp CLI test runner required
for those runtime gates. They therefore remain pending rather than being inferred from source checks.

## Safety evidence

- [x] `CommandDispatcher` journals only exception type names; exception messages are not copied into user-visible activity history.
- [x] Dynamic command registration enforces core namespace protection (`wincare.core.*`, `system.*`).
- [x] A single application-scoped `ActivityJournalService` is shared by command execution and Activity UI.
- [x] Activity refreshes from that journal when its cached page is navigated to.
- [x] File and cleanup roots are canonicalized and reject reparse-point operation roots; recursive work skips reparse descendants and uses bounded iterative traversal.
- [x] Process execution uses argument lists rather than shell command construction for native tool calls.
- [x] All Rust FFI exports are wrapped in `catch_unwind` — panics cannot cross the FFI boundary.

## Design and accessibility source checks

- [x] All eight status-pill color pairs satisfy the repository's WCAG 2.1 AA contrast gate.
- [x] Required visual tokens exist in Light, Dark, and HighContrast dictionaries.
- [x] Primary WinUI navigation, table, command, and automation metadata pass structural validation.
- [x] Dynamic ViewModel status styling is resolved in the view layer rather than returning XAML brushes from ViewModels.
- [x] Parameter JSON editor has automation metadata and malformed/non-object JSON is blocked locally.

## Finalized native-source gate

Run from the repository root of the **finalized native source archive**:

```bash
python tools/verify_native_foundation.py
python -m unittest discover -s tests/native -v
cargo test --manifest-path native/Cargo.toml
python tests/tools/test_plugin_cli.py -v
python tools/verify_visual_tokens.py
python tools/verify_pill_contrast.py
```

A passing source gate means **statically validated & unit-tested**, not Windows behavior verified.

## Release-candidate finalization

```bash
python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version 2.5.0-rc1 \
  --mode rc
```

The finalization manifest records artifact paths, file counts, SHA-256 hashes, migration counts, and
production readiness. Native source contains zero `.ps1`, `.psm1`, and `.psd1` files. Historical
executable PowerShell remains isolated in the legacy-oracle archive.

## Automated CI release gate

The GitHub Actions workflow (`native-winui.yml`) incorporates an automated `release-gate` job. Upon successful completion of source verification, Rust compilation, C# test suites, and package builds on `master`, the `release-gate` job automatically:

1. Downloads built MSIX installers and portable ZIP distributions (`x64` / `ARM64`).
2. Collects native source finalization archives.
3. Publishes/updates the official release on GitHub Releases with attached executable packages.

## Rollback model

Source rollback is Git-based in the hosted repository. Runtime mutation rollback is command-specific and
must never be inferred from a generic success flag. Where a safe native compensation path does not exist,
the command reports no generic undo capability.
