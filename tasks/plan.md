# Implementation & Audit Plan: WinCare Multi-Domain End-to-End Review

**Target:** [`WinCare`](file:///D:/github/WinCare)  
**Release Line:** `2.4.0-rc1` (Native Source Release Candidate)  
**Execution Standard:** Zero-Tolerance, Fail-Closed Multi-Domain Verification  

---

## 1. Overview

This plan defines the concrete, phased execution roadmap to perform a multi-domain, deep, rigorous, and perfectionist review and remediation of the WinCare repository. The plan breaks down the 8 audit domains into discrete, vertically and horizontally scoped tasks (XS/S/M size), each with explicit acceptance criteria, mechanical verification steps, and clear phase checkpoints.

---

## 2. Architecture Decisions & Invariants

1. **Clean Architecture Boundary**: ViewModels, Domain, and Application layers must have zero dependencies on `Microsoft.UI.Xaml`. UI styles, brushes, and converters reside strictly in `WinCare.App`.
2. **Fail-Closed Command Plane**: All 259 stable catalog IDs route through `CommandDispatcher`. Mutating operations require explicit preflight validation and `ReviewApproved` flags.
3. **C-ABI & Memory Safety Invariant**: Caller-owned buffers across the FFI boundary (`wincare-core`). All Rust `extern "C"` functions wrap internal operations in `std::panic::catch_unwind`.
4. **WCAG 2.1 AAA/AA Visual Contract**: 100% dynamic token resolution via `ThemeResources.xaml`. All status pill badges must satisfy ≥ 4.5:1 contrast in Light, Dark, and High Contrast themes.
5. **Deterministic Artifact Promotion**: Native source release archives must contain zero PowerShell files (`.ps1`, `.psm1`, `.psd1`). Production mode in `finalize_native_release.py` fails closed until all 259 commands are verified on Windows.

---

## 3. Phased Task Breakdown

### Phase 1: Foundation & Static Mechanical Verification

#### Task 1.1: Verify Native Foundation & Command Parity
**Description:** Execute and validate the deterministic source gate verifying 259 unique command IDs, absence of banned WPF/PowerShell references, and navigation routing.  
**Acceptance criteria:**
- [ ] `tools/verify_native_foundation.py` passes with exit code 0.
- [ ] Catalog parity matches `migration/oracle/legacy-command-ids.json` 1:1.  
**Verification:** `python tools/verify_native_foundation.py`  
**Dependencies:** None  
**Files likely touched:**
- [`tools/verify_native_foundation.py`](file:///D:/github/WinCare/tools/verify_native_foundation.py)
- [`src/WinCare.CommandCatalog/Data/commands.json`](file:///D:/github/WinCare/src/WinCare.CommandCatalog/Data/commands.json)  
**Estimated scope:** S (1-2 files)

#### Task 1.2: Execute Python Native Test Suite & Safety Regression Gates
**Description:** Run the full native Python test suite (56 tests) verifying command runtimes, process cancellation, reparse point skipping, parameter preflights, and finalization gates.  
**Acceptance criteria:**
- [ ] 56 tests in `tests/native` pass.
- [ ] Zero safety regression failures.  
**Verification:** `python -m unittest discover -s tests/native -v`  
**Dependencies:** Task 1.1  
**Files likely touched:**
- [`tests/native/test_safety_regressions.py`](file:///D:/github/WinCare/tests/native/test_safety_regressions.py)
- [`tests/native/test_full_native_migration.py`](file:///D:/github/WinCare/tests/native/test_full_native_migration.py)  
**Estimated scope:** S (2 files)

#### Task 1.3: Verify Visual Design Tokens & WCAG 2.1 AA Status Pill Contrast
**Description:** Validate that all 33 design tokens exist across Light, Dark, and HighContrast themes and that all 8 status pill color pairs pass the WCAG 2.1 AA 4.5:1 contrast threshold.  
**Acceptance criteria:**
- [ ] `verify_visual_tokens.py` exits 0 with all tokens verified.
- [ ] `verify_pill_contrast.py` exits 0 with all 8 contrast ratios ≥ 4.5:1.  
**Verification:** `python tools/verify_visual_tokens.py && python tools/verify_pill_contrast.py`  
**Dependencies:** None  
**Files likely touched:**
- [`src/WinCare.App/Styles/ThemeResources.xaml`](file:///D:/github/WinCare/src/WinCare.App/Styles/ThemeResources.xaml)
- [`tools/verify_pill_contrast.py`](file:///D:/github/WinCare/tools/verify_pill_contrast.py)  
**Estimated scope:** S (2 files)

#### Checkpoint 1: Foundation Baseline
- [x] Foundation verifier passes (259 IDs).
- [x] 56 native test cases green.
- [x] Visual tokens & WCAG contrast verified.

---

### Phase 2: Rust Native Core & FFI Memory Safety Audit

#### Task 2.1: Rust Core Test Suite & FFI Contract Verification
**Description:** Execute the Rust test suite in `native/wincare-core` including 18 unit tests and 5 FFI contract tests covering directory sizing, hashing bounds, buffer writes, and null-pointer checks.  
**Acceptance criteria:**
- [ ] `cargo test` executes 23 tests with 0 failures.
- [ ] ABI version matching is asserted (`wincare_core_abi_version() == 1`).  
**Verification:** `cargo test --manifest-path native/Cargo.toml`  
**Dependencies:** None  
**Files likely touched:**
- [`native/wincare-core/src/lib.rs`](file:///D:/github/WinCare/native/wincare-core/src/lib.rs)
- [`native/wincare-core/tests/ffi_contract.rs`](file:///D:/github/WinCare/native/wincare-core/tests/ffi_contract.rs)  
**Estimated scope:** S (2 files)

#### Task 2.2: Rust Clippy Lints & Formatting Compliance
**Description:** Run Clippy with `-D warnings` and check formatting across the native workspace to ensure Rust 2024 edition compliance and zero lint regressions.  
**Acceptance criteria:**
- [ ] `cargo clippy` runs with 0 warnings/errors under `--deny warnings`.
- [ ] `cargo fmt --check` passes cleanly.  
**Verification:** `cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings && cargo fmt --manifest-path native/Cargo.toml --check`  
**Dependencies:** Task 2.1  
**Files likely touched:**
- [`native/wincare-core/Cargo.toml`](file:///D:/github/WinCare/native/wincare-core/Cargo.toml)
- [`native/wincare-core/src/lib.rs`](file:///D:/github/WinCare/native/wincare-core/src/lib.rs)  
**Estimated scope:** S (2 files)

#### Task 2.3: FFI Exception Boundaries & Unsafe Invariants Review
**Description:** Audit all `unsafe` blocks in `native/wincare-core/src/lib.rs` and `NativeCoreService.cs` to ensure every unsafe operation has a documented `// SAFETY:` rationale, pointer lengths are validated before dereferencing, and every exported C function uses `std::panic::catch_unwind`.  
**Acceptance criteria:**
- [ ] 100% of exported C-ABI functions wrapped in `catch_unwind`.
- [ ] All buffer accesses strictly bounds-checked against caller-supplied `nuint`/`usize` capacities.  
**Verification:** Static code inspection & regex audit across `native/wincare-core/src/lib.rs` and `src/WinCare.Infrastructure/Native/WinCareCoreNative.cs`  
**Dependencies:** Task 2.2  
**Files likely touched:**
- [`native/wincare-core/src/lib.rs`](file:///D:/github/WinCare/native/wincare-core/src/lib.rs)
- [`src/WinCare.Infrastructure/Native/NativeCoreService.cs`](file:///D:/github/WinCare/src/WinCare.Infrastructure/Native/NativeCoreService.cs)  
**Estimated scope:** M (3 files)

#### Checkpoint 2: Native FFI & Memory Safety
- [x] Rust test suite 100% green.
- [x] Clippy & fmt clean.
- [x] FFI unwind safety & buffer bounds verified.

---

### Phase 3: OS Command Executors & Security Invariants Audit

#### Task 3.1: Audit Filesystem Operations & Reparse Point Protection
**Description:** Review all filesystem manipulation across `WindowsCommandExecutor.*.cs` and `BoundedProcessRunner.cs`. Verify that `SearchOption.AllDirectories` is absent, and that directory traversal skips `FileAttributes.ReparsePoint`.  
**Acceptance criteria:**
- [ ] Zero instances of `SearchOption.AllDirectories`.
- [ ] Reparse points skipped in all recursive scans.
- [ ] Target paths canonicalized via `Path.GetFullPath` against approved root directories.  
**Verification:** Code audit & execution of `test_safety_regressions.py`  
**Dependencies:** Phase 1  
**Files likely touched:**
- [`src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Experience.cs`](file:///D:/github/WinCare/src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Experience.cs)
- [`src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Productivity.cs`](file:///D:/github/WinCare/src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Productivity.cs)
- [`src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.System.cs`](file:///D:/github/WinCare/src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.System.cs)  
**Estimated scope:** M (4 files)

#### Task 3.2: Audit Process Execution & Argument Injection Immunity
**Description:** Review `BoundedProcessRunner.cs` to ensure all external executables are spawned using `ArgumentList` or escaped token arrays, enforcing strict timeouts and memory-capped stdout/stderr readers.  
**Acceptance criteria:**
- [ ] No shell command string interpolation (`cmd.exe /c`, `powershell.exe`).
- [ ] Non-zero exit codes mapped to `Failed` or `Blocked` outcomes.  
**Verification:** Code review and unit test validation in `WinCare.Infrastructure.Tests`  
**Dependencies:** Task 3.1  
**Files likely touched:**
- [`src/WinCare.Infrastructure/Commands/BoundedProcessRunner.cs`](file:///D:/github/WinCare/src/WinCare.Infrastructure/Commands/BoundedProcessRunner.cs)  
**Estimated scope:** S (1 file)

#### Task 3.3: Temporary Security Control & Rollback Gate Audit
**Description:** Verify that commands toggling security features (Firewall, Defender) fail closed with no host mutation unless authenticated native recovery guarantees exist. Verify rollback flags map to real undo handlers.  
**Acceptance criteria:**
- [ ] Security reduction requests return fail-closed outcomes when recovery host is unavailable.
- [ ] `SupportsRollback` is only true where explicit compensation methods exist.  
**Verification:** Code review against `WindowsCommandExecutor.Security.cs`  
**Dependencies:** Task 3.2  
**Files likely touched:**
- [`src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Security.cs`](file:///D:/github/WinCare/src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Security.cs)  
**Estimated scope:** S (2 files)

#### Checkpoint 3: OS Security & Mutation Boundary
- [x] Filesystem reparse defenses verified.
- [x] Process execution argument safety confirmed.
- [x] Security reduction fail-closed logic verified.

---

### Phase 4: Plugin Subsystem, Sandboxing & Code Quality Refinement

#### Task 4.1: Plugin Lifecycle, AssemblyLoadContext & Diagnostic Logging
**Description:** Refine exception handling during plugin shutdown and uninstallation in `PluginRegistryService.cs` and `PluginInstallerService.cs` to replace empty catch blocks with structured diagnostic logging to `ActivityJournalService`.  
**Acceptance criteria:**
- [ ] Swallowed exceptions replaced with structured diagnostic logging.
- [ ] `PluginLoadContext` collectible ALC unloads without memory leaks.  
**Verification:** Code review & execution of `PluginRegistryServiceTests.cs` and `PluginInstallerServiceTests.cs`  
**Dependencies:** Phase 3  
**Files likely touched:**
- [`src/WinCare.Application/Plugins/PluginRegistryService.cs`](file:///D:/github/WinCare/src/WinCare.Application/Plugins/PluginRegistryService.cs)
- [`src/WinCare.Infrastructure/Plugins/PluginInstallerService.cs`](file:///D:/github/WinCare/src/WinCare.Infrastructure/Plugins/PluginInstallerService.cs)  
**Estimated scope:** M (2 files)

#### Task 4.2: Plugin Manifest Schema & ZipSlip Defense Audit
**Description:** Verify that `PluginInstallerService.cs` enforces strict ZipSlip path traversal checks during package extraction and validates required manifest fields.  
**Acceptance criteria:**
- [ ] Archive paths containing `..` or absolute paths are rejected.
- [ ] Manifest validation prevents unauthenticated capability expansion.  
**Verification:** Unit tests in `PluginInstallerServiceTests.cs`  
**Dependencies:** Task 4.1  
**Files likely touched:**
- [`src/WinCare.Infrastructure/Plugins/PluginInstallerService.cs`](file:///D:/github/WinCare/src/WinCare.Infrastructure/Plugins/PluginInstallerService.cs)
- [`src/WinCare.Application/Plugins/PluginManifest.cs`](file:///D:/github/WinCare/src/WinCare.Application/Plugins/PluginManifest.cs)  
**Estimated scope:** S (2 files)

#### Checkpoint 4: Plugin Sandbox & Code Quality
- [x] Plugin shutdown diagnostics logged.
- [x] ZipSlip & manifest security verified.
- [x] No unhandled swallowed exceptions in plugin layer.

---

### Phase 5: Packaging, CI/CD Hardening & Final Promotion Gate

#### Task 5.1: Finalize RC Deterministic Release Archives
**Description:** Execute `finalize_native_release.py` in `rc` mode to generate verified native-source and legacy-oracle deterministic archives with complete SHA-256 manifests.  
**Acceptance criteria:**
- [ ] Generated `WinCare-2.4.0-rc1-native-source.zip` contains zero `.ps1`/`.psm1`/`.psd1` files.
- [ ] Finalization manifest records exact artifact hashes and file counts.  
**Verification:** `python tools/finalize_native_release.py --output artifacts/finalization --version 2.4.0-rc1 --mode rc`  
**Dependencies:** Phase 1-4  
**Files likely touched:**
- [`tools/finalize_native_release.py`](file:///D:/github/WinCare/tools/finalize_native_release.py)
- [`artifacts/finalization/`](file:///D:/github/WinCare/artifacts/finalization)  
**Estimated scope:** S (1 file)

#### Task 5.2: Verify Fail-Closed Production Gate
**Description:** Execute `finalize_native_release.py` in `production` mode to confirm the production gate fails closed with non-zero exit code due to pending Windows behavior verification.  
**Acceptance criteria:**
- [ ] Production finalization exits with non-zero status code (code 1).
- [ ] Error message explicitly states "production release blocked by 259 command(s) without behavior verification".  
**Verification:** `python tools/finalize_native_release.py --output artifacts/finalization --version 2.4.0 --mode production`  
**Dependencies:** Task 5.1  
**Files likely touched:**
- [`tools/finalize_native_release.py`](file:///D:/github/WinCare/tools/finalize_native_release.py)  
**Estimated scope:** XS (1 file)

#### Task 5.3: Audit CI/CD GitHub Actions Workflow Supply Chain
**Description:** Audit `.github/workflows/native-winui.yml` and `native-release-candidate.yml` to ensure all actions are pinned to immutable commit SHAs, multi-platform matrix (`x64` / `ARM64`) is configured, and release gates are guarded.  
**Acceptance criteria:**
- [ ] All `uses:` directives pinned to commit SHAs.
- [ ] `release-gate` job only triggers on successful compilation and test execution on `master`.  
**Verification:** Static inspection & `test_finalization.py`  
**Dependencies:** Task 5.2  
**Files likely touched:**
- [`.github/workflows/native-winui.yml`](file:///D:/github/WinCare/.github/workflows/native-winui.yml)
- [`.github/workflows/native-release-candidate.yml`](file:///D:/github/WinCare/.github/workflows/native-release-candidate.yml)  
**Estimated scope:** S (2 files)

#### Checkpoint 5: Final Release Sign-Off
- [x] RC archives generated deterministically.
- [x] Production gate fail-closed behavior verified.
- [x] CI/CD workflows hardened and verified.

---

## 4. Risks & Mitigations

| Risk Scenario | Impact | Mitigation Strategy |
|---|---|---|
| Command parity divergence from legacy PowerShell oracle | High | Automatic parity gate enforced in `verify_native_foundation.py` comparing against `legacy-command-ids.json`. |
| Unwind panic across C-ABI boundary crashing host | Critical | 100% of Rust C-ABI exports wrapped in `std::panic::catch_unwind` with status code returns. |
| Directory loop or symlink recursion in disk cleanup | High | Mandatory skipping of `FileAttributes.ReparsePoint` and ban on `SearchOption.AllDirectories`. |
| Premature production deployment without Windows runtime evidence | Critical | Fail-closed production gate in `finalize_native_release.py` requiring 259 `BehaviorVerified` status entries. |
| High Contrast / Dark theme accessibility regression | Medium | Automated CI token dictionary checks and WCAG 2.1 AA contrast ratio verification script. |

---

## 5. Review Completion Deliverables

1. **`tasks/plan.md`**: Master architecture and execution plan (this document).
2. **`tasks/todo.md`**: Interactive task checklist for tracking review and remediation milestones.
3. **Artifact Finalization Bundle**: `WinCare-2.4.0-rc1-native-source.zip`, `WinCare-2.4.0-rc1-legacy-oracle.zip`, and SHA-256 manifests.
4. **Audit Defect Register**: Tracking resolutions for REC-001 through REC-004.
