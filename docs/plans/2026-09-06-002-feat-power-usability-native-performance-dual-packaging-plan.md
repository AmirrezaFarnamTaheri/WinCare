---
artifact_contract: ce-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: ce-brainstorm
date: 2026-09-06
status: completed
---

# WinCare Power Usability, Native Performance, and Dual Packaging - Plan

## Goal Capsule
* **Objective:** Elevate WinCare into a premier, high-utility Windows optimization suite that pairs 1-click everyday simplicity with power-user depth, backed by sub-millisecond Rust native kernel execution and dual distribution packaging (~22 MB portable single-file executable and < 6 MB framework-dependent installer).
* **Product Authority:** User design approval on 2026-09-06 (Understanding Lock confirmed).
* **Open Blockers:** None. Architecture validated and rendered via Archify into [`docs/architecture.html`](file:///D:/github/WinCare/docs/architecture.html).

---

## Product Contract

### 1. Problem Statement & Strategic Vision
Current PC optimization utilities suffer from a false dichotomy: they are either opaque, untrustworthy "snake-oil" tools that break operating systems, or intimidating administrative consoles that overwhelm regular users. 

WinCare solves this by executing a **Hybrid Progressive Disclosure Model**:
1. **Daily Users:** Immediate, non-destructive 1-click solutions on the surface (reclaiming gigabytes of storage, tuning network latency, streamlining background startup).
2. **Power Users:** Complete operational transparency underneath. A non-intrusive inspector reveals exact microsecond probe timings, active Win32/NT system calls, dry-run file/registry diffs, and instant rollback snapshots.
3. **Daily to Power Elevation:** Regular users are actively educated on how their operating system functions without cognitive overload, turning everyday users into confident, empowered power users.

---

### 2. Architecture & Interactive Diagram
The architecture is modeled and delivered as a zero-dependency interactive SVG diagram in [`docs/architecture.html`](file:///D:/github/WinCare/docs/architecture.html) (authored in [`docs/wincare.architecture.json`](file:///D:/github/WinCare/docs/wincare.architecture.json)):

* **Presentation Layer (WinUI 3 / .NET 8):** Native Windows Mica backdrop, Fluent 2 visual tokens, high-density telemetry rendering, and responsive card layouts.
* **Governance Layer (`WinCare.Application`):** Tri-tier safety admission (`RiskTier.Safe`, `Moderate`, `Destructive`), single-use `ApprovedMutationPlan` gating, and bounded parallel concurrency via `ParallelCommandProbeRunner`.
* **Native Kernel Layer (`wincare-core` in Rust 2024):** High-speed C-ABI dynamic library (`opt-level = 3`, `lto = "fat"`, `strip = true`) executing direct Win32/NT queries without child process spawning.
* **Distribution Layer:** Dual packaging profiles addressing both zero-prerequisite standalone portability and ultra-lightweight system framework distribution.

---

### 3. Decision Log

| ID | Decision | Alternatives Considered | Rationale |
|---|---|---|---|
| **DEC-01** | **Progressive Disclosure Cockpit** | Modal popups, separate "Advanced Mode" toggle | Keeps 1-click simplicity intact while allowing in-place elevation to power-user telemetry. |
| **DEC-02** | **Shift Heavy Probes & Mutations to Rust (`wincare-core`)** | Pure C# Win32 P/Invoke, PowerShell background daemon | Maximizes CPU/memory efficiency, eliminates child process latency, and reduces managed assembly size. |
| **DEC-03** | **Dual Distribution Packaging** | Single-File only, MSIX-only, Installer-only | Delivers an ultra-portable ~22 MB standalone zero-dependency exe alongside a featherweight < 6 MB framework installer. |
| **DEC-04** | **Eliminate ReadyToRun in Portable Profile** | Keep R2R enabled | Removes 10–12 MB of pre-compiled native JIT bloat with an unnoticeable ≤40ms cold-start delta on modern PCs. |
| **DEC-05** | **Zero-Allocation Telemetry Pipeline** | Dynamic JSON serialization over FFI | Pinned `Span<byte>` buffers and unmanaged structs generate 0 bytes of managed GC allocation in live monitoring loops. |

---

### 4. Functional Requirements

* **FR-1: 1-Click Action Cards (Surface Layer):**
  * Home and Checkup pages present curated, high-impact 1-click action cards (`cleaner-disk-pressure`, `startup-streamline`, `network-latency-tune`).
  * Instant execution with inline progress ring and non-blocking asynchronous UI updates.
* **FR-2: Deep System Inspector (Power-User Layer):**
  * Action cards and checkup categories feature an expander toggle (`Inspect System Impact & Telemetry`).
  * Monospace tabular telemetry displays: probe latency in microseconds ($\mu$s), targeted filesystem paths, registry keys, dry-run diff preview, and rollback snapshot IDs.
* **FR-3: Native Rust Core Migration:**
  * Implement C-ABI exported functions in `crates/wincare-core`:
    * `wincare_sys_snapshot_all`: Batched retrieval of CPU, RAM, disk pressure, and network health.
    * `wincare_probe_category`: Sub-millisecond direct Win32/NT query for each diagnostic category.
    * `wincare_clean_locations`: Safe, parallel filesystem purge of temp caches and stale logs.
  * Wrap all C-ABI entry points in `std::panic::catch_unwind` returning negative `i32` error codes on failure.
* **FR-4: Zero-Allocation Telemetry Interop:**
  * Expose `ref NativeSnapshot` P/Invoke struct mapping directly to blittable Rust memory.
  * C# ViewModels consume telemetry without per-tick string or collection allocations.
* **FR-5: Dual Packaging Pipeline:**
  * Configure `portable-x64.pubxml` with `<PublishReadyToRun>false</PublishReadyToRun>` and localized PRI resource pruning to reach **~20–24 MB**.
  * Add `framework-x64.pubxml` with `<PublishSelfContained>false</PublishSelfContained>` to compile the **~4–6 MB** installer/winget release.

---

### 5. Non-Functional Requirements & Evaluator Contracts

* **NFR-1 (Latency):** Total system health checkup across all 10 diagnostic categories completes in $\le 5.0\text{ ms}$ (Evaluator command: `dotnet test --filter Category=PerfBenchmark`).
* **NFR-2 (Process Spawning):** $0$ child processes (`powershell.exe`, `cmd.exe`, `wmic.exe`) spawned during diagnostic checkup or Safe tier execution.
* **NFR-3 (Memory & Allocation):**
  * Steady-state memory footprint of the desktop app $\le 35\text{ MB}$ idle.
  * Live telemetry polling loop generates $0\text{ bytes}$ of managed Gen0/Gen1 heap allocations.
* **NFR-4 (Binary Footprint):**
  * Portable Standalone Executable (`WinCare.App.exe`): $\le 24\text{ MB}$ (Zip archive $\le 19\text{ MB}$).
  * Framework-Dependent Installer (`WinCare-Setup.exe`): $\le 6\text{ MB}$.
* **NFR-5 (Accessibility & Visual Standards):**
  * Full WCAG 2.2 AA contrast compliance across light and dark themes.
  * Monospace numerical metrics (`Cascadia Code`) paired with `Segoe UI Variable` headings and body.

---

### 6. Scope Boundaries

* **In-Scope:**
  * Expansion of `wincare-core` with native C-ABI probes and cleaners.
  * C# P/Invoke bridge and ViewModel progressive disclosure expanders.
  * ReadyToRun removal and PRI stripping in portable publish profiles.
  * Inno Setup framework-dependent installer target definition.
* **Explicit Non-Goals:**
  * No kernel-mode driver installation.
  * No third-party analytics, telemetry, or network phone-home.
  * No aggressive or unverified registry tweaks that risk Windows update stability.
  * No background persistent daemons consuming > 10 MB RAM at idle.

---

### 7. Verification & Quality Gates

1. **Managed Test Suite:** 100% pass on all 172 xUnit tests in `WinCare.Native.sln`.
2. **Native Rust Test Suite:** 100% pass on all unit tests in `native/wincare-core` including MIRI/safety validations.
3. **Evaluator Contract Checks:**
   * Benchmark probe run: `dotnet test --filter Category=PerfBenchmark` $\implies p95 < 5\text{ ms}$.
   * Size validation: `python tools/release_checklist.py` $\implies$ portable exe $\le 24\text{ MB}$, framework build $\le 6\text{ MB}$.
4. **Visual Token Audit:** Execution of `python tests/tools/verify_visual_tokens.py` and `python tests/tools/verify_pill_contrast.py` ensuring zero contrast regressions.
