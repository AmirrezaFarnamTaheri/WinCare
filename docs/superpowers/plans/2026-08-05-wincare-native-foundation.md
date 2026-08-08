# WinCare Native Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the first buildable native WinCare slice: a WinUI 3 shell with the approved navigation and tabular All tools experience, a typed C# command catalog preserving all 259 IDs, a bounded Rust FFI library, packaging assets, and deterministic migration gates.

**Architecture:** The WinUI project depends on application and catalog libraries, while the domain model remains XAML-free. The Rust crate exposes a versioned C ABI for bounded primitives only. Current PowerShell remains untouched as a temporary parity oracle and is excluded from the new runtime.

**Tech Stack:** .NET 8 / C# 12, WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2, Rust 2024 edition, Cargo, xUnit, Python 3 structural verification, MSIX.

## Global Constraints

- Preserve exactly 259 unique command IDs.
- Do not invoke or embed PowerShell from any native project.
- Do not reference WPF, `PresentationCore`, `PresentationFramework`, or `System.Windows.*`.
- Use `NavigationView` for primary navigation and `SelectorBar` for page sections.
- Use a virtualized `ListView` with aligned `Grid` columns for tabular content.
- Use `x:Bind`; every `DataTemplate` has `x:DataType`.
- ViewModels never reference `Microsoft.UI.Xaml.*`.
- No business logic in code-behind.
- No network or external process work before the first frame.
- Use semantic theme resources for Light, Dark, and HighContrast.
- The original ZIP is the rollback source because the uploaded tree has no `.git` directory.

---

### Task 1: Native migration contract and red structural gate

**Files:**
- Create: `docs/superpowers/specs/2026-08-05-wincare-winui3-csharp-rust-redesign.md`
- Create: `tests/native/test_native_foundation.py`
- Create: `tools/verify_native_foundation.py`

**Interfaces:**
- Consumes: legacy command arrays from `src/WinCare/UI/98-Headless.ps1` and `src/WinCare/UI/97-AdvancedCapabilities.ps1`
- Produces: a deterministic verifier callable as `python3 tools/verify_native_foundation.py`

- [x] Write structural tests that require the new project files, verify 259 unique catalog IDs, reject PowerShell/WPF references in native directories, parse all XML/JSON, and check WinUI accessibility markers.
- [x] Run `python3 -m unittest tests.native.test_native_foundation -v` and confirm failure because the native files do not exist.
- [x] Implement the verifier without creating production projects.
- [x] Run the test again and retain the expected missing-file failures until Task 2.

### Task 2: Exact 259-command migration ledger

**Files:**
- Create: `src/WinCare.CommandCatalog/WinCare.CommandCatalog.csproj`
- Create: `src/WinCare.CommandCatalog/Models/CommandDefinition.cs`
- Create: `src/WinCare.CommandCatalog/CommandCatalog.cs`
- Create: `src/WinCare.CommandCatalog/Data/commands.json`
- Create: `tests/WinCare.CommandCatalog.Tests/WinCare.CommandCatalog.Tests.csproj`
- Create: `tests/WinCare.CommandCatalog.Tests/CommandCatalogTests.cs`
- Create: `docs/migration/command-parity-ledger.md`

**Interfaces:**
- Produces: `CommandCatalog.Load()`, `CommandCatalog.Find(string id)`, and `IReadOnlyList<CommandDefinition>`
- The embedded JSON schema contains stable ID, plain title, summary, area, section, risk, read-only flag, legacy source, and migration status.

- [x] Generate the catalog from the two legacy command arrays and the 40 curated GUI definitions.
- [x] Add tests asserting count 259, uniqueness, exact legacy set equality, non-empty plain-language fields, and stable ordering.
- [x] Implement source-generated JSON deserialization and strict validation.
- [x] Run structural verification and confirm catalog checks pass.

### Task 3: Domain and application contracts

**Files:**
- Create: `src/WinCare.Domain/WinCare.Domain.csproj`
- Create: `src/WinCare.Domain/Commands/CommandRequest.cs`
- Create: `src/WinCare.Domain/Commands/CommandResult.cs`
- Create: `src/WinCare.Domain/Activity/ActivityRecord.cs`
- Create: `src/WinCare.Application/WinCare.Application.csproj`
- Create: `src/WinCare.Application/Tools/ToolCatalogService.cs`
- Create: `src/WinCare.Application/Navigation/NavigationDefinition.cs`
- Create: `src/WinCare.Application/Navigation/NavigationCatalog.cs`
- Create: `tests/WinCare.Application.Tests/WinCare.Application.Tests.csproj`
- Create: `tests/WinCare.Application.Tests/ToolCatalogServiceTests.cs`

**Interfaces:**
- Consumes: `CommandCatalog.Load()`
- Produces: `ToolCatalogService.Search(string query, ToolFilter filter)` and the approved navigation/tab definitions

- [x] Write tests for empty search, ID/title/category search, read-only filtering, risk filtering, and exact navigation/tab labels.
- [x] Implement immutable records and a small search service with ordinal-ignore-case matching.
- [x] Keep XAML types out of all ViewModels and application contracts.
- [ ] Run managed tests on an environment with .NET 8 or later. Pending because this Linux runtime has no .NET SDK.

### Task 4: Bounded Rust native core

**Files:**
- Create: `native/Cargo.toml`
- Create: `native/wincare-core/Cargo.toml`
- Create: `native/wincare-core/src/lib.rs`
- Create: `native/wincare-core/include/wincare_core.h`
- Create: `native/wincare-core/tests/ffi_contract.rs`
- Create: `src/WinCare.Infrastructure/WinCare.Infrastructure.csproj`
- Create: `src/WinCare.Infrastructure/Native/WinCareCoreNative.cs`
- Create: `src/WinCare.Infrastructure/Native/NativeCoreService.cs`

**Interfaces:**
- Produces C ABI: `wincare_core_abi_version`, `wincare_core_version`, `wincare_core_sha256_file`
- Hashing accepts a UTF-8 path plus explicit maximum byte count and returns status codes without panicking across FFI.

- [x] Write Rust tests for ABI version, successful hashing, size rejection, missing file, null pointer rejection, and output-buffer bounds.
- [x] Implement the safe core and keep each unsafe block minimal with a `SAFETY` comment.
- [x] Add the C# P/Invoke wrapper with exact integer status mapping and no business policy.
- [ ] Run `cargo fmt --check`, `cargo clippy --all-targets --all-features -- -D warnings`, and `cargo test` on a Rust-capable environment. Pending because this runtime has no Rust toolchain.

### Task 5: WinUI shell and approved page hierarchy

**Files:**
- Create: `src/WinCare.App/WinCare.App.csproj`
- Create: `src/WinCare.App/App.xaml`
- Create: `src/WinCare.App/App.xaml.cs`
- Create: `src/WinCare.App/MainWindow.xaml`
- Create: `src/WinCare.App/MainWindow.xaml.cs`
- Create: `src/WinCare.App/Package.appxmanifest`
- Create: `src/WinCare.App/Views/ShellPage.xaml`
- Create: `src/WinCare.App/Views/ShellPage.xaml.cs`
- Create: `src/WinCare.App/ViewModels/ShellViewModel.cs`
- Create: `src/WinCare.App/Services/PageService.cs`
- Create: `src/WinCare.App/Styles/ThemeResources.xaml`
- Create: `src/WinCare.App/Styles/ControlStyles.xaml`

**Interfaces:**
- Consumes: `NavigationCatalog.Items`
- Produces: stable AutomationIds `NavHome`, `NavCheckup`, `NavSystemCare`, `NavSecurity`, `NavRepairRecovery`, `NavAllTools`, `NavActivity`, and `NavSettings`

- [x] Create source-level tests that require every approved navigation label and reject obsolete primary labels.
- [x] Implement the shell with Mica, an extended title bar, global search, `Ctrl+K`, and Frame navigation.
- [x] Keep code-behind limited to window/title-bar and navigation wiring.
- [x] Add Light, Dark, and HighContrast semantic resources.

### Task 6: Tabbed pages and the complete All tools table

**Files:**
- Create: `src/WinCare.App/Views/Pages/HomePage.xaml` and `.xaml.cs`
- Create: `src/WinCare.App/Views/Pages/CheckupPage.xaml` and `.xaml.cs`
- Create: `src/WinCare.App/Views/Pages/SystemCarePage.xaml` and `.xaml.cs`
- Create: `src/WinCare.App/Views/Pages/SecurityPage.xaml` and `.xaml.cs`
- Create: `src/WinCare.App/Views/Pages/RepairRecoveryPage.xaml` and `.xaml.cs`
- Create: `src/WinCare.App/Views/Pages/AllToolsPage.xaml` and `.xaml.cs`
- Create: `src/WinCare.App/Views/Pages/ActivityPage.xaml` and `.xaml.cs`
- Create: `src/WinCare.App/Views/Pages/SettingsPage.xaml` and `.xaml.cs`
- Create: focused ViewModels under `src/WinCare.App/ViewModels/Pages/`

**Interfaces:**
- All tools consumes `ToolCatalogService` and displays all 259 definitions without instantiating 259 pages.
- Page tabs use the exact approved labels from the specification.

- [x] Write structural tests for all tab labels, `SelectorBar`, `ListView`, Grid headers, `x:DataType`, `x:Bind`, and automation metadata.
- [x] Implement page shells with meaningful loading, empty, error, and ready states.
- [x] Implement All tools search, risk/read-only filters, row selection, and a detail pane with Advanced details.
- [x] Ensure narrow-window presentation retains every required action through responsive visual states.

### Task 7: Package assets, dual distribution configuration, and CI

**Files:**
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `WinCare.Native.sln`
- Create: package assets under `src/WinCare.App/Assets/`
- Create: `.github/workflows/native-winui.yml`
- Create: `docs/migration/windows-validation.md`

**Interfaces:**
- MSIX and portable jobs consume the same Release build outputs and Rust DLL.

- [x] Generate correctly sized package assets from the existing approved 512px WinCare logo.
- [x] Configure x64 and ARM64, MSIX, and self-contained portable publication.
- [x] Add Windows CI for restore, Rust checks, managed tests, WinUI build, catalog gate, package creation, and artifact upload.
- [x] Document commands for local Windows validation without adding PowerShell to the new runtime.

### Task 8: Verification and migration status

**Files:**
- Modify: `DESIGN.md`
- Modify: `docs/Architecture.md`
- Modify: `README.md`
- Modify: `docs/migration/command-parity-ledger.md`

**Interfaces:**
- Produces an evidence table that separates source-complete, Linux-static-verified, Windows-build-verified, UIA-verified, and behavior-parity-verified states.

- [x] Run `python3 tools/verify_native_foundation.py`.
- [ ] Run managed and Rust tests where the toolchains are available. Source tests pass; .NET and Rust toolchains are unavailable here.
- [ ] On Windows, build and launch with `winapp run`, execute scripted UIA tests, and capture light/dark/High Contrast screenshots. Pending Windows runner evidence.
- [x] Do not remove legacy PowerShell until the parity ledger reports 259 behavior-parity-verified commands.
