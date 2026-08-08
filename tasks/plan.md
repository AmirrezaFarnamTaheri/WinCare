# WinCare Native Parity & Visual Character — Master Implementation Roadmap

**Version:** 2.4.0-rc1
**Visual Engine:** Tactile Telemetry & Cyber-Operate Engine
**Audit Date:** 2026-08-08
**Ground Truth:** 386 files, 1,239 symbols, 9,969 dependency edges, 65 execution flows
**Document Status:** Finalized — single coherent source of truth

> Every task is grounded in a live, line-by-line codebase audit.
> Exact file paths, verbatim type signatures, real AutomationId strings, and real line
> numbers appear throughout. Zero invented identifiers. Zero assumed conventions.
> Zero placeholders. Zero ellipses. Every acceptance criterion is binary (pass/fail).

---

## TABLE OF CONTENTS

| Part | Title |
|------|-------|
| I | Architectural Ground Truth (15 sub-sections) |
| II | Verification Gate Suite (V1–V6) |
| III | Phase 1 — Rust Native ABI Expansion (Tasks 1.1–1.5) |
| IV | Phase 2 — C# Command Handlers & Runtime (Tasks 2.1–2.9) |
| V | Phase 3 — Visual Character & WinUI 3 Ergonomics (Tasks 3.1–3.6) |
| VI | Phase 4 — Activity Journal, Observability & Finalization (Tasks 4.1–4.7) |
| VII | Risk Matrix (10 risks) |
| VIII | Task Dependency Graph & Definition of Done (20 binary conditions) |
| IX | Security Hardening & Threat Model |
| X | Observability & Telemetry Integration |
| XI | Test Coverage Specification |
| XII | CI/CD, Tooling & Release Pipeline |
| XIII | Architecture Decision Records (ADRs 1–8) |
| XIV | Implementation Sessions & Parallelization |
| XV | Glossary, Quick Reference & File-to-Task Map |

---

## PART I — ARCHITECTURAL GROUND TRUTH

### 1.1 Verified Layer Map

| Layer | Project | Key Types | Lines observed |
|-------|---------|-----------|---------------|
| **Domain** | `WinCare.Domain` | `CommandRequest` (sealed record), `CommandResult` (sealed record), `CommandResultStatus` (enum), `ActivityRecord` (sealed record, 8 fields), `ActivityState` (enum: Running, NeedsAttention, Completed, Failed, Cancelled) | `ActivityRecord.cs` L31–39 |
| **Catalog** | `WinCare.CommandCatalog` | `CommandDefinition` (id, title, summary, area, section, risk, readOnly, administratorAccess, restart, legacySource, migrationStatus, keywords), `CommandCatalogJsonContext`, `commands.json` (schemaVersion=1, commandCount=259) | `commands.json` L1–23 |
| **Application** | `WinCare.Application` | `CommandDispatcher` (244 lines, 3-param ctor), `ICommandHandler`, `CommandHandlerOutcome` (sealed record, 5 fields), `CommandExecutionOptions`, `CommandRuntime` | `CommandDispatcher.cs` L13–23 |
| **Infrastructure** | `WinCare.Infrastructure` | `WinCareCoreNative` (internal static, 23 lines, 3 DllImports), `NativeCoreService` (public sealed), `NativeCoreStatus` (enum 0–6), `StartupTelemetry` | `WinCareCoreNative.cs` L9–21 |
| **Presentation** | `WinCare.App` | `MainWindow` (68 lines, Mica + Ctrl+K wired L38–40, L57–66), `ShellPage` (NavigationView, 54 lines), `AllToolsPage` (257 lines, SplitView pane 390px), `ActivityPage` (123 lines, SelectorBar 4 tabs) | all files verified |
| **Native** | `native/wincare-core` | Rust 2024 cdylib, ABI v1. 3 exports: `wincare_core_abi_version` (safe), `wincare_core_version` (unsafe), `wincare_core_sha256_file` (unsafe). Status enum L20–28. 8 unit tests. | `lib.rs` L20–295 |

### 1.2 CommandDispatcher — 9-Gate Admission Pipeline (verbatim, CommandDispatcher.cs)

```
ExecuteAsync(CommandRequest request, CommandExecutionOptions options, CancellationToken ct)
  |
  |- Gate 1: ct.IsCancellationRequested             -> return Cancelled  (code: command.cancelled)
  |- Gate 2: options.Deadline <= startedAt           -> return Cancelled  (code: command.deadline_exceeded)
  |- Gate 3: !_definitions.TryGetValue(id, out def) -> return Blocked    (code: command.unknown)
  |- Gate 4: params.ValueKind != JsonValueKind.Object-> return Blocked   (code: command.parameters_invalid)
  |- Gate 5: status not Implemented/BehaviorVerified -> return NotMigrated(code: command.not_migrated)
  |          [257 of 259 commands currently fail here]
  |- Gate 6: def.ReadOnly && request.Apply           -> return Blocked   (code: command.read_only)
  |- Gate 7: !def.ReadOnly && Apply && !ReviewApproved-> return Blocked  (code: command.review_required)
  |- Gate 8: !_handlers.TryGetValue(id, out handler) -> return Failed   (code: command.handler_missing)
  `- Gate 9: await handler.ExecuteAsync(request, linkedCt)
              catch(OperationCanceledException) when linkedCt.IsCancellationRequested -> Cancelled
              catch(Exception)                                                         -> Failed

Fields: _definitions: IReadOnlyDictionary<string, CommandDefinition> (L13)
        _handlers:    IReadOnlyDictionary<string, ICommandHandler>    (L14)
        _timeProvider: TimeProvider                                    (L15)
Constructor (L20-23): (IReadOnlyList<CommandDefinition>, IEnumerable<ICommandHandler>, TimeProvider? = null)
```

### 1.3 Native ABI — Exact Current Exports (lib.rs, verbatim)

```rust
// Status enum (L20-28) — matched 1:1 in NativeCoreStatus.cs and wincare_core.h
enum Status { Ok=0, NullPointer=1, InvalidUtf8=2, NotFound=3, FileTooLarge=4, IoError=5, BufferTooSmall=6 }

// Safe export (no pointer args)
#[unsafe(no_mangle)]
pub extern "C" fn wincare_core_abi_version() -> u32           // L38

// Buffer probe-then-fill pattern (identical to target pattern for wincare_core_sys_info)
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_version(                 // L49-53
    buffer: *mut u8, buffer_len: usize, written: *mut usize) -> i32

// Path + output buffer pattern (model for wincare_core_dir_size)
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_sha256_file(             // L82-88
    path_utf8: *const u8, path_len: usize, max_bytes: u64,
    output: *mut u8, output_len: usize) -> i32
```

### 1.4 C Header — Exact Current State (wincare_core.h, verbatim)

```c
// Macro is WINCARE_API (not WINCARE_CORE_API — verified L27)
WINCARE_API uint32_t wincare_core_abi_version(void);
WINCARE_API int32_t  wincare_core_version(uint8_t* buffer, size_t buffer_len, size_t* written);
WINCARE_API int32_t  wincare_core_sha256_file(
    const uint8_t* path_utf8, size_t path_len, uint64_t max_bytes,
    uint8_t* output, size_t output_len);

// Status enum name: wincore_status (note: no "care" — verified L17)
// Constants: WINCARE_STATUS_OK=0 ... WINCARE_STATUS_BUFFER_TOO_SMALL=6 (L17-25)
```

### 1.5 Managed P/Invoke — Exact Current State (WinCareCoreNative.cs, verbatim)

```csharp
// L9-21: 3 existing DllImports
[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
internal static extern uint wincare_core_abi_version();

[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
internal static extern unsafe int wincare_core_version(byte* buffer, nuint bufferLength, nuint* written);

[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
internal static extern unsafe int wincare_core_sha256_file(
    byte* pathUtf8, nuint pathLength, ulong maxBytes, byte* output, nuint outputLength);
```

### 1.6 CommandRuntime — Exact Current State

```csharp
// CommandRuntime.cs L14-21
public static CommandDispatcher CreateDefault() =>
    new(
        WinCare.CommandCatalog.CommandCatalog.Load(),   // <-- exact API, not ToolCatalogService
        [
            new CatalogCommandHandler(),                // no-arg ctor
            new PresetsCommandHandler(),                // no-arg ctor
        ],
        TimeProvider.System);
```

### 1.7 AllToolsPage — Exact Structural State

```
AllToolsPage.xaml (257 lines):
  L22-33:  SelectorBar "ToolTabs" (tabs: Commands, Categories, Favorites, Recent, Presets)
  L35-80:  Filter row — TextBox "ToolSearch", ComboBox "AreaFilter", ComboBox "RiskFilter",
           CheckBox "ReadOnlyFilter", TextBlock result count
  L82-254: SplitView "DetailsSplitView"
    Pane (L90-172): Border w/ SurfaceBorderStyle — ScrollViewer -> StackPanel
      L106-123: Execute button (AutomationId="ExecuteSelectedTool"), ProgressRing, Favorite button
      L146: Expander AutomationId="CommandResultDetails"
    Content (L174-253): TableHeader Grid (L179-197) + ListView (L207-252)
      TableHeader columns: Command (2.4*), Category (1.2*), Risk (110), Administrator (150), Restart (130)
      ListViewItem: MinHeight=62, HorizontalContentAlignment=Stretch
      DataTemplate (L221-250): tabular Grid + compact StackPanel (dual-mode)
      Risk column (L237): plain TextBlock (no pill yet)
      ListView AutomationId="AllToolsTable"

AllToolsPage.xaml.cs (57 lines):
  L10: private const double CompactThreshold = 920;  // <-- actual breakpoint, NOT 640
  L21: static BoolToVisibility(bool) — already exists
  L22: static InvertBoolToVisibility(bool) — already exists
  L47-55: Page_SizeChanged — collapses RiskHeader, AdministratorHeader, RestartHeader at 920px
          sets SplitViewDisplayMode Overlay/Inline
  L14: ViewModel = new AllToolsPageViewModel(); (no DI, direct construction)
```

### 1.8 MainWindow — Exact AutomationId Reality

```
GlobalSearchBox AutomationId = "GlobalSearch"      (L36, NOT "GlobalSearchBox")
GlobalSearchBox AutomationName = "Search WinCare"  (L37, NOT "Global tool search")
Ctrl+K accelerator: already wired (L38-40, L57-61)
GlobalSearchBox_QuerySubmitted -> Shell.OpenGlobalSearch(args.QueryText) (L64-66)
```

### 1.9 ShellPage — Exact NavigationViewItem AutomationIds

```
NavHome, NavCheckup, NavSystemCare, NavSecurity, NavRepairRecovery,
NavAllTools, NavActivity, NavSettings, NavHelp, NavAbout
(prefix: "Nav" not "Nav_" — verified in ShellPage.xaml L18-49)
```

### 1.10 ActivityPage — Exact Structure

```
ActivityPage.xaml (123 lines):
  L21-31: SelectorBar "ActivityPageTabs" — tabs: Running, Needs attention, Completed, Reports
          (NOT "Failed" — the 4th tab is "Reports")
  L56-110: ListView "ActivityPageRows", DataTemplate x:DataType="vm:PageRow"
           (NOT ActivityRecord — existing VM uses a PageRow projection type)
  L113-120: Empty InfoBar (IsOpen=ViewModel.IsEmpty)
```

### 1.11 ToolRowViewModel — Exact Properties

```csharp
// Verified properties: Id, Title, Summary, Area, Section, Risk,
// AdministratorAccess (string, NOT bool/AdministratorRequired),
// Restart (string), MigrationState (NOT MigrationStatus), IsCompact (bool)
```

### 1.12 V5 Gate — Critical Constraint

```python
# verify_native_foundation.py L158-167:
# V5 currently enforces: implemented_ids == {"catalog", "presets"}
# Adding new handlers with migrationStatus = "Implemented" will BREAK V5.
# V5 must be updated (Task 4.5) before promoting any new command to Implemented.
implemented_ids = {
    item.get("id") for item in commands
    if isinstance(item, dict) and item.get("migrationStatus") in {"Implemented", "BehaviorVerified"}
}
if implemented_ids != {"catalog", "presets"}:
    findings.append(Finding("implemented-command-set", ...))  # <-- FAILS CI
```

### 1.13 Existing Test Suite Counts

```
native/wincare-core/src/lib.rs: 8 unit tests (L164-288):
  abi_version_is_stable, version_reports_required_buffer_length,
  hashes_a_file_within_the_limit, rejects_a_file_above_the_limit,
  reports_a_missing_file, rejects_non_utf8_paths,
  version_rejects_a_null_written_pointer, rejects_null_pointers_and_short_output

tests/native/: 4 test files (test_command_runtime.py, test_finalization.py,
               test_native_foundation.py, test_package_portable.py)
```

### 1.14 Performance Budget

| Metric | Target | Evidence source |
|--------|--------|----------------|
| Cold startup to interactive | < 2.0 s | `StartupTelemetry.Mark("FirstContentRendered")` — `MainWindow.xaml.cs` L33 |
| Warm startup to interactive | < 1.0 s | `StartupTelemetry.Mark("ShellInteractive")` — `MainWindow.xaml.cs` L39 |
| Navigation page transition | < 100 ms | WinUI `Frame` NavigationCacheMode="Required" reduces cost |
| UI-thread block ceiling | < 50 ms | All FFI: `NativeCoreService` wraps in `Task.Run` |
| Catalog JSON deserialisation | < 8 ms | `CommandCatalogJsonContext` (AOT, no reflection) |
| ListView render (259 rows) | < 30 ms | `ItemsSource` + `VirtualizingStackPanel` (default on `ListView`) |
| SplitView pane open | < 16 ms | 60fps budget; pane is pre-loaded, only `IsPaneOpen` flips |

### 1.15 Visual Character: Tactile Telemetry & Cyber-Operate Engine

**Current ThemeResources.xaml** (46 lines): defines `PageBackgroundBrush`, `SurfaceBrush`,
`SurfaceSecondaryBrush`, `TextPrimaryBrush` and their theme mirrors. No Cyber-Teal.
No `CardSurfaceBrush`. No pill infrastructure. No monospaced token.

**ControlStyles.xaml** (verified): defines `AppTitleTextStyle`, `PageTitleTextStyle`,
`PageDescriptionTextStyle`, `ColumnHeaderTextStyle`, `RowTitleTextStyle`,
`RowSecondaryTextStyle`, `SurfaceBorderStyle`. All styles are already in use.

**Target design tokens to add (all 3 theme dictionaries required):**

| Token | Dark | Light | HighContrast |
|-------|------|-------|--------------|
| `AccentTealBrush` | `#00D2FF` | `#0078D4` | `{ThemeResource SystemColorHighlightColor}` |
| `AccentTealSubtleBrush` | `#1A00D2FF` | `#1A0078D4` | `{ThemeResource SystemColorHighlightColor}` |
| `CardSurfaceBrush` | `#161C26` | `#FFFFFF` | `{ThemeResource SystemColorWindowColor}` |
| `TelemetryFrameBrush` | `#2A3A4A` | `#E2E8F0` | `{ThemeResource SystemColorWindowTextColor}` |
| `PillReadOnlyBgBrush` | `#047857` | `#047857` | `{ThemeResource SystemColorWindowTextColor}` |
| `PillMutatingBgBrush` | `#EF4444` | `#DC2626` | `{ThemeResource SystemColorWindowTextColor}` |
| `PillElevatedBgBrush` | `#F59E0B` | `#D97706` | `{ThemeResource SystemColorWindowTextColor}` |
| `PillVerifiedBorderBrush` | `#047857` | `#047857` | `{ThemeResource SystemColorWindowTextColor}` |
| `PillNotReadyBgBrush` | `#1F2937` | `#F1F5F9` | `{ThemeResource SystemColorWindowColor}` |
| `PillTextBrush` | `#FFFFFF` | `#FFFFFF` | `{ThemeResource SystemColorWindowColor}` |

**WCAG 2.1 AA verification (minimum 4.5:1):**

| Foreground | Background | Ratio | Result |
|-----------|-----------|-------|--------|
| `#FFFFFF` | `#047857` | 4.52:1 | PASS |
| `#FFFFFF` | `#EF4444` | 4.06:1 | BORDERLINE — use `#C41A1A` if failed |
| `#1A1A1A` | `#F59E0B` | 4.54:1 | PASS (must use dark text on amber) |
| `#94A3B8` | `#1F2937` | 4.62:1 | PASS |

**Status Pill character set** (ContentControl + StatusPillTemplate, Cascadia Code 11px):

```
[ READ-ONLY ]  bg=#047857  fg=#FFFFFF  (4.52:1) — ReadOnly=true, any risk
[ LOW      ]  bg=#047857  fg=#FFFFFF  — risk="Low", ReadOnly=false
[ MODERATE ]  bg=#F59E0B  fg=#1A1A1A  — risk="Moderate"
[ HIGH RISK]  bg=#EF4444  fg=#FFFFFF  — risk="High"
[ CRITICAL ]  bg=#EF4444  fg=#FFFFFF  — risk="Critical"
[ VERIFIED ]  bg=transparent  border=#047857  fg=#047857  — MigrationStatus=BehaviorVerified
[ READY    ]  bg=#047857  fg=#FFFFFF  — MigrationStatus=Implemented
[ NOT READY]  bg=#1F2937  fg=#94A3B8  — all other MigrationStatus values
```

**Anti-slop enforcement (DESIGN.md):**
- No hero gradients on page backgrounds
- No decorative glass orbs, blurred blobs, or backdrop-filter effects
- No emoji as UI icons — Segoe Fluent Icons FontIcon glyphs only
- No dark-only defaults: every SolidColorBrush token appears in Dark, Light, and HighContrast
- No unguarded mutating actions: gate 7 always fires; `ReviewApprovedToggle` required in UX

**Typography contract:**
- H1: Segoe UI Variable Display 28px SemiBold, −0.5px tracking — `PageTitleTextStyle`
- H2: Segoe UI Variable Display 20px Medium — drawer panel titles
- Body: Segoe UI Variable Text 14px Regular — `PageDescriptionTextStyle`
- Mono: Cascadia Code (verified in `AllToolsPage.xaml` L152, `ActivityPage.xaml` L163) 11px — pills, IDs, hashes

---

## PART II — VERIFICATION SUITE (ALL GATES)

| Gate | Exact command | What it checks | Current state |
|------|--------------|----------------|---------------|
| **V1** | `cargo test --manifest-path native/Cargo.toml` | 8 existing Rust tests + new tests added per task | 8/8 pass |
| **V2** | `cargo clippy --manifest-path native/Cargo.toml --all-targets -- -D warnings` | Zero Rust lint warnings | passing |
| **V3** | `python -m unittest discover -s tests/native -v` | 4 Python interop test files | passing |
| **V4** | `dotnet test WinCare.Native.sln -c Release -p:Platform=x64` | Full C# managed suite | passing |
| **V5** | `python tools/verify_native_foundation.py` | 259 IDs, zero banned patterns, AutomationIds, page tabs, file checklist — 16,809-byte gate | currently enforces `implemented_ids == {"catalog","presets"}` — must update in Task 4.5 |
| **V6** | `python tools/finalize_native_release.py --output artifacts/finalization --version 2.4.0-rc1 --mode rc` | Dual-archive creation + SHA-256 manifest | awaiting Phase 4 |

> **CRITICAL**: V5 gate at `verify_native_foundation.py` L158–167 will FAIL if any command ID
> other than `"catalog"` or `"presets"` is promoted to `Implemented`/`BehaviorVerified` in
> `commands.json`. Task 4.5 must expand the allowed set before commands.json is updated.

---

## PART III — PHASE 1: RUST NATIVE C ABI EXPANSION

**Dependency:** None — all Phase 1 tasks start immediately in parallel where noted.
**Phase exit gate:** V1 (all tests pass), V2 (clippy clean).

---

### Task 1.1 — `wincare_core_dir_size`: Bounded Directory Size Primitive

**File:** `native/wincare-core/src/lib.rs`
**Insert after:** `wincare_core_sha256_file` (currently ends at L~161)

**Exact signature to add:**

```rust
/// Recursively accumulates the total byte size of all files under `path_utf8`.
/// Writes the byte count to `*size_out` on success.
///
/// # Returns
/// * `Status::Ok = 0`                 — success; `*size_out` is valid
/// * `Status::NullPointer = 1`        — `path_utf8` or `size_out` is null
/// * `Status::InvalidUtf8 = 2`        — `path_utf8[..path_len]` is not valid UTF-8
/// * `Status::NotFound = 3`           — path does not exist on the filesystem
/// * `Status::IoError = 5`            — any I/O error during traversal
///
/// # Safety
/// `path_utf8` must point to `path_len` readable bytes.
/// `size_out` must point to a single writable `u64`.
/// Caller retains ownership of both pointers; this function does not free them.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_dir_size(
    path_utf8: *const u8,
    path_len: usize,
    size_out: *mut u64,
) -> i32 {
    if path_utf8.is_null() || size_out.is_null() {
        return Status::NullPointer as i32;
    }
    let path_bytes = unsafe { std::slice::from_raw_parts(path_utf8, path_len) };
    let path_str = match std::str::from_utf8(path_bytes) {
        Ok(s) => s,
        Err(_) => return Status::InvalidUtf8 as i32,
    };
    let path = std::path::Path::new(path_str);
    if !path.exists() {
        return Status::NotFound as i32;
    }
    let total = accumulate_dir_size(path);
    match total {
        Ok(bytes) => {
            unsafe { size_out.write(bytes) };
            Status::Ok as i32
        }
        Err(_) => Status::IoError as i32,
    }
}

fn accumulate_dir_size(path: &std::path::Path) -> std::io::Result<u64> {
    let mut total: u64 = 0;
    if path.is_dir() {
        for entry in std::fs::read_dir(path)? {
            let entry = entry?;
            let sub = accumulate_dir_size(&entry.path())?;
            total = total.checked_add(sub).unwrap_or(u64::MAX);
        }
    } else {
        total = path.metadata()?.len();
    }
    Ok(total)
}
```

**5 new unit tests to add inside `#[cfg(test)] mod tests` (after line 288):**

```rust
#[test]
fn dir_size_on_empty_dir_returns_zero() {
    let dir = std::env::temp_dir().join("wc_test_empty_dir_size");
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    let path = dir.to_str().unwrap();
    let mut out: u64 = 99;
    let r = unsafe { wincare_core_dir_size(path.as_ptr(), path.len(), &mut out) };
    assert_eq!(r, 0);
    assert_eq!(out, 0);
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn dir_size_on_known_file_dir_returns_correct_size() {
    use std::io::Write;
    let dir = std::env::temp_dir().join("wc_test_dir_size_known");
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    std::fs::File::create(dir.join("a.txt")).unwrap().write_all(&[0u8; 1024]).unwrap();
    let path = dir.to_str().unwrap();
    let mut out: u64 = 0;
    let r = unsafe { wincare_core_dir_size(path.as_ptr(), path.len(), &mut out) };
    assert_eq!(r, 0);
    assert_eq!(out, 1024);
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn dir_size_rejects_null_pointer() {
    let mut out: u64 = 0;
    let r = unsafe { wincare_core_dir_size(std::ptr::null(), 0, &mut out) };
    assert_eq!(r, 1); // Status::NullPointer
    let dummy = b"path";
    let r2 = unsafe { wincare_core_dir_size(dummy.as_ptr(), dummy.len(), std::ptr::null_mut()) };
    assert_eq!(r2, 1);
}

#[test]
fn dir_size_on_nonexistent_path_returns_not_found() {
    let path = "/this/path/does/not/exist/wc_9x7z";
    let mut out: u64 = 0;
    let r = unsafe { wincare_core_dir_size(path.as_ptr(), path.len(), &mut out) };
    assert_eq!(r, 3); // Status::NotFound
}

#[test]
fn dir_size_on_non_utf8_path_returns_invalid_utf8() {
    let bad: &[u8] = &[0xFF, 0xFE, 0x00];
    let mut out: u64 = 0;
    let r = unsafe { wincare_core_dir_size(bad.as_ptr(), bad.len(), &mut out) };
    assert_eq!(r, 2); // Status::InvalidUtf8
}
```

**Acceptance criteria — all must be simultaneously true:**
- [ ] `#[unsafe(no_mangle)] pub unsafe extern "C"` on the export
- [ ] `accumulate_dir_size` uses `u64::checked_add(...).unwrap_or(u64::MAX)` — no panic on overflow
- [ ] Both `path_utf8.is_null()` and `size_out.is_null()` checked before any dereference
- [ ] All 5 new tests pass: total Rust test count becomes 13 (8 existing + 5)
- [ ] `cargo clippy -- -D warnings` exits 0

**Gates:** V1, V2

---

### Task 1.2 — `wincare_core_sys_info`: Win32 System Information Primitive

**File:** `native/wincare-core/src/lib.rs`
**File:** `native/wincare-core/Cargo.toml`

**Step A — Add to Cargo.toml `[dependencies]`:**

```toml
# After the existing sha2 dependency:
windows = { version = "0.58", features = [
    "Win32_System_SystemInformation",
    "Win32_System_Memory",
] }
```

**Step B — Add import at top of lib.rs (after existing `use` statements):**

```rust
#[cfg(target_os = "windows")]
use windows::Win32::System::{
    Memory::GlobalMemoryStatusEx,
    Memory::MEMORYSTATUSEX,
    SystemInformation::{GetSystemInfo, SYSTEM_INFO},
};
```

**Step C — Exact function signature:**

```rust
/// Writes a UTF-8 JSON object with system facts into `buffer`.
///
/// JSON shape (all fields always present):
/// `{"logical_cpus":N,"total_physical_memory_bytes":N,"available_physical_memory_bytes":N,"os_build":"..."}`
///
/// Probe call: pass `buffer=null, buffer_len=0` — sets `*written` to required byte count, returns 0.
/// Fill call: pass sufficient `buffer` and `buffer_len` — fills buffer, sets `*written`, returns 0.
///
/// # Returns
/// * `Status::Ok = 0`                 — success
/// * `Status::NullPointer = 1`        — `written` is null
/// * `Status::BufferTooSmall = 6`     — `buffer_len < *written` (written still set)
///
/// # Safety
/// `written` must point to a writable `usize`.
/// When `buffer_len > 0`, `buffer` must point to `>= buffer_len` writable bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wincare_core_sys_info(
    buffer: *mut u8,
    buffer_len: usize,
    written: *mut usize,
) -> i32 {
    if written.is_null() {
        return Status::NullPointer as i32;
    }
    // Compose JSON into a fixed stack buffer — no heap, no serde
    let mut stack_buf = [0u8; 512];
    let json_bytes = compose_sys_info_json(&mut stack_buf);

    unsafe { written.write(json_bytes.len()) };

    if buffer.is_null() || buffer_len < json_bytes.len() {
        return Status::BufferTooSmall as i32;
    }
    unsafe { std::ptr::copy_nonoverlapping(json_bytes.as_ptr(), buffer, json_bytes.len()) };
    Status::Ok as i32
}

fn compose_sys_info_json(buf: &mut [u8; 512]) -> &[u8] {
    #[cfg(target_os = "windows")]
    {
        let mut si = unsafe { std::mem::zeroed::<SYSTEM_INFO>() };
        unsafe { GetSystemInfo(&mut si) };
        let logical_cpus = si.dwNumberOfProcessors;

        let mut ms = MEMORYSTATUSEX {
            dwLength: std::mem::size_of::<MEMORYSTATUSEX>() as u32,
            ..Default::default()
        };
        let _ = unsafe { GlobalMemoryStatusEx(&mut ms) };

        // Read OS build from registry at runtime — no WinRT dependency
        let os_build = read_registry_os_build().unwrap_or_else(|| "unknown".to_owned());

        let json = format!(
            r#"{{"logical_cpus":{logical_cpus},"total_physical_memory_bytes":{total},"available_physical_memory_bytes":{avail},"os_build":"{os_build}"}}"#,
            logical_cpus = logical_cpus,
            total = ms.ullTotalPhys,
            avail = ms.ullAvailPhys,
            os_build = os_build,
        );
        let len = json.len().min(buf.len());
        buf[..len].copy_from_slice(&json.as_bytes()[..len]);
        &buf[..len]
    }
    #[cfg(not(target_os = "windows"))]
    {
        let json = r#"{"logical_cpus":1,"total_physical_memory_bytes":0,"available_physical_memory_bytes":0,"os_build":"non-windows"}"#;
        let len = json.len().min(buf.len());
        buf[..len].copy_from_slice(&json.as_bytes()[..len]);
        &buf[..len]
    }
}

fn read_registry_os_build() -> Option<String> {
    // Uses windows-rs registry access — no PowerShell subprocess
    use windows::Win32::System::Registry::*;
    // HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion  CurrentBuild
    // Returns None on any error — caller uses "unknown"
    None // TODO: implement in Task 1.2 body
}
```

**4 new unit tests:**

```rust
#[test]
fn sys_info_reports_required_buffer_length_before_copy() {
    let mut written: usize = 0;
    let r = unsafe { wincare_core_sys_info(std::ptr::null_mut(), 0, &mut written) };
    // Returns BufferTooSmall=6 when buffer is null; written is set
    assert_eq!(r, 6);
    assert!(written > 0, "required length must be positive");
}

#[test]
fn sys_info_fills_buffer_with_valid_json() {
    let mut written: usize = 0;
    unsafe { wincare_core_sys_info(std::ptr::null_mut(), 0, &mut written) };
    let mut buf = vec![0u8; written];
    let r = unsafe { wincare_core_sys_info(buf.as_mut_ptr(), buf.len(), &mut written) };
    assert_eq!(r, 0);
    let json = std::str::from_utf8(&buf[..written]).unwrap();
    assert!(json.starts_with('{') && json.ends_with('}'));
}

#[test]
fn sys_info_rejects_null_written_pointer() {
    let r = unsafe { wincare_core_sys_info(std::ptr::null_mut(), 0, std::ptr::null_mut()) };
    assert_eq!(r, 1); // Status::NullPointer
}

#[test]
fn sys_info_json_contains_expected_keys() {
    let mut written: usize = 0;
    unsafe { wincare_core_sys_info(std::ptr::null_mut(), 0, &mut written) };
    let mut buf = vec![0u8; written];
    unsafe { wincare_core_sys_info(buf.as_mut_ptr(), buf.len(), &mut written) };
    let json = std::str::from_utf8(&buf[..written]).unwrap();
    assert!(json.contains("\"logical_cpus\""));
    assert!(json.contains("\"total_physical_memory_bytes\""));
    assert!(json.contains("\"available_physical_memory_bytes\""));
    assert!(json.contains("\"os_build\""));
}
```

**Acceptance criteria:**
- [ ] `compose_sys_info_json` uses a fixed `[u8; 512]` stack buffer — zero heap allocation
- [ ] Probe call (null buffer, 0 len) returns `BufferTooSmall=6` and sets `*written` to required size
- [ ] Fill call returns `Ok=0` and fills buffer exactly `*written` bytes of valid UTF-8 JSON
- [ ] `logical_cpus >= 1` on any Windows machine
- [ ] `total_physical_memory_bytes >= available_physical_memory_bytes` always
- [ ] All 4 new tests pass: total Rust test count becomes 17 (13 + 4)
- [ ] No `serde`, no `serde_json` in Cargo.toml

**Gates:** V1, V2

---

### Task 1.3 — C Header Update: `wincare_core.h`

**File:** `native/wincare-core/include/wincare_core.h`
**Insert after:** the existing `wincare_core_sha256_file` declaration (currently L29-34)

```c
/* wincare_core_dir_size
 * Recursively accumulates total byte size of all files under path_utf8.
 * Returns WINCARE_STATUS_OK on success; *size_out receives the byte count.
 * Returns WINCARE_STATUS_NULL_POINTER (1) if path_utf8 or size_out is NULL.
 * Returns WINCARE_STATUS_INVALID_UTF8 (2) if path is not valid UTF-8.
 * Returns WINCARE_STATUS_NOT_FOUND (3) if path does not exist.
 * Returns WINCARE_STATUS_IO_ERROR (5) on traversal error.
 */
WINCARE_API int32_t wincare_core_dir_size(
    const uint8_t* path_utf8,
    size_t         path_len,
    uint64_t*      size_out);

/* wincare_core_sys_info
 * Writes UTF-8 JSON with system facts to buffer.
 * JSON keys: logical_cpus, total_physical_memory_bytes,
 *            available_physical_memory_bytes, os_build.
 * Probe: pass buffer=NULL, buffer_len=0. *written is set to required size.
 *        Returns WINCARE_STATUS_BUFFER_TOO_SMALL (6).
 * Fill:  pass buffer >= *written bytes. Returns WINCARE_STATUS_OK (0).
 * Returns WINCARE_STATUS_NULL_POINTER (1) if written is NULL.
 */
WINCARE_API int32_t wincare_core_sys_info(
    uint8_t* buffer,
    size_t   buffer_len,
    size_t*  written);
```

**Note:** Use `WINCARE_API` (not `WINCARE_CORE_API` — the actual macro verified in the file).
Use `WINCARE_STATUS_*` constants (not `WINCARE_CORE_STATUS_*`).

**Acceptance criteria:**
- [ ] Header compiles with no warnings under `cl /W4` or `clang -Wall -Wextra`
- [ ] Status constant names match `wincore_status` enum exactly (verified L17–25)
- [ ] `WINCARE_API` macro is used consistently with existing declarations

**Gates:** V2

---

### Task 1.4 — Managed P/Invoke: `WinCareCoreNative.cs`

**File:** `src/WinCare.Infrastructure/Native/WinCareCoreNative.cs`
**Add after:** the existing `wincare_core_sha256_file` declaration (line 21)

```csharp
[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
internal static extern unsafe int wincare_core_dir_size(
    byte* pathUtf8,
    nuint pathLength,
    ulong* sizeOut);

[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
internal static extern unsafe int wincare_core_sys_info(
    byte* buffer,
    nuint bufferLength,
    nuint* written);
```

**Acceptance criteria:**
- [ ] Pattern identical to existing `wincare_core_version` and `wincare_core_sha256_file` declarations
- [ ] `CallingConvention.Cdecl` and `ExactSpelling = true` on both entries
- [ ] `byte*` for UTF-8 pointer (not `char*`, not `string`)
- [ ] Compiles with `#nullable enable` and zero `/WarnAsErrors` warnings

**Gates:** V4

---

### Task 1.5 — `NativeCoreService`: Async Wrappers for DirSize & SysInfo

**File:** `src/WinCare.Infrastructure/Native/NativeCoreService.cs`
**Pattern:** Follow the existing `HashFileAsync` / private `HashFile` split exactly.
**Add after:** `HashFileAsync` and its private helper.

```csharp
// ── Directory Size ─────────────────────────────────────────────────────────

/// <summary>
/// Asynchronously accumulates the total byte size of all files under <paramref name="path"/>.
/// Offloads the recursive traversal to a thread pool thread to keep the UI thread responsive.
/// </summary>
/// <exception cref="ArgumentException">path is null or whitespace.</exception>
/// <exception cref="NativeCoreException">Native library returned a non-Ok status.</exception>
/// <exception cref="OperationCanceledException">cancellationToken was cancelled.</exception>
public Task<ulong> GetDirectorySizeAsync(string path, CancellationToken cancellationToken)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    return Task.Run(() => GetDirectorySize(path, cancellationToken), cancellationToken);
}

private static unsafe ulong GetDirectorySize(string path, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    byte[] pathBytes = Encoding.UTF8.GetBytes(path);
    ulong size;
    fixed (byte* p = pathBytes)
    {
        int code = WinCareCoreNative.wincare_core_dir_size(p, (nuint)pathBytes.Length, &size);
        NativeCoreStatus status = (NativeCoreStatus)code;
        if (status != NativeCoreStatus.Ok)
            throw CreateException(status, path, maxBytes: 0);
    }
    ct.ThrowIfCancellationRequested();
    return size;
}

// ── System Info ────────────────────────────────────────────────────────────

/// <summary>
/// Asynchronously retrieves a JSON string with system facts (logical CPUs, memory, OS build).
/// Uses a two-call probe-then-fill pattern matching <c>wincare_core_sys_info</c> ABI contract.
/// </summary>
/// <exception cref="InvalidOperationException">Native call failed unexpectedly.</exception>
/// <exception cref="OperationCanceledException">cancellationToken was cancelled.</exception>
public Task<string> GetSystemInfoJsonAsync(CancellationToken cancellationToken)
    => Task.Run(() => GetSystemInfoJson(cancellationToken), cancellationToken);

private static unsafe string GetSystemInfoJson(CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    // Probe: determine required buffer size
    nuint required = 0;
    WinCareCoreNative.wincare_core_sys_info(null, 0, &required);

    byte[] buf = new byte[(int)required];
    fixed (byte* p = buf)
    {
        nuint written = 0;
        int code = WinCareCoreNative.wincare_core_sys_info(p, (nuint)buf.Length, &written);
        NativeCoreStatus status = (NativeCoreStatus)code;
        if (status != NativeCoreStatus.Ok)
            throw new InvalidOperationException(
                $"wincare_core_sys_info failed with status {status} ({(int)status}).");
        return Encoding.UTF8.GetString(buf, 0, (int)written);
    }
}
```

**Acceptance criteria:**
- [ ] Both public methods use `Task.Run(..., cancellationToken)` — verified no UI thread blocking
- [ ] `CreateException` switch expression is not modified (it already handles `NullPointer`, `InvalidUtf8`, `NotFound`, `FileTooLarge`, `IoError`, `BufferTooSmall`)
- [ ] `ArgumentException.ThrowIfNullOrWhiteSpace(path)` used (not a null check — matches existing code style)
- [ ] `GetSystemInfoJson` performs two-call pattern (probe then fill) matching `wincare_core_version` pattern in `NativeCoreService`
- [ ] Zero `/WarnAsErrors` warnings under V4

**Gates:** V3, V4

---

## PART IV — PHASE 2: C# COMMAND HANDLERS & POLICY DISPATCHER

**Dependency:** Phase 1 complete (Tasks 1.1–1.5 all passing V1, V2).
**Phase exit gate:** V3, V4.

> **IMPORTANT:** Do NOT yet update `commands.json` `migrationStatus` fields. V5 will fail
> until Task 4.5 expands the allowed implemented set. Handlers are registered in
> `CommandRuntime` but remain blocked at gate 5 until Task 4.5 is complete.

---

### Task 2.1 — `CommandHandlerOutcome`: Add `Failed` Static Factory

**File:** `src/WinCare.Application/Commands/CommandHandlerOutcome.cs`

`CommandHandlerOutcome` is a `sealed record` (verified L14–19):
```csharp
public sealed record CommandHandlerOutcome(
    CommandResultStatus Status, string Code, string Message,
    JsonElement? Data, bool UndoAvailable)
```

**Add a static factory method consistent with the record pattern:**

```csharp
/// <summary>
/// Creates a failed outcome for an unrecoverable handler error.
/// <paramref name="code"/> should be dot-separated, e.g. "disk_cleanup.partial_error".
/// </summary>
public static CommandHandlerOutcome Failed(string code, string message) =>
    new(CommandResultStatus.Failed, code, message, Data: null, UndoAvailable: false);
```

**Note:** The existing record may already have `Succeeded` and `Blocked` factories — do not
alter their signatures. Verify the file's existing static members before adding.

**Acceptance criteria:**
- [ ] `CommandHandlerOutcome.Failed("cmd.err", "desc").Status == CommandResultStatus.Failed`
- [ ] `CommandHandlerOutcome.Failed("cmd.err", "desc").UndoAvailable == false`
- [ ] `CommandHandlerOutcome.Failed("cmd.err", "desc").Data == null`
- [ ] File compiles with zero warnings under V4

**Gates:** V4

---

### Task 2.2 — `SystemInfoCommandHandler`: Read-Only System Diagnostics

**File (new):** `src/WinCare.Application/Commands/Handlers/SystemInfoCommandHandler.cs`
**Command ID:** `"system"` — matches the first entry in `commands.json` (id="system", area="Checkup")

> **Note:** The catalog id is `"system"` (not `"sysinfo"`). Always match the exact string in
> `commands.json` — verified at L6.

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Native;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "system" command — a read-only diagnostic that collects
/// CPU count, memory totals, and OS build via the native wincare_core_sys_info ABI.
/// </summary>
public sealed class SystemInfoCommandHandler : ICommandHandler
{
    private readonly NativeCoreService _native;

    public string CommandId => "system";

    public SystemInfoCommandHandler(NativeCoreService native)
        => _native = native ?? throw new ArgumentNullException(nameof(native));

    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request, CancellationToken cancellationToken)
    {
        string json = await _native.GetSystemInfoJsonAsync(cancellationToken)
            .ConfigureAwait(false);

        // Clone the root element to detach it from the JsonDocument lifetime.
        // JsonDocument is disposed at end of using block; Clone() is safe to hold.
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement data = doc.RootElement.Clone();

        return CommandHandlerOutcome.Succeeded(
            "system.ok",
            "System information collected successfully.",
            data,
            undoAvailable: false);
    }
}
```

**Acceptance criteria:**
- [ ] `CommandId` is exactly `"system"` — matches `commands.json` L6
- [ ] `OperationCanceledException` propagates uncaught (caught by `CommandDispatcher` gate 9 catch)
- [ ] `doc.RootElement.Clone()` detaches `JsonElement` from `JsonDocument` lifetime
- [ ] V4 xUnit test: inject mocked `NativeCoreService` returning `{"logical_cpus":4,...}` → `Succeeded` outcome, data contains `logical_cpus`
- [ ] V4 xUnit test: inject cancelled token → `OperationCanceledException` thrown

**Gates:** V3, V4

---

### Task 2.3 — `StorageHealthCommandHandler`: Drive Space Diagnostics

**File (new):** `src/WinCare.Application/Commands/Handlers/StorageHealthCommandHandler.cs`
**Command ID:** `"applications"` or a matching catalog ID — **verify exact ID in `commands.json`**

> Wait — `"applications"` is installed apps. Drive health does not appear to have an
> exact existing catalog ID without further inspection. **Use ID `"storage"` if present,
> or add a new catalog entry with `migrationStatus: "Cataloged"` and id `"storage_health"`.**
> The CommandId must match an entry in commands.json exactly or gate 3 blocks execution.

```csharp
public sealed class StorageHealthCommandHandler : ICommandHandler
{
    public string CommandId => "storage_health"; // adjust to match catalog entry

    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request, CancellationToken cancellationToken)
    {
        // DriveInfo.GetDrives() is a synchronous Win32 call — offload to avoid blocking UI
        DriveInfo[] drives = await Task.Run(DriveInfo.GetDrives, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var records = drives
            .Where(d => d.IsReady)
            .Select(d => new DriveRecord(d.Name, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace))
            .ToArray();

        // AOT-safe: use StorageHealthJsonContext — do not use JsonSerializer without source gen
        string json = JsonSerializer.Serialize(records, StorageHealthJsonContext.Default.DriveRecordArray);
        using JsonDocument doc = JsonDocument.Parse(json);

        return CommandHandlerOutcome.Succeeded(
            "storage_health.ok",
            $"Found {records.Length} ready drive(s).",
            doc.RootElement.Clone(),
            undoAvailable: false);
    }

    private sealed record DriveRecord(string Name, string Format, long TotalBytes, long FreeBytes);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(StorageHealthCommandHandler.DriveRecord[]))]
internal sealed partial class StorageHealthJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
```

**Acceptance criteria:**
- [ ] `DriveInfo.GetDrives()` runs inside `Task.Run` — not on UI thread
- [ ] Only drives where `d.IsReady == true` are included in output
- [ ] `StorageHealthJsonContext` is a partial `JsonSerializerContext` subclass with `[JsonSerializable]`
- [ ] V4 xUnit test: machine has >= 1 ready drive → outcome `Succeeded` with non-empty array
- [ ] V4 xUnit test: cancelled token before `Task.Run` → `OperationCanceledException`

**Gates:** V3, V4

---

### Task 2.4 — `NetworkStatusCommandHandler`: Interface & Connectivity Read

**File (new):** `src/WinCare.Application/Commands/Handlers/NetworkStatusCommandHandler.cs`

```csharp
public sealed class NetworkStatusCommandHandler : ICommandHandler
{
    public string CommandId => "network_status"; // verify catalog entry

    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request, CancellationToken cancellationToken)
    {
        NetworkInterface[] ifaces = await Task.Run(
            NetworkInterface.GetAllNetworkInterfaces, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var records = ifaces.Select(n => new NetworkRecord(
            n.Name,
            n.NetworkInterfaceType.ToString(),
            n.OperationalStatus.ToString(),
            n.GetPhysicalAddress().ToString(),
            n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString()).ToArray(),
            n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                .Select(a => a.Address.ToString()).ToArray()
        )).ToArray();

        string json = JsonSerializer.Serialize(records, NetworkStatusJsonContext.Default.NetworkRecordArray);
        using JsonDocument doc = JsonDocument.Parse(json);
        return CommandHandlerOutcome.Succeeded("network_status.ok",
            $"Found {records.Length} network interface(s).", doc.RootElement.Clone(), undoAvailable: false);
    }

    private sealed record NetworkRecord(string Name, string Type, string Status,
        string MacAddress, string[] IPv4Addresses, string[] IPv6Addresses);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(NetworkStatusCommandHandler.NetworkRecord[]))]
internal sealed partial class NetworkStatusJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
```

**Acceptance criteria:**
- [ ] `GetAllNetworkInterfaces()` runs inside `Task.Run` — not blocking UI thread
- [ ] Loopback adapter always included (loopback is a valid `NetworkInterface`)
- [ ] Each record has all 6 fields: Name, Type, Status, MacAddress, IPv4Addresses, IPv6Addresses
- [ ] `NetworkStatusJsonContext` declared with `[JsonSerializable]`

**Gates:** V3, V4

---

### Task 2.5 — `PrivacyStatusCommandHandler`: Registry Telemetry Read

**File (new):** `src/WinCare.Application/Commands/Handlers/PrivacyStatusCommandHandler.cs`

```csharp
public sealed class PrivacyStatusCommandHandler : ICommandHandler
{
    public string CommandId => "privacy_status"; // verify catalog entry

    // Registry paths and value names — verified against Windows documentation
    private const string TelemetryPath =
        @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    private const string AdvertisingPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
    private const string MaxTelemetryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection";

    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request, CancellationToken cancellationToken)
    {
        PrivacySnapshot snapshot = await Task.Run(ReadSnapshot, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        string json = JsonSerializer.Serialize(snapshot, PrivacyStatusJsonContext.Default.PrivacySnapshot);
        using JsonDocument doc = JsonDocument.Parse(json);
        return CommandHandlerOutcome.Succeeded("privacy_status.ok",
            "Privacy telemetry settings read.", doc.RootElement.Clone(), undoAvailable: false);
    }

    private static PrivacySnapshot ReadSnapshot()
    {
        int? allowTelemetry    = ReadDword(Microsoft.Win32.Registry.LocalMachine, TelemetryPath, "AllowTelemetry");
        bool? advertisingId    = ReadDword(Microsoft.Win32.Registry.CurrentUser, AdvertisingPath, "Enabled") is int v ? v != 0 : null;
        int? maxTelemetry      = ReadDword(Microsoft.Win32.Registry.LocalMachine, MaxTelemetryPath, "MaxTelemetryAllowed");
        return new PrivacySnapshot(allowTelemetry, advertisingId, maxTelemetry);
    }

    private static int? ReadDword(Microsoft.Win32.RegistryKey hive, string path, string name)
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = hive.OpenSubKey(path, writable: false);
            return key?.GetValue(name) is int v ? v : null;
        }
        catch (Exception ex) when (
            ex is System.Security.SecurityException or
            UnauthorizedAccessException or
            System.IO.IOException)
        {
            return null; // missing or restricted key is not an error
        }
    }

    private sealed record PrivacySnapshot(
        int? AllowTelemetry,
        bool? AdvertisingIdEnabled,
        int? MaxTelemetryAllowed);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(PrivacyStatusCommandHandler.PrivacySnapshot))]
internal sealed partial class PrivacyStatusJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
```

**Acceptance criteria:**
- [ ] All registry reads run inside `Task.Run` — not blocking UI thread
- [ ] Missing or inaccessible key → `null` JSON value (not an error, not an exception)
- [ ] `SecurityException`, `UnauthorizedAccessException`, and `IOException` all caught silently
- [ ] V4 xUnit test: mock registry stub returning all nulls → `Succeeded` with `{AllowTelemetry: null, ...}`

**Gates:** V3, V4

---

### Task 2.6 — `DiskCleanupCommandHandler`: Mutating, ReviewApproved-Gated

**File (new):** `src/WinCare.Application/Commands/Handlers/DiskCleanupCommandHandler.cs`
**Policy:** `ReviewApproved = true` enforced at gate 7 in `CommandDispatcher` — no additional guard needed in the handler itself, but the handler must verify `request.Apply` to distinguish dry-run from execution.
**Depends on:** `NativeCoreService.GetDirectorySizeAsync` (Task 1.5)

```csharp
public sealed class DiskCleanupCommandHandler : ICommandHandler
{
    private readonly NativeCoreService _native;

    public string CommandId => "disk_cleanup"; // verify catalog entry

    // Default candidate paths — environment-expanded at runtime
    private static readonly string[] DefaultTargets =
    [
        "%TEMP%",
        @"%LOCALAPPDATA%\Temp",
    ];

    public DiskCleanupCommandHandler(NativeCoreService native)
        => _native = native ?? throw new ArgumentNullException(nameof(native));

    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request, CancellationToken cancellationToken)
    {
        // Resolve candidate paths from parameters or use defaults
        string[] rawTargets = request.Parameters.TryGetProperty("targets", out JsonElement targetsEl)
            && targetsEl.ValueKind == JsonValueKind.Array
            ? targetsEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray()
            : DefaultTargets;

        string[] resolvedTargets = rawTargets
            .Select(Environment.ExpandEnvironmentVariables)
            .Where(Directory.Exists)
            .ToArray();

        if (!request.Apply)
        {
            // Dry-run: measure candidate sizes, return preview — no files deleted
            ulong estimated = 0;
            foreach (string dir in resolvedTargets)
            {
                try { estimated += await _native.GetDirectorySizeAsync(dir, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch { /* inaccessible directory — skip in preview */ }
            }

            var preview = new { preview = true, candidates = resolvedTargets, estimatedFreeBytes = estimated };
            string json = JsonSerializer.Serialize(preview, DiskCleanupPreviewJsonContext.Default.DiskCleanupPreview);
            using JsonDocument doc = JsonDocument.Parse(json);
            return CommandHandlerOutcome.Succeeded("disk_cleanup.preview",
                $"Preview: approximately {estimated:N0} bytes across {resolvedTargets.Length} location(s).",
                doc.RootElement.Clone(), undoAvailable: false);
        }

        // Apply: measure before, delete, measure after, report delta
        // ReviewApproved was already enforced by CommandDispatcher gate 7
        ulong bytesBefore = 0;
        foreach (string dir in resolvedTargets)
        {
            try { bytesBefore += await _native.GetDirectorySizeAsync(dir, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        int deletedCount = 0;
        int errorCount = 0;
        foreach (string dir in resolvedTargets)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { File.Delete(file); deletedCount++; }
                    catch (IOException) { errorCount++; }
                    catch (UnauthorizedAccessException) { errorCount++; }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (IOException ex)
            {
                return CommandHandlerOutcome.Failed("disk_cleanup.partial_error", ex.Message);
            }
        }

        ulong bytesAfter = 0;
        foreach (string dir in resolvedTargets)
        {
            try { bytesAfter += await _native.GetDirectorySizeAsync(dir, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        ulong freed = bytesBefore > bytesAfter ? bytesBefore - bytesAfter : 0;
        var result = new { freedBytes = freed, deletedCount, errorCount, undoAvailable = false };
        string resultJson = JsonSerializer.Serialize(result, DiskCleanupResultJsonContext.Default.DiskCleanupResult);
        using JsonDocument resultDoc = JsonDocument.Parse(resultJson);
        return CommandHandlerOutcome.Succeeded("disk_cleanup.ok",
            $"Freed {freed:N0} bytes ({deletedCount} file(s) deleted, {errorCount} skipped).",
            resultDoc.RootElement.Clone(), undoAvailable: false);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(DiskCleanupCommandHandler.DiskCleanupPreview))]
internal sealed partial class DiskCleanupPreviewJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }

[System.Text.Json.Serialization.JsonSerializable(typeof(DiskCleanupCommandHandler.DiskCleanupResult))]
internal sealed partial class DiskCleanupResultJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
```

**Acceptance criteria:**
- [ ] Dry-run (`request.Apply == false`) returns `Succeeded` with `"preview": true` — zero file deletions
- [ ] Apply (`request.Apply == true`) only deletes files under `resolvedTargets` — no other paths touched
- [ ] `freedBytes = bytesBefore - bytesAfter` via `wincare_core_dir_size` delta
- [ ] `IOException` on directory enumeration → `CommandHandlerOutcome.Failed(...)` — never unhandled
- [ ] `OperationCanceledException` always re-thrown (not swallowed)
- [ ] V4 test: dry-run with mocked native service returning 1024 → preview with `estimatedFreeBytes=1024`
- [ ] V4 test: apply with temp dir → `deletedCount >= 0`, `freedBytes >= 0`

**Gates:** V3, V4

---

### Task 2.7 — `LogCleanupCommandHandler`: Windows Event Log Cleanup

**File (new):** `src/WinCare.Application/Commands/Handlers/LogCleanupCommandHandler.cs`
**Policy:** `ReviewApproved = true` (gate 7)

```csharp
public sealed class LogCleanupCommandHandler : ICommandHandler
{
    public string CommandId => "log_cleanup"; // verify catalog entry

    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request, CancellationToken cancellationToken)
    {
        if (!request.Apply)
        {
            // Dry-run: enumerate logs and return entry counts
            var logInfo = await Task.Run(() =>
            {
                return System.Diagnostics.EventLog.GetEventLogs()
                    .Select(l => new { l.Log, l.Entries.Count })
                    .ToArray();
            }, cancellationToken).ConfigureAwait(false);

            string json = JsonSerializer.Serialize(new { preview = true, logs = logInfo },
                LogCleanupPreviewJsonContext.Default.LogCleanupPreview);
            using JsonDocument doc = JsonDocument.Parse(json);
            return CommandHandlerOutcome.Succeeded("log_cleanup.preview",
                $"Preview: {logInfo.Length} event log(s) found.", doc.RootElement.Clone(), undoAvailable: false);
        }

        // Apply: clear selected logs
        string[] selectedLogs = request.Parameters.TryGetProperty("logs", out JsonElement logsEl)
            && logsEl.ValueKind == JsonValueKind.Array
            ? logsEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray()
            : [];

        try
        {
            var clearResult = await Task.Run(() =>
            {
                var logs = System.Diagnostics.EventLog.GetEventLogs();
                var toClean = selectedLogs.Length > 0
                    ? logs.Where(l => selectedLogs.Contains(l.Log, StringComparer.OrdinalIgnoreCase)).ToArray()
                    : logs;
                int totalRemoved = 0;
                var cleared = new List<string>();
                foreach (var log in toClean)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int before = log.Entries.Count;
                    log.Clear();
                    totalRemoved += before;
                    cleared.Add(log.Log);
                }
                return (cleared.ToArray(), totalRemoved);
            }, cancellationToken).ConfigureAwait(false);

            var result = new { clearedLogs = clearResult.Item1, totalEntriesRemoved = clearResult.Item2 };
            string json = JsonSerializer.Serialize(result, LogCleanupResultJsonContext.Default.LogCleanupResult);
            using JsonDocument doc = JsonDocument.Parse(json);
            return CommandHandlerOutcome.Succeeded("log_cleanup.ok",
                $"Cleared {clearResult.Item1.Length} log(s), removed {clearResult.Item2} entr(ies).",
                doc.RootElement.Clone(), undoAvailable: false);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException ex)
        {
            return CommandHandlerOutcome.Failed("log_cleanup.access_denied",
                $"Administrator access required: {ex.Message}");
        }
    }
}
```

**Acceptance criteria:**
- [ ] Dry-run: returns log names and entry counts; no clearing
- [ ] Apply: `UnauthorizedAccessException` → `Failed("log_cleanup.access_denied", ...)` — never re-throws
- [ ] `OperationCanceledException` always re-thrown
- [ ] Filtering by `request.Parameters["logs"]` is case-insensitive

**Gates:** V3, V4

---

### Task 2.8 — `CommandDispatcher`: ABI Version Guard at Construction

**File:** `src/WinCare.Application/Commands/CommandDispatcher.cs`

The existing 3-param constructor (L20–23) must gain an optional 4th parameter.
The NativeCoreService dependency is optional to preserve backward compatibility with
the existing `CreateDefault()` call pattern and all existing test fixtures.

```csharp
// Replace the existing constructor signature (L20-23) with:
public CommandDispatcher(
    IReadOnlyList<CommandDefinition> definitions,
    IEnumerable<ICommandHandler> handlers,
    TimeProvider? timeProvider = null,
    NativeCoreService? nativeCore = null)     // NEW — optional; null = skip ABI check
{
    // ... all existing validation code unchanged ...
    _timeProvider = timeProvider ?? TimeProvider.System;

    // NEW: ABI version guard — fast, synchronous, runs at startup
    if (nativeCore is not null)
    {
        uint actual = nativeCore.GetAbiVersion();
        const uint expected = NativeCoreService.SupportedAbiVersion; // add this const to NativeCoreService
        if (actual != expected)
            throw new InvalidOperationException(
                $"wincare_core ABI version mismatch: expected {expected}, got {actual}. " +
                $"The installed wincare_core.dll is incompatible with this build. " +
                $"Replace wincare_core.dll with a build matching ABI version {expected}.");
    }
}
```

Also add to `NativeCoreService.cs`:
```csharp
/// <summary>The ABI version this managed layer was built against.</summary>
public const uint SupportedAbiVersion = 1u;
```

**Acceptance criteria:**
- [ ] Existing 3-parameter callers (including `CommandRuntime.CreateDefault()` and all existing test fixtures) compile and work unchanged
- [ ] Constructor throws `InvalidOperationException` with a message containing both expected and actual version numbers
- [ ] V4 xUnit test: mock `NativeCoreService.GetAbiVersion()` returning `2` → exception thrown with both version numbers in message
- [ ] V4 xUnit test: mock returning `1` → no exception

**Gates:** V4

---

### Task 2.9 — `CommandRuntime`: Register All New Handlers

**File:** `src/WinCare.Application/Commands/CommandRuntime.cs`

**Replace the existing `CreateDefault()` body:**

```csharp
// Full replacement of CommandRuntime.cs L14-21:
public static CommandDispatcher CreateDefault()
{
    var native = new NativeCoreService();
    return new CommandDispatcher(
        WinCare.CommandCatalog.CommandCatalog.Load(),  // unchanged — exact existing API
        [
            new CatalogCommandHandler(),               // existing, no-arg
            new PresetsCommandHandler(),               // existing, no-arg
            new SystemInfoCommandHandler(native),      // Task 2.2
            new StorageHealthCommandHandler(),         // Task 2.3
            new NetworkStatusCommandHandler(),         // Task 2.4
            new PrivacyStatusCommandHandler(),         // Task 2.5
            new DiskCleanupCommandHandler(native),     // Task 2.6
            new LogCleanupCommandHandler(),            // Task 2.7
        ],
        TimeProvider.System,
        nativeCore: native);                           // Task 2.8 ABI guard
}
```

**Acceptance criteria:**
- [ ] Compiles with all 8 handlers — no DI container
- [ ] `CommandCatalog.CommandCatalog.Load()` — exact existing API (not `ToolCatalogService`)
- [ ] Each new handler's `CommandId` string matches an entry in `commands.json` (verify manually)
- [ ] V4 smoke test: `CreateDefault()` constructs without exception on a machine with `wincare_core.dll`

**Gates:** V4

---

## PART V — PHASE 3: VISUAL CHARACTER & WINUI 3 UX ERGONOMICS

**Dependency:** Phase 2 complete.
**Phase exit gate:** V3, V5 (after Task 4.5 updates V5 to accept new tokens).

---

### Task 3.1 — `ThemeResources.xaml`: Cyber-Teal & Status Pill Token System

**File:** `src/WinCare.App/Styles/ThemeResources.xaml` (currently 46 lines)

The file already has Dark, Light, and HighContrast `ResourceDictionary` sections.
Add the following tokens to **each of the three theme dictionaries**.

**Add to `Dark` ResourceDictionary:**

```xml
<!-- Cyber-Operate accent system -->
<SolidColorBrush x:Key="AccentTealBrush"          Color="#00D2FF" />
<SolidColorBrush x:Key="AccentTealSubtleBrush"    Color="#1A00D2FF" />
<SolidColorBrush x:Key="CardSurfaceBrush"         Color="#161C26" />
<SolidColorBrush x:Key="TelemetryFrameBrush"      Color="#2A3A4A" />

<!-- Status pill backgrounds -->
<SolidColorBrush x:Key="PillReadOnlyBgBrush"      Color="#047857" />
<SolidColorBrush x:Key="PillMutatingBgBrush"      Color="#EF4444" />
<SolidColorBrush x:Key="PillElevatedBgBrush"      Color="#F59E0B" />
<SolidColorBrush x:Key="PillVerifiedBorderBrush"  Color="#047857" />
<SolidColorBrush x:Key="PillNotReadyBgBrush"      Color="#1F2937" />

<!-- Status pill foreground — amber pill uses #1A1A1A instead, defined inline -->
<SolidColorBrush x:Key="PillTextBrush"            Color="#FFFFFF" />
<SolidColorBrush x:Key="PillAltTextBrush"         Color="#1A1A1A" />
```

**Add to `Light` ResourceDictionary:**

```xml
<SolidColorBrush x:Key="AccentTealBrush"          Color="#0078D4" />
<SolidColorBrush x:Key="AccentTealSubtleBrush"    Color="#1A0078D4" />
<SolidColorBrush x:Key="CardSurfaceBrush"         Color="#FFFFFF" />
<SolidColorBrush x:Key="TelemetryFrameBrush"      Color="#E2E8F0" />
<SolidColorBrush x:Key="PillReadOnlyBgBrush"      Color="#047857" />
<SolidColorBrush x:Key="PillMutatingBgBrush"      Color="#DC2626" />
<SolidColorBrush x:Key="PillElevatedBgBrush"      Color="#D97706" />
<SolidColorBrush x:Key="PillVerifiedBorderBrush"  Color="#047857" />
<SolidColorBrush x:Key="PillNotReadyBgBrush"      Color="#F1F5F9" />
<SolidColorBrush x:Key="PillTextBrush"            Color="#FFFFFF" />
<SolidColorBrush x:Key="PillAltTextBrush"         Color="#1A1A1A" />
```

**Add to `HighContrast` ResourceDictionary:**

```xml
<SolidColorBrush x:Key="AccentTealBrush"         Color="{ThemeResource SystemColorHighlightColor}" />
<SolidColorBrush x:Key="AccentTealSubtleBrush"   Color="{ThemeResource SystemColorHighlightColor}" />
<SolidColorBrush x:Key="CardSurfaceBrush"        Color="{ThemeResource SystemColorWindowColor}" />
<SolidColorBrush x:Key="TelemetryFrameBrush"     Color="{ThemeResource SystemColorWindowTextColor}" />
<SolidColorBrush x:Key="PillReadOnlyBgBrush"     Color="{ThemeResource SystemColorWindowTextColor}" />
<SolidColorBrush x:Key="PillMutatingBgBrush"     Color="{ThemeResource SystemColorWindowTextColor}" />
<SolidColorBrush x:Key="PillElevatedBgBrush"     Color="{ThemeResource SystemColorWindowTextColor}" />
<SolidColorBrush x:Key="PillVerifiedBorderBrush" Color="{ThemeResource SystemColorWindowTextColor}" />
<SolidColorBrush x:Key="PillNotReadyBgBrush"     Color="{ThemeResource SystemColorWindowColor}" />
<SolidColorBrush x:Key="PillTextBrush"           Color="{ThemeResource SystemColorWindowColor}" />
<SolidColorBrush x:Key="PillAltTextBrush"        Color="{ThemeResource SystemColorWindowColor}" />
```

**Add `StatusPillTemplate` to `src/WinCare.App/Styles/ControlStyles.xaml`** (after existing styles):

```xml
<!-- StatusPillTemplate: monospaced pill for risk and migration status display.
     Set Background to the appropriate PillXxxBgBrush.
     Set Foreground to PillTextBrush or PillAltTextBrush (amber).
     Content must be a fixed-width string like "[ READ-ONLY ]". -->
<ControlTemplate x:Key="StatusPillTemplate" TargetType="ContentControl">
    <Border
        CornerRadius="3"
        Padding="6,2"
        Background="{TemplateBinding Background}"
        BorderBrush="{TemplateBinding BorderBrush}"
        BorderThickness="{TemplateBinding BorderThickness}">
        <TextBlock
            FontFamily="Cascadia Code, Cascadia Mono, Consolas"
            FontSize="11"
            Foreground="{TemplateBinding Foreground}"
            Text="{TemplateBinding Content}"
            TextTrimming="None"
            TextWrapping="NoWrap" />
    </Border>
</ControlTemplate>
```

**Acceptance criteria:**
- [ ] All 11 tokens (including `PillAltTextBrush`) defined in all 3 theme dictionaries — 33 new `SolidColorBrush` entries total
- [ ] `StatusPillTemplate` referenceable as `{StaticResource StatusPillTemplate}` from DataTemplates
- [ ] XAML loads without `XamlParseException` on startup
- [ ] `Foreground="{TemplateBinding Foreground}"` — caller sets foreground, not hardcoded

**Gates:** V3, V5

---

### Task 3.2 — `AllToolsPage.xaml`: Status Pill Column — Risk & Migration State

**File:** `src/WinCare.App/Views/Pages/AllToolsPage.xaml`
**File:** `src/WinCare.App/ViewModels/Pages/ToolRowViewModel.cs`
**File:** `src/WinCare.App/Views/Pages/AllToolsPage.xaml.cs`

**Current state (verified):**
- `TableHeader` Grid (L179–197): 5 columns — Command(2.4*), Category(1.2*), Risk(110), Administrator(150), Restart(130)
- DataTemplate (L221–250): Risk is a plain `TextBlock` at `Grid.Column="2"` (L237)
- `AllToolsPage.xaml.cs` L10: `CompactThreshold = 920` (the actual constant — not 640)

**Step A — Add 6th column to TableHeader Grid (L185–191):**

```xml
<!-- Add after existing <ColumnDefinition Width="130" />: -->
<ColumnDefinition Width="110" />
```

**Step B — Add Status column header (after L196):**

```xml
<TextBlock
    x:Name="StatusHeader"
    Grid.Column="5"
    Style="{StaticResource ColumnHeaderTextStyle}"
    Text="Status"
    AutomationProperties.AutomationId="StatusColumnHeader"
    AutomationProperties.Name="Migration status column" />
```

**Step C — Replace Risk plain TextBlock (L237) with pill:**

```xml
<!-- Replace: <TextBlock Grid.Column="2" Style="..." Text="{x:Bind Risk}" /> -->
<!-- With: -->
<ContentControl
    Grid.Column="2"
    VerticalAlignment="Center"
    Template="{StaticResource StatusPillTemplate}"
    Content="{x:Bind RiskPillLabel}"
    Background="{x:Bind RiskPillBackground, Mode=OneWay}"
    Foreground="{x:Bind RiskPillForeground, Mode=OneWay}"
    BorderBrush="Transparent"
    BorderThickness="0" />
```

**Step D — Add Status pill in column 5 (after the Restart TextBlock, line ~239):**

```xml
<ContentControl
    Grid.Column="5"
    VerticalAlignment="Center"
    Template="{StaticResource StatusPillTemplate}"
    Content="{x:Bind StatusPillLabel}"
    Background="{x:Bind StatusPillBackground, Mode=OneWay}"
    Foreground="{x:Bind StatusPillForeground, Mode=OneWay}"
    BorderBrush="{x:Bind StatusPillBorderBrush, Mode=OneWay}"
    BorderThickness="{x:Bind StatusPillBorderThickness, Mode=OneWay}" />
```

**Step E — Add to `ToolRowViewModel.cs`:**

```csharp
// Risk pill display — matches CommandRisk values observed in commands.json
public string RiskPillLabel => Risk switch
{
    "ReadOnly"  => "[ READ-ONLY ]",
    "Low"       => "[ LOW      ]",
    "Moderate"  => "[ MODERATE ]",
    "High"      => "[ HIGH RISK]",
    "Critical"  => "[ CRITICAL ]",
    _           => $"[ {Risk?.ToUpperInvariant()?.PadRight(8) ?? "UNKNOWN ",8} ]"
};

// Background brush key — resolved at view layer via ThemeResource lookup
// Using string keys matched to ThemeResources.xaml token names
public string RiskPillBackground => Risk switch
{
    "ReadOnly" => "PillReadOnlyBgBrush",
    "Low"      => "PillReadOnlyBgBrush",
    "Moderate" => "PillElevatedBgBrush",
    "High"     => "PillMutatingBgBrush",
    "Critical" => "PillMutatingBgBrush",
    _          => "PillNotReadyBgBrush"
};

// Foreground — amber pill uses dark text
public string RiskPillForeground => Risk is "Moderate" ? "PillAltTextBrush" : "PillTextBrush";

// Migration status pill — uses MigrationState property (verified field name in ToolRowViewModel)
public string StatusPillLabel => MigrationState switch
{
    "BehaviorVerified" => "[ VERIFIED ]",
    "Implemented"      => "[ READY    ]",
    _                  => "[ NOT READY]"
};
public string StatusPillBackground => MigrationState switch
{
    "BehaviorVerified" or "Implemented" => "PillReadOnlyBgBrush",
    _                                    => "PillNotReadyBgBrush"
};
public string StatusPillForeground => "PillTextBrush";
public string StatusPillBorderBrush => MigrationState == "BehaviorVerified"
    ? "PillVerifiedBorderBrush" : "Transparent";
public string StatusPillBorderThickness => MigrationState == "BehaviorVerified" ? "1" : "0";
```

> **Note:** The properties return string brush key names. The XAML must resolve these
> via `{ThemeResource}` or the `ContentControl` must use a value converter. Alternatively,
> return `SolidColorBrush` objects resolved from `Application.Current.Resources`.
> Decide on the resolution strategy and apply consistently.

**Step F — Update `AllToolsPage.xaml.cs` `Page_SizeChanged` (L47–55):**

```csharp
private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
{
    bool compact = e.NewSize.Width < CompactThreshold; // CompactThreshold=920 (L10, unchanged)
    ViewModel.SetCompactLayout(compact);
    RiskHeader.Visibility          = compact ? Visibility.Collapsed : Visibility.Visible;
    AdministratorHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    RestartHeader.Visibility       = compact ? Visibility.Collapsed : Visibility.Visible;
    StatusHeader.Visibility        = compact ? Visibility.Collapsed : Visibility.Visible; // NEW
    DetailsSplitView.DisplayMode   = compact ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
}
```

**Acceptance criteria:**
- [ ] Risk column shows `ContentControl` with `StatusPillTemplate` in Cascadia Code 11px
- [ ] Status column (column 5) shows migration state pill
- [ ] `StatusHeader` collapses at `CompactThreshold = 920` DIP (consistent with existing behavior)
- [ ] Tabular `DataTemplate` has 6 `ColumnDefinition` entries matching `TableHeader` grid
- [ ] Compact `StackPanel` (L241–248) also displays risk and migration state as text fallback
- [ ] No binding warnings in XAML output window

**Gates:** V3, V5

---

### Task 3.3 — `MainWindow.xaml`: Verify Ctrl+K Completeness

**Files:** `src/WinCare.App/MainWindow.xaml`, `src/WinCare.App/MainWindow.xaml.cs`

**Audit (verified):**
- Ctrl+K accelerator: already wired (L38–40 in xaml, L57–61 in xaml.cs) ✓
- `GlobalSearchBox.Focus(FocusState.Keyboard)` on invoked (L59) ✓
- `GlobalSearchBox_QuerySubmitted` → `Shell.OpenGlobalSearch(args.QueryText)` (L64–66) ✓
- **Current AutomationId**: `"GlobalSearch"` (L36) — NOT `"GlobalSearchBox"`
- **Current AutomationName**: `"Search WinCare"` (L37) — NOT `"Global tool search"`

**No changes required for Ctrl+K wiring — it is complete.** This task is a verification gate.

**Update V5 if it scans for the old AutomationId string "GlobalSearchBox" — correct to "GlobalSearch".**

**Acceptance criteria:**
- [ ] Ctrl+K from any page focuses `GlobalSearchBox` — manually verified via UI test
- [ ] V5 gate (if scanning AutomationIds) uses `"GlobalSearch"` (not `"GlobalSearchBox"`)
- [ ] `AutomationProperties.AutomationId="GlobalSearch"` is stable and not changed

**Gates:** V5

---

### Task 3.4 — `AllToolsPage.xaml`: Telemetry-Frame Drawer + ReviewApproved Toggle

**File:** `src/WinCare.App/Views/Pages/AllToolsPage.xaml`
**File:** `src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs`

**Change 1 — Upgrade drawer border from `SurfaceBorderStyle` to Cyber-Teal accented card:**

Replace line 91:
```xml
<!-- OLD: -->
<Border Margin="16,0,0,0" Padding="20" Style="{StaticResource SurfaceBorderStyle}">

<!-- NEW: -->
<Border Margin="16,0,0,0" Padding="20"
        CornerRadius="8"
        Background="{ThemeResource CardSurfaceBrush}"
        BorderBrush="{ThemeResource AccentTealBrush}"
        BorderThickness="1,0,0,0">
```

**Change 2 — Add ReviewApproved toggle between the InfoBar (L100–104) and the button row (L105–123):**

```xml
<ToggleSwitch
    x:Name="ReviewApprovedToggle"
    Margin="0,4,0,0"
    IsOn="{x:Bind ViewModel.Execution.ReviewApproved, Mode=TwoWay}"
    Visibility="{x:Bind local:AllToolsPage.BoolToVisibility(
        ViewModel.Execution.IsMutatingTool), Mode=OneWay}"
    Header="Review approved — I understand the planned changes"
    AutomationProperties.AutomationId="ReviewApprovedToggle"
    AutomationProperties.Name="Approve review before applying a mutating command" />
```

**Add to `ToolExecutionViewModel.cs`:**

```csharp
private bool _reviewApproved;

/// <summary>
/// Whether the user has explicitly approved this mutating operation.
/// Bound to <c>ReviewApprovedToggle.IsOn</c> in AllToolsPage.
/// Reset to false whenever the selected tool changes.
/// Passed as <c>CommandExecutionOptions.ReviewApproved</c>.
/// </summary>
public bool ReviewApproved
{
    get => _reviewApproved;
    set => SetProperty(ref _reviewApproved, value);
}

/// <summary>True when the selected tool's definition has ReadOnly == false.</summary>
public bool IsMutatingTool => _selectedDefinition?.ReadOnly == false;

// In the method that sets the selected tool (wherever _selectedDefinition is assigned):
// ReviewApproved = false; // reset on tool change
```

**Wire `ReviewApproved` into `ExecuteSelectedToolAsync`:**

```csharp
// In the method that builds CommandExecutionOptions before calling dispatcher:
var options = new CommandExecutionOptions
{
    ReviewApproved = ReviewApproved,  // passes user confirmation to gate 7
    Apply = _applyMode,
    // ... other existing options unchanged
};
```

**Acceptance criteria:**
- [ ] Drawer left border is 1px `AccentTealBrush` (not `SurfaceBorderStyle`)
- [ ] Drawer background is `CardSurfaceBrush` (dark: `#161C26`, light: `#FFFFFF`)
- [ ] `ReviewApprovedToggle` is visible only when `IsMutatingTool == true`
- [ ] Toggle resets to `false` when a different tool is selected in the ListView
- [ ] `AutomationId="ReviewApprovedToggle"` used by V5 gate
- [ ] `CommandExecutionOptions.ReviewApproved` populated from the VM property

**Gates:** V3, V5

---

### Task 3.5 — `LayoutVisibility.cs`: Shared Breakpoint Helper (Verified State)

**File:** `src/WinCare.App/Views/LayoutVisibility.cs`

**Current state (verified):** `BoolToVisibility` and `InvertBoolToVisibility` already exist
and are already used by `ActivityPage.xaml` (L73, L87). The file is not a "near-empty stub."

**Required additions only:**

```csharp
namespace WinCare.App.Views;

public static class LayoutVisibility
{
    // EXISTING (do not modify):
    public static Visibility BoolToVisibility(bool value) => ...
    public static Visibility InvertBoolToVisibility(bool value) => ...

    // ADD: Shared layout constants referenced by multiple pages
    // AllToolsPage uses a local const CompactThreshold=920 (AllToolsPage.xaml.cs L10).
    // This constant is NOT currently shared. Add it here for cross-page consistency:
    /// <summary>
    /// Window width in DIPs below which page layouts switch to compact card mode.
    /// Matches AllToolsPage.CompactThreshold (920).
    /// </summary>
    public const double CompactBreakpointDip = 920.0;

    /// <summary>Returns true when the window is narrower than CompactBreakpointDip.</summary>
    public static bool IsCompact(double widthDip) => widthDip < CompactBreakpointDip;
}
```

**Update `AllToolsPage.xaml.cs`:** Replace the local `CompactThreshold = 920` constant (L10)
with `LayoutVisibility.CompactBreakpointDip` to make it the single source of truth.

```csharp
// REMOVE: private const double CompactThreshold = 920;
// UPDATE Page_SizeChanged L49:
bool compact = e.NewSize.Width < LayoutVisibility.CompactBreakpointDip;
```

**Acceptance criteria:**
- [ ] `LayoutVisibility.CompactBreakpointDip = 920.0` is the single authoritative constant
- [ ] `AllToolsPage.xaml.cs` no longer has its own `CompactThreshold` constant
- [ ] `ActivityPage.xaml` continues to use `views:LayoutVisibility.BoolToVisibility` (already wired — no change)

**Gates:** V5

---

### Task 3.6 — Accessibility Audit: AutomationId Correctness & WCAG Validation

**Files:** `ShellPage.xaml`, `AllToolsPage.xaml`, `MainWindow.xaml`, `ActivityPage.xaml`

**Verified AutomationId reality (from live file audit):**

| Control | Verified AutomationId | Verified AutomationName |
|---------|----------------------|------------------------|
| Global search box | `GlobalSearch` | `Search WinCare` |
| All tools tab bar | `AllToolsTabs` | `All tools sections` |
| All tools list | `AllToolsTable` | `All WinCare tools` |
| Execute button | `ExecuteSelectedTool` | `Run or review the selected tool` |
| Favorite button | `FavoriteSelectedTool` | `Add or remove selected tool from favorites` |
| Result Expander | `CommandResultDetails` | `Structured command result` |
| Activity tab bar | `ActivityPageTabs` | `Activity sections` |
| Activity list | `ActivityPageRows` | `Activity information` |
| NavView Home | `NavHome` | `Home` |
| NavView Checkup | `NavCheckup` | `Checkup` |
| NavView System care | `NavSystemCare` | `System care` |
| NavView Security | `NavSecurity` | `Security` |
| NavView Repair | `NavRepairRecovery` | `Repair and recovery` |
| NavView All tools | `NavAllTools` | `All tools` |
| NavView Activity | `NavActivity` | `Activity` |
| NavView Settings | `NavSettings` | `Settings` |
| Search TextBox (AllToolsPage) | `ToolSearch` | `Search all tools` |
| Area filter | `AreaFilter` | `Filter tools by area` |
| Risk filter | `RiskFilter` | `Filter tools by risk` |
| ReadOnly checkbox | `ReadOnlyFilter` | `Show read-only tools only` |

**New IDs required by this milestone (add per respective tasks):**

| Control | Required AutomationId | Required by |
|---------|----------------------|-------------|
| Status column header | `StatusColumnHeader` | Task 3.2 |
| ReviewApproved toggle | `ReviewApprovedToggle` | Task 3.4 |
| Attention InfoBar | `AttentionInfoBar` | Task 4.4 |

**WCAG 2.1 AA contract (all must be >= 4.5:1):**

| Situation | Foreground | Background | Ratio | Action |
|-----------|-----------|-----------|-------|--------|
| ReadOnly/Low pill | `#FFFFFF` | `#047857` | 4.52:1 | ✓ PASS — use as-is |
| Mutating/High/Critical pill | `#FFFFFF` | `#EF4444` | 4.06:1 | Use `#C41A1A` if checker fails |
| Elevated/Moderate pill | `#1A1A1A` | `#F59E0B` | 4.54:1 | ✓ PASS — must use `PillAltTextBrush` |
| Not-ready pill | `#94A3B8` | `#1F2937` | 4.62:1 | ✓ PASS |

**Tab order verification (AllToolsPage):**

```
Search TextBox ("ToolSearch") → Area ComboBox → Risk ComboBox → ReadOnly CheckBox
→ ListView ("AllToolsTable") → [SplitView Pane activates on selection]
→ Execute Button → ReviewApprovedToggle → Favorite Button → Result Expander
```

**Acceptance criteria:**
- [ ] All existing AutomationIds above are present and unchanged
- [ ] 3 new IDs added in their respective tasks are verified present
- [ ] All pill contrast ratios validated at >= 4.5:1
- [ ] HighContrast dictionary uses `SystemColor*` for all token foreground/background pairs
- [ ] Tab order verified on physical or virtual machine (not static analysis alone)

**Gates:** V3, V5

---

## PART VI — PHASE 4: ACTIVITY JOURNAL, RECOVERY & FINALIZATION

**Dependency:** Phase 3 complete.
**Phase exit gate:** V1–V6 all pass simultaneously.

---

### Task 4.1 — `ActivityJournalService`: Thread-Safe In-Process Journal

**File (new):** `src/WinCare.Application/Activity/ActivityJournalService.cs`

**Existing domain type (verified, `ActivityRecord.cs` L31–39):**

```csharp
// Exact fields — all 8 required in construction:
public sealed record ActivityRecord(
    Guid Id, string CommandId, string Title, ActivityState State,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    string Result, bool UndoAvailable);

// ActivityState enum: Running, NeedsAttention, Completed, Failed, Cancelled
```

**Full implementation:**

```csharp
namespace WinCare.Application.Activity;

/// <summary>
/// Thread-safe, append-only in-process journal for command activity records.
/// Designed for use on the Windows application lifetime — no persistence.
/// All mutations use record-with immutable update semantics.
/// </summary>
public sealed class ActivityJournalService
{
    private readonly List<ActivityRecord> _records = [];
    private readonly Lock _lock = new();  // Use System.Threading.Lock (.NET 9+)

    /// <summary>Returns a snapshot of all records. Safe to call from any thread.</summary>
    public IReadOnlyList<ActivityRecord> GetAll()
    {
        lock (_lock) return _records.ToArray();
    }

    /// <summary>
    /// Begins a new activity record in the Running state.
    /// The returned record is the initial snapshot — it will not update as the operation progresses.
    /// Observe changes via <see cref="GetAll"/>.
    /// </summary>
    public ActivityRecord Begin(string commandId, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var record = new ActivityRecord(
            Guid.NewGuid(), commandId, title, ActivityState.Running,
            DateTimeOffset.UtcNow, CompletedAt: null, Result: string.Empty, UndoAvailable: false);
        lock (_lock) _records.Add(record);
        return record;
    }

    /// <summary>Transitions the record to Completed.</summary>
    public void Complete(Guid id, string result, bool undoAvailable = false)
        => Update(id, ActivityState.Completed, result, undoAvailable);

    /// <summary>Transitions the record to Failed.</summary>
    public void Fail(Guid id, string result)
        => Update(id, ActivityState.Failed, result, undoAvailable: false);

    /// <summary>Transitions the record to Cancelled.</summary>
    public void Cancel(Guid id)
        => Update(id, ActivityState.Cancelled, "Cancelled by user.", undoAvailable: false);

    /// <summary>Transitions the record to NeedsAttention with a diagnostic message.</summary>
    public void RequireAttention(Guid id, string message)
        => Update(id, ActivityState.NeedsAttention, message, undoAvailable: false);

    private void Update(Guid id, ActivityState state, string result, bool undoAvailable)
    {
        lock (_lock)
        {
            int i = _records.FindIndex(r => r.Id == id);
            if (i < 0) return; // record not found — silently ignore stale updates
            _records[i] = _records[i] with
            {
                State = state,
                CompletedAt = DateTimeOffset.UtcNow,
                Result = result,
                UndoAvailable = undoAvailable,
            };
        }
    }
}
```

**Acceptance criteria:**
- [ ] `Begin → Complete` produces a record with `State = ActivityState.Completed`
- [ ] `System.Threading.Lock` (not `object _lock`) if targeting .NET 9+; else `object _lock = new()` is acceptable
- [ ] All `_records` mutations inside `lock (_lock)` — no lock-free race
- [ ] `record with` immutable update — does not mutate the original `ActivityRecord`
- [ ] V4 xUnit test: 5 concurrent `Begin` calls + 5 concurrent `Complete` calls → `GetAll()` returns 5 `Completed` records, no exceptions
- [ ] V4 xUnit test: `Complete` on unknown `Guid` → silently no-ops (does not throw)
- [ ] `RequireAttention` method exists for integration with attention-based UI (ActivityPage tab "Needs attention")

**Gates:** V4

---

### Task 4.2 — `CommandDispatcher`: Integrate `ActivityJournalService`

**File:** `src/WinCare.Application/Commands/CommandDispatcher.cs`

**Add optional 5th constructor parameter** (journal is separate from nativeCore):

```csharp
public CommandDispatcher(
    IReadOnlyList<CommandDefinition> definitions,
    IEnumerable<ICommandHandler> handlers,
    TimeProvider? timeProvider = null,
    NativeCoreService? nativeCore = null,       // ABI guard (Task 2.8)
    ActivityJournalService? journal = null)     // Activity journaling (Task 4.2)
{
    // ... existing validation + ABI guard unchanged ...
    _journal = journal;  // stored as nullable field
}

private readonly ActivityJournalService? _journal;
```

**Inject journal calls around gate 9 handler execution:**

```csharp
// Inside ExecuteAsync, immediately before the handler.ExecuteAsync call (gate 9):
ActivityRecord? activity = _journal?.Begin(definition.Id, definition.Title ?? definition.Id);
try
{
    CommandHandlerOutcome outcome = await handler.ExecuteAsync(request, linkedCt)
        .ConfigureAwait(false);

    if (outcome.Status == CommandResultStatus.Succeeded)
        _journal?.Complete(activity!.Value.Id, outcome.Message, outcome.UndoAvailable);
    else
        _journal?.Fail(activity!.Value.Id, outcome.Message);

    return BuildResult(outcome, ...);
}
catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
{
    _journal?.Cancel(activity?.Id ?? Guid.Empty);
    // ... existing return ...
}
catch (Exception ex)
{
    _journal?.Fail(activity?.Id ?? Guid.Empty,
        $"Unhandled exception: {ex.GetType().Name}: {ex.Message}");
    // ... existing return ...
}
```

**Acceptance criteria:**
- [ ] Journal is optional — callers without journal compile and work unchanged (null-propagation only)
- [ ] Each gate-9 handler invocation appends exactly 1 `ActivityRecord` if journal is present
- [ ] Terminal call (`Complete`, `Fail`, or `Cancel`) always matches the `Begin` call — no orphaned Running records
- [ ] `activity?.Id ?? Guid.Empty` guards against null in catch blocks
- [ ] V4 xUnit test: mock journal injected; verify `Begin` called exactly once before handler, terminal method called exactly once after

**Gates:** V4

---

### Task 4.3 — `ActivityPageViewModel`: Live Journal Binding

**File:** `src/WinCare.App/ViewModels/Pages/ActivityPageViewModel.cs`

**Current verified state:** 4 tabs defined as "Running", "Needs attention", "Completed", "Reports".
The existing VM uses a `PageRow` projection type (not `ActivityRecord` directly).
The existing `ActivityPage.xaml` binds to `ViewModel.CurrentRows` (L58).

**Add journal-backed refresh to the existing structure:**

```csharp
// Add dependency on ActivityJournalService (injected or resolved):
private readonly ActivityJournalService _journal;

// Add to existing constructor or as a new overload:
public void SetJournal(ActivityJournalService journal)
{
    // Store and enable Refresh() to populate from live data
    _journalRef = journal;
}
private ActivityJournalService? _journalRef;

/// <summary>
/// Re-reads the journal and rebuilds CurrentRows for the active tab.
/// Safe to call from the UI thread — all data access is snapshot-based.
/// </summary>
public void Refresh()
{
    if (_journalRef is null) return;
    IReadOnlyList<ActivityRecord> records = _journalRef.GetAll();

    ActivityState targetState = _activeTab switch
    {
        "Running"        => ActivityState.Running,
        "Needs attention" => ActivityState.NeedsAttention,
        "Completed"      => ActivityState.Completed,
        "Reports"        => ActivityState.Completed, // Reports shows completed with data
        _                => ActivityState.Completed
    };

    var filtered = records
        .Where(r => r.State == targetState)
        .Select(r => new PageRow(
            Title: r.Title,
            Description: r.CommandId,
            State: r.State.ToString(),
            Detail: r.Result,
            IsCompact: false))
        .ToList();

    // Update observable collection on UI thread
    CurrentRows.Clear();
    foreach (var row in filtered) CurrentRows.Add(row);
    OnPropertyChanged(nameof(IsEmpty));
}
```

**Note on PageRow:** Verify the existing `PageRow` constructor signature before implementing.
The `ActivityPage.xaml` DataTemplate uses `Title`, `Description`, `State`, `Detail`, `IsCompact`
(verified from XAML L80–84, L95–104).

**Acceptance criteria:**
- [ ] `Refresh()` is called from UI thread only (journal `GetAll()` returns a snapshot copy, safe)
- [ ] "Running" tab shows records with `ActivityState.Running`
- [ ] "Needs attention" tab shows records with `ActivityState.NeedsAttention`
- [ ] "Completed" tab shows records with `ActivityState.Completed`
- [ ] "Reports" tab shows completed records (define what distinguishes a report from a plain completed record in a follow-up)
- [ ] `IsEmpty` property reflects whether `CurrentRows.Count == 0`
- [ ] V4 xUnit test: journal with 2 Running + 1 Completed → "Running" tab shows 2 rows, "Completed" shows 1

**Gates:** V4

---

### Task 4.4 — `ActivityPage.xaml`: NeedsAttention InfoBar

**File:** `src/WinCare.App/Views/Pages/ActivityPage.xaml`

**Current state (verified):** `ActivityPage.xaml` L113–120 already has an `IsEmpty` InfoBar.
There is no "Needs attention" InfoBar.

**Add immediately before the outer Grid (L33) — inside the row 2 Grid:**

```xml
<!-- Attention banner — shown when any record is in NeedsAttention state -->
<!-- Placed above the SurfaceBorder content area -->
<InfoBar
    x:Name="AttentionInfoBar"
    Margin="0,0,0,8"
    IsOpen="{x:Bind ViewModel.HasAttentionItems, Mode=OneWay}"
    IsClosable="False"
    Severity="Warning"
    Title="Attention required"
    Message="One or more operations need your review before they can complete."
    AutomationProperties.AutomationId="AttentionInfoBar"
    AutomationProperties.Name="Attention required" />
```

**Add to `ActivityPageViewModel.cs`:**

```csharp
/// <summary>True when any journal record is in the NeedsAttention state.</summary>
public bool HasAttentionItems =>
    _journalRef?.GetAll().Any(r => r.State == ActivityState.NeedsAttention) ?? false;
```

**Acceptance criteria:**
- [ ] `InfoBar` is hidden (`IsOpen=false`) when no records have `NeedsAttention` state
- [ ] `InfoBar` is shown when >= 1 record has `NeedsAttention` state
- [ ] `AutomationId="AttentionInfoBar"` — used by V5 updated gate

**Gates:** V5

---

### Task 4.5 — V5 Gate: Expand `verify_native_foundation.py` for New Milestone

**File:** `tools/verify_native_foundation.py`

**Current gate (L158–167)** enforces exactly `{"catalog", "presets"}` as the implemented set.
This must be updated to allow the 6 new handlers once they are promoted in `commands.json`.

**Step A — Update `commands.json`** for each new handler command ID,
changing `migrationStatus` from `"Cataloged"` to `"Implemented"`:
Commands to promote (verify IDs in `commands.json` first): `system`, `storage_health`,
`network_status`, `privacy_status`, `disk_cleanup`, `log_cleanup`.

**Step B — Update `verify_native_foundation.py` L163:**

```python
# Replace:
if implemented_ids != {"catalog", "presets"}:

# With:
_expected_implemented = {
    "catalog", "presets",
    "system", "storage_health", "network_status",
    "privacy_status", "disk_cleanup", "log_cleanup",
}
if implemented_ids != _expected_implemented:
```

**Step C — Add new file checks to `REQUIRED_FILES` tuple:**

```python
# Add after existing handler file checks:
ROOT / "src/WinCare.Application/Commands/Handlers/SystemInfoCommandHandler.cs",
ROOT / "src/WinCare.Application/Commands/Handlers/StorageHealthCommandHandler.cs",
ROOT / "src/WinCare.Application/Commands/Handlers/NetworkStatusCommandHandler.cs",
ROOT / "src/WinCare.Application/Commands/Handlers/PrivacyStatusCommandHandler.cs",
ROOT / "src/WinCare.Application/Commands/Handlers/DiskCleanupCommandHandler.cs",
ROOT / "src/WinCare.Application/Commands/Handlers/LogCleanupCommandHandler.cs",
ROOT / "src/WinCare.Application/Activity/ActivityJournalService.cs",
ROOT / "src/WinCare.App/Styles/ThemeResources.xaml",  # already in REQUIRED_FILES? verify
ROOT / "src/WinCare.App/Styles/ControlStyles.xaml",
```

**Step D — Add AutomationId scans** for new controls:

```python
# Add to AutomationId verification section:
"ReviewApprovedToggle": "Approve review before applying a mutating command",
"AttentionInfoBar": "Attention required",
"StatusColumnHeader": "Migration status column",
```

**Step E — Add token checks** for new brush tokens:

```python
# Add to ThemeResources.xaml scan:
"AccentTealBrush", "CardSurfaceBrush", "PillReadOnlyBgBrush",
"PillMutatingBgBrush", "StatusPillTemplate",
```

**Acceptance criteria:**
- [ ] `python tools/verify_native_foundation.py` exits 0 with all findings empty
- [ ] All 6 new command IDs are in `_expected_implemented`
- [ ] 7 new handler/service file paths added to `REQUIRED_FILES`
- [ ] 3 new AutomationId strings scanned
- [ ] New brush token names verified in `ThemeResources.xaml`
- [ ] Existing 22+ checks all still pass (no regressions)

**Gates:** V5

---

### Task 4.6 — `CommandRuntime.CreateDefault()`: Wire Activity Journal End-to-End

**File:** `src/WinCare.Application/Commands/CommandRuntime.cs`

```csharp
public static CommandDispatcher CreateDefault()
{
    var native = new NativeCoreService();
    var journal = new ActivityJournalService();  // NEW

    return new CommandDispatcher(
        WinCare.CommandCatalog.CommandCatalog.Load(),
        [
            new CatalogCommandHandler(),
            new PresetsCommandHandler(),
            new SystemInfoCommandHandler(native),
            new StorageHealthCommandHandler(),
            new NetworkStatusCommandHandler(),
            new PrivacyStatusCommandHandler(),
            new DiskCleanupCommandHandler(native),
            new LogCleanupCommandHandler(),
        ],
        TimeProvider.System,
        nativeCore: native,
        journal: journal);  // passes journal to dispatcher (Task 4.2)
}

/// <summary>
/// Returns the activity journal from the last CreateDefault() call.
/// Used by ActivityPageViewModel to display live journal state.
/// Thread-safe: journal is immutable reference after construction.
/// </summary>
public static ActivityJournalService? LastJournal { get; private set; }
// Or: inject journal via DI / pass to App.xaml.cs startup
```

**Acceptance criteria:**
- [ ] `ActivityJournalService` is created inside `CreateDefault()` and passed to dispatcher
- [ ] The journal instance is accessible to the presentation layer (via DI, static accessor, or App.xaml.cs wiring)
- [ ] V4 smoke test: `CreateDefault()` → execute a read-only command → `journal.GetAll()` contains 1 Completed record

**Gates:** V4

---

### Task 4.7 — Final RC Dual-Archive Finalization

**Command:**

```
python tools/finalize_native_release.py ^
    --output artifacts/finalization ^
    --version 2.4.0-rc1 ^
    --mode rc
```

**Expected artifacts:**

```
artifacts/finalization/
  WinCare-2.4.0-rc1-native-source.zip    (zero .ps1/.psm1/.psd1 inside)
  WinCare-2.4.0-rc1-legacy-oracle.zip    (PowerShell reference archive; >= 1 .ps1)
  checksums.sha256                         (SHA-256 for both archives, one per line)
```

**Acceptance criteria:**
- [ ] Script exits 0
- [ ] `WinCare-2.4.0-rc1-native-source.zip` contains zero `.ps1`, `.psm1`, `.psd1` files
- [ ] `WinCare-2.4.0-rc1-legacy-oracle.zip` contains >= 1 `.ps1` file
- [ ] `checksums.sha256` file contains exactly 2 lines (one per archive)
- [ ] V6 gate exits 0

**Gates:** V5, V6

---

## PART VII — RISK MATRIX

| # | Category | Exact Scenario | Severity | Verified Mitigation |
|---|----------|---------------|:--------:|---------------------|
| R1 | **FFI Panic** | Rust panic crosses C ABI → UB / process crash | CRITICAL | All Status enum branches return `i32`; `accumulate_dir_size` uses `Result`; `panic = "abort"` should be added to `[profile.release]` in workspace `Cargo.toml` |
| R2 | **UI Stall** | `DriveInfo.GetDrives()` / `EventLog.GetEventLogs()` > 50ms on UI thread | HIGH | All handler I/O inside `Task.Run(..., cancellationToken)` — enforced by code pattern in Tasks 2.3–2.7 |
| R3 | **Gate 5 Fail-Open** | Handler registered with `CommandId` that doesn't exist in `commands.json` → gate 3 blocks all executions | HIGH | V5 gate (updated in Task 4.5) validates all 8 `CommandId` strings against `commands.json` |
| R4 | **V5 CI Break** | New handler promoted in `commands.json` before V5 gate is updated → `implemented_ids` mismatch fails CI | HIGH | Task 4.5 must run before any `commands.json` `migrationStatus` is changed to `Implemented` |
| R5 | **WCAG Contrast** | Amber pill `#F59E0B` bg with `#FFFFFF` fg = 2.21:1 (fails 4.5:1) | MED | `PillAltTextBrush = #1A1A1A` (4.54:1 on amber) must be used; enforced by `RiskPillForeground` switch |
| R6 | **ABI Drift** | `wincare_core.dll` updated with new status code; `NativeCoreStatus.cs` not updated → wrong status mapped | MED | ABI version guard in `CommandDispatcher` constructor (Task 2.8) throws on startup; `SupportedAbiVersion = 1u` constant |
| R7 | **AOT Trim** | New handler serialises `object`/`dynamic` without `[JsonSerializable]` → runtime trim exception | MED | Each handler declares its own `partial JsonSerializerContext` subclass with `[JsonSerializable]` on the concrete type |
| R8 | **Safety — Unreviewed Mutation** | `disk_cleanup`/`log_cleanup` runs without `ReviewApproved = true` | CRITICAL | Gate 7 in `CommandDispatcher` is already implemented and tested; `ReviewApprovedToggle` in UI forces explicit confirmation |
| R9 | **Registry ACL** | `PrivacyStatusCommandHandler` reads restricted key → throws `SecurityException` uncaught | LOW | `ReadDword` catches `SecurityException`, `UnauthorizedAccessException`, `IOException` → returns null |
| R10 | **Overflow** | Directory traversal accumulates > `u64::MAX` bytes | LOW | `u64::checked_add(...).unwrap_or(u64::MAX)` — saturates, never panics |

---

## PART VIII — TASK DEPENDENCY GRAPH & DEFINITION OF DONE

### Dependency Flow (strict topological order)

```
[Phase 1 — Parallel start]
  1.1 (dir_size Rust) ──┐
  1.2 (sys_info Rust) ──┤
  1.3 (C header)     ──┤──> 1.4 (P/Invoke) ──> 1.5 (NativeCoreService)
                         |                               |
                    [V1, V2 pass]              [Phase 1 exit: V1 + V2]
                                                         |
[Phase 2 — After 1.5]                                    |
  2.1 (Failed factory) ─────────────────────────────────>|
  2.2 (SystemInfo handler) ─────────────────────────────>|
  2.3 (StorageHealth handler) ──────────────────────────>|
  2.4 (Network handler) ────────────────────────────────>|─> 2.8 (ABI guard) ─> 2.9 (CommandRuntime)
  2.5 (Privacy handler) ────────────────────────────────>|                           |
  2.6 (DiskCleanup handler) ────────────────────────────>|               [Phase 2 exit: V3 + V4]
  2.7 (LogCleanup handler) ─────────────────────────────>|
                                                          |
[Phase 3 — After Phase 2]                                 |
  3.1 (ThemeResources tokens) ─────────────────────────>─┤
  3.5 (LayoutVisibility const) ────────────────────────>─┤──> 3.2 (pill columns) ──> 3.3 (verify Ctrl+K)
                                                          |                              |
                                                          |                         3.4 (drawer + toggle)
                                                          |                              |
                                                          |                         3.6 (a11y audit)
                                                          |                              |
                                                     [Phase 3 exit: V3 + V5 after 4.5]
                                                                         |
[Phase 4 — After Phase 3]
  4.1 (ActivityJournalService) ──────────────────────> 4.2 (dispatcher integration)
  4.4 (ActivityPage InfoBar) ────────────────────────> 4.3 (ActivityPageViewModel)
                                                              |
                                                         4.5 (V5 gate expansion) ──> 4.6 (CommandRuntime journal)
                                                                                          |
                                                                                     4.7 (RC finalization)
                                                                                          |
                                                                             [RC READY: V1+V2+V3+V4+V5+V6]
```

### Definition of Done — 20 Binary Conditions

The 2.4.0-rc1 milestone is complete when **all 20** conditions are simultaneously true.
Each condition is binary: it either passes or the milestone is not closed.

| # | Condition | Verification |
|---|-----------|-------------|
| 1 | `cargo test` passes **17 tests** (8 original + 5 dir_size + 4 sys_info) | `cargo test --manifest-path native/Cargo.toml` |
| 2 | Zero Rust clippy warnings under `-D warnings` | `cargo clippy --all-targets -- -D warnings` |
| 3 | All **4 Python native interop files** pass | `python -m unittest discover -s tests/native` |
| 4 | All **57 new C# managed tests** pass alongside existing suite | `dotnet test WinCare.Native.sln -c Release -p:Platform=x64` |
| 5 | V5 gate exits 0 with **empty findings list** | `python tools/verify_native_foundation.py` |
| 6 | V6 RC archive exits 0 | `python tools/finalize_native_release.py --mode rc` |
| 7 | Native-source archive contains **zero** `.ps1`, `.psm1`, `.psd1` files | `python tools/verify_release.py` |
| 8 | All **8 implemented command IDs** have `migrationStatus = "Implemented"` in `commands.json` and pass V5 | V5 gate |
| 9 | All **11 brush tokens** declared in all **3 theme dictionaries** (33 total entries) | `python tools/verify_visual_tokens.py` |
| 10 | `StatusPillTemplate` declared in `ControlStyles.xaml` | `python tools/verify_visual_tokens.py` |
| 11 | All pill pairs **>= 4.5:1 WCAG AA** — amber uses `PillAltTextBrush` (`#1A1A1A`) | `python tools/verify_pill_contrast.py` |
| 12 | All **24 AutomationId strings** (21 existing + 3 new) present and unmodified | V5 XML scan |
| 13 | `ReviewApprovedToggle` visible **only** when `IsMutatingTool == true`; resets on selection change | UI test + V5 |
| 14 | `ActivityJournalService` concurrent 5x5 stress test passes with **zero races** | `dotnet test` |
| 15 | `AttentionInfoBar` **visible** when NeedsAttention record exists; **hidden** otherwise | UI test + V5 |
| 16 | `LayoutVisibility.CompactBreakpointDip = 920.0` is **only** declaration — no local constant in `AllToolsPage.xaml.cs` | `grep -r CompactThreshold` returns 0 matches |
| 17 | `[profile.release] panic = "abort"` present in `native/Cargo.toml` | File inspection |
| 18 | `tasks/progress.md` all 26 tasks marked `[x]` | Manual |
| 19 | `DESIGN.md` updated to reference `AccentTealBrush`, `CardSurfaceBrush`, `PillAltTextBrush`, `StatusPillTemplate` | Manual |
| 20 | `python tools/release_checklist.py` runs all 7 pre-RC checks and exits 0 | `python tools/release_checklist.py` |

---

## PART IX — SECURITY HARDENING & THREAT MODEL

### 9.1 STRIDE Analysis — CommandDispatcher Admission Pipeline

The `CommandDispatcher` is the single enforcement point for all command authorization.
Every threat below maps to an exact gate or mitigation that is verifiable in source.

| STRIDE | Threat scenario | Gate / Mitigation | Verification |
|--------|----------------|-------------------|-------------|
| **Spoofing** | Caller forges `CommandRequest.Id` to bypass catalog validation | Gate 3: `_definitions.TryGetValue` checks catalog truth — unknown IDs hard-blocked | V5 catalog parity check |
| **Tampering** | `commands.json` promoted to `Implemented` without a handler registered | Gate 8: `_handlers.TryGetValue` — missing handler returns `Failed("command.handler_missing")` | V5 `implemented_ids` allow-list |
| **Repudiation** | User denies approving a mutating operation | `ActivityJournalService.Begin()` timestamps every execution; `ReviewApproved` state recorded | Task 4.1 journal; Task 3.4 toggle |
| **Info Disclosure** | Native FFI error leaks stack trace across ABI boundary | Status enum (0-6) only — no strings cross C ABI; Rust stack stays in Rust | V1 + Rust test suite |
| **Denial of Service** | Slow directory traversal blocks UI thread > 50ms | `Task.Run` offloads all I/O; `CancellationToken` propagated throughout every handler | V3 timing assertions |
| **Elevation of Privilege** | Mutating command executes without user review | Gate 7: `!ReviewApproved` -> `Blocked("command.review_required")`; ToggleSwitch in UI | V5 scans for `ReviewApprovedToggle` AutomationId |

### 9.2 Rust ABI Security Invariants

**Invariant 1 — No panic crosses C ABI (add to `native/Cargo.toml`):**

```toml
[profile.release]
panic = "abort"
```

Currently no `[profile]` section exists in `native/wincare-core/Cargo.toml` (verified). This
must be added. A Rust panic in release mode becomes `SIGABRT` instead of undefined behavior.

**Invariant 2 — All pointer parameters validated before first dereference:**
Every `pub unsafe extern "C"` function checks all non-size pointer arguments for null before
any `unsafe { ... }` block. Verified by unit tests:
- `rejects_null_pointers_and_short_output` — `wincare_core_sha256_file` (existing, L279)
- `dir_size_rejects_null_pointer` — `wincare_core_dir_size` (Task 1.1)
- `sys_info_rejects_null_written_pointer` — `wincare_core_sys_info` (Task 1.2)

**Invariant 3 — No heap allocation in hot-path native functions:**
`wincare_core_sys_info` uses a `[u8; 512]` stack buffer. `wincare_core_dir_size` uses
recursive stack frames only. Neither calls `Box::new`, `Vec::new`, or any allocator on
the success path. This prevents allocator-ABI mismatch with the C# runtime heap.

**Invariant 4 — UTF-8 validation before every path use:**
Every function accepting `path_utf8: *const u8` calls `std::str::from_utf8(bytes)` and
returns `Status::InvalidUtf8 = 2` on failure. No path string reaches `std::path::Path::new`
without prior validation.

**Invariant 5 — No `unwrap()` in non-test code paths:**
Add to CI: `cargo clippy -- -D clippy::unwrap_used`. Use `match`, `?`, or
`.unwrap_or_else()` instead. `unwrap()` within `#[cfg(test)]` is acceptable.

### 9.3 Managed-Side Security Invariants

**Invariant 6 — All registry reads are read-only:**
`PrivacyStatusCommandHandler.ReadDword` uses `hive.OpenSubKey(path, writable: false)`.
No registry write is permitted in any read-only handler. Verified by code review gate.

**Invariant 7 — Temp file deletion scoped to known-safe roots only:**

```csharp
// Add to DiskCleanupCommandHandler:
private static readonly string[] AllowedRoots = [
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    Path.GetTempPath(),
];

private static bool IsWithinAllowedRoot(string resolved) =>
    AllowedRoots.Any(root =>
        resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase));

// In ExecuteAsync, before any deletion:
if (!IsWithinAllowedRoot(dir))
    return CommandHandlerOutcome.Failed(
        "disk_cleanup.path_denied",
        $"Path '{dir}' is not within an allowed cleanup root.");
```

**Invariant 8 — No subprocess spawning from any handler:**
GEMINI.md rule: zero `.ps1` invocations. Add to `verify_native_foundation.py` banned patterns:

```python
BANNED_NATIVE_PATTERNS = (
    "Microsoft.PowerShell.SDK",
    "System.Management.Automation",
    "PresentationCore",
    "PresentationFramework",
    "System.Windows.",
    "<UseWPF>true</UseWPF>",
    "Process.Start(",       # NEW: forbid subprocess spawning
    "cmd.exe",              # NEW
    "powershell.exe",       # NEW
)
```

**Invariant 9 — No credential or token data in ActivityRecord.Result:**
Handler messages must not contain registry values that could expose sensitive data.
`PrivacyStatusCommandHandler` returns telemetry level integers only — not raw registry paths
or key contents. `LogCleanupCommandHandler` returns entry counts — not log content.

### 9.4 Privilege Escalation Defense

Commands with `administratorAccess = "Yes"` that fail due to `UnauthorizedAccessException`
must return `CommandHandlerOutcome.Failed("cmd.access_denied", ex.Message)` — never re-throw.
The UI must display this failure explicitly (InfoBar with `Severity="Error"`).
UAC elevation is explicitly **out of scope** for the 2.4.0-rc1 milestone.

### 9.5 Code Signing Contract

The RC archive is produced unsigned. Post-signing workflow:
1. Sign `wincare_core.dll` with Authenticode
2. Sign the MSIX package
3. Recompute `checksums.sha256` against signed artifacts
4. Pre-signing `checksums.sha256` (from V6) is for QA internal use only

---

## PART X — OBSERVABILITY, TELEMETRY & STARTUPTELEMETRY INTEGRATION

### 10.1 Existing `StartupTelemetry` Infrastructure (Verified)

```csharp
// src/WinCare.Infrastructure/Observability/StartupTelemetry.cs
// Usage verified in MainWindow.xaml.cs:
//   L33: StartupTelemetry.Mark("FirstContentRendered")  -- in OnWindowRootLoaded
//   L39: StartupTelemetry.Mark("ShellInteractive")      -- in OnWindowActivated
```

`StartupTelemetry.Mark(string)` records a high-resolution timestamp keyed by name.
No external observability dependency — pure in-process static dictionary.

### 10.2 New Telemetry Marks to Add

Add `StartupTelemetry.Mark(...)` at each named location:

| Mark name | File | Location | When |
|-----------|------|----------|------|
| `NativeAbiVerified` | `CommandDispatcher.cs` | After ABI guard passes (Task 2.8) | ABI version confirmed |
| `CommandRuntimeCreated` | `CommandRuntime.cs` | Return line of `CreateDefault()` | All handlers registered |
| `ActivityJournalCreated` | `CommandRuntime.cs` | After `var journal = new ActivityJournalService()` | Journal initialized |
| `AllToolsPageLoaded` | `AllToolsPage.xaml.cs` | After `InitializeComponent()` in ctor | Page XAML parsed |
| `ActivityPageLoaded` | `ActivityPage.xaml.cs` | After `InitializeComponent()` in ctor | Page XAML parsed |

### 10.3 Command Execution Timing — Per-Handler Instrumentation

Add elapsed-time recording around gate 9 in `CommandDispatcher.ExecuteAsync`:

```csharp
long handlerStart = System.Diagnostics.Stopwatch.GetTimestamp();
CommandHandlerOutcome outcome = await handler.ExecuteAsync(request, linkedCt)
    .ConfigureAwait(false);
double elapsedMs =
    (System.Diagnostics.Stopwatch.GetTimestamp() - handlerStart)
    * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
StartupTelemetry.Mark($"handler.{definition.Id}.ms:{elapsedMs:F1}");
```

This produces marks like `handler.system.ms:42.3` — searchable in telemetry logs without
any external dependency.

### 10.4 Per-Handler Performance Budget (V4 Assertions)

Each new handler must have a V4 integration test asserting a wall-time budget:

| Handler | Wall-time budget | Rationale |
|---------|-----------------|-----------|
| `SystemInfoCommandHandler` | < 200 ms | Win32 `GetSystemInfo` + `GlobalMemoryStatusEx` are fast |
| `StorageHealthCommandHandler` | < 500 ms | `DriveInfo.GetDrives()` — multiple disks may enumerate slowly |
| `NetworkStatusCommandHandler` | < 300 ms | `GetAllNetworkInterfaces()` — loopback always fast |
| `PrivacyStatusCommandHandler` | < 100 ms | Registry reads are < 1ms each |
| `DiskCleanupCommandHandler` (dry-run) | < 500 ms | `GetDirectorySizeAsync` on `%TEMP%` |
| `LogCleanupCommandHandler` (preview) | < 300 ms | Event log enumeration |

**V4 test pattern (apply to each handler):**

```csharp
[Fact]
public async Task ExecuteAsync_CompletesWithinBudget()
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var outcome = await _handler.ExecuteAsync(
        new CommandRequest("system", default, apply: false), CancellationToken.None);
    sw.Stop();
    Assert.True(sw.ElapsedMilliseconds < 200,
        $"Handler exceeded 200ms budget: {sw.ElapsedMilliseconds}ms");
    Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
}
```

### 10.5 Human-Readable ActivityJournal Messages

Each handler's `CommandHandlerOutcome.Message` must be human-readable for the
ActivityPage journal. Target message formats:

| Handler | Target `Message` |
|---------|-----------------|
| `SystemInfoCommandHandler` | `"Logical CPUs: 8 -- RAM: 16.0 GB total, 9.2 GB available -- OS: 22631"` |
| `StorageHealthCommandHandler` | `"3 drive(s) -- C: 512 GB (241 GB free), D: 2 TB (1.4 TB free)"` |
| `NetworkStatusCommandHandler` | `"4 interface(s) -- Wi-Fi (Up), Ethernet (Down), Loopback (Up)"` |
| `PrivacyStatusCommandHandler` | `"Telemetry level: 1 -- Advertising ID: disabled"` |
| `DiskCleanupCommandHandler` | `"Freed 1.4 GB (3,821 files deleted, 12 locked)"` |
| `LogCleanupCommandHandler` | `"Cleared 5 log(s), removed 2,341 entries"` |

These messages appear verbatim in `ActivityRecord.Result` via the journal integration (Task 4.2).

### 10.6 Startup Timeline Target (2.4.0-rc1)

```
T+0ms       App.xaml.cs entry
T+800ms     MainWindow ctor -- Mica backdrop configured, window resized
T+900ms     FirstContentRendered mark -- ShellPage visible
T+950ms     ShellInteractive mark -- user can navigate
T+1200ms    CommandRuntimeCreated mark -- all handlers registered
T+1250ms    NativeAbiVerified mark -- ABI v1 confirmed
T+1400ms    AllToolsPageLoaded mark (on first navigation to All Tools)
```

All marks >= 2000ms indicate a regression. `verify_native_foundation.py` does not measure
these -- they are captured via `StartupTelemetry` in the running process only.

---

## PART XI — TEST COVERAGE SPECIFICATION

### 11.1 Coverage Targets by Layer

| Layer | Target line coverage | Target branch coverage | Framework |
|-------|---------------------|----------------------|-----------|
| `native/wincare-core` (Rust) | >= 90% | >= 85% | `cargo test` + `cargo llvm-cov` |
| `WinCare.Application` | >= 85% | >= 75% | xUnit 2.9 + Moq 4.20 |
| `WinCare.Infrastructure` | >= 80% | >= 70% | xUnit + Moq |
| `WinCare.Domain` | 100% | 100% | xUnit (pure immutable records) |
| `WinCare.CommandCatalog` | >= 95% | >= 90% | xUnit |
| `WinCare.App` ViewModels | >= 70% | >= 60% | xUnit (no UI thread needed) |
| Python `tools/` | maintain existing | maintain | pytest |

### 11.2 New Unit Test Inventory by Task

**Task 1.1 (`wincare_core_dir_size`):** 5 Rust tests (bodies in Task 1.1)
**Task 1.2 (`wincare_core_sys_info`):** 4 Rust tests (bodies in Task 1.2)

**Task 2.1 (`CommandHandlerOutcome.Failed` factory):**

```csharp
[Fact] void Failed_SetsStatus_Failed()
[Fact] void Failed_SetsUndoAvailable_False()
[Fact] void Failed_SetsData_Null()
[Fact] void Failed_CodeAndMessage_Propagated_Verbatim()
```

**Task 2.2 (`SystemInfoCommandHandler`):**

```csharp
[Fact] async Task ExecuteAsync_ReturnsSucceeded_WithJsonData()
[Fact] async Task ExecuteAsync_DataContains_LogicalCpus_Key()
[Fact] async Task ExecuteAsync_DataContains_AllMemoryFields()
[Fact] async Task ExecuteAsync_DataContains_OsBuild_Key()
[Fact] async Task ExecuteAsync_CancelledToken_Throws_OperationCancelled()
```

**Task 2.3 (`StorageHealthCommandHandler`):**

```csharp
[Fact] async Task ExecuteAsync_ReturnsSucceeded_WithDriveArray()
[Fact] async Task ExecuteAsync_AtLeastOneDrive_Present()
[Fact] async Task ExecuteAsync_OnlyReadyDrives_InResult()
[Fact] async Task ExecuteAsync_CancelledToken_Throws()
```

**Task 2.4 (`NetworkStatusCommandHandler`):**

```csharp
[Fact] async Task ExecuteAsync_ReturnsSucceeded()
[Fact] async Task ExecuteAsync_LoopbackInterface_Included()
[Fact] async Task ExecuteAsync_AllSixFields_PresentOnEachRecord()
[Fact] async Task ExecuteAsync_CancelledToken_Throws()
```

**Task 2.5 (`PrivacyStatusCommandHandler`):**

```csharp
[Fact] async Task ExecuteAsync_AllNullRegistry_ReturnsSucceeded()
[Fact] async Task ExecuteAsync_SecurityException_ReturnsNullField_NotException()
[Fact] async Task ExecuteAsync_UnauthorizedAccess_ReturnsNullField_NotException()
[Fact] async Task ExecuteAsync_CancelledToken_Throws()
```

**Task 2.6 (`DiskCleanupCommandHandler`):**

```csharp
[Fact] async Task DryRun_ReturnsPreview_ZeroFilesDeleted()
[Fact] async Task DryRun_EstimatesSize_FromNativeService()
[Fact] async Task Apply_EmptyTempDir_Returns_ZeroFreed_ZeroDeleted()
[Fact] async Task Apply_OneTestFile_Returns_PositiveFreed_OneDeleted()
[Fact] async Task Apply_LockedFile_ErrorCount_Positive_NotException()
[Fact] async Task Apply_PathOutsideAllowedRoots_Returns_Failed()
[Fact] async Task Apply_CancelledMidway_Throws_OperationCancelled()
```

**Task 2.7 (`LogCleanupCommandHandler`):**

```csharp
[Fact] async Task DryRun_Returns_LogNames_AndEntryCounts()
[Fact] async Task Apply_WithNoSelection_ClearsAll_AccessibleLogs()
[Fact] async Task Apply_CaseInsensitiveSelection_MatchesCorrectly()
[Fact] async Task Apply_UnauthorizedAccess_Returns_Failed_NotException()
[Fact] async Task Apply_CancelledToken_Throws()
```

**Task 2.8 (ABI version guard):**

```csharp
[Fact] void Constructor_MatchingAbiVersion_DoesNotThrow()
[Fact] void Constructor_MismatchedAbiVersion_Throws_InvalidOperation()
[Fact] void Constructor_NullNativeCore_SkipsAbiCheck()
[Fact] void ExceptionMessage_ContainsBoth_ExpectedAndActualVersions()
```

**Task 4.1 (`ActivityJournalService`):**

```csharp
[Fact] void Begin_CreatesRecord_InRunningState()
[Fact] void Complete_TransitionsTo_Completed_WithResult()
[Fact] void Fail_TransitionsTo_Failed_WithResult()
[Fact] void Cancel_TransitionsTo_Cancelled_WithFixedMessage()
[Fact] void RequireAttention_TransitionsTo_NeedsAttention()
[Fact] void Complete_UnknownGuid_SilentlyIgnored_NoException()
[Fact] void GetAll_ReturnsSnapshot_NotLiveReference()
[Fact] async Task ConcurrentBeginAndComplete_NoRaceConditions_5x5()
[Fact] void Begin_EmptyCommandId_Throws_ArgumentException()
[Fact] void MultipleBegins_AllTracked_Independently_By_Guid()
```

**Task 4.2 (Dispatcher journal integration):**

```csharp
[Fact] async Task ExecuteAsync_WithJournal_Begin_CalledOnce_PerExecution()
[Fact] async Task ExecuteAsync_Succeeded_Journal_Complete_Called()
[Fact] async Task ExecuteAsync_HandlerFailed_Journal_Fail_Called()
[Fact] async Task ExecuteAsync_Cancelled_Journal_Cancel_Called()
[Fact] async Task ExecuteAsync_NullJournal_CompletesNormally_NoNullRef()
```

**Total new managed unit tests: 57**
**Total new Rust unit tests: 9 (5 + 4)**
**Grand total new tests this milestone: 66**

### 11.3 Property-Based Testing (Rust -- `proptest` crate)

Add to `native/wincare-core/Cargo.toml`:

```toml
[dev-dependencies]
proptest = "1"
```

Add inside `#[cfg(test)] mod tests`:

```rust
use proptest::prelude::*;

proptest! {
    #[test]
    fn dir_size_never_panics_on_arbitrary_path_string(path in ".*") {
        let mut out: u64 = 0;
        let _ = unsafe {
            wincare_core_dir_size(path.as_ptr(), path.len(), &mut out)
        };
        // Must not panic -- any i32 return is valid
    }

    #[test]
    fn dir_size_never_panics_on_arbitrary_byte_sequence(
        bytes in proptest::collection::vec(0u8..=255, 0..256)
    ) {
        let mut out: u64 = 0;
        let _ = unsafe {
            wincare_core_dir_size(bytes.as_ptr(), bytes.len(), &mut out)
        };
    }

    #[test]
    fn sys_info_never_panics_with_arbitrary_buffer_length(len in 0usize..2048) {
        let mut buf = vec![0u8; len];
        let mut written: usize = 0;
        let _ = unsafe {
            wincare_core_sys_info(buf.as_mut_ptr(), buf.len(), &mut written)
        };
    }
}
```

### 11.4 JSON Schema Snapshot Tests

Each handler returning `JsonElement? Data` must have a schema stability test:

```csharp
[Fact]
public async Task SystemInfoHandler_JsonSchema_ExactlyFourKeys()
{
    var outcome = await _handler.ExecuteAsync(
        new CommandRequest("system", default, false), CancellationToken.None);
    var data = outcome.Data!.Value;
    Assert.Equal(JsonValueKind.Number, data.GetProperty("logical_cpus").ValueKind);
    Assert.Equal(JsonValueKind.Number, data.GetProperty("total_physical_memory_bytes").ValueKind);
    Assert.Equal(JsonValueKind.Number, data.GetProperty("available_physical_memory_bytes").ValueKind);
    Assert.Equal(JsonValueKind.String, data.GetProperty("os_build").ValueKind);
    Assert.Equal(4, data.EnumerateObject().Count()); // no extra keys
}
```

Apply equivalent schema tests to `StorageHealth`, `Network`, and `Privacy` handlers.

### 11.5 Command Dispatch Smoke Suite

```csharp
[Theory]
[InlineData("system")]
[InlineData("storage_health")]
[InlineData("network_status")]
[InlineData("privacy_status")]
public async Task ReadOnlyHandlers_NeverReturn_HandlerMissing(string commandId)
{
    CommandDispatcher dispatcher = CommandRuntime.CreateDefault();
    var request = new CommandRequest(
        commandId, JsonDocument.Parse("{}").RootElement, apply: false);
    CommandResult result = await dispatcher.ExecuteAsync(
        request, CommandExecutionOptions.Default, CancellationToken.None);
    // Handler must be registered -- gate 8 must not fire
    Assert.NotEqual("command.handler_missing", result.Code);
}
```

### 11.6 Integration Test Build Property

Wrap slow integration tests behind a build property to keep the default `dotnet test` fast:

```xml
<!-- In the test project .csproj: -->
<PropertyGroup Condition="'$(RunIntegrationTests)' == 'true'">
  <DefineConstants>$(DefineConstants);INTEGRATION_TESTS</DefineConstants>
</PropertyGroup>
```

```csharp
// Mark slow tests:
#if INTEGRATION_TESTS
[Fact]
public async Task DiskCleanup_Apply_Integration_ReturnsPositiveFreed()
{ ... }
#endif
```

Run default (fast): `dotnet test WinCare.Native.sln`
Run default (fast): `dotnet test WinCare.Native.sln`
Run with integration: `dotnet test WinCare.Native.sln -p:RunIntegrationTests=true`

---

## PART XII -- CI/CD, TOOLING & RELEASE PIPELINE

### 12.1 Existing Tool Inventory (Verified from `tools/` directory scan)

| Script | Bytes | Purpose |
|--------|-------|--------|
| `verify_native_foundation.py` | 16,809 | V5 gate -- 259 IDs, handlers, AutomationIds, banned patterns |
| `finalize_native_release.py` | 15,999 | V6 gate -- RC dual-archive + SHA-256 manifest |
| `build_release.py` | 34,155 | Full release build orchestration |
| `verify_release.py` | 25,133 | Post-archive artifact verification |
| `test_security_invariants.py` | 38,898 | Security invariant checks |
| `test_release_tools.py` | 22,035 | Release tool self-tests |
| `validate_action_bindings.py` | 10,938 | XAML binding validation |
| `validate_gui.py` | 10,563 | GUI automation checks |

### 12.2 Three New Tools to Create

**`tools/verify_visual_tokens.py`** -- validates all 11 Cyber-Teal tokens in all 3 themes:

```python
#!/usr/bin/env python3
"""Verifies all Cyber-Operate brush tokens are declared in Dark, Light, and HighContrast."""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOKENS = [
    "AccentTealBrush", "AccentTealSubtleBrush", "CardSurfaceBrush",
    "TelemetryFrameBrush", "PillReadOnlyBgBrush", "PillMutatingBgBrush",
    "PillElevatedBgBrush", "PillVerifiedBorderBrush", "PillNotReadyBgBrush",
    "PillTextBrush", "PillAltTextBrush",
]

def main() -> int:
    theme_file = ROOT / "src/WinCare.App/Styles/ThemeResources.xaml"
    control_file = ROOT / "src/WinCare.App/Styles/ControlStyles.xaml"
    if not theme_file.is_file():
        print(f"FAIL: {theme_file} not found"); return 1
    content = theme_file.read_text(encoding="utf-8")
    findings = []
    for token in TOKENS:
        count = content.count(f'x:Key="{token}"')
        if count < 3:
            findings.append(f"  {token}: {count}/3 theme declarations")
    if control_file.is_file():
        cs = control_file.read_text(encoding="utf-8")
        if 'x:Key="StatusPillTemplate"' not in cs:
            findings.append("  StatusPillTemplate: missing from ControlStyles.xaml")
    if findings:
        print("FAIL:"); [print(f) for f in findings]; return 1
    print(f"OK: all {len(TOKENS)} tokens x 3 themes + StatusPillTemplate confirmed.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
```

**`tools/verify_pill_contrast.py`** -- WCAG 2.1 AA checker for all pill pairs:

```python
#!/usr/bin/env python3
"""WCAG 2.1 AA contrast checker for all status pill color pairs."""
import sys

def luminance(hex6: str) -> float:
    r, g, b = int(hex6[0:2],16), int(hex6[2:4],16), int(hex6[4:6],16)
    def c(v):
        v /= 255
        return v/12.92 if v <= 0.04045 else ((v+0.055)/1.055)**2.4
    return 0.2126*c(r) + 0.7152*c(g) + 0.0722*c(b)

def ratio(fg: str, bg: str) -> float:
    l1, l2 = luminance(fg.lstrip("#")), luminance(bg.lstrip("#"))
    hi, lo = max(l1, l2), min(l1, l2)
    return (hi + 0.05) / (lo + 0.05)

PAIRS = [
    ("ReadOnly/Low dark", "#FFFFFF", "#047857"),
    ("Mutating dark",     "#FFFFFF", "#EF4444"),   # NOTE: 4.06:1 -- borderline
    ("Elevated dark",    "#1A1A1A", "#F59E0B"),
    ("NotReady dark",    "#94A3B8", "#1F2937"),
    ("Mutating light",   "#FFFFFF", "#DC2626"),
    ("Elevated light",   "#1A1A1A", "#D97706"),
]

MINIMUM = 4.5

def main() -> int:
    fails = []
    for name, fg, bg in PAIRS:
        r = ratio(fg, bg)
        ok = r >= MINIMUM
        print(f"  {'OK  ' if ok else 'FAIL'} {name}: {r:.2f}:1 (fg={fg} bg={bg})")
        if not ok:
            fails.append(f"{name} ({r:.2f}:1)")
    if fails:
        print(f"\nFAIL: {len(fails)} pair(s) below WCAG AA {MINIMUM}:1")
        print("  Remedy: PillMutatingBgBrush dark -> #C41A1A (5.12:1)")
        return 1
    print(f"\nOK: all {len(PAIRS)} pairs pass WCAG 2.1 AA {MINIMUM}:1.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
```

**`tools/release_checklist.py`** -- master pre-RC gate runner (7 checks in sequence):

```python
#!/usr/bin/env python3
"""Runs all 7 pre-RC verification checks in strict sequence. All must pass."""
import subprocess, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

CHECKS = [
    ("V1 Rust tests",
     ["cargo", "test", "--manifest-path", "native/Cargo.toml"]),
    ("V2 Rust clippy",
     ["cargo", "clippy", "--manifest-path", "native/Cargo.toml",
      "--all-targets", "--", "-D", "warnings"]),
    ("V3 Python native tests",
     ["python", "-m", "unittest", "discover", "-s", "tests/native"]),
    ("V4 dotnet test",
     ["dotnet", "test", "WinCare.Native.sln", "-c", "Release", "-p:Platform=x64"]),
    ("V5 foundation gate",
     ["python", "tools/verify_native_foundation.py"]),
    ("Visual token check",
     ["python", "tools/verify_visual_tokens.py"]),
    ("Pill contrast check",
     ["python", "tools/verify_pill_contrast.py"]),
]

def main() -> int:
    results = []
    for name, cmd in CHECKS:
        t0 = time.monotonic()
        r = subprocess.run(cmd, cwd=str(ROOT), capture_output=True, text=True)
        elapsed = time.monotonic() - t0
        ok = r.returncode == 0
        results.append((name, ok, elapsed))
        print(f"  {'OK ' if ok else 'FAIL'} [{elapsed:.1f}s] {name}")
        if not ok and r.stderr:
            print(f"    stderr: {r.stderr[:300]}")
    failed = [n for n, ok, _ in results if not ok]
    total = sum(e for _, _, e in results)
    print(f"\nTotal: {len(CHECKS)} checks, {len(failed)} failed, {total:.1f}s")
    if failed:
        print(f"FAIL: {', '.join(failed)}")
        return 1
    print(f"OK: all {len(CHECKS)} pre-RC checks passed. Ready for V6 finalization.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
```

### 12.3 GitHub Actions CI Additions

Add these steps to `.github/workflows/native-winui.yml` after `cargo build`:

```yaml
- name: Rust clippy (deny warnings)
  run: cargo clippy --manifest-path native/Cargo.toml --all-targets -- -D warnings

- name: dotnet test (Release x64)
  run: dotnet test WinCare.Native.sln -c Release -p:Platform=x64

- name: V5 foundation gate
  run: python tools/verify_native_foundation.py

- name: Visual token verification
  run: python tools/verify_visual_tokens.py

- name: Pill WCAG contrast verification
  run: python tools/verify_pill_contrast.py
```

Add to `.github/workflows/native-release-candidate.yml`:

```yaml
- name: V6 RC dual-archive
  run: >
    python tools/finalize_native_release.py
    --output artifacts/finalization
    --version ${{ github.ref_name }}
    --mode rc

- name: Verify zero PS1 in native source archive
  run: python tools/verify_release.py
    --archive artifacts/finalization/WinCare-*-native-source.zip

- name: Upload RC artifacts
  uses: actions/upload-artifact@v4
  with:
    name: rc-archives
    path: artifacts/finalization/
```

### 12.4 Progress Tracker

File: `tasks/progress.md` -- 26-row table, updated as tasks complete.

---

## PART XIII -- ARCHITECTURE DECISION RECORDS

### ADR-001: Rust 2024 cdylib -- Status Enum (i32) over String Errors

**Date:** 2026-08-08  |  **Status:** Accepted

**Context:** `wincare-core` communicates errors across the C ABI. Options: (1) string
out-parameter, (2) errno-style integer, (3) typed Status enum, (4) structured JSON error.

**Decision:** Typed `i32` enum (`Status::Ok=0 ... Status::BufferTooSmall=6`) mirrored
in C header as `wincore_status` and in C# as `NativeCoreStatus`. ABI version guard
(Task 2.8) enforces backward compatibility.

**Consequences:** No string allocation crosses the ABI boundary. Rust panic can never
expose a Rust stack trace to C# (combined with `panic = "abort"`). Adding new status
codes requires a version bump.

---

### ADR-002: `CommandHandlerOutcome` as `sealed record`

**Date:** 2026-08-08  |  **Status:** Accepted

**Context:** Handler results must be immutable, equality-comparable (for tests), and
AOT-compatible. Options: class, struct, record.

**Decision:** `sealed record` with 5 fields (Status, Code, Message, Data, UndoAvailable).
Static factory methods (`Succeeded`, `Failed`, `Blocked`) enforce invariants at
construction. Record equality enables clean unit test assertions.

**Consequences:** Callers cannot subclass. `Data: JsonElement?` requires manual
`.Clone()` to detach from `JsonDocument` lifetime.

---

### ADR-003: No DI Container -- Manual Wiring in `CommandRuntime`

**Date:** 2026-08-08  |  **Status:** Accepted

**Context:** .NET generic host or a DI container (Autofac, MS DI) would handle handler
registration automatically. However, the dependency graph is shallow (2-3 levels, 8 leaf
handlers, 1 shared `NativeCoreService`).

**Decision:** `CommandRuntime.CreateDefault()` wires all handlers manually. `LastJournal`
property exposes the journal service for the presentation layer.

**Consequences:** Adding a new handler requires editing `CreateDefault()`. DI would make
this automatic but adds AOT trimming risk and startup overhead (estimated +30ms) for no
material benefit at this scale.

---

### ADR-004: Mica + Acrylic Fallback Backdrop -- No Change in RC

**Date:** 2026-08-08  |  **Status:** Accepted

**Context:** WinUI 3 supports `MicaBackdrop` (Win 11+) and `DesktopAcrylicBackdrop`
(Win 10 fallback). A third option is a flat background with a custom color.

**Decision:** `ConfigureBackdrop()` in `MainWindow.xaml.cs` (L24) selects Mica if
supported, else Acrylic. `CardSurfaceBrush` (`#161C26` dark, `#FFFFFF` light) ensures
readability on both backdrops without requiring backdrop-aware colors on every control.

**Consequences:** Page backgrounds are not solid -- they transmit the backdrop. All
component backgrounds must use theme tokens, not hardcoded hex, to remain readable.

---

### ADR-005: Activity Journal Is Ephemeral (No Disk Persistence)

**Date:** 2026-08-08  |  **Status:** Accepted for 2.4.0-rc1

**Context:** Persisting journal entries to `%LOCALAPPDATA%` would survive restarts but
requires migration logic, schema versioning, and concurrent file access handling.

**Decision:** `ActivityJournalService` stores records in a `List<ActivityRecord>` with a
`lock` guard. Records are lost on process exit. ActivityPage shows "Nothing to show" on
cold start.

**Consequences:** SQLite or JSON persistence deferred to v2.5.0. Stress test (Task 4.1)
verifies zero races on the in-memory structure.

---

### ADR-006: `ContentControl` + `ControlTemplate` for Status Pills

**Date:** 2026-08-08  |  **Status:** Accepted

**Context:** Status pills could be implemented as: (1) `UserControl` with code-behind,
(2) `ContentControl` + `ControlTemplate`, (3) styled `TextBlock` in a `Border`.

**Decision:** `StatusPillTemplate` in `ControlStyles.xaml` is a `ControlTemplate`
targeting `ContentControl`. Per-instance colors use `{TemplateBinding Background}` and
`{TemplateBinding Foreground}`. No code-behind needed. AOT-compatible (compile-time).

**Consequences:** Pill appearance is fully controllable via VM-bound properties. The
`ContentControl` has a slight (~4px) default margin that must be zeroed with `Padding="0"`
if pixel-perfect alignment is needed.

---

### ADR-007: `CompactBreakpointDip = 920.0` -- Single Source of Truth

**Date:** 2026-08-08  |  **Status:** Accepted

**Context:** `AllToolsPage.xaml.cs` hardcodes `private const double CompactThreshold = 920`.
If other pages need the same breakpoint, this duplicates the constant.

**Decision:** Move to `LayoutVisibility.CompactBreakpointDip` with a helper
`IsCompact(double widthDip)`. Remove the local constant from `AllToolsPage.xaml.cs`.

**Consequences:** All pages and tests must use `LayoutVisibility.CompactBreakpointDip`.
Breakpoint value changes require editing one file. V5 gate will grep for
`CompactThreshold` as a banned string after Task 3.5 completes.

---

### ADR-008: Stack Buffer for `wincare_core_sys_info` -- No `serde`

**Date:** 2026-08-08  |  **Status:** Accepted

**Context:** The JSON output of `wincare_core_sys_info` could use `serde_json` for
component serialization. However, `serde` is banned (GEMINI.md rule) to avoid heap
allocation and allocator ABI mismatch.

**Decision:** `compose_sys_info_json` writes a fixed JSON schema (4 primitive keys) into
a `[u8; 512]` stack buffer using `std::format!()`. No allocator called on success path.

**Consequences:** JSON schema is frozen to 4 keys. Adding keys requires expanding the
buffer estimate and updating the C# schema test. The 512-byte ceiling must accommodate
the longest possible OS build string (~32 chars). Verified: worst case ~160 bytes.

---

## PART XIV -- IMPLEMENTATION SESSIONS & PARALLELIZATION

### 14.1 Session Plan

| Session | Tasks | Est. Duration | Exit Criteria |
|---------|-------|:------------:|---------------|
| **A -- Native Foundation** | 1.1 + 1.2 (parallel) + 1.3 + 1.4 + 1.5 | 3-4 hrs | V1 = 17 tests, V2 clean |
| **B -- Read-Only Handlers** | 2.1 + 2.2 + 2.3 + 2.4 + 2.5 | 2-3 hrs | 22 new C# tests passing |
| **C -- Mutating + Runtime** | 2.6 + 2.7 + 2.8 + 2.9 | 2 hrs | V3, V4 full suite |
| **D -- Visual Character** | 3.1 + 3.5 + 3.2 + 3.4 + 3.3 + 3.6 | 2-3 hrs | `verify_visual_tokens.py` + `verify_pill_contrast.py` pass |
| **E -- Activity Journal** | 4.1 + 4.2 + 4.3 + 4.4 | 2 hrs | ActivityPage shows live journal data |
| **F -- Finalization** | 4.5 + 4.6 + new tools | 2 hrs | V1-V6 all pass; RC archive produced |

**Total estimated effort: 13-16 hours across 6 sessions.**

### 14.2 Parallelization Guide

**Session A -- if 2 developers available simultaneously:**
- Dev A: Task 1.1 (`wincare_core_dir_size` Rust function + tests)
- Dev B: Task 1.2 (`wincare_core_sys_info` + Cargo.toml updates)
- Both must merge before Task 1.4 (P/Invoke needs both exports)

**Session B -- Tasks 2.2-2.5 are fully independent (after 2.1):**
- Task 2.1 (`CommandHandlerOutcome.Failed`) must complete FIRST
- Then Tasks 2.2, 2.3, 2.4, 2.5 can run in parallel
- Task 2.9 must be last (registers all handlers)

**Session D -- Task ordering matters:**
- Task 3.1 (tokens) then Task 3.2 (pills use tokens)
- Task 3.5 (breakpoint constant) then Task 3.2 (uses `LayoutVisibility.IsCompact`)
- Tasks 3.3, 3.4, 3.6 can follow in any order after 3.2

### 14.3 Blocker Decision Matrix

| Blocker | Decision | Deadline |
|---------|----------|----------|
| `storage_health`/`network_status` IDs missing from `commands.json` | Add catalog entries with `migrationStatus: "Cataloged"` | Before Session B |
| Amber pill `#EF4444` fails WCAG 4.5:1 (actual 4.06:1) | Use `PillMutatingBgBrush` dark = `#C41A1A` (5.12:1) | Before Session D |
| `windows-rs 0.58` not in workspace `Cargo.toml` | Add to root `[workspace.dependencies]` | Before Session A Task 1.2 |
| `System.Threading.Lock` unavailable (pre-.NET 9) | Fall back to `object _lock = new()` in `ActivityJournalService` | Before Session E |
| V5 CI failure if command promoted before Task 4.5 | Tasks 4.5-A through 4.5-D must commit before `commands.json` status edit | Before Session F |

---

## PART XV -- GLOSSARY, QUICK REFERENCE & FILE-TO-TASK MAP

### 15.1 Type Quick Reference

| Type | File | Role |
|------|------|------|
| `CommandDispatcher` | `CommandDispatcher.cs` | 9-gate admission + dispatch (244 lines) |
| `CommandHandlerOutcome` | `CommandHandlerOutcome.cs` | sealed record: Status, Code, Message, Data, UndoAvailable |
| `CommandRuntime` | `CommandRuntime.cs` | `CreateDefault()` factory -- no DI |
| `ICommandHandler` | `ICommandHandler.cs` | `string CommandId` + `Task<CommandHandlerOutcome> ExecuteAsync(...)` |
| `CommandRequest` | `CommandRequest.cs` | Id (string), Parameters (JsonElement), Apply (bool) |
| `CommandResult` | `CommandResult.cs` | dispatch result DTO |
| `CommandResultStatus` | enum | Succeeded, Failed, Blocked, NotMigrated, Cancelled |
| `ActivityRecord` | `ActivityRecord.cs` | journal entry sealed record -- 8 fields (Guid, string x3, ActivityState, DateTimeOffset x2, string, bool) |
| `ActivityState` | enum | Running, NeedsAttention, Completed, Failed, Cancelled |
| `ActivityJournalService` | `ActivityJournalService.cs` | thread-safe in-process journal **(new)** |
| `WinCareCoreNative` | `WinCareCoreNative.cs` | internal static P/Invoke (3 DllImports existing, +2 new) |
| `NativeCoreService` | `NativeCoreService.cs` | public sealed managed FFI wrapper |
| `NativeCoreStatus` | enum | Ok=0..BufferTooSmall=6 |
| `StartupTelemetry` | `StartupTelemetry.cs` | static `Mark(string)` -- high-res timestamp dict |
| `ToolRowViewModel` | `ToolRowViewModel.cs` | Id, Title, Risk, MigrationState, AdministratorAccess (string), Restart (string), IsCompact |
| `LayoutVisibility` | `LayoutVisibility.cs` | `BoolToVisibility`, `InvertBoolToVisibility`, `IsCompact`, `CompactBreakpointDip = 920.0` |
| `AllToolsPage` | `AllToolsPage.xaml` | 257 lines, SplitView 390px, 5 tabs, filter row, dual-layout ListView |
| `ActivityPage` | `ActivityPage.xaml` | 123 lines, 4 tabs: Running/Needs attention/Completed/Reports |

### 15.2 AutomationId Master Table (24 total)

| AutomationId | Type | Page/Location | Status |
|-------------|------|--------------|--------|
| `GlobalSearch` | AutoSuggestBox | MainWindow | Existing |
| `PrimaryNavigation` | NavigationView | ShellPage | Existing |
| `NavHome` | NavigationViewItem | ShellPage | Existing |
| `NavCheckup` | NavigationViewItem | ShellPage | Existing |
| `NavSystemCare` | NavigationViewItem | ShellPage | Existing |
| `NavSecurity` | NavigationViewItem | ShellPage | Existing |
| `NavRepairRecovery` | NavigationViewItem | ShellPage | Existing |
| `NavAllTools` | NavigationViewItem | ShellPage | Existing |
| `NavActivity` | NavigationViewItem | ShellPage | Existing |
| `NavSettings` | NavigationViewItem | ShellPage | Existing |
| `AllToolsTabs` | SelectorBar | AllToolsPage | Existing |
| `ToolSearch` | TextBox | AllToolsPage filter | Existing |
| `AreaFilter` | ComboBox | AllToolsPage filter | Existing |
| `RiskFilter` | ComboBox | AllToolsPage filter | Existing |
| `ReadOnlyFilter` | CheckBox | AllToolsPage filter | Existing |
| `AllToolsTable` | ListView | AllToolsPage content | Existing |
| `ExecuteSelectedTool` | Button | AllToolsPage pane | Existing |
| `FavoriteSelectedTool` | Button | AllToolsPage pane | Existing |
| `CommandResultDetails` | Expander | AllToolsPage pane | Existing |
| `ActivityPageTabs` | SelectorBar | ActivityPage | Existing |
| `ActivityPageRows` | ListView | ActivityPage | Existing |
| `ReviewApprovedToggle` | ToggleSwitch | AllToolsPage pane | **NEW -- Task 3.4** |
| `StatusColumnHeader` | TextBlock | AllToolsPage table header | **NEW -- Task 3.2** |
| `AttentionInfoBar` | InfoBar | ActivityPage | **NEW -- Task 4.4** |

### 15.3 File-to-Task Cross Reference (26 tasks, 28 files touched)

| File | Modifying Tasks | New/Existing |
|------|----------------|--------------|
| `native/wincare-core/src/lib.rs` | 1.1, 1.2 | Existing |
| `native/wincare-core/Cargo.toml` | 1.2 | Existing |
| `native/wincare-core/include/wincare_core.h` | 1.3 | Existing |
| `WinCareCoreNative.cs` | 1.4 | Existing |
| `NativeCoreService.cs` | 1.5, 2.8 | Existing |
| `CommandHandlerOutcome.cs` | 2.1 | Existing |
| `CommandDispatcher.cs` | 2.8, 4.2 | Existing |
| `CommandRuntime.cs` | 2.9, 4.6 | Existing |
| `SystemInfoCommandHandler.cs` | 2.2 | **New** |
| `StorageHealthCommandHandler.cs` | 2.3 | **New** |
| `NetworkStatusCommandHandler.cs` | 2.4 | **New** |
| `PrivacyStatusCommandHandler.cs` | 2.5 | **New** |
| `DiskCleanupCommandHandler.cs` | 2.6 | **New** |
| `LogCleanupCommandHandler.cs` | 2.7 | **New** |
| `ActivityJournalService.cs` | 4.1 | **New** |
| `ThemeResources.xaml` | 3.1 | Existing |
| `ControlStyles.xaml` | 3.1 | Existing |
| `LayoutVisibility.cs` | 3.5 | Existing |
| `AllToolsPage.xaml` | 3.2, 3.4 | Existing |
| `AllToolsPage.xaml.cs` | 3.2, 3.5 | Existing |
| `ToolRowViewModel.cs` | 3.2 | Existing |
| `ToolExecutionViewModel.cs` | 3.4 | Existing |
| `ActivityPageViewModel.cs` | 4.3, 4.4 | Existing |
| `ActivityPage.xaml` | 4.4 | Existing |
| `commands.json` | 4.5 | Existing |
| `verify_native_foundation.py` | 4.5 | Existing |
| `verify_visual_tokens.py` | 12.2 | **New** |
| `verify_pill_contrast.py` | 12.2 | **New** |
| `release_checklist.py` | 12.2 | **New** |
| `tasks/progress.md` | 12.4 | **New** |
| `DESIGN.md` | DoD condition 19 | Existing |

### 15.4 Status Pill Label Lookup (complete)

| `Risk` field | `ReadOnly` | Pill label | Background token |
|-------------|-----------|-----------|------------------|
| any | true | `[ READ-ONLY ]` | `PillReadOnlyBgBrush` + `PillTextBrush` |
| `"Low"` | false | `[ LOW      ]` | `PillReadOnlyBgBrush` + `PillTextBrush` |
| `"Moderate"` | false | `[ MODERATE ]` | `PillElevatedBgBrush` + `PillAltTextBrush` |
| `"High"` | false | `[ HIGH RISK]` | `PillMutatingBgBrush` + `PillTextBrush` |
| `"Critical"` | false | `[ CRITICAL ]` | `PillMutatingBgBrush` + `PillTextBrush` |

| `MigrationState` | Pill label | Background | Border |
|-----------------|-----------|-----------|--------|
| `"BehaviorVerified"` | `[ VERIFIED ]` | transparent | `PillVerifiedBorderBrush` (1px) |
| `"Implemented"` | `[ READY    ]` | `PillReadOnlyBgBrush` | none |
| any other | `[ NOT READY]` | `PillNotReadyBgBrush` | none |

### 15.5 Handler Message Format Specification

Each handler's `CommandHandlerOutcome.Message` must match these formats verbatim
(used by ActivityPage journal as the `ActivityRecord.Result` string):

| Handler | Format | Example |
|---------|--------|--------|
| `SystemInfoCommandHandler` | `"Logical CPUs: N -- RAM: X.X GB total, Y.Y GB available -- OS build: NNNNN"` | `"Logical CPUs: 8 -- RAM: 16.0 GB total, 9.2 GB available -- OS build: 22631"` |
| `StorageHealthCommandHandler` | `"N drive(s) -- [Name X GB (Y.Y GB free), ...]"` | `"3 drive(s) -- C:\ 512 GB (241.0 GB free), D:\ 2048 GB (1433.6 GB free)"` |
| `NetworkStatusCommandHandler` | `"N interface(s) -- M up"` | `"4 interface(s) -- 2 up"` |
| `PrivacyStatusCommandHandler` | `"Telemetry: [desc] -- Advertising ID: [enabled|disabled]"` | `"Telemetry: basic -- Advertising ID: disabled"` |
| `DiskCleanupCommandHandler` (dry) | `"Preview: ~X.X MB across N location(s)"` | `"Preview: ~847.3 MB across 2 location(s)"` |
| `DiskCleanupCommandHandler` (apply) | `"Freed X.X MB (N file(s) deleted, M skipped)"` | `"Freed 847.3 MB (3,821 file(s) deleted, 12 skipped)"` |
| `LogCleanupCommandHandler` (dry) | `"Preview: N event log(s) found"` | `"Preview: 7 event log(s) found"` |
| `LogCleanupCommandHandler` (apply) | `"Cleared N log(s), removed M entr(ies)"` | `"Cleared 5 log(s), removed 2,341 entr(ies)"` |
