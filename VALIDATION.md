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

## Current 2.4.0-rc1 evidence

### Source and migration checks available in this workspace

- [x] `python tools/verify_native_foundation.py`
- [x] `python -m unittest discover -s tests/native -v` — 55/55 passed in the full migration workspace
- [x] `python tools/verify_visual_tokens.py`
- [x] `python tools/verify_pill_contrast.py`
- [x] `python tools/validate_gui.py` — full migration workspace compatibility check
- [x] `python -m unittest tools.test_gui -v` — 9/9 passed in the full migration workspace
- [x] Python verifier/test sources compile with `python -m compileall`
- [x] Native source scan contains no executable PowerShell/WPF runtime dependency
- [x] All 259 catalog IDs have an explicit executor route
- [x] All 104 mutating IDs have explicit parameter preflight before approval
- [x] No bulk generated handler directory or known fabricated-success payloads remain
- [x] Native recursive file operations reject or skip reparse points and avoid `SearchOption.AllDirectories`
- [x] Native source does not advertise generic undo where no recovery action is wired
- [x] ViewModels contain no `Microsoft.UI.Xaml` dependency
- [x] External GitHub Actions used by native workflows are pinned to immutable commit SHAs
- [x] No obvious source-code secrets were found by the repository scan used for this candidate

### Windows-only checks not executed in this environment

- [ ] Rust fmt, Clippy, tests, and release DLL build — **Pending**
- [ ] .NET restore, managed tests, and WinUI build — **Pending**
- [ ] x64 and ARM64 package builds — **Pending**
- [ ] WinUI launch with WinApp CLI — **Pending**
- [ ] UI Automation, keyboard flows, theme screenshots, and runtime accessibility — **Pending**
- [ ] MSIX install/launch/repair/upgrade/uninstall — **Pending**
- [ ] Portable package launch/data-location behavior — **Pending**
- [ ] Command-by-command Windows comparison with historical oracle — **Pending**

The current execution environment does not provide the Windows/.NET/Rust/WinApp toolchain required
for those gates. They therefore remain pending rather than being inferred from source checks.

## Safety evidence

- [x] `CommandDispatcher` journals only exception type names; exception messages are not copied into
  user-visible activity history.
- [x] A single application-scoped `ActivityJournalService` is shared by command execution and Activity UI.
- [x] Activity refreshes from that journal when its cached page is navigated to.
- [x] File and cleanup roots are canonicalized and reject reparse-point operation roots; recursive work
  skips reparse descendants and uses bounded iterative traversal.
- [x] Process execution uses argument lists rather than shell command construction for native tool calls.
- [x] Temporary security-control reduction performs **no host mutation** until a native recovery host can
  prove authenticated automatic restoration. Unsupported reduction/restore requests fail closed.
- [x] Windows Update result handling does not promote aborted, failed, or succeeded-with-errors states to success.
- [x] Studio ADB and Xbox FSE tool execution requires a reviewed SHA-256 before invoking the supplied binary.
- [x] Rust FFI source retains `catch_unwind` boundaries and `// SAFETY:` invariants around unsafe blocks.

## Design and accessibility source checks

- [x] All eight status-pill color pairs satisfy the repository's WCAG 2.1 AA contrast gate.
- [x] Required visual tokens exist in Light, Dark, and HighContrast dictionaries.
- [x] Primary WinUI navigation, table, command, and automation metadata pass structural validation.
- [x] Dynamic ViewModel status styling is resolved in the view layer rather than returning XAML brushes from ViewModels.
- [x] Parameter JSON editor has automation metadata and malformed/non-object JSON is blocked locally.

These are source-level checks. Runtime accessibility still requires the Windows UI Automation gate.

## Finalized native-source gate

Run from the repository root of the **finalized native source archive**:

```text
python tools/verify_native_foundation.py
python -m unittest discover -s tests/native -v
python tools/verify_visual_tokens.py
python tools/verify_pill_contrast.py
```

The finalized archive intentionally omits executable legacy PowerShell. Tests that specifically require
that executable oracle report an explicit skip; they are not silently treated as passed. The full migration
workspace can additionally run:

```text
python tools/validate_gui.py
python -m unittest tools.test_gui -v
```

A passing source gate means **statically validated**, not Windows behavior verified.

## Release-candidate finalization

```text
python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version 2.4.0-rc1 \
  --mode rc
```

The finalization manifest records artifact paths, file counts, SHA-256 hashes, migration counts, and
production readiness. Native source must contain zero `.ps1`, `.psm1`, and `.psd1` files. Historical
executable PowerShell remains isolated in the legacy-oracle archive.

## Production gate

```text
python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version 2.4.0 \
  --mode production
```

Production mode requires:

- Exactly 259 unique stable command IDs
- All 259 commands marked `BehaviorVerified`
- Zero production behavior-verification blockers
- Successful deterministic staging

Current candidate intentionally fails production promotion with 259 behavior-verification blockers.

## Windows gate

Run against the exact candidate source on supported Windows runners:

```text
cargo fmt --manifest-path native/Cargo.toml --check
cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings
cargo test --manifest-path native/Cargo.toml
dotnet restore WinCare.Native.sln -p:Platform=x64
dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
dotnet build src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64
```

Repeat the supported build/package path for ARM64. Required runtime evidence includes WinUI launch,
UI Automation, keyboard navigation, Light/Dark/Contrast screenshots, cancellation/timeout/fail-closed
states, Rust DLL loading, MSIX lifecycle, portable launch, startup timing, and command-by-command oracle
comparison.

## Promotion rule

Promote only the exact bytes that passed every required gate. Rebuilding or re-zipping after verification
creates a new candidate and requires the affected gates again.

## Rollback model

Source rollback is Git-based in the hosted repository. Runtime mutation rollback is command-specific and
must never be inferred from a generic success flag. Where a safe native compensation path does not exist,
the command reports no generic undo capability. Temporary security reduction remains unavailable until its
recovery guarantee can be proven independently.
