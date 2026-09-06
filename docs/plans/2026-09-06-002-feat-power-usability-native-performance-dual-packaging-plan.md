---
artifact_contract: ce-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: ce-brainstorm
date: 2026-09-06
status: completed
---

# WinCare Power Usability, Native Performance, and Portable Packaging - Plan

## Goal Capsule

- **Objective:** Pair 1-click everyday maintenance with inspectable power-user detail, a Rust native C-ABI core, parallel read-only diagnostics, and portable x64/ARM64 distribution.
- **Product Authority:** User design approval on 2026-09-06 (Understanding Lock confirmed).
- **Architecture:** [Interactive diagram](../architecture.html), authored from [`docs/wincare.architecture.json`](../wincare.architecture.json).
- **Portable size contract:** `artifacts/portable/<rid>/WinCare.App.exe` must be `<= 70,000,000` bytes.

---

## Product Contract

### 1. Problem Statement & Strategic Vision

WinCare uses progressive disclosure instead of a false choice between opaque one-click utilities and an admin console:

1. **Daily users:** curated Safe operations remain one-click.
2. **Power users:** measured telemetry and affected-resource detail are available without blocking the primary workflow.
3. **Risk-specific review:** Moderate operations use lightweight confirmation; only Destructive operations require the dispatcher-issued preview plan.

Product-truth rule: telemetry, health, rollback, installer, and performance claims must correspond to an executable implementation or measurement. Do not infer runtime safety from build success alone.

---

### 2. Architecture & Interactive Diagram

The repository-relative architecture sources are:

- [Interactive architecture diagram](../architecture.html)
- [C4 architecture model](../architecture/c4-model.md)
- [Ponytail trimming/complexity audit](../architecture/ponytail-audit.md)

Layers:

- **Presentation (`WinCare.App`):** WinUI 3, responsive layouts, curated actions, and progressive telemetry disclosure.
- **Governance (`WinCare.Application`):** `CommandDispatcher.ExecuteAsync`, risk-tier admission, activity journal integration, and `ParallelCommandProbeRunner.RunPreviewsAsync`.
- **Native (`native/wincare-core`):** Rust 2024 C-ABI primitives used by the managed native repositories.
- **Distribution:** MSIX plus trimmed portable x64/ARM64 artifacts. The Inno Setup definition consumes the same `artifacts/portable/win-x64/*` payload when an installer is built; it is not a separate framework application.

---

### 3. Decision Log

| ID | Decision | Rationale |
|---|---|---|
| **DEC-01** | Progressive-disclosure cockpit | Keeps everyday actions direct while allowing deeper inspection. |
| **DEC-02** | Rust C-ABI for native snapshot/clean primitives | Avoids child-process probes and provides a typed native boundary. |
| **DEC-03** | Risk-tiered admission | Safe = direct; Moderate = confirmation; Destructive = preview plan. |
| **DEC-04** | Parallel local probes + background WUA | Prevents Windows Update latency from blocking the primary Checkup result. |
| **DEC-05** | One measured portable regression ceiling | Replaces conflicting historical 22/24/35 MB claims with a single gate grounded in the actual self-contained WinUI payload. |
| **DEC-06** | Native-architecture trimmed runtime smoke | Build/link success is insufficient evidence for XAML/reflection/COM/plugin/native paths. |

---

### 4. Functional Requirements

#### FR-1: 1-Click Surface Actions

- Safe routine maintenance executes directly with `Apply=true`.
- UI shows in-flight and result state inline.
- No mandatory preview/approval toggle is added to Safe commands.

#### FR-2: Deep System Inspector

- Inspector values identify unavailable telemetry instead of inventing values such as `0.0%` CPU after a probe failure.
- Metrics and affected paths remain inspectable without changing admission policy.

#### FR-3: Native Rust Core

Implement and consume the existing C-ABI surface under `native/wincare-core`:

- `wincare_sys_snapshot_all`: batched CPU/RAM/disk/network snapshot.
- `wincare_clean_temp_files`: safe temp cleanup / dry-run result.

The cleaner must not traverse Windows reparse-point directories/junctions. Native tests include a junction-to-outside sentinel regression.

#### FR-4: Checkup Concurrency

- `system`, `storage`, and `security` use `ParallelCommandProbeRunner.RunPreviewsAsync` with bounded concurrency and request-order preservation.
- `wua-search` runs independently and updates the view later.
- A failed/incomplete probe is reported as incomplete/review-needed; it must not produce a Healthy result.

#### FR-5: Portable Packaging

- Build trimmed single-file `win-x64` and `win-arm64` executables.
- Canonical artifact under measurement: `artifacts/portable/<rid>/WinCare.App.exe`.
- Canonical regression ceiling: **70,000,000 bytes**, enforced by:

```text
python tools/release_checklist.py --portable-artifact artifacts/portable/<rid>/WinCare.App.exe
```

- The ceiling is intentionally above the measured exact-head artifacts (67,362,718 bytes x64 and 66,071,465 bytes ARM64) so it catches material footprint regressions without pretending the self-contained WinUI payload is a 35 MB binary.
- The Inno script consumes `artifacts/portable/win-x64/*`; do not describe it as a separate 4–6 MB framework-dependent application.
- MSIX packaging/signature validation remains a separate CI path.

---

### 5. Non-Functional Requirements & Evaluator Contracts

#### NFR-1A: Native Probe Latency

The native snapshot primitive has a **native-call** latency objective. This is distinct from the user-visible Quick Check SLA.

Correctness evaluator:

```text
cargo test --manifest-path native/Cargo.toml --release
```

A concrete p95 microsecond/millisecond claim requires a dedicated Windows benchmark of `wincare_sys_snapshot_all`; the Rust correctness suite alone does not establish p95 latency.

#### NFR-1B: Primary Quick Check Latency

The primary local Checkup batch (`system`, `storage`, `security`, excluding background WUA completion) targets **<= 3.0 seconds on representative Windows hardware**.

Concurrency/correctness evaluator:

```text
dotnet test tests/WinCare.Application.Tests/WinCare.Application.Tests.csproj --filter FullyQualifiedName~ParallelCommandProbeRunnerTests
```

This test proves overlap, result ordering, and failure isolation. It is not a hardware p95 benchmark; documentation must not publish a p95 claim until an end-to-end Windows measurement is captured.

#### NFR-2: Child Processes

Native snapshot and Safe temp-clean primitives do not introduce PowerShell/cmd/wmic child processes.

#### NFR-3: Trimmed Runtime Correctness

- x64 portable artifact runs `--smoke-test` on a native x64 Windows runner.
- ARM64 portable artifact runs the same test on a native Windows ARM64 runner.
- Smoke constructs the WinUI window, initializes app/plugin runtime, validates Rust ABI loading, and executes a read-only `system` command through the dispatcher.
- Dynamic COM/WUA/plugin paths outside this smoke require their own focused Windows runtime coverage and any required preservation roots/annotations; suppressed trim warnings are not proof.

#### NFR-4: Accessibility

Primary interactive surfaces follow the mandatory `DESIGN.md` accessibility floor: keyboard navigation, visible focus, meaningful automation names, minimum 44 x 44 DIP targets, and Windows High Contrast operation.

---

### 6. Scope Boundaries

**In scope**

- Native C-ABI snapshot and safe temp cleaner.
- Managed native repository bridge.
- Risk-tier admission and regression coverage.
- Parallel local diagnostics and decoupled WUA.
- Trimmed portable x64/ARM64 outputs and native-architecture runtime smoke.
- Inno Setup definition consuming the x64 portable payload.

**Non-goals**

- Kernel-mode driver installation.
- Blanket confirmation/token ceremony for Safe or Moderate commands.
- Generic rollback claims without an executable compensator.
- Fabricated health percentages or performance numbers.
- A fictional framework-dependent installer artifact used as product-truth evidence.

---

### 7. Verification & Quality Gates

1. **Managed tests:** `dotnet test WinCare.Native.sln` on x64 Windows CI.
2. **Rust:** format, clippy, x64 unit tests, and release builds for x64 + ARM64.
3. **Structural:** `python -m unittest discover -s tests/native -v`.
4. **Portable bytes:** `tools/release_checklist.py --portable-artifact .../WinCare.App.exe` for each RID.
5. **Portable runtime:** `--smoke-test` on native x64 and native ARM64 Windows runners.
6. **MSIX:** build, sign with runner-local development identity, and verify signature/publisher in CI.
7. **Visual/product truth:** design contract and documentation must describe the actual runtime and packaging paths.
