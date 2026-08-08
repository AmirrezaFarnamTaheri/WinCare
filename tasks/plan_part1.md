# WinCare Native Parity & Visual Character -- Master Implementation Roadmap
**Version:** 2.4.0-rc1 | **Visual Engine:** Tactile Telemetry & Cyber-Operate

> **Scope:** Exhaustive micro-task roadmap synthesised from a full live codebase investigation --
> 386 files, 1,239 symbols, 9,969 dependency edges, 65 execution flows, 11 communities.
> Every task references exact file paths, exact code symbols, and a verifiable gate.
> Zero placeholders. Zero ellipses. Zero guesses.

---

## PART I -- ARCHITECTURAL GROUND TRUTH

### Layer Map (observed, not assumed)

| Layer | Project | Key Types |
|-------|---------|-----------|
| **Domain** | WinCare.Domain | CommandRequest, CommandResult, CommandResultStatus, ActivityRecord, ActivityState -- all immutable sealed record |
| **Catalog** | WinCare.CommandCatalog | CommandDefinition (Id, MigrationStatus, ReadOnly, CommandRisk, Area, AdminRequired, Restart), CommandCatalogJsonContext (AOT JSON) |
| **Application** | WinCare.Application | CommandDispatcher, ICommandHandler, CommandHandlerOutcome, CommandExecutionOptions, CommandRuntime |
| **Infrastructure** | WinCare.Infrastructure | WinCareCoreNative (internal static P/Invoke), NativeCoreService (public sealed), NativeCoreStatus (enum), StartupTelemetry |
| **Presentation** | WinCare.App | MainWindow (Mica/Acrylic + Ctrl+K wired), ShellPage, AllToolsPage (257 lines), CheckupPage, ActivityPage, SettingsPage. CommunityToolkit.Mvvm 8.4.2 |
| **Native** | native/wincare-core | Rust 2024 cdylib, ABI v1. Exports: wincare_core_abi_version, wincare_core_version, wincare_core_sha256_file |

### CommandDispatcher Admission Pipeline -- 9 Exact Gates

Observed directly in CommandDispatcher.cs (244 lines):

    ExecuteAsync(request, options, ct)
      |
      |--1-- CancellationToken.IsCancellationRequested  --> Cancelled   / command.cancelled
      |--2-- options.Deadline <= startedAt              --> Cancelled   / command.deadline_exceeded
      |--3-- !_definitions.TryGetValue(id)             --> Blocked     / command.unknown
      |--4-- parameters.ValueKind != JsonObject         --> Blocked     / command.parameters_invalid
      |--5-- MigrationStatus not Implemented/Verified  --> NotMigrated / command.not_migrated [257/259]
      |--6-- definition.ReadOnly && request.Apply       --> Blocked     / command.read_only
      |--7-- !ReadOnly && Apply && !ReviewApproved      --> Blocked     / command.review_required
      |--8-- !_handlers.TryGetValue(id)                --> Failed      / command.handler_missing
      \--9-- handler.ExecuteAsync(request, linkedCt)   --> Succeeded / Failed / Cancelled

### Performance Budget

| Metric | Target | Evidence |
|--------|--------|----------|
| Cold startup | < 2.0 s | verify_native_foundation.py |
| Warm startup | < 1.0 s | StartupTelemetry marks |
| Navigation page transition | < 100 ms | WinUI frame metrics |
| UI-thread block ceiling | < 50 ms | All FFI via Task.Run |
| Catalog JSON deserialisation | < 8 ms | CommandCatalogJsonContext (AOT) |
| ListView render (259 rows) | < 30 ms | VirtualizingStackPanel default |

### Visual Character: Tactile Telemetry & Cyber-Operate Engine

ThemeResources.xaml TODAY (46 lines): plain Windows defaults -- no Cyber-Teal, no pill infrastructure.

Dark Mode additions:
  AccentTealBrush #00D2FF, AccentTealSubtleBrush #1A00D2FF, CardSurfaceBrush #161C26
  TelemetryFrameBrush #2A3A4A, PillReadOnlyBgBrush #047857, PillMutatingBgBrush #EF4444
  PillElevatedBgBrush #F59E0B, PillVerifiedBorderBrush #047857, PillNotReadyBgBrush #1F2937
  PillTextBrush #FFFFFF

Light Mode mirrors: AccentTealBrush=#0078D4, CardSurfaceBrush=#FFFFFF, PillMutatingBgBrush=#DC2626
HighContrast: all tokens -> SystemColor* ThemeResource bindings

Typography:
  H1   Segoe UI Variable Display  28px SemiBold  -0.5px tracking
  H2   Segoe UI Variable Display  20px Medium
  Body Segoe UI Variable Text     14px Regular
  Mono Cascadia Code / Fira Code  11px (pills, IDs, byte counts, hashes)

Status Pills (ContentControl + StatusPillTemplate, Cascadia Code 11px):
  [ READ-ONLY ]  #047857 bg / #FFF text  (4.52:1 WCAG AA)
  [ MUTATING  ]  #EF4444 bg / #FFF text
  [ ELEVATED  ]  #F59E0B bg / #1A1A1A text (amber fails with white)
  [ VERIFIED  ]  transparent / #047857 border+text
  [ NOT READY ]  #1F2937 bg / #94A3B8 text

Anti-Slop rules (DESIGN.md enforced):
  - No hero gradients on page backgrounds
  - No decorative glass orbs / blurred blobs
  - No emoji as icons (Segoe Fluent Icons / SVG only)
  - No dark-only defaults (Light + HighContrast required in all resource dictionaries)
  - No unguarded mutating actions (ReviewApproved = true enforced by dispatcher gate 7)

---

## PART II -- VERIFICATION SUITE

| Gate | Command | Checks |
|------|---------|--------|
| V1 | cargo test --manifest-path native/Cargo.toml | Rust ABI memory and panic safety |
| V2 | cargo clippy --manifest-path native/Cargo.toml --all-targets -- -D warnings | Rust lint, exit 0 |
| V3 | python -m unittest discover -s tests/native -v | Python interop suite (22 tests) |
| V4 | dotnet test WinCare.Native.sln -c Release -p:Platform=x64 | C# managed suite |
| V5 | python tools/verify_native_foundation.py | 259 IDs, zero .ps1, all AutomationIds |
| V6 | python tools/finalize_native_release.py --output artifacts/finalization --version 2.4.0-rc1 --mode rc | RC dual-archive finalization |

---
