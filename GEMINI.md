# Project Instructions: WinCare

## Tech Stack
- **UI Shell**: WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2
- **Application & Domain**: C# (.NET 8/9, C# 12.0), System.Text.Json
- **Native Core**: Rust 2024 edition (`wincare-core`), `sha2` crate, versioned C ABI (`cdylib`)
- **Legacy Oracle**: PowerShell v5+ provider modules (`src/WinCare/Providers/`)
- **Validation**: Python 3.11+ test runners, xUnit, Pester

## Code Style & Conventions
- **Naming**: PascalCase for C# classes/records/methods, snake_case for Rust functions/variables (`wincare_core_sha256_file`), PascalCase for XAML views.
- **Fail Closed Policy**: Missing implementations, unknown commands, or native errors MUST return explicit error/blocked results. Never return fake success or swallow exceptions.
- **No Upstream Naming**: Target native code file paths and types must not inherit legacy tool vendor names.
- **Null Safety**: C# `#nullable enable` enforced; warnings treated as errors.

## Testing & Verification
- **Foundation Gate**: `python tools/verify_native_foundation.py`
- **Native Tests**: `python -m unittest discover -s tests/native -v`
- **Rust Native Suite**: `cargo test --manifest-path native/Cargo.toml`
- **C# Managed Suite**: `dotnet test WinCare.Native.sln -c Release -p:Platform=x64`

## Build & Release Tools
- **Build Release Archive**: `python tools/build_release.py`
- **RC Finalization**: `python tools/finalize_native_release.py --output artifacts/finalization --version 2.4.0-rc1 --mode rc`
- **Production Gate**: `python tools/finalize_native_release.py --output artifacts/finalization --version 2.4.0 --mode production`

## Project Structure
- `src/WinCare.App/` → WinUI 3 pages, viewmodels, themes, app lifecycle
- `src/WinCare.Application/` → Use cases, `CommandDispatcher`, `ToolCatalogService`
- `src/WinCare.Domain/` → Typed immutable requests (`CommandRequest`), results (`CommandResult`), activity records
- `src/WinCare.CommandCatalog/` → Catalog data for 259 command IDs, rules, presets
- `src/WinCare.Infrastructure/` → Win32/Rust P/Invoke interop (`WinCareCoreNative`), telemetry
- `native/wincare-core/` → Bounded Rust native primitives and C ABI exported library
- `tools/` → Verification, finalization, static checks, packaging scripts
