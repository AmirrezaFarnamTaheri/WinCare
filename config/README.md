# Configuration

WinCare writes the active user configuration to `%LOCALAPPDATA%\WinCare\settings.json` on first launch. The file is schema-versioned and merged with new defaults during upgrades.

The source repository intentionally does not ship mutable user configuration. Use the Settings screen or edit the generated JSON while WinCare is closed.

## WinCare 4.3 productivity, operations, and expert defaults

- `WorkspaceMode` selects All, Maintenance, Productivity, Support, Developer, or Minimal discovery views without changing authority.
- `PowerSessionDefaultMinutes` and `PowerSessionRefreshSeconds` bound proposed awake sessions and host refresh cadence; policy imposes final limits.
- `BrowserExtensionInventoryLimit` bounds local manifest discovery.
- `RemoteConsentDefaultMinutes` proposes consent expiry; policy imposes the maximum.
- `CommandPaletteMaxResults` bounds fuzzy command output and persisted usage influence.


- `SecurityMaintenanceDefaultMinutes` controls the proposed expiry interval; policy may impose a lower maximum.
- `NetworkExperimentSampleCount` and `NetworkExperimentTimeoutMilliseconds` control bounded baseline/post-change samples.
- `InstrumentationEventLimit` bounds injection-surface and event evidence shown by default.

Configuration changes usability and defaults only. It cannot override machine/organization policy or action-contract risk floors.

### Operations defaults

- `DownloadMaximumConcurrent` bounds simultaneous WinCare-owned transferring records.
- `DownloadRetryLimit` bounds mirror/retry state.
- `DownloadMaximumBytes` is a user-level ceiling; the effective limit is the lower of this value and `MaximumDownloadBytes` policy.
- `TelemetryCpuAlertPercent`, `TelemetryMemoryAlertPercent`, and `TelemetryHistoryLimit` bound local alerting and retention.
- `LauncherIndexLimit` bounds Start-menu indexing.
- `GameStateFileLimit` and `GameStateMaximumBytes` bound Steam cloud inventories and backups.
- `WorkspaceLayoutLimit` bounds revisioned saved layouts.

## WinCare 4.8 Experience Studio defaults

Experience Studio uses existing state roots, download limits, power/brightness bounds, and policy floors. Visual manifests are written only through managed-file actions. Location, remote access, power, and privacy selections are usability inputs; they cannot lower action risk or bypass organization policy.
