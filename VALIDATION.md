# Native validation and promotion

WinCare uses separate source, Windows runtime, package, and command-parity gates.
One evidence class cannot substitute for another.

## Evidence terms

- **Verified:** behavior executed successfully in the named environment.
- **Statically validated:** source or artifact structure checked without executing Windows behavior.
- **Reviewed:** implementation or evidence inspected against its contract.
- **Pending:** required evidence was unavailable.
- **Blocked:** a declared prerequisite or gate prevented execution.
- **Failed:** the check ran and did not satisfy its contract.

## Pre-launch checklist (2.4.0-rc1)

### Code quality
- [x] All Python verification gates pass (`verify_visual_tokens.py`, `verify_pill_contrast.py`, `validate_gui.py`, `test_gui.py`)
- [x] All Rust unit and FFI contract tests pass (`cargo test` — 18/18)
- [x] Rust fmt and clippy clean (`cargo fmt --check`, `cargo clippy -- -D warnings`)
- [ ] .NET build succeeds with 0 warnings (requires SDK 8.0.416 in CI — not installable locally)
- [ ] .NET managed unit tests pass (`dotnet test WinCare.Native.sln`)
- [x] No phantom checkmarks in `tasks/todo.md` (all items verified against source)
- [x] No `console.log`/`Debug.WriteLine` in production journal paths (only `System.Diagnostics.Debug` in non-journal code)
- [x] Error handling covers all expected dispatcher failure modes (NotFound, NotMigrated, ReadOnlyMutation, ReviewRequired, Timeout, Cancel, Fault)

### Security
- [x] No secrets in code or version control
- [x] `DiskCleanupCommandHandler` enforces `AllowedBasePaths` safelist (path traversal prevented)
- [x] `CommandDispatcher` journal never logs `ex.Message` (PII / file-path leakage prevented)
- [x] No static mutable global state (`CommandRuntime.LastJournal` removed)
- [x] All Rust FFI exports wrapped in `catch_unwind` (UB on panic eliminated)
- [x] `// SAFETY:` comments on all `unsafe` blocks in `lib.rs`
- [x] `AllowedBasePaths` uses `Path.GetFullPath` normalization (Unicode bypass / relative path attacks prevented)

### Design and accessibility
- [x] All 8 pill color pairs pass WCAG 2.1 AA (≥ 4.5:1 contrast ratio)
- [x] All 33 theme tokens defined in Light, Dark, and HighContrast dictionaries
- [x] HighContrast `PillNotReadyBgBrush` uses `SystemColorHighlightColor` pairing (no same-color trap)
- [x] 3px `FocusVisualPrimaryThickness` enforced on `ListViewItem` styles
- [x] `Ctrl+F` keyboard accelerator implemented and bound in XAML
- [x] Column header `ToolTipService.ToolTip` on all 6 headers
- [x] `FavoriteToggleButton.IsChecked` bound to `ViewModel.IsSelectedToolFavorite`
- [x] `TableHeader` bound to `ViewModel.IsCompactLayout` (INPC-compliant, not a dead proxy)
- [x] 920 DIP compact breakpoint consistent across both pages

### Performance and reliability
- [x] `DebounceSearch` uses real `Task.Delay(250, token)` — no synchronous full rebuild on keystrokes
- [x] `CancellationTokenSource` properly disposed on superseded searches
- [x] `SelectedTool` preserved across filter/refresh cycles in `AllToolsPageViewModel.Refresh()`
- [x] `async void DebounceSearch` has outer `catch (Exception)` guard to prevent unobserved faults

### Infrastructure and CI
- [x] `ci.yml` — full evidence + Windows matrix (x64 + ARM64) — SHA-pinned actions
- [x] `native-winui.yml` — Rust fmt/clippy/test + .NET build + portable ZIP
- [x] `native-python-gates.yml` — visual/GUI verification, native source contract, safety regressions, Rust FFI gates, and managed build/test
- [x] `native-release-candidate.yml` — manual RC and production finalization dispatch
- [x] `dependabot.yml` — weekly Action SHA-digest update proposals
- [x] `release.yml` — sealed, attested, two-stage GitHub release with immutable byte verification
- [x] `release-dispatch.yml` — immutable source-SHA pinning before hardened release dispatch

### Documentation
- [x] `CHANGELOG.md` updated with complete 2.4.0-rc1 feature set
- [x] `README.md` updated: handler count, Cyber-Teal table, safety model, security invariants, CI table
- [x] `DESIGN.md` color values aligned with actual `ThemeResources.xaml` implementation
- [x] `tasks/todo.md` verified: all checked items confirmed against source
- [x] `tasks/plan.md` — all quality gates marked complete

---

## Linux-capable source gate

Run from the repository root:

```text
python tools/verify_native_foundation.py
python -m unittest discover -s tests/native -v
python tools/verify_visual_tokens.py
python tools/verify_pill_contrast.py
python tools/validate_gui.py
python -m unittest tools.test_gui -v
```

These gates verify:

- Exact frozen-oracle parity for 259 command IDs
- Native project separation from PowerShell and WPF
- Approved navigation, tabs, table structure, and accessibility metadata
- XML and JSON validity
- Rust ABI source contract
- Deterministic ZIP metadata and sorted entries
- Native-source and legacy-oracle separation
- Release metadata
- Fail-closed production readiness accounting
- All visual token definitions across 3 theme dictionaries
- WCAG 2.1 AA contrast on all pill color pairs
- GUI structure: ThemeResource references, named controls, binding counts

A passing source gate means **statically validated**, not Windows verified.

## Release-candidate finalization

```text
python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version 2.4.0-rc1 \
  --mode rc
```

The finalization manifest records artifact paths, file counts, SHA-256 hashes, command migration
counts, and production readiness. The native source archive must contain zero `.ps1`, `.psm1`,
and `.psd1` files. The legacy oracle archive must remain separate.

## Production gate

```text
python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version 2.4.0 \
  --mode production
```

Production mode requires:

- Exactly 259 unique command IDs
- All 259 commands marked `BehaviorVerified`
- Zero production behavior-verification blockers
- Successful deterministic staging

The current project intentionally fails this gate with 259 behavior-verification blockers.

## Windows gate

The Windows matrix must run on the exact candidate source:

```text
cargo fmt --manifest-path native/Cargo.toml --check
cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings
cargo test --manifest-path native/Cargo.toml
dotnet restore WinCare.Native.sln -p:Platform=x64
dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
dotnet build src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64
```

Required runtime evidence includes:

- WinUI launch through WinApp CLI
- Keyboard navigation and command-palette behavior
- UI Automation flows for every primary page
- Screenshots in light, dark, and Windows Contrast themes
- Cancellation, timeout, and fail-closed result states
- x64 and ARM64 Rust DLL loading
- MSIX installation, launch, repair, upgrade, and uninstall
- Portable launch and data-location behavior
- Startup markers and representative-device timing
- Command-by-command comparison with the frozen historical oracle

## Package promotion

A production release requires all source and Windows gates, signed and timestamped packages,
coherent checksums and provenance, and exact-byte promotion of the artifacts that passed
validation. Rebuilding or re-zipping after verification creates a new candidate and requires
the full gate again.

## Rollback plan

| Trigger | Action |
|---|---|
| Error rate 2x baseline | Revert to previous branch HEAD via `git revert` + push |
| Critical security finding | Close PR immediately; patch in isolated branch |
| .NET build regression | Revert last commit with `git revert HEAD~1` |
| Rust FFI panic in CI | Use `git revert <commit>` for the commit that changed `Cargo.lock` or the FFI source |
| Design token regression | Run `verify_visual_tokens.py`; revert `ThemeResources.xaml` |

All rollbacks are reversible via Git history. No database migrations are involved.
