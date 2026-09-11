# Coverage ledger — WinCare b30516a

Updated 2026-09-11. This is an audit of evidence, not a claim that every branch or Windows configuration was executed. Source inventory and runtime proof are deliberately separate. See `repository-inventory.csv`, `command-matrix.csv`, `control-inventory.csv`, and `evidence/`.

**Forensic follow-up:** Runtime coverage remains incomplete: 144 of 155 read-only commands and all 104 mutations were not individually executed. The [follow-up](D:/GitHub/WinCare/docs/audit-2026-09-11/forensic-follow-up.md) distinguishes genuine omissions from incorrect critique claims. No new platform, mutation, pruning comparison, ALC collection or soak-test completion is implied by the documentation revision.

| Subsystem / responsibility | Review depth and evidence | Runtime / tooling | Remaining gap |
|---|---|---|---|
| Repository/source of truth | Tracked-file inventory; native WinUI/.NET/Rust runtime, catalog, migration oracle, tooling, tests distinguished | 342 tracked files classified | Inventory does not imply line-by-line certification |
| App startup/composition/native ABI | App, MainWindow, AppRuntime, shell, preferences and native adapters traced | Installed Home launches; eleven backend probes succeed; local publish | Clean machine, missing runtime/DLL, corrupt startup state not injected |
| Shell/navigation | All twelve destinations accounted for; route handlers traced | Installed: eight destinations crash; Home/Settings/Help/About survive. Fresh local build: Checkup/AllTools/PluginStore survive; AI Doctor crashes | Remaining fresh-build routes, protocol deep links end to end |
| Home | XAML, view model, events, journal-derived evidence and direct commands traced; all three supplied images reviewed (first two duplicate scrolled view) | UIA names captured; idle and two read-only action probe | Cleanup intentionally not executed; all DPI/theme/keyboard states not exercised |
| Checkup | Probe orchestration, bindings, rows, evaluation, background WUA, cleanup traced | Installed page crash; fresh local page opens; view-model tests | Full WUA/repeat/cancel matrix and screen-reader behavior |
| System care/Security/Repair | Static sections, filters, page bindings and navigation traced | All three installed routes crash; catalog query checks | Each tool on each OS/elevation combination |
| All tools | Catalog/search/schema/editor/preview/approval/execute path traced | 259 catalog routes inventoried; installed crash; fresh local page opens | 248 commands not individually executed; generated input types and invalid JSON UI not exhaustively exercised |
| Activity/journal | Begin/Complete/failure/retention/persistence/shutdown path traced | Installed page crashes; isolated backend journal used | Forced crash during durable flush, full export/redaction stress |
| AI Doctor | Intent/evidence/plans, XAML converter, action buttons traced | Both installed and fresh local page crash; explicit missing converter stack | AI page cannot be exercised before repair |
| Settings/Help/About | XAML, settings storage, documentation copy and navigation | All three installed pages open and close | Restart persistence combinations, assistive technology, all external links |
| Dispatcher/catalog/domain | All route IDs mapped; risk admission, receipts, parameter validation and exception semantics reviewed | Existing managed tests; eleven harmless command previews | Real administrative mutations/rollback not run |
| Backend system/storage/network/security | Core queries, native fallback, health, WUA, process runner examined | Eleven read-only success results with shape/timing | Hardware-dependent/failure branches; no actual cleanup/service/registry mutation |
| Backend experience/state/remediation | Cleanup, schedules, state helpers, incremental mutation and records examined | Two-store concurrency fixture demonstrates failed/lost update | Full mutation family and crash recovery matrix |
| Backend desktop/productivity | Representative window/monitor/context-menu/widget/power/download paths, shared parameter/process/state helpers examined | Routing inventory and relevant suite coverage | Hardware DDC/CI, third-party customization tools, actual window mutation and downloads unverified |
| Plugins | Registry/host/ALC/admission/trust/installer/catalog/state/scripts/widgets/lifecycle inspected | Managed security tests; plugin CLI tests; failed persistence fixture | No external install; all admitted plugin behaviors cannot be certified |
| Native core/FFI | ABI layout, paths/bounds, system snapshots, hash/size, cleaner, panic behavior inspected | Rust tests + clippy/fmt | Release panic injection, ARM64 hardware, long FFI cancellation |
| Guard/IPC | Daemon loop, pipe ACL/limits/deadlines/client ownership, monitor/notification wiring inspected | Rust tests | Separate optional daemon not running in app; service install/notification delivery unverified |
| Persistence | Preferences/journal/command state/plugin state/installer transaction boundaries traced | Two independent stores; blocked plugin state write | Power loss, low disk, multiple actual app processes mutating concurrently |
| Resource usage | Startup chain, async ownership, bounded process runner, loops, network read bounds inspected | Installed Home process samples and read-only action probes | Cold-cache startup, ETW whole-system attribution, multi-hour leak profiling |
| Visual/accessibility | Supplied Home regions inspected twice; all page XAML inventory; theme and contrast token tests | UIA navigation/name capture; 11 tokens × 3 themes, 8 contrast pairs | No complete UIA accessibility compliance claim; dark/HC/DPI/localization/screen readers runtime unverified |
| Build/dependencies | Project properties, lockfiles, pins, profiles, native staging, pruning examined | Untrimmed publish; strict trimmed restore/publish fails IL2026; NuGet advisory query no findings | Fresh ARM64/MSIX install/uninstall; native DLL provenance from staged existing binary |
| CI/release/tools | Both native workflows, smoke, release finalization/staging, install/CLI gates inspected | Python/native validation + CLI tests | No remote release or certificate installation performed |
| Tests/docs/oracles | Managed suites, Rust suites, Python structural tests; README/validation/ADR expectations compared | Test logs retained | Coverage percentage not generated; structural tests are not feature proof |

## Deliberate passes

Reconnaissance, source-of-truth, screenshots, product model, runtime reproduction, architecture, wiring, tooling, correctness, frontend/backend, performance, defensive security, lifecycle/concurrency, persistence, cross-module flow, release, documentation and adversarial checks have evidence in the report. Second visual review and gap accounting explicitly preserve unknowns. No production repairs are included.

## Adversarial checks and rejected conclusions

- Rejected “the backend crashes immediately”: eight installed navigation crashes are UI failures; eleven backend read-only queries succeeded.
- Rejected “all buttons are dead”: navigation works for Settings/Help/About and fresh-build routes differ; direct read-only Home controls are separately probed.
- Rejected “trimming proven”: initial no-restore variants were byte-identical. The restored strict trim build fails diagnostics; this establishes a build defect, not exact cause of the installed WinRT crash.
- Rejected “high sustained idle CPU proven”: measure per-process CPU, memory and connections; user-observed whole-system load is still a separate hypothesis.
- Rejected “cropped Home header”: first two screenshots are scrolled states.
- Rejected “ARM runtime absent from CI”: current workflow explicitly includes an ARM64 runner smoke job.
- Rejected “plugin store trust gate is a bypass”: absence of a production trust root is an intentional fail-closed limit.
- Corrected earlier cleanup interpretation: catalog name-based Safe downgrade means direct execution is admitted when elevation exists; there is no universal receipt gate.
