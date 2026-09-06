---
artifact_contract: ce-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: ce-brainstorm
date: 2026-09-06
title: WinCare Modernization & Ergonomics Overhaul
status: Completed
---

# WinCare Modernization & Ergonomics Overhaul — Product Contract

## 1. Executive Summary

WinCare is a native Windows maintenance and diagnostic desktop application built with .NET 8, WinUI 3, and Rust. This modernization addresses five concrete friction points:

1. **Portable footprint:** reduce the large untrimmed self-contained baseline while measuring the actual shipped executable.
2. **Checkup latency:** run independent local diagnostics concurrently instead of serializing them with Windows Update.
3. **Confirmation fatigue:** apply risk-specific admission rather than forcing every mutation through a destructive-operation workflow.
4. **Usability/product truth:** prefer curated actions and evidence-based categorical findings over raw JSON-first workflows or completion percentages presented as health.
5. **Distribution:** keep MSIX validation while also maintaining portable artifacts and an Inno Setup definition that can wrap the canonical x64 portable payload.

---

## 2. Problem Statement & User Needs

| Area | Legacy State | Desired Outcome |
|---|---|---|
| **Safety Model** | Mandatory preview/token ceremony for every mutation. | Safe = direct; Moderate = lightweight confirmation; Destructive = preview + current single-use review plan. |
| **UX** | Large flat catalog, prominent raw JSON, completion-rate “health.” | Curated routine actions, advanced JSON disclosure, categorical findings grounded in actual evidence. |
| **Performance** | Independent probes serialized with slower WUA work. | Bounded parallel local probes; WUA reports asynchronously. |
| **Portable Size** | Large untrimmed standalone executable. | Trimmed single-file x64/ARM64 artifacts, each measured against one byte ceiling. |
| **Distribution** | Development-signed MSIX path is high-friction for ordinary users. | Keep verified MSIX artifacts plus portable artifacts; Inno consumes the x64 portable payload when a setup build is produced. |

---

## 3. Key Decisions

### 3.1 Risk-Tiered Command Admission

- **Safe:** routine low-risk actions execute directly with `Apply=true`.
- **Moderate:** require a lightweight explicit confirmation, without a mandatory preview plan.
- **Destructive:** require a successful preview, explicit approval, and the current dispatcher-issued single-use `ApprovedMutationPlan`.
- Explicit metadata cannot downgrade a higher derived tier, and unsupported tier values are blocked before handler execution.

### 3.2 Curated Everyday Workflows

Home and Checkup surface routine actions and direct evidence. Checkup reports `Healthy`, `Attention`, `Action`, or incomplete/checking states from actual storage/security/update findings; it does not present probe completion as machine health.

### 3.3 Parallel Probe Architecture

`system`, `storage`, and `security` run through `ParallelCommandProbeRunner.RunPreviewsAsync` with bounded concurrency and stable result ordering. `wua-search` is independent and can update the page after the primary result is available.

### 3.4 Trimming and Distribution

- Portable x64/ARM64 profiles use trimmed, compressed, single-file publishing.
- **Canonical portable size contract:** `artifacts/portable/<rid>/WinCare.App.exe <= 35,000,000 bytes`.
- The size gate measures the executable itself, not the ZIP and not an installer wrapper.
- Runtime trim correctness is checked by launching the real packaged x64/ARM64 executables on native-architecture Windows runners with `--smoke-test`.
- `tools/installer/wincare_setup.iss` consumes `artifacts/portable/win-x64/*`. It is not a separate framework-dependent application build and has no separate 6 MB product contract.
- The current PR validates/uploads MSIX and portable artifacts; it does **not** claim that CI already compiles and publishes `WinCare-Setup.exe`.

---

## 4. Functional Requirements

### 4.1 Safety & Command Admission

- **REQ-SAF-01:** `CommandDefinition` exposes `RiskTier` (`Safe`, `Moderate`, `Destructive`).
- **REQ-SAF-02:** Safe mutating commands execute directly without `ApprovedMutationPlan`.
- **REQ-SAF-03:** Moderate commands require confirmation but not a mandatory preview plan.
- **REQ-SAF-04:** Destructive commands require current preview-plan validation and replay protection.
- **REQ-SAF-05:** Invalid/unsupported risk metadata is blocked before execution.
- **REQ-SAF-06:** Activity remains recorded for admitted command execution.

### 4.2 Home & Checkup UX

- **REQ-UX-01:** Home exposes curated routine action cards.
- **REQ-UX-02:** Checkup findings are based on actual evidence rather than completion percentage.
- **REQ-UX-03:** Finding action buttons remain live as asynchronous results change.
- **REQ-UX-04:** Raw JSON remains an advanced disclosure surface.
- **REQ-UX-05:** Unavailable native telemetry is labeled unavailable (`N/A`) rather than synthesized as a legitimate zero reading.

### 4.3 Probe Execution

- **REQ-PERF-01:** Independent local probes run concurrently through the active parallel runner.
- **REQ-PERF-02:** The primary local Quick Check targets `<= 3.0s` on representative Windows hardware; test-level overlap checks are not presented as a p95 benchmark.
- **REQ-PERF-03:** WUA readiness runs independently from the primary local batch.

### 4.4 Native Cleaner

- **REQ-NATIVE-01:** Safe temp cleanup stays within the intended temp scope.
- **REQ-NATIVE-02:** Windows reparse-point directories/junctions are detected using non-following metadata before traversal.
- **REQ-NATIVE-03:** A junction-to-outside sentinel regression test proves the cleaner does not recurse into the target.

### 4.5 Build, Footprint & Packaging

- **REQ-PKG-01:** Portable profiles publish trimmed single-file x64 and ARM64 executables.
- **REQ-PKG-02:** Each `WinCare.App.exe` is `<= 35,000,000` bytes, enforced by `tools/release_checklist.py --portable-artifact <path>`.
- **REQ-PKG-03:** Both trimmed artifacts pass native-architecture packaged runtime smoke before release promotion.
- **REQ-PKG-04:** The Inno definition wraps the canonical x64 portable payload when invoked; no fictional framework payload is documented.

---

## 5. Non-Functional Requirements & Boundaries

- **Accessibility:** Follow the mandatory `DESIGN.md` floor: logical keyboard navigation, visible focus, meaningful automation names, minimum `44 x 44 DIP` interactive targets, and Windows High Contrast usability.
- **Advanced access:** The full command catalog remains available to technicians through All Tools.
- **Product truth:** Do not claim generic rollback, installer publication, native p95 latency, Quick Check p95 latency, or trim safety without corresponding executable evidence.
- **Trim boundary:** Dynamic COM/WUA, plugin reflection, XAML activation, and native library loading require runtime coverage; warning suppression is not proof.

---

## 6. Verification & Acceptance Criteria

1. **Portable bytes:** both x64 and ARM64 `WinCare.App.exe` artifacts pass the `35,000,000`-byte gate.
2. **Packaged runtime:** both portable artifacts exit successfully from `--smoke-test` on native x64/ARM64 Windows runners after constructing WinUI, initializing runtime/plugins, validating the Rust ABI, and executing the read-only `system` command.
3. **Concurrency:** application tests prove real probe overlap, stable ordering, and failure isolation; hardware p95 claims require separate measurement.
4. **Safety ergonomics:** Safe remains one-click, Moderate confirmation-only, Destructive preview-plan gated.
5. **Cleaner:** the junction regression proves an external sentinel survives cleanup.
6. **Distribution truth:** CI validates MSIX and portable artifacts; the repository contains an Inno Setup definition consuming x64 portable output, without claiming an installer artifact that the workflow does not currently build.
