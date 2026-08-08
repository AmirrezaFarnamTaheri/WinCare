# Ultra-Comprehensive Micro-Task Checklist: WinCare Native Parity & Visual Character Upgrade

> Synthesized from all codebase investigations, knowledge graph indexes, Tactile Telemetry UI/UX specifications, backend patterns, code reviews, performance goals, and verification evidence paths.

## Phase 1: Rust Native Core C ABI Expansion & Managed Interop
- [ ] **Task 1.1**: Rust C ABI Primitives (`wincare_core_dir_size` & `wincare_core_sys_info` in `native/wincare-core/src/lib.rs`)
- [ ] **Task 1.2**: Rust C ABI Header Export (`wincare_core.h`)
- [ ] **Task 1.3**: Managed P/Invoke Declaration (`WinCareCoreNative.cs` in `WinCare.Infrastructure`)
- [ ] **Task 1.4**: Native Status Exception Mapper (`NativeCoreStatus.cs` & `NativeCoreService.cs`)

## Checkpoint 1: Rust Core & Infrastructure Verification
- [ ] Rust C ABI test suite passes: `cargo test --manifest-path native/Cargo.toml`
- [ ] Rust clippy passes cleanly: `cargo clippy --manifest-path native/Cargo.toml --all-targets -- -D warnings`
- [ ] Native Python test suite passes: `python -m unittest discover -s tests/native -v`

## Phase 2: Managed C# Command Handlers & Policy Dispatcher
- [ ] **Task 2.1**: `ICommandHandler` Contract & Outcome Model (`CommandHandlerOutcome.cs` & `ICommandHandler.cs`)
- [ ] **Task 2.2**: Read-Only System Diagnostic Handlers (`SystemInfoCommandHandler` & `StorageHealthCommandHandler`)
- [ ] **Task 2.3**: Read-Only Network & Privacy Handlers (`NetworkStatusCommandHandler` & `PrivacyStatusCommandHandler`)
- [ ] **Task 2.4**: Mutating Cleanup Handlers (`DiskCleanupCommandHandler` & `LogCleanupCommandHandler`) with `ReviewApproved` Guards
- [ ] **Task 2.5**: Fail-Closed `CommandDispatcher` Admission Pipeline Integration (`CommandDispatcher.cs`)

## Checkpoint 2: Command Engine & Dispatcher Verification
- [ ] C# solution restores & compiles: `dotnet test WinCare.Native.sln -c Release -p:Platform=x64`
- [ ] Foundation audit passes: `python tools/verify_native_foundation.py`

## Phase 3: Visual Character & WinUI 3 UX Ergonomics Upgrade
- [ ] **Task 3.1**: Tactile Telemetry Theme Tokens & Monospaced Status Pills (`ThemeResources.xaml` & `AllToolsPage.xaml`)
- [ ] **Task 3.2**: Global `Ctrl+K` Search Palette Accelerator in `MainWindow` (`MainWindow.xaml`)
- [ ] **Task 3.3**: Tabular Surface Grid & Monospaced Telemetry Headers in `AllToolsPage.xaml` (`AllToolsPage.xaml`)
- [ ] **Task 3.4**: Tactical Detail Drawer & Command Review Overlay (`AllToolsPage.xaml` & `CheckupPage.xaml`)
- [ ] **Task 3.5**: Responsive Breakpoint Engine (`LayoutVisibility.cs` 640 DIP threshold)
- [ ] **Task 3.6**: Accessibility & High Contrast Resource Dictionary Validation (`ShellPage.xaml` & `AutomationId`)

## Checkpoint 3: Visual Character & Accessibility Verification
- [ ] Foundation gate passes: `python tools/verify_native_foundation.py`
- [ ] Python test suite passes: `python -m unittest discover -s tests/native -v`

## Phase 4: Activity Journaling, Recovery, & Finalization Gates
- [ ] **Task 4.1**: Immutable `ActivityRecord` Logging & Persistence (`ActivityRecord.cs` & `ActivityJournalService.cs`)
- [ ] **Task 4.2**: Real-time `ActivityPageViewModel` Telemetry Progress Banners (`ActivityPageViewModel.cs`)
- [ ] **Task 4.3**: Automated Native Foundation Gate Check (`tools/verify_native_foundation.py`)
- [ ] **Task 4.4**: Deterministic Release Candidate Dual-Archive Finalization (`tools/finalize_native_release.py --mode rc`)

## Final Checkpoint: Release Candidate Promotion
- [ ] Foundation gate passes 100%: `python tools/verify_native_foundation.py`
- [ ] Finalizer exits 0: `python tools/finalize_native_release.py --output artifacts/finalization --version 2.4.0-rc1 --mode rc`
- [ ] Native source archive contains zero `.ps1`, `.psm1`, or `.psd1` files
