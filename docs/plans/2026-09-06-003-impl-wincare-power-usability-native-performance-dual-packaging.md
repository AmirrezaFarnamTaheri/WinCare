# WinCare Power Usability, Native Performance, and Dual Packaging Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. All tasks must be executed sequentially in the primary agent context (zero subagents per project rules). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform WinCare into a high-utility, high-performance Windows optimization cockpit featuring 1-click everyday maintenance with deep power-user telemetry, powered by a native Rust C-ABI kernel engine (`wincare-core`) and dual packaging profiles (~20–24 MB portable single-file executable and < 6 MB framework installer).

**Architecture:** A lightweight WinUI 3 presentation shell connects via direct, zero-allocation P/Invoke to `wincare_core.dll` (Rust 2024 C-ABI). All heavy diagnostic probes, file system cleaning, and telemetry queries execute in native Rust using direct Win32/NT kernel APIs without child process spawning. Progressive disclosure in XAML elevates everyday users into confident power users through inspectable system diffs and microsecond telemetry.

**Tech Stack:** 
- .NET 8 (`net8.0-windows10.0.19041.0`)
- Windows App SDK 2.3.1 (WinUI 3 / XAML)
- CommunityToolkit.Mvvm 8.4.2
- Rust Workspace (Edition 2024, toolchain 1.97.1, MSRV 1.85, `opt-level = 3`, `lto = "fat"`, `strip = true`)
- Inno Setup 6 / Python 3.11 for automated packaging

---

## Global Constraints

- **Strict Tri-Tier Safety Admission:** `RiskTier.Safe` (1-click), `RiskTier.Moderate` (single confirmation), `RiskTier.Destructive` (single-use `ApprovedMutationPlan` token with mandatory pre-mutation snapshot).
- **Zero Child Process Spawning:** Diagnostic probes and Safe cleaners must NEVER spawn `powershell.exe`, `cmd.exe`, or `wmic.exe`.
- **Zero Uncaught Native Panics:** Every `extern "C"` Rust export must be wrapped in `std::panic::catch_unwind` returning negative error codes.
- **Zero Heap Allocations in Telemetry Loop:** Telemetry data passes across FFI via unmanaged stack-allocated or pinned blittable structs.
- **Strict Size Ceilings:** Portable single-file `WinCare.App.exe` $\le 24\text{ MB}$; Framework installer $\le 6\text{ MB}$.
- **WCAG 2.2 AA Contrast Compliance:** All UI states must maintain $\ge 4.5:1$ contrast ratio for text and pills against their immediate background.
- **Test Suite Pass Floor:** 100% pass on all existing 172 managed xUnit tests and 90 native tests.

---

### Task 1: Native Rust Core C-ABI Extensions (`wincare-core`)

**Files:**
- Modify: `native/wincare-core/src/lib.rs`
- Create: `native/wincare-core/src/telemetry.rs`
- Create: `native/wincare-core/src/cleaner.rs`
- Test: `native/wincare-core/tests/c_abi_tests.rs`

**Interfaces:**
- Consumes: Native Win32/NT APIs (`GetSystemTimes`, `GlobalMemoryStatusEx`, `GetDiskFreeSpaceExW`).
- Produces:
  ```rust
  #[repr(C)]
  pub struct NativeSysSnapshot {
      pub cpu_usage_pct: f32,
      pub ram_used_bytes: u64,
      pub ram_total_bytes: u64,
      pub disk_free_bytes: u64,
      pub disk_total_bytes: u64,
      pub net_active: u8,
  }

  #[repr(C)]
  pub struct NativeCleanResult {
      pub bytes_reclaimed: u64,
      pub files_removed: u32,
      pub error_code: i32,
  }

  #[unsafe(no_mangle)]
  pub unsafe extern "C" fn wincare_sys_snapshot_all(out_snapshot: *mut NativeSysSnapshot) -> i32;

  #[unsafe(no_mangle)]
  pub unsafe extern "C" fn wincare_clean_temp_files(dry_run: u8, out_result: *mut NativeCleanResult) -> i32;
  ```

- [x] **Step 1: Write the failing integration test**
Create `native/wincare-core/tests/c_abi_tests.rs`:
```rust
use std::mem::MaybeUninit;
use wincare_core::{wincare_sys_snapshot_all, wincare_clean_temp_files, NativeSysSnapshot, NativeCleanResult};

#[test]
fn test_sys_snapshot_c_abi() {
    let mut snapshot = MaybeUninit::<NativeSysSnapshot>::uninit();
    // SAFETY: snapshot pointer is valid and properly aligned
    let status = unsafe { wincare_sys_snapshot_all(snapshot.as_mut_ptr()) };
    assert_eq!(status, 0, "wincare_sys_snapshot_all must return 0 on success");
    let snapshot = unsafe { snapshot.assume_init() };
    assert!(snapshot.ram_total_bytes > 0);
}

#[test]
fn test_clean_temp_dry_run_c_abi() {
    let mut result = MaybeUninit::<NativeCleanResult>::uninit();
    // SAFETY: result pointer is valid and properly aligned
    let status = unsafe { wincare_clean_temp_files(1, result.as_mut_ptr()) };
    assert_eq!(status, 0, "wincare_clean_temp_files dry-run must return 0 on success");
    let result = unsafe { result.assume_init() };
    assert_eq!(result.error_code, 0);
}
```

- [x] **Step 2: Run test to verify it fails**
Run: `cargo test --manifest-path native/wincare-core/Cargo.toml --test c_abi_tests`
Expected: FAIL with unresolved imports `wincare_sys_snapshot_all`.

- [x] **Step 3: Implement `telemetry.rs` and `cleaner.rs` with safe C-ABI exports**
Implement telemetry and cleaner logic in `native/wincare-core/src/` with `// SAFETY:` proofs, `std::panic::catch_unwind`, and `#repr(C)` structs. Export from `native/wincare-core/src/lib.rs`.

- [x] **Step 4: Run test to verify it passes**
Run: `cargo test --manifest-path native/wincare-core/Cargo.toml --test c_abi_tests`
Expected: PASS with 2 passed tests.

- [x] **Step 5: Build release dynamic library and stage to WinCare.App**
Run: `cargo build --manifest-path native/Cargo.toml --release --target x86_64-pc-windows-msvc`
Stage `wincare_core.dll` into `src/WinCare.App/Native/x64/wincare_core.dll`.

---

### Task 2: Managed C# Infrastructure P/Invoke Bridge & Repository (`WinCare.Infrastructure`)

**Files:**
- Create: `src/WinCare.Domain/Telemetry/INativeSystemProbeRepository.cs`
- Create: `src/WinCare.Infrastructure/Native/NativeMethods.cs`
- Create: `src/WinCare.Infrastructure/Native/NativeSystemProbeRepository.cs`
- Test: `tests/WinCare.Infrastructure.Tests/NativeSystemProbeRepositoryTests.cs`

**Interfaces:**
- Consumes: C-ABI exports in `wincare_core.dll`.
- Produces:
  ```csharp
  public interface INativeSystemProbeRepository
  {
      ValueTask<SystemSnapshot> GetSystemSnapshotAsync(CancellationToken ct = default);
      ValueTask<CleanExecutionResult> CleanTempFilesAsync(bool dryRun, CancellationToken ct = default);
  }
  ```

- [x] **Step 1: Write failing unit test**
Create `tests/WinCare.Infrastructure.Tests/NativeSystemProbeRepositoryTests.cs` asserting `GetSystemSnapshotAsync` retrieves non-zero RAM and CPU statistics via the native repository without throwing.

- [x] **Step 2: Run test to verify failure**
Run: `dotnet test tests/WinCare.Infrastructure.Tests/WinCare.Infrastructure.Tests.csproj --filter FullyQualifiedName~NativeSystemProbeRepositoryTests`
Expected: FAIL (types do not exist).

- [x] **Step 3: Implement `NativeMethods.cs` and `NativeSystemProbeRepository.cs`**
Implement P/Invoke with explicit `[DllImport("wincare_core.dll", CallingConvention = CallingConvention.Cdecl)]`, blittable struct marshaling, and error code translation.

- [x] **Step 4: Run test to verify pass**
Run: `dotnet test tests/WinCare.Infrastructure.Tests/WinCare.Infrastructure.Tests.csproj --filter FullyQualifiedName~NativeSystemProbeRepositoryTests`
Expected: PASS.

---

### Task 3: Dual Packaging Profiles & ReadyToRun Trimming Optimization

**Files:**
- Modify: `src/WinCare.App/Properties/PublishProfiles/portable-x64.pubxml`
- Modify: `src/WinCare.App/Properties/PublishProfiles/portable-ARM64.pubxml`
- Create: `src/WinCare.App/Properties/PublishProfiles/framework-x64.pubxml`
- Modify: `src/WinCare.App/WinCare.App.csproj`
- Test: `tools/finalize_native_release.py`

**Interfaces:**
- Consumes: MSBuild single-file bundling pipeline.
- Produces:
  - `artifacts/portable/win-x64/WinCare.App.exe` ($\le 24\text{ MB}$)
  - `artifacts/framework/win-x64/WinCare.App.exe` ($\le 6\text{ MB}$)

- [x] **Step 1: Configure ReadyToRun removal and PRI pruning**
In `portable-x64.pubxml` and `portable-ARM64.pubxml`:
```xml
<PublishReadyToRun>false</PublishReadyToRun>
```
In `WinCare.App.csproj`, ensure `PruneUnusedHeavyweightProjections` strips unneeded localized `.mui` files and satellite resources.

- [x] **Step 2: Create `framework-x64.pubxml`**
```xml
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <Platform>x64</Platform>
    <Configuration>Release</Configuration>
    <PublishProtocol>FileSystem</PublishProtocol>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishDir>..\..\artifacts\framework\win-x64\</PublishDir>
    <SelfContained>false</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <WindowsPackageType>None</WindowsPackageType>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <DebugType>none</DebugType>
  </PropertyGroup>
</Project>
```

- [x] **Step 3: Run publish commands and verify binary size**
Run portable publish: `dotnet publish src/WinCare.App/WinCare.App.csproj /p:PublishProfile=portable-x64`
Run framework publish: `dotnet publish src/WinCare.App/WinCare.App.csproj /p:PublishProfile=framework-x64`
Verify:
- Portable executable $\le 24\text{ MB}$.
- Framework executable $\le 6\text{ MB}$.

---

### Task 4: High-End Progressive Disclosure UI (Double-Bezel & Telemetry Bay)

**Files:**
- Modify: `src/WinCare.App/Styles/ControlStyles.xaml`
- Modify: `src/WinCare.App/Views/Pages/HomePage.xaml`
- Modify: `src/WinCare.App/ViewModels/Pages/HomePageViewModel.cs`
- Test: `tests/WinCare.Application.Tests/HomePageViewModelTests.cs`
- Token Check: `tools/verify_visual_tokens.py` & `tools/verify_pill_contrast.py`

**Interfaces:**
- Consumes: `INativeSystemProbeRepository`, `CommandDispatcher`.
- Produces:
  - 1-Click Action Card with Button-in-Button trailing icon.
  - Expandable Double-Bezel Telemetry Bay with `Cascadia Code` microsecond latency and path diffs.

- [x] **Step 1: Write ViewModel unit tests for Progressive Disclosure**
Add test to `tests/WinCare.Application.Tests/HomePageViewModelTests.cs` verifying `ToggleInspectorCommand` toggles `IsInspectorExpanded` and populates `TelemetryMetrics` with microsecond probe latency and targeted paths.

- [x] **Step 2: Run test to verify failure**
Run: `dotnet test tests/WinCare.Application.Tests/WinCare.Application.Tests.csproj --filter FullyQualifiedName~HomePageViewModelTests`
Expected: FAIL (`IsInspectorExpanded` not found).

- [x] **Step 3: Implement Double-Bezel and Telemetry Bay XAML**
Update `HomePage.xaml` to wrap the 3 action cards in the Double-Bezel nested architecture:
Outer Shell (`CornerRadius="14"`, `Padding="3"`, `CardBorderBrush`) + Inner Core (`CornerRadius="10"`, `CardSurfaceBrush`). Embed the Button-in-Button CTA and the collapsible dark carbon Telemetry Bay.

- [x] **Step 4: Verify visual tokens and contrast compliance**
Run: `python tools/verify_visual_tokens.py`
Run: `python tools/verify_pill_contrast.py`
Expected: ALL PASS with WCAG 2.2 AA contrast $\ge 4.5:1$.

- [x] **Step 5: Run ViewModel tests to verify pass**
Run: `dotnet test tests/WinCare.Application.Tests/WinCare.Application.Tests.csproj --filter FullyQualifiedName~HomePageViewModelTests`
Expected: PASS.

---

### Task 5: End-to-End Release Validation & Evaluator Gate

**Files:**
- Test: `WinCare.Native.sln`
- Script: `tools/release_checklist.py`

- [x] **Step 1: Run full managed test suite**
Run: `dotnet test WinCare.Native.sln --no-build`
Expected: 172/172 PASS.

- [x] **Step 2: Run native test suite**
Run: `pytest tests/native`
Expected: ALL PASS.

- [x] **Step 3: Execute Release Checklist Evaluator Gate**
Run: `python tools/release_checklist.py`
Expected: ALL 7 AUDIT CHECKS PASS.
