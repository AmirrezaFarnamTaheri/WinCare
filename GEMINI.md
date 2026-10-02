# WinCare: System & Codebase Guide

## Tech Stack
- **Languages:** C# 12 (.NET 8.0 SDK `8.0.416`), Rust 2021 (Rustc `1.97.1`), XAML (WinUI 3 / Windows App SDK 1.5)
- **Frameworks:** Windows App SDK 1.5, WinUI 3 desktop, CommunityToolkit.Mvvm
- **Native Interop:** Rust cdylib (`wincare_core.dll` ABI v1), P/Invoke, `windows-targets`
- **Testing:** xUnit, coverlet, Python 3 test harnesses

## Code Style & Rules
- **Nullability & Warnings:** `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. Zero warnings allowed.
- **Layering:** `Domain` (zero outer dependencies) → `CommandCatalog` → `Application` → `Infrastructure` → `WinCare.App` (WinUI 3 presentation).
- **Naming:** PascalCase types/methods, `_camelCase` private fields, semantic XAML resource keys.
- **Fail-Closed Execution:** Mutating operations are dispatched through `CommandDispatcher`. Destructive work requires a preview receipt plus explicit approval; Safe and Moderate work (ADR-001) run in one deliberate click and record the outcome.

## Build & Run
- **Restore & Build (.NET):** `dotnet build WinCare.Native.sln -c Release -p:Platform=x64`
- **Build Native Core (Rust):** `cargo build --manifest-path native/wincare-core/Cargo.toml --release`
- **Run WinUI 3 App:** Launch `src/WinCare.App/WinCare.App.csproj` in unpackaged mode via VS or `dotnet run --project src/WinCare.App`

## Testing
- **Run All Unit Tests:** `dotnet test WinCare.Native.sln -c Release -p:Platform=x64` (617 across Catalog, Application, Infrastructure)
- **Run Specific Suite:**
  - `dotnet test tests/WinCare.CommandCatalog.Tests/WinCare.CommandCatalog.Tests.csproj`
  - `dotnet test tests/WinCare.Application.Tests/WinCare.Application.Tests.csproj`
  - `dotnet test tests/WinCare.Infrastructure.Tests/WinCare.Infrastructure.Tests.csproj`
- **Native & Visual Verification:**
  - `python tools/verify_native_foundation.py`
  - `python tools/verify_pill_contrast.py`
  - `python tools/verify_palette_contrast.py`

## Project Structure
- `src/WinCare.Domain/`: Domain primitives, command results, risk tier definitions, status models.
- `src/WinCare.CommandCatalog/`: Native command catalog with 296 commands across core and pack fragments, with parameter validation.
- `src/WinCare.Application/`: Dispatcher, admission gate, journal, plugin contracts, playbook engine.
- `src/WinCare.Infrastructure/`: OS probes, registry/WMI/PowerShell providers, native P/Invoke bridges.
- `src/WinCare.App/`: WinUI 3 presentation layer, MVVM view models, fluent design system, theme brushes.
- `native/wincare-core/`: High-performance Rust engine exporting C ABI v1 for system diagnostics.
- `native/wincare-guard/`: System protection and sandbox boundary monitor.
- `tests/`: Automated xUnit tests for application logic, catalog validation, and infrastructure adapters.
- `tools/`: Python automation for verification, release packaging, visual contrast, and screenshot capture.

## Key Conventions
- **Commands:** Declarative catalog definitions; the dispatcher owns admission, preview/approval, execution, and Activity evidence.
- **Fluent UI:** Segoe UI Variable, 8/12 DIP radii, high-contrast support, contrast ratio gates (WCAG AA).
- **Commits:** Conventional commits (`feat:`, `fix:`, `refactor:`, `docs:`, `test:`).
