# WinCare Multi-Domain Review & Remediation Checklist

## Phase 1: Foundation & Static Mechanical Verification
- [x] **Task 1.1**: Verify Native Foundation & Command Parity (`python tools/verify_native_foundation.py`) — **PASS** (259 commands matched)
- [x] **Task 1.2**: Execute Python Native Test Suite & Safety Regression Gates (`python -m unittest discover -s tests/native -v`) — **PASS** (56/56 tests passed)
- [x] **Task 1.3**: Verify Visual Design Tokens & WCAG 2.1 AA Status Pill Contrast (`python tools/verify_visual_tokens.py && python tools/verify_pill_contrast.py`) — **PASS** (33 tokens & 8 pill pairs verified)
- [x] **Checkpoint 1**: Foundation Baseline Verified

## Phase 2: Rust Native Core & FFI Memory Safety Audit
- [x] **Task 2.1**: Rust Core & Guard Test Suite (`cargo test --manifest-path native/Cargo.toml`) — **PASS** (29/29 tests passed)
- [x] **Task 2.2**: Rust Clippy Lints & Formatting Compliance (`cargo clippy` & `cargo fmt`) — **PASS** (0 warnings under `-D warnings`)
- [x] **Task 2.3**: FFI Exception Boundaries (`catch_unwind`) & Unsafe Invariants Review — **PASS** (Caller-owned buffers & panic unwinds safe)
- [x] **Checkpoint 2**: Native FFI & Memory Safety Verified

## Phase 3: OS Command Executors & Security Invariants Audit
- [x] **Task 3.1**: Audit Filesystem Operations & Reparse Point Protection (no `SearchOption.AllDirectories`, reparse skipping) — **PASS**
- [x] **Task 3.2**: Audit Process Execution & Argument Injection Immunity (`BoundedProcessRunner.cs`) — **PASS**
- [x] **Task 3.3**: Temporary Security Control & Rollback Gate Audit (`WindowsCommandExecutor.Security.cs`) — **PASS**
- [x] **Checkpoint 3**: OS Security & Mutation Boundary Verified

## Phase 4: Plugin Subsystem, Package Admission & Security Invariants (PR #22 Findings 1–12)
- [x] **Finding 1 & 9**: Explicit full-trust in-process execution disclosure & declared capabilities consent in `PluginDetailDialog.xaml`.
- [x] **Finding 2**: Dynamic command registration in `ICommandDispatcher` / `CommandDispatcher` + `PluginScriptCommandHandler`.
- [x] **Finding 3**: Enforced package admission equality `manifest.Id == targetPluginId` in `PluginInstallerService.cs`.
- [x] **Finding 4**: Mandatory stream SHA-256 digest validation in `PluginInstallerService.cs`.
- [x] **Finding 5 & 6**: Core namespace collision rejection (`wincare.core.*`, `system.*`), isolated `.staging/backups/`, and backup exclusion during discovery in `PluginRegistryService.cs` & `ToolCatalogService.cs`.
- [x] **Finding 7**: `IPluginRegistry.RegistryChanged` event lifecycle and startup discovery in `AppRuntime.cs`.
- [x] **Finding 8**: Full installed plugin management (Enable, Disable, Uninstall) in `PluginStorePageViewModel.cs` and `PluginStorePage.xaml`.
- [x] **Finding 10**: Fail-closed enum parsing (`Risk`, `AdministratorAccess`, `Restart`) in `PluginManifest.cs`.
- [x] **Finding 11 & 12**: Embedded offline fallback catalog and proper cancellation distinction in `RemoteCatalogService.cs`.
- [x] **Checkpoint 4**: PR #22 Full Architectural Remediation Verified

## Phase 5: Category C Future Roadmap Specifications Implementation
- [x] **Specification 1**: AI System Doctor (`docs/plans/2026-08-14-ai-system-doctor-plan.md`) — Tasks 1.1–3.3 complete (`ModelManager`, `OnnxInferenceEngine`, `IntentTranslator`, `AiDoctorPage`, `IntentTranslatorTests`).
- [x] **Specification 2**: Community Plugin SDK (`docs/plans/2026-08-14-community-plugin-sdk-plan.md`) — CLI scaffold, linter, zip builder, templates & unit tests complete.
- [x] **Specification 3**: Rust Background Health Guard Service (`docs/plans/2026-08-14-rust-background-guard-service-plan.md`) — `wincare-guard` crate, monitors, toast notifications, IPC pipe server & C# client complete.
- [x] **Specification 4**: Architecture Explorations (`docs/ideas/`) — AES-256-GCM Cloud Sync & Roslyn Source-Generated Dispatcher complete.
- [x] **Checkpoint 5**: All Roadmap Backlog Items Complete
