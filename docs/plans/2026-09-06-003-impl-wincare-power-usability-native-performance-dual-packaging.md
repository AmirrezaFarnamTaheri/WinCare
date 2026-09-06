# WinCare Power Usability, Native Performance, and Portable Packaging Implementation Plan

> **For agentic workers:** Keep changes direct and testable. Do not reintroduce blanket review ceremony for routine Safe or Moderate commands.

**Goal:** Deliver a high-utility WinUI 3 maintenance cockpit with Rust-backed native telemetry/cleaning, bounded parallel diagnostics, and trimmed portable x64/ARM64 builds whose real executables are validated at runtime.

**Architecture:** WinUI 3 presentation calls the existing managed command plane and P/Invoke bridge. The Rust C-ABI implementation lives under `native/wincare-core`. Safe commands remain direct, Moderate commands use lightweight confirmation, and Destructive commands retain preview-plan approval.

**Tech Stack:**
- .NET 8 (`net8.0-windows10.0.19041.0`)
- Windows App SDK / WinUI 3
- CommunityToolkit.Mvvm
- Rust workspace under `native/`
- Python 3.13 in CI
- MSIX validation plus portable x64/ARM64 artifacts; Inno Setup consumes the x64 portable payload when built

---

## Global Constraints

- **Risk-specific admission:** Safe = one click; Moderate = lightweight confirmation; Destructive = successful preview + dispatcher-issued single-use `ApprovedMutationPlan`.
- **No child-process probes for the native snapshot/temp-clean primitives.**
- **Native cleaner boundary:** do not traverse reparse-point directories/junctions.
- **Canonical portable regression ceiling:** `artifacts/portable/<rid>/WinCare.App.exe <= 70,000,000 bytes` for both x64 and ARM64.
- **Trim safety requires runtime evidence:** publish success and suppressed linker warnings are insufficient.
- **Product truth:** no generic rollback, installer-size, health-percentage, or p95 latency claim without executable evidence.

---

## Task 1: Native Rust C-ABI Extensions (`native/wincare-core`)

**Files**
- `native/wincare-core/src/lib.rs`
- `native/wincare-core/src/telemetry.rs`
- `native/wincare-core/src/cleaner.rs`
- `native/wincare-core/tests/c_abi_tests.rs`

**Implemented exports**

```rust
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_sys_snapshot_all(
    out_snapshot: *mut NativeSysSnapshot,
) -> i32;

#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_clean_temp_files(
    dry_run: u8,
    out_result: *mut NativeCleanResult,
) -> i32;
```

- [x] Snapshot/cleaner C-ABI coverage exists.
- [x] Release builds target x64 and ARM64.
- [x] Cleaner skips reparse-point traversal and includes a Windows junction regression test.

Verification:

```text
cargo test --manifest-path native/Cargo.toml
cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings
```

---

## Task 2: Managed Native Repository

**Files**
- `src/WinCare.Domain/Telemetry/INativeSystemProbeRepository.cs`
- `src/WinCare.Infrastructure/Native/NativeMethods.cs`
- `src/WinCare.Infrastructure/Native/NativeSystemProbeRepository.cs`
- `tests/WinCare.Infrastructure.Tests/NativeSystemProbeRepositoryTests.cs`

- [x] Typed blittable P/Invoke bridge exists.
- [x] Infrastructure tests require the staged x64 `wincare_core.dll`; the test prerequisite is not silently skipped when the DLL is absent.
- [x] Home telemetry reports unavailable CPU as `N/A` rather than `0.0%` when the native probe fails.

---

## Task 3: Portable Packaging and Size Contract

**Files**
- `src/WinCare.App/Properties/PublishProfiles/portable-x64.pubxml`
- `src/WinCare.App/Properties/PublishProfiles/portable-ARM64.pubxml`
- `src/WinCare.App/WinCare.App.csproj`
- `tools/release_checklist.py`
- `.github/workflows/native-winui.yml`

The portable profiles use single-file publishing, compression, partial trimming, and no ReadyToRun image.

**Canonical artifacts**

```text
artifacts/portable/win-x64/WinCare.App.exe
artifacts/portable/win-arm64/WinCare.App.exe
```

**Single executable regression contract**

```text
WinCare.App.exe <= 70,000,000 bytes
```

The ceiling is grounded in exact-head CI measurements of 67,362,718 bytes for x64 and 66,071,465 bytes for ARM64. It protects against material growth while describing the real self-contained Windows App SDK payload.

Evaluator:

```text
python tools/release_checklist.py --portable-artifact artifacts/portable/<rid>/WinCare.App.exe
```

The Inno definition at `tools/installer/wincare_setup.iss` consumes `artifacts/portable/win-x64/*`. It is not a separate framework-dependent application and has no separate 4–6 MB product contract.

- [x] x64/ARM64 portable lock graphs are committed and selected directly through `NuGetLockFilePath` for single-file publishing.
- [x] CI enforces the same executable size ceiling for both RIDs.
- [x] CI uploads the staged portable executables/archives.

---

## Task 4: Trimmed Portable Runtime Smoke

**Files**
- `src/WinCare.App/App.xaml.cs`
- `.github/workflows/native-winui.yml`

The packaged app supports a CI-only `--smoke-test` launch path that still constructs the real WinUI window, then:

1. Initializes `AppRuntime` and plugin discovery.
2. Validates the bundled Rust C-ABI version.
3. Executes the read-only `system` command through `CommandDispatcher`.
4. Exits non-zero on failure.

CI executes that exact trimmed artifact on:

- x64: native `windows-latest` runner.
- ARM64: native `windows-11-vs2026-arm` runner.

Release promotion depends on both runtime smoke jobs.

This validates the shipped artifact rather than assuming `PublishTrimmed=true`, `x:Bind`, or warning suppression proves runtime compatibility. Dynamic COM/WUA/plugin paths outside this smoke still require focused Windows runtime tests and preservation roots/annotations when applicable.

---

## Task 5: Checkup and Home Correctness

**Files**
- `src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs`
- `src/WinCare.App/Views/Pages/CheckupPage.xaml`
- `src/WinCare.App/ViewModels/Pages/HomePageViewModel.cs`
- related application tests

- [x] Primary local probes use `ParallelCommandProbeRunner.RunPreviewsAsync`.
- [x] Windows Update is decoupled from the primary quick-check completion.
- [x] Async WUA results are ignored if a newer quick check supersedes them.
- [x] Incomplete probes cannot produce Healthy state.
- [x] Finding action text/commands use live OneWay bindings.
- [x] Free-space findings format one decimal place with `0.0`.
- [x] CPU telemetry exposes availability and renders `N/A` when unavailable.
- [x] Parallel-runner test proves real overlap with a deterministic peak-concurrency tracker rather than a fragile sub-second stopwatch threshold.

---

## Task 6: Risk-Tier Integrity

**Files**
- `src/WinCare.CommandCatalog/Models/CommandDefinition.cs`
- `src/WinCare.Application/Commands/CommandDispatcher.cs`
- `tests/WinCare.Application.Tests/RiskTierAdmissionTests.cs`

- [x] Explicit risk metadata cannot downgrade a higher derived risk tier.
- [x] Unknown enum values are blocked before a dynamic handler can execute.
- [x] Safe remains direct.
- [x] Moderate remains confirmation-only without mandatory preview.
- [x] Destructive retains preview-plan validation and replay protection.

---

## Task 7: Validation

Source contract:

```text
python tools/verify_native_foundation.py
python -m unittest discover -s tests/native -v
python tests/tools/test_plugin_cli.py -v
```

Windows managed/native/package validation:

```text
dotnet restore WinCare.Native.sln -p:Platform=x64
dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
cargo test --manifest-path native/Cargo.toml
```

Portable artifact validation:

```text
python tools/release_checklist.py --portable-artifact artifacts/portable/win-x64/WinCare.App.exe
python tools/release_checklist.py --portable-artifact artifacts/portable/win-arm64/WinCare.App.exe
```

Runtime validation is performed by the native-architecture `portable-runtime-smoke` CI matrix, not by source-only tests.
