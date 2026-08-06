# WinCare Command Runtime First Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add the first safe, typed native command execution path and make the `catalog` and `presets` read-only commands runnable from the approved WinUI All tools experience.

**Architecture:** `WinCare.Application` owns admission, deadlines, cancellation, dispatch, and result shaping. `WinCare.CommandCatalog` owns embedded immutable command and remediation data plus strict schema validation. WinUI calls only the application dispatcher through its ViewModel. The two commands are marked `Implemented`, not `BehaviorVerified`, because this Linux environment cannot run the PowerShell oracle or the WinUI application.

**Tech Stack:** .NET 8, C#, System.Text.Json source generation, CommunityToolkit.Mvvm, WinUI 3, Python 3 structural verification.

## Global Constraints

- Preserve all 259 existing command IDs.
- Do not delete the PowerShell oracle in this slice.
- Do not add PowerShell, WPF, network, process, CIM, or registry dependencies to native startup.
- Keep command handlers lazy and cancellation-aware.
- A read-only command rejects `Apply=true`.
- A mutating command cannot execute without explicit reviewed approval.
- Unknown and unmigrated commands fail closed with structured results.
- UI business behavior stays in ViewModels and application services, not code-behind.
- Use `x:Bind`, `x:DataType`, semantic ThemeResources, AutomationProperties, and native WinUI controls.
- Runtime and visual validation remain Windows-only and must not be claimed from Linux source checks.

---

### Task 1: Pin the command runtime contract with failing tests

**Files:**
- Create: `tests/native/test_command_runtime.py`
- Create: `tests/WinCare.Application.Tests/CommandDispatcherTests.cs`
- Modify: `tests/WinCare.Application.Tests/WinCare.Application.Tests.csproj`

**Interfaces:**
- Consumes: existing `CommandRequest`, `CommandResult`, `CommandDefinition`, and 259-command catalog.
- Produces: executable expectations for `ICommandHandler`, `CommandDispatcher`, `CommandExecutionOptions`, and the two safe handlers.

- [x] **Step 1: Write a Python structural test that requires the dispatcher, embedded data, handlers, UI command binding, two `Implemented` catalog entries, and valid XAML.**
- [x] **Step 2: Run `python3 -m unittest tests.native.test_command_runtime -v`.**
  Expected: FAIL because the command runtime files do not exist and the All tools XAML contains a duplicate attribute line.
- [x] **Step 3: Write C# unit tests for unknown, unmigrated, read-only apply, expired deadline, cancellation, successful catalog execution, and successful preset execution.**
- [x] **Step 4: Record that managed tests cannot run because `dotnet` is unavailable in this environment.**

### Task 2: Add strictly validated embedded remediation data

**Files:**
- Create: `src/WinCare.CommandCatalog/Data/remediation-rules.json`
- Create: `src/WinCare.CommandCatalog/Data/presets.json`
- Create: `src/WinCare.CommandCatalog/Models/RemediationRule.cs`
- Create: `src/WinCare.CommandCatalog/Models/PresetDefinition.cs`
- Create: `src/WinCare.CommandCatalog/RemediationCatalog.cs`
- Create: `src/WinCare.CommandCatalog/RemediationCatalogJsonContext.cs`
- Modify: `src/WinCare.CommandCatalog/WinCare.CommandCatalog.csproj`

**Interfaces:**
- Consumes: copied immutable schema-version-1 data from `src/WinCare/Data/Catalog`.
- Produces: `RemediationCatalog.LoadRules()` and `RemediationCatalog.LoadPresets()` returning immutable typed arrays.

- [x] **Step 1: Copy the 69 built-in remediation rules and 7 built-in presets into embedded native resources.**
- [x] **Step 2: Implement bounded JSON loading with a 4 MiB ceiling, depth limit, exact root and item keys, ID validation, duplicate detection, and preset reference validation.**
- [x] **Step 3: Add source-generated JSON metadata for load and result serialization.**
- [x] **Step 4: Run the Python test and confirm failures move from missing data to missing dispatcher/UI implementation.**

### Task 3: Implement the fail-closed application dispatcher

**Files:**
- Create: `src/WinCare.Application/Commands/ICommandHandler.cs`
- Create: `src/WinCare.Application/Commands/CommandHandlerOutcome.cs`
- Create: `src/WinCare.Application/Commands/CommandExecutionOptions.cs`
- Create: `src/WinCare.Application/Commands/CommandDispatcher.cs`
- Create: `src/WinCare.Application/Commands/CommandRuntime.cs`
- Create: `src/WinCare.Application/Commands/Handlers/CatalogCommandHandler.cs`
- Create: `src/WinCare.Application/Commands/Handlers/PresetsCommandHandler.cs`

**Interfaces:**
- Consumes: `CommandRequest`, the command catalog, embedded remediation data, cancellation, and an optional deadline.
- Produces: `Task<CommandResult> CommandDispatcher.ExecuteAsync(CommandRequest, CommandExecutionOptions, CancellationToken)`.

- [x] **Step 1: Implement handler registration with duplicate-ID rejection.**
- [x] **Step 2: Implement admission checks for blank or unknown IDs, migration status, handler availability, read-only apply, reviewed mutation approval, deadline, and pre-cancelled tokens.**
- [x] **Step 3: Own timestamps and exception translation in the dispatcher.**
- [x] **Step 4: Implement `catalog` and `presets` handlers that return arrays matching the legacy command output shape.**
- [x] **Step 5: Update only those two command definitions to `Implemented`.**

### Task 4: Connect the safe commands to All tools

**Files:**
- Modify: `src/WinCare.App/ViewModels/Pages/AllToolsPageViewModel.cs`
- Modify: `src/WinCare.App/Views/Pages/AllToolsPage.xaml`
- Modify: `src/WinCare.App/Views/Pages/AllToolsPage.xaml.cs`

**Interfaces:**
- Consumes: `CommandRuntime.CreateDefault()` and selected `ToolRowViewModel`.
- Produces: an async command, busy state, structured result copy, formatted result JSON, and recent-tool tracking.

- [x] **Step 1: Add an `AsyncRelayCommand` that dispatches only through `CommandDispatcher`.**
- [x] **Step 2: Expose plain-language action labels, busy state, success/error state, and result details without XAML types in the ViewModel.**
- [x] **Step 3: Bind the primary button to the async command and add accessible result `InfoBar` and details disclosure.**
- [x] **Step 4: Show executed tools in Recent and expose the `presets` command in the Presets tab.**
- [x] **Step 5: Remove the duplicate XAML attribute and keep the responsive table contract intact.**

### Task 5: Verify and document the slice

**Files:**
- Modify: `docs/migration/command-parity-ledger.md`
- Create: `docs/migration/command-runtime-first-slice-evidence.md`
- Modify: `tools/verify_native_foundation.py`

**Interfaces:**
- Consumes: the completed native source and static tests.
- Produces: honest migration counts and reproducible evidence.

- [x] **Step 1: Extend the native verifier to require the dispatcher, embedded data, two implementations, and no native PowerShell/WPF references.**
- [x] **Step 2: Run `python3 -m unittest tests.native.test_native_foundation tests.native.test_package_portable tests.native.test_command_runtime -v`.**
  Expected: PASS.
- [x] **Step 3: Run `python3 tools/verify_native_foundation.py`.**
  Expected: PASS with 259 catalog IDs and two native handler IDs.
- [x] **Step 4: Run `python3 -m py_compile` over changed Python files.**
  Expected: PASS.
- [x] **Step 5: Update the ledger to 259 cataloged, 2 contract verified, 2 implemented, and 0 behavior verified.**
- [x] **Step 6: Package the workspace as `/mnt/data/WinCare-2.3.0-native-command-runtime.zip`.**

## Execution record

- Static red-green cycle completed for the command-runtime source contract.
- Final Linux gate: 14 Python tests passed, native verifier passed, and changed Python files compiled.
- New CI-coverage and result-envelope checks were falsified with known-bad specimens before the final pass.
- Managed, Rust, WinUI, UI Automation, PowerShell-oracle, and packaging runtime gates remain assigned to Windows CI because the required toolchains are unavailable here.
- Source archive: `/mnt/data/WinCare-2.3.0-native-command-runtime.zip`.
