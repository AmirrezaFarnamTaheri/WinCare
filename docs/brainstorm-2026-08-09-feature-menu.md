# WinCare — Broad Feature & Module Brainstorm Menu
**Session:** 2026-08-09 | Branch: post-RC1

> Vast prioritized menu of what to build next. Grouped by domain.

---

## TIER A — Command Engine & Execution Core

### A1. Command Migration Engine (257 NotMigrated to Live)
Bulk-migrate the 257 `NotMigrated` stubs into real `ICommandHandler` implementations.
- Per-command parameter schemas (JSON-backed), validation, and dry-run mode
- Pluggable "migration wizard" that generates handler scaffold from `commands.json` entry
- Highest-impact unlock in the entire catalog

### A2. Dry-Run / Preview Mode
Before execution, every `Mutating`/`Critical` command shows a diff-style preview panel:
- Files that will be deleted, keys that will be changed, services that will be restarted
- Rust FFI call to enumerate affected paths before writing
- "Apply" is only enabled after preview render completes

### A3. Undo / Rollback Engine
Snapshot state before `Mutating` commands execute; write a rollback manifest to disk.
- `CommandRollbackService` backed by a lightweight SQLite journal
- UI: "Undo Last Action" button in ActivityPage with per-op granularity
- Time-bounded: manifests expire after 7 days

### A4. Command Scheduling & Automation
Schedule commands on a cron-style timer:
- `ScheduledTaskService` wraps Windows Task Scheduler API (Win32 P/Invoke)
- UI: "Schedule" action in the detail drawer, calendar/time picker
- Persisted in extended `presets.json` format

### A5. Preset & Profile System
Named collections of commands with a single "Run All" trigger:
- "Monthly Maintenance" preset = disk cleanup + log rotation + defrag
- Import/export presets as `.wincare-preset` JSON files
- Share presets via clipboard URL

### A6. Command Sequencing / Pipelines
Chain commands with pass/fail branching:
- `If disk_cleanup succeeds then run storage_health; else alert user`
- Visual pipeline builder in XAML
- Persisted as pipeline definitions in `commands.json`

### A7. Parallel Command Execution
Run multiple `ReadOnly` commands simultaneously on a `SemaphoreSlim`-governed thread pool:
- Progress aggregated into unified ActivityPage view
- Cancel all / cancel individual

---

## TIER B — Diagnostics, Telemetry & Reporting

### B1. System Health Dashboard (KPI Surface)
Landing screen with live KPI tiles:
- CPU%, RAM%, Disk% via extended Rust FFI `sys_info`
- SMART disk health status via WMI
- Last scan timestamp, pending actions count, overall health score (0-100)

### B2. Checkup Engine (Automated Scan)
Scheduled or on-demand full-system diagnostic:
- Runs all `ReadOnly` commands in parallel
- Scores each area (Disk, Network, Security, Privacy, Performance)
- Produces `CheckupReport` surfaced in a dedicated `CheckupPage`

### B3. Trend / History Graphs
Time-series charts of key metrics (disk usage, RAM, event log counts):
- Lightweight charting via `ItemsRepeater` + Canvas or WinUI Charts
- History stored in SQLite (`WinCare.db`)
- Export to CSV/PNG

### B4. Windows Event Log Viewer
Integrated event log surface:
- Filter by Source, Level (Error/Warning/Info), Time Range
- Group by Application / System / Security
- "Explain this error" button that calls an AI endpoint with event text

### B5. Export & Reporting
Generate structured reports from checkup runs:
- PDF export via `PdfDocument` API or HTML + WebView2 print
- HTML report with embedded charts
- Share via Windows share sheet (`DataTransferManager`)

### B6. Startup Impact Analyzer
Enumerate startup programs with per-app boot delay estimate:
- Rust FFI reads `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
- UI: impact badge (Low / Moderate / High), disable toggle
- "Defer" option: move to scheduled post-login task

### B7. Memory Pressure Analyzer
Working set per-process, standby list, pool nonpaged:
- Rust FFI wraps `GlobalMemoryStatusEx` + `EnumProcesses`
- Identify top 5 memory consumers with kill/restart option

---

## TIER C — Native Rust FFI Expansions

### C1. Network Diagnostics Module
New FFI exports:
- `wincare_net_ping(host, timeout_ms)` returns RTT/packet loss
- `wincare_net_adapters()` returns JSON list (name, IP, MAC, speed, status)
- `wincare_net_open_ports()` returns listening ports with PID mapping

### C2. File System Integrity Check
- `wincare_fs_find_large_files(path, min_bytes)` returns top-N files by size
- `wincare_fs_find_orphans(path)` returns files with no associated program
- `wincare_fs_integrity_scan()` SFC-style hash verification on system32

### C3. Registry Health Module
- `wincare_registry_find_orphans()` invalid Add/Remove Programs references
- `wincare_registry_backup(path)` exports subtree to `.reg` file
- `wincare_registry_diff(snap1, snap2)` delta between two snapshots

### C4. Process & Service Manager
- `wincare_processes_list()` JSON with name, PID, CPU%, RAM, integrity level
- `wincare_services_list()` service name, status, start type, recovery actions
- `wincare_service_set_start_type(name, start_type)` mutating action

### C5. Windows Update Status
- `wincare_updates_pending()` pending KB articles via WUA COM API
- `wincare_updates_history()` last N installed updates
- Surface in `UpdatesPage` with install/defer options

### C6. Defender & Security Status
- `wincare_defender_status()` real-time protection on/off, signature date, last scan
- `wincare_firewall_status()` per-profile (Domain/Private/Public) on/off
- Alert badge in navigation rail when protection is degraded

---

## TIER D — UI/UX Surface & Pages

### D1. Dashboard / HomePage
Currently missing — app opens directly to AllTools. Proper landing:
- Hero KPI tiles (Health Score, Last Scan, Pending Actions)
- Quick-access row: "Run Checkup", "View Activity", "Browse Tools"
- Animated health ring (WinUI 3 `ProgressRing` or custom Canvas arc)

### D2. CheckupPage
Dedicated page for structured system scan results:
- Category cards (Disk, Network, Security, Memory, Startup)
- Per-category severity badge + expandable findings list
- "Fix All" CTA that batches safe auto-fixable items

### D3. SchedulePage / Automation Hub
- Calendar view of scheduled tasks
- "Add Schedule" dialog with command picker + cron expression builder
- Last run status + next run countdown

### D4. SettingsPage
Currently implied but absent:
- Theme picker (Light / Dark / System)
- Language / locale
- Telemetry opt-in toggle
- Dangerous operations confirmation threshold
- Log retention policy
- Update channel (Stable / Beta)

### D5. Onboarding / First-Run Wizard
3-step welcome sequence:
1. Permissions grant (Admin elevation explanation)
2. Preset selection ("Basic Maintenance" / "Power User" / "Security Focus")
3. Initial scan trigger

### D6. Command Detail Drawer Upgrade
Current drawer shows description only. Expand with tabs:
- Affected Paths: enumerate what the command touches
- History: last 5 executions with timestamps and outcomes
- Risk Breakdown: why this command has its risk classification

### D7. Compact / Micro View Mode
Ultra-narrow sidebar mode (< 300px):
- Only icon + status pill visible per row
- Expand on hover to show name
- Useful when WinCare is docked to screen edge

### D8. Toast / Notification Center
In-app notification tray:
- Command completion notices
- Scheduled task results
- System health alerts (SMART warning, low disk)
- Persistent log, dismissible per-item or all

### D9. Command Palette (Ctrl+K)
Full-text fuzzy search across the entire 259-command catalog:
- Keyboard-first: arrow keys to navigate, Enter to open detail
- `>` prefix to run immediately without opening detail drawer
- Tags/aliases support for discoverability

### D10. Animated Theme Transition
Smooth animated theme switch:
- Cross-fade via `ThemeAnimation` or storyboard
- Persist preference to `ApplicationData.LocalSettings`

---

## TIER E — MCP Server / AI Integration

### E1. WinCare MCP Server
Expose WinCare's command catalog and execution engine as a local MCP server:
- Tool: `list_tools()` returns full catalog with risk metadata
- Tool: `run_tool(id, params)` dispatches through `CommandDispatcher`
- Tool: `get_activity_log()` returns recent activity entries
- Enables Claude / Gemini / Copilot to operate WinCare autonomously

### E2. AI Explain / Assist
"Explain this" button on every command, error, and event log entry:
- Calls configurable AI endpoint (local Ollama or cloud Gemini/OpenAI)
- Response shown inline in expandable `InfoBar`
- Caches results per-context to avoid repeat API calls

### E3. AI-Powered Checkup Narrative
After a checkup run, generate a plain-English health summary:
- "Your PC is in good shape. Disk usage is 78% — consider running Disk Cleanup. 3 startup apps are slowing boot by ~4s."
- Configurable: local model vs cloud API

### E4. Agentic Automation Mode
Let an AI agent autonomously run a maintenance session:
- User grants approval scope (e.g., "ReadOnly + Maintenance category only")
- Agent runs checkup, selects safe fixes, executes, reports
- Full activity log and undo capability preserved

### E5. Natural Language Command Dispatch
Type "clean up my downloads folder" → AI maps to `disk_cleanup` with path parameter:
- Embedding-based semantic search over command descriptions
- Confidence threshold guard before execution

---

## TIER F — Plugin & Extensibility System

### F1. Plugin SDK / Extension Points
Allow third-party command packs to be installed:
- Plugin manifest: `wincare-plugin.json` (name, version, commands[], permissions[])
- Commands contributed by plugins loaded into `CommandCatalog` at startup
- Isolation: plugins run in separate process with IPC bridge

### F2. Remediation Rules Engine (Dynamic)
Current `remediation-rules.json` is static. Make it dynamic:
- Rule DSL: `IF disk_usage > 90% THEN suggest disk_cleanup`
- Rule editor in SettingsPage
- Community rule packs downloadable from a registry

### F3. Scripting / Macro Language
Power users write custom maintenance scripts:
- Simple WinCare DSL: `run disk_cleanup; if success then run log_cleanup`
- Or: embed PowerShell script runner as a special command type
- Script editor with syntax highlighting (WebView2 + Monaco)

### F4. Custom Command Builder
GUI wizard to create a new command entry:
- Name, description, risk level, parameters
- Action type: PowerShell script / Batch / Rust FFI call
- Saved to user's local `commands.json` extension file

---

## TIER G — Security, Privacy & Integrity

### G1. Command Audit Log (Tamper-Evident)
Append-only log of every command execution:
- HMAC-signed entries stored in SQLite
- Export as JSON/CSV for compliance
- Viewable in ActivityPage with signature verification badge

### G2. Admin Elevation Guard
Per-command elevation check before dispatch:
- UAC prompt with WinCare-branded dialog explaining what requires elevation
- "Run as different user" option

### G3. Dangerous Operation Confirmation Modal
For `Critical` risk commands:
- Type the command name to confirm (GitHub-repo-delete style)
- 5s countdown timer before "Confirm" button activates
- Optional: require Windows Hello biometric confirmation

### G4. Privacy Cleaner Module
Dedicated module for privacy-sensitive operations:
- Clear browser history (Edge, Chrome, Firefox via profile path detection)
- Clear Windows Search index history, clipboard, MRU lists, recent documents
- Each item individually toggleable

### G5. Vulnerability Scanner
Check installed software versions against CVE database:
- `wincare_installed_software()` Rust FFI returns JSON list of name + version
- Match against NVD API or local cache
- Surface vulnerable packages with CVE links

---

## TIER H — Distribution, Update & Infrastructure

### H1. Auto-Updater
In-app update check and install:
- Check GitHub Releases API for latest version
- Download MSIX delta package in background
- Prompt user, apply on restart

### H2. Crash Reporting & Telemetry
Opt-in structured telemetry:
- Unhandled exception capture via `CoreApplication.UnhandledExceptionRaised`
- Attach to Sentry or self-hosted endpoint
- Anonymized: no PII, only command IDs, error types, timing

### H3. Portable / Standalone Mode
Current app requires MSIX. Add portable `.exe` fallback:
- Self-contained .NET 8 single-file publish
- Store config/data in `%APPDATA%\WinCare`

### H4. Remote / SSH Mode
Run WinCare commands against a remote Windows machine:
- WinRM or SSH transport layer
- UI shows which machine is active ("Local" vs remote hostname)
- Rust FFI calls tunneled through remote agent

---

## TIER I — Developer Experience & Testing

### I1. Managed Unit Test Suite Expansion
Current: 0 managed unit tests for ViewModel layer. Add xUnit tests for:
- `ToolRowViewModel`: brush resolution per risk level, all 6 branches
- `AllToolsPageViewModel`: filter logic, search debounce, SelectedTool preservation
- `CommandDispatcher`: all 5 admission gate paths
- `ActivityJournalService`: append, query, pagination

### I2. Integration Test Harness
End-to-end tests against real `CommandDispatcher` with mock handlers:
- Verify `NotMigrated` commands return `CommandNotMigrated` outcome
- Verify cancellation propagation
- Verify audit log entries written correctly

### I3. UI Automation Tests (WinAppDriver)
Automated UI tests for critical flows:
- Open app, run a ReadOnly command, verify ActivityPage entry
- Search for a tool by name, open detail drawer
- Toggle favorite, verify persistence

### I4. Benchmark Suite
Performance benchmarks for hot paths:
- `accumulate_dir_size` on large directory trees (BenchmarkDotNet + Criterion)
- SHA-256 file hashing throughput
- ViewModel filter+search render time

### I5. Visual Regression Testing
Screenshot-based tests for XAML pages:
- Baseline screenshots per theme (Light / Dark / HighContrast)
- CI gate: pixel-diff comparison blocks merge on visual regressions

---

## TIER J — Community & Ecosystem

### J1. Command Catalog Contribution Workflow
- PR template for new command contributions
- Automated `commands.json` schema validation gate
- Risk level review required for `Critical` commands

### J2. WinCare CLI (`wincare.exe`)
Headless CLI companion:
- `wincare run disk_cleanup`
- `wincare checkup --json`
- `wincare schedule add --command log_cleanup --cron "0 3 * * 0"`
- Ideal for server/headless Windows SKUs

### J3. Documentation Site
Auto-generated from `commands.json`:
- Per-command docs with risk matrix, parameters, examples
- Built with Docusaurus or MkDocs, hosted on GitHub Pages

### J4. Localization / i18n
Current UI is English-only:
- Extract all strings to `Resources.resw`
- First targets: Persian (fa-IR), German, Spanish
- RTL layout support for Arabic/Hebrew

---

## Priority Matrix

| ID | Name | Value | Effort | Risk | Tag |
|---|---|---|---|---|---|
| A1 | Command Migration Engine | 5/5 | High | Low | Next |
| A2 | Dry-Run Preview | 5/5 | Medium | Low | Next |
| B1 | Health Dashboard | 5/5 | Medium | Low | Next |
| D1 | HomePage | 4/5 | Medium | Low | Next |
| E1 | WinCare MCP Server | 5/5 | Low | Low | Quick Win |
| D9 | Ctrl+K Command Palette | 4/5 | Low | Low | Quick Win |
| D4 | SettingsPage | 4/5 | Low | Low | Quick Win |
| G3 | Critical Op Confirmation | 4/5 | Low | Low | Quick Win |
| C1 | Network Diagnostics FFI | 4/5 | Medium | Low | Soon |
| A3 | Undo/Rollback | 4/5 | High | Medium | Soon |
| I1 | VM Unit Tests | 4/5 | Medium | Low | Soon |
| A4 | Command Scheduling | 3/5 | High | Medium | Soon |
| F1 | Plugin SDK | 3/5 | Very High | High | Later |
| H1 | Auto-Updater | 3/5 | Medium | Low | Later |
| J2 | WinCare CLI | 3/5 | Medium | Low | Later |
| E4 | Agentic Mode | 4/5 | High | Medium | Later |
