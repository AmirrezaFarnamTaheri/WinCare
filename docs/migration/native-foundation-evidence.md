# Native foundation evidence

> **Historical snapshot:** This document records the native foundation before command execution was added. Current command-runtime evidence and migration counts are in `docs/migration/command-runtime-first-slice-evidence.md`.


**Recorded:** 2026-08-05

## Delivered source slice

- WinUI 3 application shell with the approved primary navigation.
- Horizontal SelectorBar sections on every main area.
- Responsive tabular ListView layouts with compact row fallbacks.
- Searchable All tools workbench backed by the complete command catalog.
- Typed C# projects for app, application, domain, infrastructure, and command catalog responsibilities.
- Versioned Rust C ABI with bounded file hashing as the first native primitive.
- x64 and ARM64 MSIX and portable publish profiles.
- Windows CI source, Rust, managed, package, and UI Automation gates.
- Startup telemetry markers and a source gate that forbids network and external process work on the initial shell path.

## Verified in this environment

| Gate | Result |
|---|---|
| Python native foundation tests | 8 passed, 0 failed |
| Native foundation verifier | Passed |
| Command catalog count | 259 |
| Unique command IDs | 259 |
| Exact legacy ID parity | Passed |
| Native PowerShell files | 0 |
| Native WPF references | 0 |
| XML/XAML/project parsing | Passed |
| Python source compilation | Passed |
| Portable ZIP deterministic metadata tests | Passed |
| Portable ZIP symbolic-link rejection test | Passed |

## Migration status

| State | Commands |
|---|---:|
| Cataloged | 259 |
| Contract verified | 0 |
| Implemented | 0 |
| Behavior verified | 0 |

The command catalog is complete, but command behavior has not been rewritten yet. Tool execution remains disabled until each command reaches at least `Implemented`, and release removal of the legacy source requires `BehaviorVerified` parity.

## Legacy retention

The repository still contains 246 PowerShell files. They are intentionally excluded from the native runtime and retained only as the current behavior oracle. Deleting them now would remove the only implementation of the 259 preserved capabilities and would violate the approved parity requirement.

## Runtime validation not available here

This execution environment is Linux and does not provide `dotnet`, `cargo`, `rustc`, `winapp`, or a Windows desktop session. The following gates are defined but were not run here:

- NuGet restore and C# compilation
- xUnit execution
- Rust formatting, Clippy, unit tests, and Windows DLL builds
- WinUI launch and first-frame measurement
- UI Automation and screenshot review in light, dark, and High Contrast
- MSIX creation, signing, installation, upgrade, and uninstall
- portable x64 and ARM64 launch testing

These gates are encoded in `.github/workflows/native-winui.yml` and documented in `docs/migration/windows-validation.md`.

## Independent review

The Codex review lane did not run because the Codex CLI is not installed in this environment. This is an unavailable review, not an approval.
