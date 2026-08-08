# Release Notes

## 2.4.0-rc1

- Replaced the WPF product surface with an approved WinUI 3 shell using task-based navigation, page-level tabs, responsive tabular lists, system theming, keyboard support, and UI Automation metadata.
- Preserved all 259 stable command IDs in a typed embedded catalog.
- Added a fail-closed C# command dispatcher and native read-only handlers for `catalog` and `presets`.
- Added a bounded Rust C ABI foundation for native primitives.
- Removed PowerShell from the native source artifact and preserved the historical implementation in a separate immutable oracle archive.
- Added frozen parity fixtures with source provenance and SHA-256 hashes.
- Added deterministic native-source and legacy-oracle packaging with a machine-readable finalization manifest.
- Added a production gate that refuses promotion until all 259 commands are behavior-verified.
- Added Linux-capable structural tests and Windows CI matrices for x64 and ARM64.

This release candidate is not production-promotable. Current migration status is 259 cataloged, 2 implemented, and 0 behavior-verified.

## 2.3.0
The final PowerShell/WPF release remains available in Git history and the legacy oracle archive for parity work.

## 1.1.0

Historical PowerShell module compatibility baseline retained for deterministic legacy packaging and validator contracts.
