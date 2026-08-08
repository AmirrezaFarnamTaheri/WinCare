# WinCare 2.4.0-rc1 — Task Progress Tracker

**Milestone:** 2.4.0-rc1  
**DoD:** All 26 rows marked `[x]` + all 20 binary conditions in `tasks/plan.md` Part VIII pass.

> Update this file manually as each task completes. This is DoD condition 18.

---

## Phase 1 — Rust Native ABI Expansion

| Status | Task | File(s) Modified | Exit Criterion |
|--------|------|-----------------|----------------|
| `[x]` | **1.1** `wincare_core_dir_size` Rust impl + 5 tests | `lib.rs` | `cargo test` → 13 tests pass |
| `[x]` | **1.2** `wincare_core_sys_info` Rust impl + 4 tests + `windows-rs` dep | `lib.rs`, `Cargo.toml` | `cargo test` → 17 tests pass |
| `[x]` | **1.3** C header declarations for both new exports | `wincare_core.h` | Header parseable by `cffi` |
| `[x]` | **1.4** P/Invoke `DllImport` for both new functions | `WinCareCoreNative.cs` | Compiles Release x64 |
| `[x]` | **1.5** `NativeCoreService` wrapper methods + XML docs | `NativeCoreService.cs` | `DirSize()` / `SysInfo()` return correct results |

## Phase 2 — C# Command Handlers & Runtime

| Status | Task | File(s) Modified | Exit Criterion |
|--------|------|-----------------|----------------|
| `[x]` | **2.1** `CommandHandlerOutcome.Failed` factory + `Data?` field | `CommandHandlerOutcome.cs` | Existing tests unchanged; 3 new tests pass |
| `[x]` | **2.2** `SystemInfoCommandHandler` (read-only, native) | `SystemInfoCommandHandler.cs` (new) | Unit test: `Succeeded`, message format matches §15.5 |
| `[x]` | **2.3** `StorageHealthCommandHandler` (read-only, WMI) | `StorageHealthCommandHandler.cs` (new) | Unit test: 3 drives enumerated |
| `[x]` | **2.4** `NetworkStatusCommandHandler` (read-only) | `NetworkStatusCommandHandler.cs` (new) | Unit test: adapters enumerated |
| `[x]` | **2.5** `PrivacyStatusCommandHandler` (read-only, registry) | `PrivacyStatusCommandHandler.cs` (new) | Unit test: reads telemetry level |
| `[x]` | **2.6** `DiskCleanupCommandHandler` (mutating, dry + apply) | `DiskCleanupCommandHandler.cs` (new) | Dry-run: `Preview:` message; apply: `Freed:` message |
| `[x]` | **2.7** `LogCleanupCommandHandler` (mutating, dry + apply) | `LogCleanupCommandHandler.cs` (new) | Dry-run: `Preview:` message; apply: `Cleared:` message |
| `[x]` | **2.8** ABI version guard in `NativeCoreService` + `CommandDispatcher` | `NativeCoreService.cs` | Guard throws `InvalidOperationException` when mismatch |
| `[x]` | **2.9** `CommandRuntime.CreateDefault()` wires all 8 handlers | `CommandRuntime.cs` | All 8 `CommandId` values resolvable |

## Phase 3 — Visual Character & WinUI 3 Ergonomics

| Status | Task | File(s) Modified | Exit Criterion |
|--------|------|-----------------|----------------|
| `[ ]` | **3.1** 11 Cyber-Teal brush tokens in all 3 theme dicts | `ThemeResources.xaml`, `ControlStyles.xaml` | `verify_visual_tokens.py` exits 0 |
| `[ ]` | **3.2** Status pill column in AllToolsPage (wide + compact) | `AllToolsPage.xaml`, `AllToolsPage.xaml.cs`, `ToolRowViewModel.cs` | Pill visible in both layouts |
| `[ ]` | **3.3** Ctrl+K focus shortcut → `ToolSearch` TextBox | `AllToolsPage.xaml.cs` | `KeyboardAccelerator` fires; focus moves |
| `[ ]` | **3.4** `ReviewApprovedToggle` ToggleSwitch + `IsMutatingTool` binding | `AllToolsPage.xaml`, `ToolExecutionViewModel.cs` | Toggle visible only when mutating tool selected |
| `[ ]` | **3.5** `LayoutVisibility.IsCompact(double)` + remove local `CompactThreshold` | `LayoutVisibility.cs`, `AllToolsPage.xaml.cs` | `grep -r CompactThreshold` returns 0 matches |
| `[ ]` | **3.6** A11y audit: 24 AutomationId strings verified + 3 new added | `AllToolsPage.xaml`, `ActivityPage.xaml` | V5 XML scan passes |

## Phase 4 — Activity Journal, Observability & Finalization

| Status | Task | File(s) Modified | Exit Criterion |
|--------|------|-----------------|----------------|
| `[ ]` | **4.1** `ActivityJournalService` (in-memory, thread-safe) + stress test | `ActivityJournalService.cs` (new) | Concurrent 5×5 test: zero races |
| `[ ]` | **4.2** `CommandDispatcher` integration: begin/complete journal entries | `CommandDispatcher.cs` | Journal populated after dispatch |
| `[ ]` | **4.3** `ActivityPageViewModel` binds to `IActivityJournalService` | `ActivityPageViewModel.cs` | ViewModel exposes `ObservableCollection<ActivityRecord>` |
| `[ ]` | **4.4** `AttentionInfoBar` in `ActivityPage.xaml` (visible when NeedsAttention) | `ActivityPage.xaml`, `ActivityPageViewModel.cs` | InfoBar shows/hides correctly |
| `[ ]` | **4.5** V5 gate extended: 8 command IDs, `CompactThreshold` ban, 3 new AutomationIds | `verify_native_foundation.py` | V5 exits 0 after all Phase 1-3 tasks |
| `[ ]` | **4.6** `CommandRuntime.LastJournal` property exposed | `CommandRuntime.cs` | `ShellPage` can access journal service |
| `[ ]` | **4.7** RC finalization: `tasks/progress.md` all `[x]`, `DESIGN.md` updated | `DESIGN.md`, `tasks/progress.md` | V6 archive exits 0; DoD conditions 18-20 pass |

## New Tooling

| Status | Task | File | Exit Criterion |
|--------|------|------|----------------|
| `[ ]` | Create `tools/verify_visual_tokens.py` | `tools/verify_visual_tokens.py` (new) | Script runs; exits 0 after Task 3.1 |
| `[ ]` | Create `tools/verify_pill_contrast.py` | `tools/verify_pill_contrast.py` (new) | Script runs; exits 0 with `#C41A1A` bg |
| `[ ]` | Create `tools/release_checklist.py` | `tools/release_checklist.py` (new) | Script runs all 7 checks sequentially |

---

## DoD Summary (20 conditions)

Track these separately — each must be ✅ before milestone closes:

| # | Condition | Status |
|---|-----------|--------|
| 1 | 17 Rust tests | `[ ]` |
| 2 | Zero clippy warnings | `[ ]` |
| 3 | 4 Python interop files | `[ ]` |
| 4 | 57 new C# managed tests | `[ ]` |
| 5 | V5 gate exits 0 | `[ ]` |
| 6 | V6 RC archive exits 0 | `[ ]` |
| 7 | Zero PS1 in archive | `[ ]` |
| 8 | 8 commands Implemented in commands.json | `[ ]` |
| 9 | 11 tokens × 3 themes | `[ ]` |
| 10 | StatusPillTemplate in ControlStyles | `[ ]` |
| 11 | All pill pairs ≥ 4.5:1 WCAG AA | `[ ]` |
| 12 | 24 AutomationIds present | `[ ]` |
| 13 | ReviewApprovedToggle IsMutatingTool only | `[ ]` |
| 14 | Journal stress test zero races | `[ ]` |
| 15 | AttentionInfoBar shows/hides correctly | `[ ]` |
| 16 | CompactBreakpointDip single source of truth | `[ ]` |
| 17 | panic = "abort" in Cargo.toml | `[ ]` |
| 18 | tasks/progress.md all 26 tasks [x] | `[ ]` |
| 19 | DESIGN.md updated with 4 token refs | `[ ]` |
| 20 | release_checklist.py exits 0 | `[ ]` |
